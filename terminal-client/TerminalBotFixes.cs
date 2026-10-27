using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalBotFixes
{
	[HarmonyPatch(typeof(AICoversData), "CachePoints")]
	internal static class Patch_CoversCache
	{
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception == null)
			{
				return null;
			}
			if (!TerminalGate.On)
			{
				return __exception;
			}
			Plugin.Log.LogWarning("[BotFix] swallowed AICoversData.CachePoints: " + __exception.Message);
			return null;
		}
	}

	[HarmonyPatch(typeof(BotDoorsController), "RefreshData")]
	internal static class Patch_BotDoorsRefresh
	{
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception == null)
			{
				return null;
			}
			if (!TerminalGate.On)
			{
				return __exception;
			}
			Plugin.Log.LogWarning("[BotFix] swallowed BotDoorsController.RefreshData: " + __exception.Message);
			return null;
		}
	}

	[HarmonyPatch(typeof(GlobalEventDispatcher), "PlaySound")]
	internal static class Patch_PlaySoundAirbag
	{
		private static float _lastLog;

		private static Exception Finalizer(Exception __exception)
		{
			if (__exception == null)
			{
				return null;
			}
			if (!TerminalGate.On)
			{
				return __exception;
			}
			if (Time.unscaledTime - _lastLog > 30f)
			{
				_lastLog = Time.unscaledTime;
				Plugin.Log.LogWarning("[BotFix] swallowed a bot-hearing exception (dangling despawned-bot ref) — player actions protected. inner: " + __exception.Message);
			}
			return null;
		}
	}

	[HarmonyPatch(typeof(WorldInteractiveObject), "PushTriggers")]
	internal static class Patch_DoorTriggerEmit
	{
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception != null && !TerminalGate.On)
			{
				return __exception;
			}
			return null;
		}
	}

	[HarmonyPatch(typeof(BotOwner), "method_10")]
	internal static class Patch_BotActivationStepwise
	{
		private static readonly MethodInfo _m2 = AccessTools.Method(typeof(BotOwner), "AfterActivationSubscribe");

		private static readonly MethodInfo _m11 = AccessTools.Method(typeof(BotOwner), "method_11");

		private static readonly FieldInfo _activateTime = AccessTools.Field(typeof(BotOwner), "_activateTime");

		private static bool Prefix(BotOwner __instance)
		{
			if (!TerminalGate.On)
			{
				return true;
			}
			BotOwner b = __instance;
			using var activationScope = TerminalTickProfiler.Measure("BotFix.Activation");
			int failed = 0;
			Step("VoxelesPersonalData", () =>
			{
				b.VoxelesPersonalData.Activate(b.BotsGroup.BotGame.BotsController.CoversData);
			});
			Step("LookSensor", () =>
			{
				b.LookSensor.Activate();
			});
			Step("Settings", () =>
			{
				b.Settings.Activate();
			});
			Step("ExternalItemsController", () =>
			{
				b.ExternalItemsController.Activate();
			});
			Step("ItemTaker", () =>
			{
				b.ItemTaker.Activate();
			});
			Step("BewarePlantedMine", () =>
			{
				b.BewarePlantedMine.Activate();
			});
			Step("EnemyChooser", () =>
			{
				b.EnemyChooser.Activate();
			});
			Step("PlanDropItem", () =>
			{
				b.PlanDropItem.Activate();
			});
			Step("MinesData", () =>
			{
				b.MinesData.Activate();
			});
			Step("ItemDropper", () =>
			{
				b.ItemDropper.Activate();
			});
			Step("SuppressStationary", () =>
			{
				b.SuppressStationary.Activate();
			});
			Step("NavMeshCutterController", () =>
			{
				b.NavMeshCutterController.Activate();
			});
			Step("BotFollower", () =>
			{
				b.BotFollower.Activate();
			});
			Step("FriendlyTilt", () =>
			{
				b.FriendlyTilt.Activate();
			});
			Step("RandomPlanItemDropper", () =>
			{
				b.RandomPlanItemDropper.Activate();
			});
			Step("Tactic", () =>
			{
				b.Tactic.Activate();
			});
			Step("EnemiesController", () =>
			{
				b.EnemiesController.Activate(b.BotsGroup.BotGame.BotsController.OnlineDependenceSettings.CanPersueAxeman);
			});
			Step("HearingSensor", () =>
			{
				b.HearingSensor.Init();
			});
			Step("LeaveData", () =>
			{
				b.LeaveData.Activate(b.BotsGroup.BotZone.Modifier.LeaveDist);
			});
			Step("Receiver", () =>
			{
				b.Receiver.Init();
			});
			Step("Mover", () =>
			{
				b.Mover.Activate();
			});
			Step("BotTalk", () =>
			{
				b.BotTalk.Activate();
			});
			Step("LoyaltyData", () =>
			{
				b.LoyaltyData.Activate();
			});
			Step("AssaultDangerArea", () =>
			{
				b.AssaultDangerArea.Activate();
			});
			Step("DangerArea", () =>
			{
				b.DangerArea.Activate();
			});
			Step("BotPersonalStats", () =>
			{
				b.BotPersonalStats.Init(b, b.BotsGroup.BotZone.name);
			});
			Step("StandBy.InitPoints", () =>
			{
				b.StandBy.InitPoints(b.BotsGroup.BotZone.Modifier.DistToActivate, b.BotsGroup.BotZone.Modifier.DistToSleep);
			});
			Step("method_2", () =>
			{
				_m2.Invoke(b, null);
			});
			Step("FlashGrenade", () =>
			{
				b.FlashGrenade.Activate();
			});
			Step("PeaceHardAim", () =>
			{
				b.PeaceHardAim.Activate();
			});
			Step("ShootData", () =>
			{
				b.ShootData.Activate();
			});
			Step("PeaceLook", () =>
			{
				b.PeaceLook.Activate();
			});
			Step("NearDoorData", () =>
			{
				b.NearDoorData.Activate();
			});
			Step("AIData", () =>
			{
				b.AIData.Activate();
			});
			Step("UnityEditorRunChecker", () =>
			{
				b.UnityEditorRunChecker.Activate();
			});
			Step("NightVision", () =>
			{
				b.NightVision.Activate();
			});
			Step("SearchData", () =>
			{
				b.SearchData.Activate();
			});
			Step("Medecine", () =>
			{
				b.Medecine.Activate();
			});
			b.BotState = EBotState.Active;
			Step("Memory", () =>
			{
				b.Memory.Activate();
			});
			Step("SuppressShoot", () =>
			{
				b.SuppressShoot.Activate();
			});
			Step("EatDrinkData", () =>
			{
				b.EatDrinkData.Activate();
			});
			Step("SecondWeaponData", () =>
			{
				b.SecondWeaponData.Activate();
			});
			Step("BotLay", () =>
			{
				b.BotLay.Activate();
			});
			Step("SuppressGrenade", () =>
			{
				b.SuppressGrenade.Activate();
			});
			Step("method_11", () =>
			{
				_m11.Invoke(b, null);
			});
			Step("Brain", () =>
			{
				b.Brain.Activate();
			});
			Step("PatrollingData", () =>
			{
				b.PatrollingData.Activate();
			});
			Step("WeaponManager", () =>
			{
				b.WeaponManager.Activate();
			});
			Step("BotFollower.TryFindBoss", () =>
			{
				b.BotFollower.TryFindBoss();
			});
			Step("ActivateTime", () =>
			{
				_activateTime.SetValue(b, Time.time);
			});
			if (failed > 0)
			{
				Plugin.Log.LogWarning($"[BotFix] '{b.name}' activated with {failed} failed step(s) — degraded but alive");
			}
			return false;
			void Step(string name, Action a)
			{
				try
				{
					using var stepScope = TerminalTickProfiler.Measure(name);
					a();
				}
				catch (Exception arg)
				{
					failed++;
					Plugin.Log.LogError($"[BotFix] '{b.name}' activation step {name} FAILED: {arg}");
				}
			}
		}
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_PatrolSubPoints
	{
		private static readonly FieldInfo SubPointsField = AccessTools.Field(typeof(PatrolPoint), "subPoints");

		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!TerminalGate.On)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			try
			{
				BotZone[] array = UnityEngine.Object.FindObjectsOfType<BotZone>(includeInactive: true);
				foreach (BotZone botZone in array)
				{
					if (botZone.PatrolWays == null)
					{
						continue;
					}
					PatrolWay[] patrolWays = botZone.PatrolWays;
					foreach (PatrolWay patrolWay in patrolWays)
					{
						if (patrolWay == null || patrolWay.Points == null)
						{
							continue;
						}
						num++;
						foreach (PatrolPoint point in patrolWay.Points)
						{
							if (point == null)
							{
								continue;
							}
							num4 += Scrub(point);
							if (point.SubPointsCount == 0)
							{
								try
								{
									point.CreateSubPoints(patrolWay);
									num2++;
								}
								catch
								{
									num3++;
								}
								num4 += Scrub(point);
							}
							if (point.SubPointsCount == 0)
							{
								num5++;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[SubPoints] sweep failed: " + ex.Message);
			}
			Plugin.Log.LogWarning($"[SubPoints] built formation sub-points on {num2} patrol point(s) ({num} ways, {num3} failed)" + $"; scrubbed {num4} dead sub-point(s)" + ((num5 > 0) ? $", {num5} point(s) left with none (GetSubPoint returns the point itself)" : ""));
		}

		private static int Scrub(PatrolPoint p)
		{
			try
			{
				if (SubPointsField == null)
				{
					return 0;
				}
				if (!(SubPointsField.GetValue(p) is List<PatrolPoint> { Count: not 0, Count: var count } list))
				{
					return 0;
				}
				list.RemoveAll((PatrolPoint x) => x == null);
				return count - list.Count;
			}
			catch
			{
				return 0;
			}
		}
	}

	[HarmonyPatch(typeof(PatrolPoint), "GetSubPoint")]
	internal static class Patch_GetSubPointEmptyGuard
	{
		[HarmonyPrefix]
		private static bool Prefix(PatrolPoint __instance, ref PatrolPoint __result)
		{
			if (__instance.SubPointsCount > 0)
			{
				return true;
			}
			__result = __instance;
			return false;
		}
	}
}
