using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalSpawnGate
{
	private sealed class AdmissionReservation
	{
		internal int Remaining;

		internal float SoftExpiresAt;

		internal float HardExpiresAt;
	}

	private sealed class QueuedBoss
	{
		internal BotsController Controller;

		internal BossLocationSpawn Wave;

		internal BossLocationSpawn Source;

		internal float QueuedAt;
	}

	private enum OpeningBatchRowKind
	{
		None,
		Ruaf,
		Storm4Scav,
		Storm7Scav
	}

	internal sealed class Patch_ArmGate : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(GameWorld), "OnGameStarted");
		}

		[PatchPostfix]
		private static void Postfix()
		{
			if (!TerminalGate.On || !TerminalCoop.Authority)
			{
				return;
			}
			TerminalAILimitFirewall.EnforceAtRaidStart();
			if (_raidStart >= 0f)
			{
				return;
			}
			_raidStart = Time.realtimeSinceStartup;
			Plugin.Log.LogInfo($"[SpawnGate] raid started; retained {_waves.Count} ordinary and {_bosses.Count} boss/event startup wave(s)");
			if (BotsDisabled)
			{
				Plugin.Log.LogWarning("[BotControl] Terminal botless diagnostic armed — all runtime spawn paths blocked");
				return;
			}
			if (CapEnabled)
			{
				Plugin.Log.LogWarning($"[SpawnGate] early ordinary-scav admission armed at {Plugin.MaxAliveScavs.Value} living scavs — profile creation is serialized");
			}
			if (TotalCapEnabled)
			{
				Plugin.Log.LogWarning($"[SpawnGate] total living-AI ceiling armed at {Plugin.MaxAliveBots.Value}; authored special waves wait intact and faction recyclers get first refusal");
			}
			if (ScavResidentCapEnabled)
			{
				Plugin.Log.LogWarning($"[SpawnGate] ordinary-scav resident-resource ceiling armed at {Plugin.MaxResidentScavs.Value} living + uncleaned bodies; corpse retirement refunds capacity");
			}
			if (FreshProfilePacingEnabled)
			{
				Plugin.Log.LogWarning($"[SpawnPacing] fresh-profile waves paced at {Plugin.FreshProfileWaveInterval.Value:0.0}s, " + $"catching up at {Plugin.LowPopulationWaveInterval.Value:0.0}s while living + admitted AI <= {Plugin.LowPopulationWaveThreshold.Value}; recycled waves bypass pacing");
			}
			if (LifetimeCapEnabled)
			{
				Plugin.Log.LogWarning($"[SpawnGate] lifetime budget armed at {Plugin.MaxBotsCreatedPerRaid.Value} total bot placements — deaths do not refund slots");
			}
			new GameObject("Terminal_SpawnGate").AddComponent<FlushHost>();
		}
	}

	[HarmonyPatch(typeof(BotsController), "ActivateBotsByWave", new Type[] { typeof(SpawnWave) })]
	internal static class Patch_GateWaves
	{
		[HarmonyPrefix]
		private static bool Prefix(BotsController __instance, ref SpawnWave wave, ref Task __result)
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			if (!TerminalCoop.Authority || _endingStopped)
			{
				__result = Task.CompletedTask;
				return false;
			}
			if (BotsDisabled)
			{
				LogBotlessBlock("assault wave");
				__result = Task.CompletedTask;
				return false;
			}
			if (_releasingQueuedWave)
			{
				return true;
			}
			if (!LifetimeBudgetOpen)
			{
				LogLifetimeHold();
				__result = Task.CompletedTask;
				return false;
			}
			if (Open && !IsOrdinaryScavWave(wave) && !TotalCapEnabled && !FreshProfilePacingEnabled)
			{
				return true;
			}
			if (Open && TerminalPopulationDirector.TryPreparePlainWave(__instance, ref wave))
			{
				__result = Task.CompletedTask;
				return false;
			}
			_waves.Add((__instance, wave, Time.realtimeSinceStartup));
			if (!Open)
			{
				LogHold("assault wave");
			}
			else
			{
				LogPopHold(!IsOrdinaryScavWave(wave));
			}
			__result = Task.CompletedTask;
			return false;
		}
	}

	internal sealed class Patch_GateBosses : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(BotsController), "ActivateBotsByWave", new Type[1] { typeof(BossLocationSpawn) });
		}

		[PatchPrefix]
		private static bool Prefix(BotsController __instance, ref BossLocationSpawn wave)
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			if (!TerminalCoop.Authority || _endingStopped)
			{
				return false;
			}
			TerminalWaveRoster.Normalize(wave);
			if (TerminalStageWaveBlock.IsBlocked(wave))
			{
				Plugin.Log.LogDebug("[SpawnGate] blocked native boss callback for retired source zone '" + wave?.BossZone + "'");
				return false;
			}
			if (BotsDisabled)
			{
				LogBotlessBlock("boss/event wave");
				return false;
			}
			if (_releasingQueuedWave)
			{
				return true;
			}
			if (IsOpeningBatchReleased(wave))
			{
				_duplicateBossCallbacks++;
				return false;
			}
			if (IsQueuedBoss(wave))
			{
				_duplicateBossCallbacks++;
				return false;
			}
			BossLocationSpawn source = wave;
			bool flag = GetOpeningBatchRowKind(source) != OpeningBatchRowKind.None;
			if (flag && _openingBatchResolved && Open && !ManagedAdmissionEnabled)
			{
				MarkOpeningBatchReleased(source);
				LogAuthoredRowReleased(source, wave);
				if (IsAuthoredSpecialCapBypass(source, wave))
				{
					LogAuthoredRowCapBypass(source, wave);
				}
				return true;
			}
			if (flag && !LifetimeBudgetOpen)
			{
				LogLifetimeHold();
				return false;
			}
			if (!flag && Open && wave != null && !IsOrdinaryScavWave(wave) && LifetimeBudgetOpen)
			{
				bool flag2 = IsAuthoredSpecialCapBypass(source, wave);
				if (TerminalPopulationDirector.IsRuaf(wave.BossType) && TerminalPopulationDirector.TryPrepareRuafWave(__instance, ref wave))
				{
					LogAuthoredRowReleased(source, wave);
					return false;
				}
				if (TerminalPopulationDirector.IsBlackDivision(wave.BossType) && TerminalPopulationDirector.TryPrepareBlackDivisionWave(__instance, ref wave))
				{
					LogAuthoredRowReleased(source, wave);
					return false;
				}
				if (!TotalCapEnabled && !FreshProfilePacingEnabled)
				{
					LogAuthoredRowReleased(source, wave);
					if (flag2)
					{
						LogAuthoredRowCapBypass(source, wave);
					}
					return true;
				}
				QueueBoss(__instance, wave, source);
				LogPopHold(special: true);
				return false;
			}
			if (!LifetimeBudgetOpen)
			{
				LogLifetimeHold();
				return false;
			}
			if (!flag && Open && !IsOrdinaryScavWave(wave) && !TotalCapEnabled && !FreshProfilePacingEnabled)
			{
				LogAuthoredRowReleased(source, wave);
				if (IsAuthoredSpecialCapBypass(source, wave))
				{
					LogAuthoredRowCapBypass(source, wave);
				}
				return true;
			}
			if (!flag && Open && TerminalPopulationDirector.TryPrepareBossWave(__instance, ref wave))
			{
				return false;
			}
			QueueBoss(__instance, wave, source);
			if (!Open)
			{
				LogHold("boss/event wave");
			}
			else
			{
				LogPopHold(!IsOrdinaryScavWave(wave));
			}
			return false;
		}
	}

	[HarmonyPatch(typeof(BotSpawner), "SpawnBotsInZoneOnPositions")]
	internal static class Patch_FinalLifetimeCap
	{
		[HarmonyPrefix]
		private static bool Prefix(BotSpawner __instance, List<ISpawnPoint> openedPositions, BotCreationData data)
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			if (!TerminalCoop.Authority || _endingStopped)
			{
				return false;
			}
			if (BotsDisabled)
			{
				LogBotlessBlock("final placement");
				return false;
			}
			if (openedPositions == null || openedPositions.Count == 0)
			{
				return true;
			}
			if (LifetimeCapEnabled && _placementsCommittedThisRaid + openedPositions.Count > Plugin.MaxBotsCreatedPerRaid.Value)
			{
				_lifetimeBudgetClosed = true;
				LogLifetimeHold();
				return false;
			}
			if (LifetimeCapEnabled)
			{
				_placementsCommittedThisRaid += openedPositions.Count;
				if (_placementsCommittedThisRaid >= Plugin.MaxBotsCreatedPerRaid.Value)
				{
					_lifetimeBudgetClosed = true;
				}
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(BotsController), "ActivateBotsWithoutWave")]
	internal static class Patch_AdmitWithoutWave
	{
		[HarmonyPrefix]
		private static bool Prefix(int count, IGetProfileData data)
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			if (!TerminalCoop.Authority || _endingStopped)
			{
				return false;
			}
			if (BotsDisabled)
			{
				LogBotlessBlock("non-wave profile admission");
				return false;
			}
			if (!Open || !LifetimeBudgetOpen)
			{
				return false;
			}
			int num = Math.Max(1, count);
			if (!IsOrdinaryScavProfile(data))
			{
				if (!CanAdmitTotal(num))
				{
					LogPopHold(special: true);
					return false;
				}
				if (!FreshProfileAdmissionOpen)
				{
					LogPacingHold();
					return false;
				}
				ReserveTotalAdmissions(num);
				NoteFreshProfileAdmission();
				return true;
			}
			if (CanAdmitScavs(num) && CanAdmitOrdinaryScavTotal(num))
			{
				if (!FreshProfileAdmissionOpen)
				{
					LogPacingHold();
					return false;
				}
				ReserveScavAdmissions(num);
				ReserveTotalAdmissions(num);
				NoteFreshProfileAdmission();
				return true;
			}
			LogPopHold();
			return false;
		}
	}

	[HarmonyPatch(typeof(BotSpawner), "TryToSpawnInZoneInner")]
	internal static class Patch_BypassNativeCapForAdmittedScavs
	{
		[HarmonyPrefix]
		private static void Prefix(BotCreationData data, ref bool withCheckMinMax)
		{
			if (TerminalGate.On && data != null && IsOrdinaryScavProfile(data._profileData))
			{
				withCheckMinMax = false;
			}
		}
	}

	[HarmonyPatch(typeof(NonWavesSpawnScenario), "Update")]
	internal static class Patch_GateNonWaves
	{
		[HarmonyPrefix]
		private static bool Prefix()
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			if (!TerminalCoop.Authority || _endingStopped)
			{
				return false;
			}
			if (BotsDisabled)
			{
				LogBotlessBlock("non-wave scenario");
				return false;
			}
			if (!LifetimeBudgetOpen)
			{
				LogLifetimeHold();
				return false;
			}
			return Open;
		}
	}

	internal class FlushHost : MonoBehaviour
	{
		private float _next;

		private void Update()
		{
			using (TerminalTickProfiler.Measure("SpawnGate.FlushHost.Update"))
			{
				PruneBlockedBosses();
				if (_endingStopped)
				{
					_waves.Clear();
					_bosses.Clear();
					UnityEngine.Object.Destroy(gameObject);
				}
				else if (BotsDisabled)
				{
					_waves.Clear();
					_bosses.Clear();
					UnityEngine.Object.Destroy(gameObject);
				}
				else
				{
					if (Time.realtimeSinceStartup < _next)
					{
						return;
					}
					bool flag = CapEnabled || TotalCapEnabled || ScavResidentCapEnabled || FreshProfilePacingEnabled;
					_next = Time.realtimeSinceStartup + (flag ? 0.5f : 1f);
					if (!Open || !ProfilesLoadedForOpening())
					{
						return;
					}
					BeginOpeningBatchWindow();
					if (TryDrainOpeningBatch())
					{
						return;
					}
					if (!LifetimeBudgetOpen)
					{
						LogLifetimeHold();
						_waves.Clear();
						_bosses.Clear();
					}
					else if (!flag)
					{
						if (_waves.Count + _bosses.Count <= 0)
						{
							return;
						}
						Plugin.Log.LogInfo($"[SpawnGate] cutscene done — releasing {_waves.Count} wave(s) + {_bosses.Count} boss wave(s)");
						_releasingQueuedWave = true;
						try
						{
							foreach (var (botsController, wave, _) in _waves)
							{
								try
								{
									using (TerminalTickProfiler.Measure("SpawnGate.ActivateWave.Legacy"))
									{
										botsController.ActivateBotsByWave(wave);
									}
								}
								catch (Exception ex)
								{
									Plugin.Log.LogWarning("[SpawnGate] wave release failed: " + ex.Message);
								}
							}
							foreach (QueuedBoss boss in _bosses)
							{
								MarkOpeningBatchReleased(boss);
								LogAuthoredRowReleased(boss.Source, boss.Wave);
								if (IsAuthoredSpecialCapBypass(boss.Source, boss.Wave))
								{
									LogAuthoredRowCapBypass(boss.Source, boss.Wave);
								}
								try
								{
									using (TerminalTickProfiler.Measure("SpawnGate.ActivateBoss.Legacy"))
									{
										boss.Controller.ActivateBotsByWave(boss.Wave);
									}
								}
								catch (Exception ex2)
								{
									Plugin.Log.LogWarning("[SpawnGate] boss release failed: " + ex2.Message);
								}
							}
						}
						finally
						{
							_releasingQueuedWave = false;
						}
						_waves.Clear();
						_bosses.Clear();
					}
					else if (_waves.Count + _bosses.Count != 0)
					{
						DrainOne();
					}
				}
			}
		}

		private static void DrainOne()
		{
			try
			{
				bool flag = false;
				int num = -1;
				for (int i = 0; i < _bosses.Count; i++)
				{
					if (!IsOrdinaryScavWave(_bosses[i].Wave))
					{
						num = i;
						break;
					}
				}
				if (num >= 0)
				{
					QueuedBoss queuedBoss = _bosses[num];
					BotsController controller = queuedBoss.Controller;
					BossLocationSpawn wave = queuedBoss.Wave;
					if (TerminalPopulationDirector.IsRuaf(wave.BossType) && TerminalPopulationDirector.TryPrepareRuafWave(controller, ref wave))
					{
						_bosses.RemoveAt(num);
						MarkOpeningBatchReleased(queuedBoss);
						LogAuthoredRowReleased(queuedBoss.Source, wave);
						LogReleased("recycled RUAF wave", queuedBoss.QueuedAt, generatedProfiles: false);
						return;
					}
					if (TerminalPopulationDirector.IsBlackDivision(wave.BossType) && TerminalPopulationDirector.TryPrepareBlackDivisionWave(controller, ref wave))
					{
						_bosses.RemoveAt(num);
						MarkOpeningBatchReleased(queuedBoss);
						LogAuthoredRowReleased(queuedBoss.Source, wave);
						LogReleased("recycled Black Division wave", queuedBoss.QueuedAt, generatedProfiles: false);
						return;
					}
					queuedBoss.Wave = wave;
					int num2 = ScavDemand(wave);
					TerminalSpawnAdmissionPolicy.TotalDecision totalDecision = EvaluateBossTotalAdmission(num2, queuedBoss.Source, wave);
					if (!totalDecision.Allowed)
					{
						_bosses.RemoveAt(num);
						_bosses.Add(queuedBoss);
						flag = true;
					}
					else
					{
						if (FreshProfileAdmissionOpen)
						{
							_bosses.RemoveAt(num);
							LogAuthoredRowReleased(queuedBoss.Source, wave);
							if (totalDecision.CapBypassed)
							{
								LogAuthoredRowCapBypass(queuedBoss.Source, wave);
							}
							ReserveTotalAdmissions(num2);
							NoteFreshProfileAdmission();
							MarkOpeningBatchReleased(queuedBoss);
							_releasingQueuedWave = true;
							try
							{
								using (TerminalTickProfiler.Measure("SpawnGate.ActivateBoss.Queued"))
								{
									controller.ActivateBotsByWave(wave);
								}
							}
							finally
							{
								_releasingQueuedWave = false;
							}
							LogReleased(wave.BossName, queuedBoss.QueuedAt, generatedProfiles: true);
							return;
						}
						LogPacingHold();
					}
				}
				int num3 = -1;
				for (int j = 0; j < _waves.Count; j++)
				{
					if (!IsOrdinaryScavWave(_waves[j].w))
					{
						num3 = j;
						break;
					}
				}
				if (num3 >= 0)
				{
					(BotsController c, SpawnWave w, float queuedAt) tuple = _waves[num3];
					BotsController item = tuple.c;
					SpawnWave item2 = tuple.w;
					float item3 = tuple.queuedAt;
					int num4 = ScavDemand(item2);
					if (!EvaluateTotalAdmission(num4, ordinaryScav: false, authoredIgnoreMaxBots: false).Allowed)
					{
						_waves.RemoveAt(num3);
						_waves.Add((item, item2, item3));
						flag = true;
					}
					else
					{
						if (FreshProfileAdmissionOpen)
						{
							_waves.RemoveAt(num3);
							ReserveTotalAdmissions(num4);
							NoteFreshProfileAdmission();
							_releasingQueuedWave = true;
							try
							{
								using (TerminalTickProfiler.Measure("SpawnGate.ActivateWave.QueuedSpecial"))
								{
									item.ActivateBotsByWave(item2);
								}
							}
							finally
							{
								_releasingQueuedWave = false;
							}
							LogReleased("non-scav wave", item3, generatedProfiles: true);
							return;
						}
						LogPacingHold();
					}
				}
				if (flag)
				{
					LogPopHold(special: true);
				}
				else if (_bosses.Count > 0)
				{
					QueuedBoss queuedBoss2 = _bosses[0];
					BotsController controller2 = queuedBoss2.Controller;
					BossLocationSpawn wave2 = queuedBoss2.Wave;
					if (TerminalPopulationDirector.TryPrepareBossWave(controller2, ref wave2))
					{
						_bosses.RemoveAt(0);
						MarkOpeningBatchReleased(queuedBoss2);
						LogReleased("recycled scav boss-wave", queuedBoss2.QueuedAt, generatedProfiles: false);
						return;
					}
					queuedBoss2.Wave = wave2;
					if (!CanAdmitScavs(ScavDemand(wave2)) || !CanAdmitOrdinaryScavTotal(ScavDemand(wave2)))
					{
						LogPopHold();
						return;
					}
					if (!FreshProfileAdmissionOpen)
					{
						LogPacingHold();
						return;
					}
					_bosses.RemoveAt(0);
					wave2.IgnoreMaxBots = true;
					ReserveScavAdmissions(ScavDemand(wave2));
					ReserveTotalAdmissions(ScavDemand(wave2));
					NoteFreshProfileAdmission();
					MarkOpeningBatchReleased(queuedBoss2);
					_releasingQueuedWave = true;
					try
					{
						using (TerminalTickProfiler.Measure("SpawnGate.ActivateBoss.QueuedScav"))
						{
							controller2.ActivateBotsByWave(wave2);
						}
					}
					finally
					{
						_releasingQueuedWave = false;
					}
					LogReleased(wave2.BossName, queuedBoss2.QueuedAt, generatedProfiles: true);
				}
				else
				{
					if (_waves.Count <= 0)
					{
						return;
					}
					(BotsController c, SpawnWave w, float queuedAt) tuple2 = _waves[0];
					BotsController item4 = tuple2.c;
					SpawnWave item5 = tuple2.w;
					float item6 = tuple2.queuedAt;
					SpawnWave wave3 = item5;
					if (TerminalPopulationDirector.TryPreparePlainWave(item4, ref wave3))
					{
						_waves.RemoveAt(0);
						LogReleased("recycled scav wave", item6, generatedProfiles: false);
						return;
					}
					_waves[0] = (item4, wave3, item6);
					if (!CanAdmitScavs(ScavDemand(wave3)) || !CanAdmitOrdinaryScavTotal(ScavDemand(wave3)))
					{
						LogPopHold();
						return;
					}
					if (!FreshProfileAdmissionOpen)
					{
						LogPacingHold();
						return;
					}
					_waves.RemoveAt(0);
					wave3.WithCheckMinMax = false;
					ReserveScavAdmissions(ScavDemand(wave3));
					ReserveTotalAdmissions(ScavDemand(wave3));
					NoteFreshProfileAdmission();
					_releasingQueuedWave = true;
					try
					{
						using (TerminalTickProfiler.Measure("SpawnGate.ActivateWave.QueuedScav"))
						{
							item4.ActivateBotsByWave(wave3);
						}
					}
					finally
					{
						_releasingQueuedWave = false;
					}
					LogReleased("scav wave", item6, generatedProfiles: true);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[SpawnGate] queued release failed: " + ex.Message);
			}
		}

		private static void LogReleased(string what, float queuedAt, bool generatedProfiles)
		{
			float num = Mathf.Max(0f, Time.realtimeSinceStartup - queuedAt);
			float num2 = (FreshProfilePacingEnabled ? Mathf.Max(0f, _nextFreshProfileAdmission - Time.realtimeSinceStartup) : 0f);
			Plugin.Log.LogDebug(string.Format("[SpawnGate] released queued '{0}' age={1:0.0}s profiles={2} nextFresh={3:0.0}s ", what, num, generatedProfiles ? "fresh" : "recycled", num2) + string.Format("(aliveScavs={0}/{1}, ", AliveScavCount(), CapEnabled ? Plugin.MaxAliveScavs.Value.ToString() : "unlimited") + string.Format("aliveTotal={0}/{1}, pendingTotal={2}, ", TerminalPopulationDirector.LivingCount, TotalCapEnabled ? Plugin.MaxAliveBots.Value.ToString() : "unlimited", PendingTotalAdmissions) + string.Format("residentScavs={0}/{1}, ", TerminalPopulationDirector.ResidentOrdinaryScavCount, ScavResidentCapEnabled ? Plugin.MaxResidentScavs.Value.ToString() : "unlimited") + $"lifetimeCreatedScavs={TerminalPopulationDirector.OrdinaryScavsCreatedCount}, " + $"pendingScavs={PendingScavAdmissions}, {_waves.Count + _bosses.Count} still queued)");
		}
	}

	private static float _raidStart = -1f;

	private static float _attackStartedAt = -1f;

	private static bool _holdLogged;

	private static bool _botlessBlockLogged;

	private static bool _lifetimeCapLogged;

	private static bool _lifetimeBudgetClosed;

	private static bool _releasingQueuedWave;

	private static bool _endingStopped;

	private static int _placementsCommittedThisRaid;

	private static float _lastPopLog;

	private static float _lastPacingLog;

	private static float _nextFreshProfileAdmission;

	private static float _pipelineActuallyIdleSince = -1f;

	private static int _duplicateBossCallbacks;

	private const float OpeningBatchWaitSeconds = 10f;

	private const string OpeningRuafZone = "Zone1VSRF1Spawn3";

	private const string OpeningStorm4ScavZone = "Zone1ScavEnterStorm4";

	private const string OpeningStorm7ScavZone = "Zone1ScavEnterStorm7";

	private static float _openingBatchFirstSeenAt = -1f;

	private static bool _openingBatchResolved;

	private static bool _openingBatchHoldLogged;

	private static readonly HashSet<BossLocationSpawn> _openingBatchReleasedSources = new HashSet<BossLocationSpawn>();

	private static readonly HashSet<BossLocationSpawn> _authoredRowsQueuedLogged = new HashSet<BossLocationSpawn>();

	private static readonly HashSet<BossLocationSpawn> _authoredRowsReleasedLogged = new HashSet<BossLocationSpawn>();

	private static readonly HashSet<BossLocationSpawn> _authoredRowsBypassLogged = new HashSet<BossLocationSpawn>();

	private static readonly List<AdmissionReservation> _scavReservations = new List<AdmissionReservation>();

	private static readonly List<AdmissionReservation> _totalReservations = new List<AdmissionReservation>();

	private static readonly List<(BotsController c, SpawnWave w, float queuedAt)> _waves = new List<(BotsController, SpawnWave, float)>();

	private static readonly List<QueuedBoss> _bosses = new List<QueuedBoss>();

	private static bool CapEnabled => Plugin.MaxAliveScavs.Value > 0;

	private static bool TotalCapEnabled => Plugin.MaxAliveBots.Value > 0;

	private static bool ScavResidentCapEnabled => Plugin.MaxResidentScavs.Value > 0;

	private static bool LifetimeCapEnabled
	{
		get
		{
			if (Plugin.MaxBotsCreatedPerRaid != null)
			{
				return Plugin.MaxBotsCreatedPerRaid.Value > 0;
			}
			return false;
		}
	}

	private static bool FreshProfilePacingEnabled
	{
		get
		{
			if (Plugin.FreshProfileWaveInterval != null)
			{
				return Plugin.FreshProfileWaveInterval.Value > 0f;
			}
			return false;
		}
	}

	internal static bool BotsDisabled
	{
		get
		{
			if (Plugin.DisableAllBots != null)
			{
				return Plugin.DisableAllBots.Value;
			}
			return false;
		}
	}

	private static bool ManagedAdmissionEnabled
	{
		get
		{
			if (!CapEnabled && !TotalCapEnabled && !ScavResidentCapEnabled && !FreshProfilePacingEnabled)
			{
				return LifetimeCapEnabled;
			}
			return true;
		}
	}

	private static float FreshProfileInterval
	{
		get
		{
			if (!FreshProfilePacingEnabled)
			{
				return 0f;
			}
			int num = TerminalPopulationDirector.LivingCount + PendingTotalAdmissions;
			if (Plugin.LowPopulationWaveThreshold != null && Plugin.LowPopulationWaveInterval != null && num <= Plugin.LowPopulationWaveThreshold.Value)
			{
				return Plugin.LowPopulationWaveInterval.Value;
			}
			return Plugin.FreshProfileWaveInterval.Value;
		}
	}

	private static bool FreshProfileAdmissionOpen
	{
		get
		{
			if (FreshProfilePacingEnabled)
			{
				return Time.realtimeSinceStartup >= _nextFreshProfileAdmission;
			}
			return true;
		}
	}

	private static int ScavHeadroom
	{
		get
		{
			if (!CapEnabled)
			{
				return int.MaxValue;
			}
			return Math.Max(0, Plugin.MaxAliveScavs.Value - AliveScavCount() - PendingScavAdmissions);
		}
	}

	private static int ScavResidentHeadroom
	{
		get
		{
			if (!ScavResidentCapEnabled)
			{
				return int.MaxValue;
			}
			return Math.Max(0, Plugin.MaxResidentScavs.Value - TerminalPopulationDirector.ResidentOrdinaryScavCount - PendingScavAdmissions);
		}
	}

	private static int PendingScavAdmissions
	{
		get
		{
			ExpireScavReservations();
			int num = 0;
			foreach (AdmissionReservation scavReservation in _scavReservations)
			{
				num += scavReservation.Remaining;
			}
			return num;
		}
	}

	internal static int PendingScavCount => PendingScavAdmissions;

	private static int PendingTotalAdmissions
	{
		get
		{
			ExpireReservations(_totalReservations, "total");
			int num = 0;
			foreach (AdmissionReservation totalReservation in _totalReservations)
			{
				num += totalReservation.Remaining;
			}
			return num;
		}
	}

	private static bool LifetimeBudgetOpen
	{
		get
		{
			if (LifetimeCapEnabled)
			{
				if (!_lifetimeBudgetClosed)
				{
					return _placementsCommittedThisRaid < Plugin.MaxBotsCreatedPerRaid.Value;
				}
				return false;
			}
			return true;
		}
	}

	internal static bool Open
	{
		get
		{
			if (!TerminalCoop.Authority)
			{
				return false;
			}
			if (!Plugin.HoldBotsForCutscene.Value)
			{
				return true;
			}
			if (TerminalCoop.Active)
			{
				if (!TerminalCoop.AttackReleased)
				{
					if (TerminalCoop.AttackStarted >= 0.0)
					{
						return TerminalCoop.Now - TerminalCoop.AttackStarted >= 37.9;
					}
					return false;
				}
				return true;
			}
			if (_raidStart > 0f && Time.realtimeSinceStartup - _raidStart > 480f)
			{
				return true;
			}
			if (TerminalAttackCutscene.PlayingNow && _attackStartedAt < 0f)
			{
				_attackStartedAt = Time.realtimeSinceStartup;
			}
			if (!(TerminalAttackCutscene.FinishedAt > 0f) && TerminalAttackCutscene.Available && (!(_attackStartedAt > 0f) || !(Time.realtimeSinceStartup - _attackStartedAt > 37.9f)))
			{
				return false;
			}
			try
			{
				BotSpawner botSpawner = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance.BotsController : null)?.BotSpawner;
				if (botSpawner != null && !botSpawner.IsProfilesLoaded)
				{
					return false;
				}
			}
			catch
			{
			}
			return true;
		}
	}

	internal static void ResetForRaid()
	{
		TerminalStageWaveBlock.ResetForRaid();
		_raidStart = -1f;
		_attackStartedAt = -1f;
		_holdLogged = false;
		_botlessBlockLogged = false;
		_lifetimeCapLogged = false;
		_lifetimeBudgetClosed = false;
		_releasingQueuedWave = false;
		_endingStopped = false;
		_placementsCommittedThisRaid = 0;
		_lastPopLog = 0f;
		_lastPacingLog = 0f;
		_nextFreshProfileAdmission = 0f;
		_pipelineActuallyIdleSince = -1f;
		_duplicateBossCallbacks = 0;
		_openingBatchFirstSeenAt = -1f;
		_openingBatchResolved = false;
		_openingBatchHoldLogged = false;
		_openingBatchReleasedSources.Clear();
		_authoredRowsQueuedLogged.Clear();
		_authoredRowsReleasedLogged.Clear();
		_authoredRowsBypassLogged.Clear();
		_scavReservations.Clear();
		_totalReservations.Clear();
		_waves.Clear();
		_bosses.Clear();
	}

	internal static void StopForEnding()
	{
		if (!_endingStopped)
		{
			_endingStopped = true;
			int num = _waves.Count + _bosses.Count;
			_waves.Clear();
			_bosses.Clear();
			_scavReservations.Clear();
			_totalReservations.Clear();
			Plugin.Log.LogInfo($"[SpawnGate] ending started — spawn admission closed and {num} queued wave(s) discarded");
		}
	}

	private static int AliveScavCount()
	{
		return TerminalPopulationDirector.LivingOrdinaryScavCount;
	}

	private static void NoteFreshProfileAdmission()
	{
		if (FreshProfilePacingEnabled)
		{
			_nextFreshProfileAdmission = Time.realtimeSinceStartup + FreshProfileInterval;
		}
	}

	private static void LogPacingHold()
	{
		if (FreshProfilePacingEnabled && !(Time.realtimeSinceStartup - _lastPacingLog < 10f))
		{
			_lastPacingLog = Time.realtimeSinceStartup;
			float num = Mathf.Max(0f, _nextFreshProfileAdmission - Time.realtimeSinceStartup);
			Plugin.Log.LogInfo($"[SpawnPacing] fresh-profile admission waiting {num:0.0}s " + $"(alive={TerminalPopulationDirector.LivingCount}, pending={PendingTotalAdmissions}, queued={_waves.Count + _bosses.Count}); recycled fulfillment remains immediate");
		}
	}

	private static void LogBotlessBlock(string path)
	{
		if (!_botlessBlockLogged)
		{
			_botlessBlockLogged = true;
			Plugin.Log.LogWarning("[BotControl] blocked runtime bot spawn path '" + path + "' — botless diagnostic is active");
		}
	}

	private static void ExpireScavReservations()
	{
		ExpireReservations(_scavReservations, "scav");
	}

	private static void ExpireReservations(List<AdmissionReservation> reservations, string label)
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		bool flag = false;
		bool flag2 = false;
		for (int num = reservations.Count - 1; num >= 0; num--)
		{
			AdmissionReservation admissionReservation = reservations[num];
			if (admissionReservation.Remaining <= 0)
			{
				reservations.RemoveAt(num);
			}
			else if (!(realtimeSinceStartup < admissionReservation.SoftExpiresAt))
			{
				if (!flag)
				{
					flag2 = PlacementPipelineBusy();
					flag = true;
				}
				if (flag2 && realtimeSinceStartup < admissionReservation.HardExpiresAt)
				{
					admissionReservation.SoftExpiresAt = Math.Min(admissionReservation.HardExpiresAt, realtimeSinceStartup + 15f);
				}
				else
				{
					string arg = ((realtimeSinceStartup >= admissionReservation.HardExpiresAt) ? "hard timeout" : "pipeline idle");
					Plugin.Log.LogWarning($"[SpawnGate] released stale {label} admission reservation ({admissionReservation.Remaining} placement(s), {arg})");
					reservations.RemoveAt(num);
				}
			}
		}
	}

	private static void ReserveScavAdmissions(int count)
	{
		if (count > 0)
		{
			ExpireScavReservations();
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			_scavReservations.Add(new AdmissionReservation
			{
				Remaining = count,
				SoftExpiresAt = realtimeSinceStartup + 15f,
				HardExpiresAt = realtimeSinceStartup + 90f
			});
		}
	}

	private static void ReserveTotalAdmissions(int count)
	{
		if (count > 0 && TotalCapEnabled)
		{
			ExpireReservations(_totalReservations, "total");
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			_totalReservations.Add(new AdmissionReservation
			{
				Remaining = count,
				SoftExpiresAt = realtimeSinceStartup + 15f,
				HardExpiresAt = realtimeSinceStartup + 90f
			});
		}
	}

	internal static void NoteBotCreated(bool ordinaryScav)
	{
		ConsumeReservation(_totalReservations);
		if (ordinaryScav)
		{
			ConsumeReservation(_scavReservations);
		}
	}

	private static void ConsumeReservation(List<AdmissionReservation> reservations)
	{
		ExpireReservations(reservations, (reservations == _scavReservations) ? "scav" : "total");
		for (int i = 0; i < reservations.Count; i++)
		{
			if (reservations[i].Remaining > 0)
			{
				reservations[i].Remaining--;
				if (reservations[i].Remaining <= 0)
				{
					reservations.RemoveAt(i);
				}
				break;
			}
		}
	}

	private static void LogLifetimeHold()
	{
		if (!_lifetimeCapLogged)
		{
			_lifetimeCapLogged = true;
			Plugin.Log.LogWarning($"[SpawnGate] lifetime budget closed ({_placementsCommittedThisRaid} bot placement(s) accepted; limit {Plugin.MaxBotsCreatedPerRaid.Value}) — deaths will not spawn replacements");
		}
	}

	private static void LogPopHold(bool special = false)
	{
		if (!(Time.realtimeSinceStartup - _lastPopLog < 20f))
		{
			_lastPopLog = Time.realtimeSinceStartup;
			string arg = (special ? "special admission" : "scav admission");
			Plugin.Log.LogInfo($"[SpawnGate] {arg} waiting (aliveScavs={AliveScavCount()}/{Plugin.MaxAliveScavs.Value}, " + string.Format("aliveTotal={0}/{1}, pendingTotal={2}, ", TerminalPopulationDirector.LivingCount, TotalCapEnabled ? Plugin.MaxAliveBots.Value.ToString() : "unlimited", PendingTotalAdmissions) + string.Format("residentScavs={0}/{1}, ", TerminalPopulationDirector.ResidentOrdinaryScavCount, ScavResidentCapEnabled ? Plugin.MaxResidentScavs.Value.ToString() : "unlimited") + $"lifetimeCreatedScavs={TerminalPopulationDirector.OrdinaryScavsCreatedCount}, " + $"pendingScavs={PendingScavAdmissions}, nativePipelineBusy={PlacementPipelineBusy()}, duplicateBossCallbacksSuppressed={_duplicateBossCallbacks}) " + $"— {_waves.Count + _bosses.Count} wave(s) queued");
		}
	}

	private static bool IsOrdinaryScavProfile(IGetProfileData data)
	{
		try
		{
			if (data == null || !data.TryGetRole(out var role, out var _))
			{
				return false;
			}
			return TerminalPopulationDirector.IsOrdinaryScav(role);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsOrdinaryScavWave(SpawnWave wave)
	{
		if (wave != null)
		{
			return TerminalPopulationDirector.IsOrdinaryScav(wave.WildSpawnType);
		}
		return false;
	}

	private static bool IsOrdinaryScavWave(BossLocationSpawn wave)
	{
		if (wave != null)
		{
			return TerminalPopulationDirector.IsOrdinaryScav(wave.BossType);
		}
		return false;
	}

	private static int ScavDemand(SpawnWave wave)
	{
		return Math.Max(0, wave?.BotsCount ?? 0);
	}

	private static int ScavDemand(BossLocationSpawn wave)
	{
		return TerminalWaveRoster.Count(wave);
	}

	private static bool CanAdmitScavs(int wanted)
	{
		if (wanted <= 0)
		{
			return true;
		}
		if (PendingScavAdmissions > 0)
		{
			return false;
		}
		if (wanted <= ScavHeadroom)
		{
			return wanted <= ScavResidentHeadroom;
		}
		return false;
	}

	private static bool CanAdmitTotal(int wanted)
	{
		return EvaluateTotalAdmission(wanted, ordinaryScav: false, authoredIgnoreMaxBots: false).Allowed;
	}

	private static bool CanAdmitOrdinaryScavTotal(int wanted)
	{
		return EvaluateTotalAdmission(wanted, ordinaryScav: true, authoredIgnoreMaxBots: false).Allowed;
	}

	private static TerminalSpawnAdmissionPolicy.TotalDecision EvaluateTotalAdmission(int wanted, bool ordinaryScav, bool authoredIgnoreMaxBots)
	{
		return TerminalSpawnAdmissionPolicy.EvaluateTotal(wanted, TerminalPopulationDirector.LivingCount, PendingTotalAdmissions, TotalCapEnabled ? Plugin.MaxAliveBots.Value : 0, ordinaryScav, authoredIgnoreMaxBots);
	}

	private static bool IsAuthoredSpecialCapBypass(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		if (bossLocationSpawn != null && bossLocationSpawn.IgnoreMaxBots)
		{
			return IsAuthoredSpecialRow(source, wave);
		}
		return false;
	}

	private static TerminalSpawnAdmissionPolicy.TotalDecision EvaluateBossTotalAdmission(int wanted, BossLocationSpawn source, BossLocationSpawn wave)
	{
		return EvaluateTotalAdmission(wanted, IsOrdinaryScavWave(source ?? wave), IsAuthoredSpecialCapBypass(source, wave));
	}

	private static bool PlacementPipelineBusy()
	{
		try
		{
			BotsController botsController = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance.BotsController : null);
			if (botsController?.BotSpawner == null)
			{
				return false;
			}
			BotSpawner botSpawner = botsController.BotSpawner;
			if (botSpawner._inSpawnProcess < 0)
			{
				Plugin.Log.LogWarning($"[SpawnGate] clamped negative native activation counter ({botSpawner._inSpawnProcess} -> 0)");
				botSpawner._inSpawnProcess = 0;
			}
			if (BotCreationData.ProfilesLoadingProcess > 0 || botSpawner._botCreator.BotsLoading > 0 || botSpawner._botCreator.BundlesLoading > 0 || botSpawner.SpawnDelaysService.WaitCount > 0)
			{
				_pipelineActuallyIdleSince = -1f;
				return true;
			}
			if (botSpawner._inSpawnProcess > 0)
			{
				float realtimeSinceStartup = Time.realtimeSinceStartup;
				if (_pipelineActuallyIdleSince < 0f)
				{
					_pipelineActuallyIdleSince = realtimeSinceStartup;
					return true;
				}
				if (realtimeSinceStartup - _pipelineActuallyIdleSince < 10f)
				{
					return true;
				}
				int inSpawnProcess = botSpawner._inSpawnProcess;
				botSpawner._inSpawnProcess = 0;
				_pipelineActuallyIdleSince = -1f;
				Plugin.Log.LogWarning($"[SpawnGate] repaired stale native activation counter ({inSpawnProcess} orphaned placement(s); all real loaders idle)");
			}
			else
			{
				_pipelineActuallyIdleSince = -1f;
			}
			return false;
		}
		catch
		{
			return true;
		}
	}

	private static bool IsQueuedBoss(BossLocationSpawn wave)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		if (wave != null)
		{
			return ValueEnumerableExtensions.Any<FromList<QueuedBoss>, QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(_bosses), (Func<QueuedBoss, bool>)((QueuedBoss queued) => queued.Source == wave || queued.Wave == wave));
		}
		return false;
	}

	private static bool IsOpeningBatchReleased(BossLocationSpawn wave)
	{
		if (wave != null)
		{
			return _openingBatchReleasedSources.Contains(wave);
		}
		return false;
	}

	private static OpeningBatchRowKind GetOpeningBatchRowKind(BossLocationSpawn source)
	{
		if (source == null || !source.IsStartWave() || !string.IsNullOrEmpty(source.TriggerId))
		{
			return OpeningBatchRowKind.None;
		}
		if (source.BossZone == "Zone1VSRF1Spawn3" && TerminalPopulationDirector.IsRuaf(source.BossType))
		{
			return OpeningBatchRowKind.Ruaf;
		}
		if (source.BossZone == "Zone1ScavEnterStorm4" && source.BossType == WildSpawnType.assault)
		{
			return OpeningBatchRowKind.Storm4Scav;
		}
		if (source.BossZone == "Zone1ScavEnterStorm7" && source.BossType == WildSpawnType.assault)
		{
			return OpeningBatchRowKind.Storm7Scav;
		}
		return OpeningBatchRowKind.None;
	}

	private static string OpeningBatchRowLabel(OpeningBatchRowKind kind)
	{
		return kind switch
		{
			OpeningBatchRowKind.Storm4Scav => "Storm4 scav", 
			OpeningBatchRowKind.Ruaf => "RUAF", 
			_ => "Storm7 scav", 
		};
	}

	private static bool ProfilesLoadedForOpening()
	{
		try
		{
			return ((Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance.BotsController : null)?.BotSpawner)?.IsProfilesLoaded ?? true;
		}
		catch
		{
			return true;
		}
	}

	private static void BeginOpeningBatchWindow()
	{
		if (!_openingBatchResolved && !(_openingBatchFirstSeenAt >= 0f))
		{
			_openingBatchFirstSeenAt = Time.realtimeSinceStartup;
			Plugin.Log.LogInfo($"[SpawnGate] opening batch window started — waiting up to {10f:0}s for RUAF + Storm4 scav + Storm7 scav");
		}
	}

	private static QueuedBoss FindOpeningBatchRow(OpeningBatchRowKind kind)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		return ValueEnumerableExtensions.FirstOrDefault<FromList<QueuedBoss>, QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(_bosses), (Func<QueuedBoss, bool>)((QueuedBoss queued) => queued != null && GetOpeningBatchRowKind(queued.Source) == kind));
	}

	private static void LogOpeningBatchRow(string phase, QueuedBoss queued)
	{
		if (queued != null)
		{
			BossLocationSpawn bossLocationSpawn = queued.Source ?? queued.Wave;
			OpeningBatchRowKind openingBatchRowKind = GetOpeningBatchRowKind(bossLocationSpawn);
			Plugin.Log.LogInfo($"[SpawnGate] opening row {phase}: {OpeningBatchRowLabel(openingBatchRowKind)} zone='{bossLocationSpawn?.BossZone}' role={bossLocationSpawn?.BossType} sourceTime={bossLocationSpawn?.Time:0.0} squad={TerminalWaveRoster.Count(queued.Wave)}");
		}
	}

	private static void MarkOpeningBatchReleased(BossLocationSpawn source)
	{
		if (GetOpeningBatchRowKind(source) != OpeningBatchRowKind.None)
		{
			_openingBatchReleasedSources.Add(source);
		}
	}

	private static void MarkOpeningBatchReleased(QueuedBoss queued)
	{
		MarkOpeningBatchReleased(queued?.Source ?? queued?.Wave);
	}

	private static bool CanAdmitOpeningBatch(int scavDemand, int totalDemand, int specialBypassDemand, out string reason)
	{
		reason = string.Empty;
		if (BotsDisabled)
		{
			reason = "botless diagnostic";
			return false;
		}
		if (!LifetimeBudgetOpen)
		{
			reason = "lifetime budget";
			return false;
		}
		if (LifetimeCapEnabled && _placementsCommittedThisRaid + totalDemand > Plugin.MaxBotsCreatedPerRaid.Value)
		{
			reason = $"lifetime headroom {_placementsCommittedThisRaid}/{Plugin.MaxBotsCreatedPerRaid.Value}";
			return false;
		}
		if (!CanAdmitScavs(scavDemand))
		{
			reason = "scav/resident headroom or pending scav admission";
			return false;
		}
		if (!EvaluateTotalAdmission(Math.Max(0, totalDemand - specialBypassDemand), ordinaryScav: false, authoredIgnoreMaxBots: false).Allowed)
		{
			reason = "total-AI headroom";
			return false;
		}
		return true;
	}

	private static void LogOpeningBatchFallback(QueuedBoss[] rows, float waited, string reason)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		List<string> list = new List<string>();
		if (ValueEnumerableExtensions.All<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(rows), (Func<QueuedBoss, bool>)((QueuedBoss row) => GetOpeningBatchRowKind(row.Source) != OpeningBatchRowKind.Ruaf)))
		{
			list.Add("RUAF");
		}
		if (ValueEnumerableExtensions.All<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(rows), (Func<QueuedBoss, bool>)((QueuedBoss row) => GetOpeningBatchRowKind(row.Source) != OpeningBatchRowKind.Storm4Scav)))
		{
			list.Add("Storm4 scav");
		}
		if (ValueEnumerableExtensions.All<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(rows), (Func<QueuedBoss, bool>)((QueuedBoss row) => GetOpeningBatchRowKind(row.Source) != OpeningBatchRowKind.Storm7Scav)))
		{
			list.Add("Storm7 scav");
		}
		string arg = ((list.Count > 0) ? ("missing=" + string.Join(",", list)) : ("blocked=" + reason));
		Plugin.Log.LogWarning($"[SpawnGate] opening batch fallback after {waited:0.0}s — normal queue priority restored ({arg})");
	}

	private static bool TryDrainOpeningBatch()
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Unknown result type (might be due to invalid IL or missing references)
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		if (_openingBatchResolved)
		{
			return false;
		}
		if (_openingBatchFirstSeenAt < 0f)
		{
			BeginOpeningBatchWindow();
		}
		if (!ProfilesLoadedForOpening())
		{
			return true;
		}
		float num = Mathf.Max(0f, Time.realtimeSinceStartup - _openingBatchFirstSeenAt);
		QueuedBoss queuedBoss = FindOpeningBatchRow(OpeningBatchRowKind.Ruaf);
		QueuedBoss queuedBoss2 = FindOpeningBatchRow(OpeningBatchRowKind.Storm4Scav);
		QueuedBoss queuedBoss3 = FindOpeningBatchRow(OpeningBatchRowKind.Storm7Scav);
		QueuedBoss[] array = ValueEnumerableExtensions.ToArray<QueuedBoss>(ValueEnumerableExtensions.Where<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(new QueuedBoss[3] { queuedBoss, queuedBoss2, queuedBoss3 }), (Func<QueuedBoss, bool>)((QueuedBoss row) => row != null)));
		if (array.Length != 3)
		{
			if (num < 10f)
			{
				return true;
			}
			_openingBatchResolved = true;
			LogOpeningBatchFallback(array, num, "one or more authored opening rows did not arrive");
			return false;
		}
		int num2 = ValueEnumerableExtensions.Sum<FromArray<QueuedBoss>, QueuedBoss, int>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(array), (Func<QueuedBoss, int>)((QueuedBoss row) => TerminalWaveRoster.Count(row.Wave)));
		int num3 = ValueEnumerableExtensions.Sum<ArrayWhere<QueuedBoss>, QueuedBoss, int>(ValueEnumerableExtensions.Where<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(array), (Func<QueuedBoss, bool>)((QueuedBoss row) => GetOpeningBatchRowKind(row.Source) != OpeningBatchRowKind.Ruaf)), (Func<QueuedBoss, int>)((QueuedBoss row) => TerminalWaveRoster.Count(row.Wave)));
		int specialBypassDemand = ValueEnumerableExtensions.Sum<ArrayWhere<QueuedBoss>, QueuedBoss, int>(ValueEnumerableExtensions.Where<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(array), (Func<QueuedBoss, bool>)((QueuedBoss row) => IsAuthoredSpecialCapBypass(row.Source, row.Wave))), (Func<QueuedBoss, int>)((QueuedBoss row) => TerminalWaveRoster.Count(row.Wave)));
		if (!CanAdmitOpeningBatch(num3, num2, specialBypassDemand, out var reason))
		{
			if (!_openingBatchHoldLogged)
			{
				_openingBatchHoldLogged = true;
				Plugin.Log.LogInfo($"[SpawnGate] opening batch waiting for admission: totalSquad={num2}, scavSquad={num3}, reason={reason}");
			}
			if (num < 10f)
			{
				return true;
			}
			_openingBatchResolved = true;
			LogOpeningBatchFallback(array, num, reason);
			return false;
		}
		_openingBatchResolved = true;
		QueuedBoss[] array2 = array;
		foreach (QueuedBoss queuedBoss4 in array2)
		{
			_bosses.Remove(queuedBoss4);
			MarkOpeningBatchReleased(queuedBoss4);
		}
		ReserveTotalAdmissions(num2);
		ReserveScavAdmissions(num3);
		NoteFreshProfileAdmission();
		Plugin.Log.LogInfo($"[SpawnGate] opening batch released waited={num:0.0}s rows={array.Length} totalSquad={num2} scavSquad={num3}");
		array2 = array;
		foreach (QueuedBoss queued in array2)
		{
			LogOpeningBatchRow("released", queued);
		}
		_releasingQueuedWave = true;
		try
		{
			array2 = array;
			foreach (QueuedBoss queuedBoss5 in array2)
			{
				LogAuthoredRowReleased(queuedBoss5.Source, queuedBoss5.Wave);
				if (IsAuthoredSpecialCapBypass(queuedBoss5.Source, queuedBoss5.Wave))
				{
					LogAuthoredRowCapBypass(queuedBoss5.Source, queuedBoss5.Wave);
				}
				try
				{
					using (TerminalTickProfiler.Measure("SpawnGate.ActivateWave.Opening"))
					{
						queuedBoss5.Controller.ActivateBotsByWave(queuedBoss5.Wave);
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[SpawnGate] opening row release failed for '" + queuedBoss5.Wave.BossZone + "': " + ex.Message);
				}
			}
		}
		finally
		{
			_releasingQueuedWave = false;
		}
		return true;
	}

	private static void QueueBoss(BotsController controller, BossLocationSpawn wave, BossLocationSpawn source)
	{
		if (wave == null)
		{
			return;
		}
		if (TerminalStageWaveBlock.IsBlocked(source) || TerminalStageWaveBlock.IsBlocked(wave))
		{
			Plugin.Log.LogDebug("[SpawnGate] dropped queued boss wave from blocked source zone '" + (source ?? wave).BossZone + "'");
			return;
		}
		if (IsQueuedBoss(source))
		{
			_duplicateBossCallbacks++;
			return;
		}
		QueuedBoss queuedBoss = new QueuedBoss
		{
			Controller = controller,
			Wave = wave,
			Source = source,
			QueuedAt = Time.realtimeSinceStartup
		};
		_bosses.Add(queuedBoss);
		if (GetOpeningBatchRowKind(source) != OpeningBatchRowKind.None)
		{
			LogOpeningBatchRow("queued", queuedBoss);
		}
		LogAuthoredRowQueued(source, wave);
	}

	private static bool IsAuthoredSpecialRow(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		if (bossLocationSpawn != null)
		{
			return !IsOrdinaryScavWave(bossLocationSpawn);
		}
		return false;
	}

	private static void PruneBlockedBosses()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		QueuedBoss[] array = ValueEnumerableExtensions.ToArray<QueuedBoss>(ValueEnumerableExtensions.Where<QueuedBoss>(ValueEnumerable.AsValueEnumerable<QueuedBoss>(_bosses), (Func<QueuedBoss, bool>)((QueuedBoss queued) => queued != null && (TerminalStageWaveBlock.IsBlocked(queued.Source) || TerminalStageWaveBlock.IsBlocked(queued.Wave)))));
		if (array.Length != 0)
		{
			QueuedBoss[] array2 = array;
			foreach (QueuedBoss item in array2)
			{
				_bosses.Remove(item);
			}
			Plugin.Log.LogInfo($"[SpawnGate] pruned {array.Length} queued boss wave(s) retired by native stage block");
		}
	}

	private static string AuthoredRowDetails(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		return $"zone='{bossLocationSpawn?.BossZone}' trigger='{bossLocationSpawn?.TriggerId}' role={bossLocationSpawn?.BossType} sourceTime={bossLocationSpawn?.Time:0.0} squad={TerminalWaveRoster.Count(bossLocationSpawn ?? wave)} ignoreMaxBots={bossLocationSpawn?.IgnoreMaxBots}";
	}

	private static void LogAuthoredRowQueued(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		if (IsAuthoredSpecialRow(source, wave) && bossLocationSpawn != null && _authoredRowsQueuedLogged.Add(bossLocationSpawn))
		{
			Plugin.Log.LogInfo("[SpawnGate] authored special row queued " + AuthoredRowDetails(source, wave));
		}
	}

	private static void LogAuthoredRowReleased(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		if (IsAuthoredSpecialRow(source, wave) && bossLocationSpawn != null && _authoredRowsReleasedLogged.Add(bossLocationSpawn))
		{
			Plugin.Log.LogInfo("[SpawnGate] authored special row released " + AuthoredRowDetails(source, wave));
		}
	}

	private static void LogAuthoredRowCapBypass(BossLocationSpawn source, BossLocationSpawn wave)
	{
		BossLocationSpawn bossLocationSpawn = source ?? wave;
		if (IsAuthoredSpecialRow(source, wave) && bossLocationSpawn != null && _authoredRowsBypassLogged.Add(bossLocationSpawn))
		{
			Plugin.Log.LogInfo("[SpawnGate] authored special row capbypass " + AuthoredRowDetails(source, wave));
		}
	}

	private static void LogHold(string what)
	{
		if (!_holdLogged)
		{
			_holdLogged = true;
			Plugin.Log.LogInfo("[SpawnGate] holding bot spawns until the attack cutscene ends (first held: " + what + ")");
		}
	}
}
