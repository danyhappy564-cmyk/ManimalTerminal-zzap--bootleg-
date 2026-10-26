using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalStageDirector
{
	private sealed class Migration
	{
		internal int Stage;

		internal string From;

		internal string To;
	}

	internal sealed class Patch_Arm : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(BotsController), "Init");
		}

		[PatchPostfix]
		private static void Postfix(BotZone[] botZones)
		{
			if (!TerminalGate.On || !TerminalCoop.Authority || !Plugin.StageDirector.Value)
			{
				return;
			}
			ResetForRaid();
			Host host = (_host = new GameObject("Terminal_StageDirector").AddComponent<Host>());
			try
			{
				host.BeginListening(botZones);
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[StageDirector] authoring could not be loaded: {arg}");
				ResetForRaid();
			}
		}
	}

	internal sealed class Host : MonoBehaviour
	{
		private GlobalEventDispatcher _dispatcher;

		private BotZone[] _botZones = Array.Empty<BotZone>();

		private bool _listening;

		private int _currentStage;

		private int _retainedCount;

		private JObject _spec;

		private readonly Dictionary<string, int> _blockedZones = new Dictionary<string, int>(StringComparer.Ordinal);

		private readonly Dictionary<int, HashSet<BotZone>> _stageZones = new Dictionary<int, HashSet<BotZone>>();

		private Migration[] _migrations = Array.Empty<Migration>();

		private float _nextMigration = float.PositiveInfinity;

		private IEnumerator _migrationPass;

		private int _migrationRejectionLogs;

		internal bool ShouldHold(BotOwner bot, bool cutscene)
		{
			if (!bot)
			{
				return false;
			}
			BotZone spawnBotZone = bot.SpawnBotZone;
			int authoredBlockStage = (((bool)spawnBotZone && _blockedZones.TryGetValue(spawnBotZone.name, out var value)) ? value : 0);
			return TerminalStagePolicy.ShouldHold(_currentStage, authoredBlockStage, cutscene);
		}

		internal void BeginListening(BotZone[] botZones)
		{
			//IL_0138: Unknown result type (might be due to invalid IL or missing references)
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0188: Unknown result type (might be due to invalid IL or missing references)
			//IL_019a: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
			_botZones = botZones ?? Array.Empty<BotZone>();
			string directoryName = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
			_spec = JObject.Parse(File.ReadAllText(Path.Combine(directoryName, "plugin-data", "aiplaces", "terminal_stages.json")));
			if (_spec.Value<int>("schema") != 1)
			{
				throw new InvalidDataException("Unknown stage authoring schema");
			}
			foreach (JToken item in (JArray)_spec["blockOnFirstStage"])
			{
				foreach (JToken item2 in (JArray)item["zones"])
				{
					_blockedZones.Add(item2.Value<string>(), item.Value<int>("stage"));
				}
			}
			foreach (JToken item3 in (JArray)_spec["stages"])
			{
				HashSet<string> names = ValueEnumerableExtensions.ToHashSet<Select<FromEnumerable<JToken>, JToken, string>, string>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, string>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)item3["zones"]), (Func<JToken, string>)((JToken zone) => zone.Value<string>())), (IEqualityComparer<string>)StringComparer.Ordinal);
				_stageZones.Add(item3.Value<int>("stage"), ValueEnumerableExtensions.ToHashSet<ArrayWhere<BotZone>, BotZone>(ValueEnumerableExtensions.Where<BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(_botZones), (Func<BotZone, bool>)((BotZone zone) => (bool)zone && names.Contains(zone.name)))));
			}
			_migrations = ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, Migration>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, Migration>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)_spec["migrations"]), (Func<JToken, Migration>)((JToken row) =>
			{
				JArray jArray = (JArray)row["to"];
				if (jArray.Count == 0)
				{
					throw new InvalidDataException("Empty migration destination list");
				}
				return new Migration
				{
					Stage = row.Value<int>("stage"),
					From = row.Value<string>("from"),
					To = jArray[UnityEngine.Random.Range(0, jArray.Count)].Value<string>()
				};
			})));
			AttachDispatcher();
			Plugin.Log.LogInfo($"[StageDirector] loaded {_blockedZones.Count} authored stage bindings and {_migrations.Length} ordered migration rows");
		}

		internal void StopListening()
		{
			StopMigrationPass();
			TerminalBotMigration.ResetForRaid();
			DetachDispatcher();
			_botZones = Array.Empty<BotZone>();
		}

		private void OnDestroy()
		{
			StopMigrationPass();
			DetachDispatcher();
			if ((object)_host == this)
			{
				TerminalBotMigration.ResetForRaid();
				TerminalStageWait.ResetForRaid();
				_host = null;
			}
		}

		private void Update()
		{
			using (TerminalTickProfiler.Measure("StageDirector.Host.Update"))
			{
				if (!TerminalGate.On || !TerminalCoop.Authority || !Plugin.StageDirector.Value)
				{
					StopListening();
					UnityEngine.Object.Destroy(gameObject);
					return;
				}
				if (!_listening)
				{
					AttachDispatcher();
				}
				if (_migrationPass == null)
				{
					if (_currentStage <= 0 || Time.time < _nextMigration)
					{
						return;
					}
					_nextMigration = Time.time + 45f;
					_migrationPass = TryMigrateSurplus();
				}
				try
				{
					if (!_migrationPass.MoveNext())
					{
						StopMigrationPass();
					}
				}
				catch (Exception ex)
				{
					StopMigrationPass();
					Plugin.Log.LogWarning("[StageDirector] migration pass failed: " + ex.Message);
				}
			}
		}

		private void StopMigrationPass()
		{
			(_migrationPass as IDisposable)?.Dispose();
			_migrationPass = null;
		}

		private void AttachDispatcher()
		{
			try
			{
				if (!_listening && Singleton<GlobalEventDispatcher>.Instantiated)
				{
					_dispatcher = Singleton<GlobalEventDispatcher>.Instance;
					if (_dispatcher != null)
					{
						_dispatcher.OnEvent += OnEvent;
						_listening = true;
						Plugin.Log.LogDebug("[StageDirector] listening for retail stage-unlock events");
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[StageDirector] event subscription failed: " + ex.Message);
			}
		}

		private void DetachDispatcher()
		{
			if (_listening && _dispatcher != null)
			{
				try
				{
					_dispatcher.OnEvent -= OnEvent;
				}
				catch (Exception ex)
				{
					Plugin.Log.LogDebug("[StageDirector] event unsubscribe failed: " + ex.Message);
				}
				_dispatcher = null;
				_listening = false;
			}
		}

		private void OnEvent(string eventName)
		{
			try
			{
				AdvanceStage(eventName);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[StageDirector] event '" + eventName + "' failed: " + ex.Message);
			}
		}

		private void AdvanceStage(string eventName)
		{
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			if (TerminalGate.On && TerminalCoop.Authority && TerminalStagePolicy.TryGetNextStage(eventName, out var nextStage) && TerminalStagePolicy.ShouldAdvance(_currentStage, nextStage))
			{
				JToken jToken = ValueEnumerableExtensions.First<FromEnumerable<JToken>, JToken>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)_spec["stages"]), (Func<JToken, bool>)((JToken t) => t.Value<int>("stage") == nextStage));
				TerminalStagePolicy.StageSnapshot stageSnapshot = TerminalStagePolicy.Snapshot(nextStage, CountBotsAtStage(nextStage), jToken.Value<float>("remainPercent"), jToken.Value<int>("minimumCount"));
				_currentStage = nextStage;
				_retainedCount = stageSnapshot.RetainedCount;
				StopMigrationPass();
				_nextMigration = Time.time;
				Plugin.Log.LogInfo($"[StageDirector] retail event '{eventName}' advanced stage {_currentStage}; " + $"snapshotted {stageSnapshot.LiveCount} live bot(s), retaining {stageSnapshot.RetainedCount}, " + $"initial surplus {stageSnapshot.InitialSurplus} (period {45f:0}s)");
			}
		}

		private IEnumerator TryMigrateSurplus()
		{
			int migrated = 0;
			int rejected = 0;
			Migration[] migrations = _migrations;
			foreach (Migration row in migrations)
			{
				if (row.Stage > _currentStage)
				{
					continue;
				}
				TerminalStageWaveBlock.BlockZone(row.From);
				BotZone destination = ValueEnumerableExtensions.FirstOrDefault<FromArray<BotZone>, BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(_botZones), (Func<BotZone, bool>)((BotZone zone) => (bool)zone && string.Equals(zone.name, row.To, StringComparison.Ordinal)));
				if (!destination || destination.SpawnPoints == null || destination.SpawnPoints.Length == 0)
				{
					continue;
				}
				List<BotOwner> list = TerminalPopulationDirector.LivingInSpawnZone(row.From);
				foreach (BotOwner bot in list)
				{
					yield return null;
					if (!bot || bot.IsDead || !bot.SpawnBotZone || !string.Equals(bot.SpawnBotZone.NameZone, row.From, StringComparison.Ordinal))
					{
						continue;
					}
					int num = CountBotsAtStage(_currentStage);
					if (_retainedCount > 0 && num <= _retainedCount)
					{
						break;
					}
					if (!TerminalBotMigration.TryMigrateTo(bot, destination, out var reason))
					{
						rejected++;
						if (_migrationRejectionLogs++ < 32)
						{
							Plugin.Log.LogInfo("[StageDirector] migration deferred profile=" + bot.ProfileId + " source='" + row.From + "' destination='" + row.To + "' reason=" + reason);
						}
					}
					else
					{
						TerminalPopulationDirector.MarkLogicalZone(bot, destination.NameZone);
						TerminalCrewJobs.OnMigrated(bot);
						migrated++;
						Plugin.Log.LogInfo($"[StageDirector] migration completed profile={bot.ProfileId} source='{row.From}' destination='{row.To}' core={bot.StartCorePoint?.Id}");
					}
				}
			}
			if (migrated + rejected > 0)
			{
				Plugin.Log.LogInfo($"[StageDirector] stage-{_currentStage} migration pass completed: migrated={migrated}, deferred={rejected}, remaining={CountBotsAtStage(_currentStage)}, retained={_retainedCount}");
			}
		}

		private int CountBotsAtStage(int stage)
		{
			if (!_stageZones.TryGetValue(stage, out var value))
			{
				return 0;
			}
			return TerminalPopulationDirector.LivingInSpawnZones(value).Count;
		}
	}

	private const float MigrationPeriodSeconds = 45f;

	private static Host _host;

	internal static bool ShouldHold(BotOwner bot)
	{
		if (TerminalGate.On && TerminalCoop.Authority && Plugin.StageDirector.Value && (bool)_host)
		{
			return _host.ShouldHold(bot, TerminalIntroCutscene.PlayingNow || TerminalAttackCutscene.PlayingNow || TerminalEndingCutscene.PlayingNow);
		}
		return false;
	}

	internal static void ResetForRaid()
	{
		TerminalMigrationProbe.ResetForRaid();
		TerminalBotMigration.ResetForRaid();
		TerminalStageWaveBlock.ResetForRaid();
		TerminalStageWait.ResetForRaid();
		if (!(_host == null))
		{
			_host.StopListening();
			UnityEngine.Object.Destroy(_host.gameObject);
			_host = null;
		}
	}
}
