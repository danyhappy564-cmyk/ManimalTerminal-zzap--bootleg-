using System;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Manimal.Terminal.Civilian;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalGate
{
	[HarmonyPatch(typeof(LocalGame), "Create")]
	internal static class Patch_CaptureLocationId
	{
		internal static void Prefix(LocationSettings.Location location)
		{
			RaidEnded = false;
			bool flag = string.Equals(PendingLocationId, "Terminal", StringComparison.OrdinalIgnoreCase);
			PendingLocationId = location?.Id;
			TerminalCoop.Reset();
			_cachedFrame = -1;
			bool flag2 = string.Equals(PendingLocationId, "Terminal", StringComparison.OrdinalIgnoreCase);
			if (!flag2)
			{
				TerminalRenderProfiler.Disable();
				TerminalFramePhase.Disable();
				TerminalLoopProbe.Restore();
			}
			if (flag2)
			{
				TerminalAILimitFirewall.TryInstall();
				TerminalSainCompat.TryInstall();
				TerminalCrewJobs.Register();
			}
			if (flag | flag2)
			{
				TerminalExternalBotCleanup.ResetForRaid();
			}
			TerminalIntroCutscene.ResetForNewRaid();
			TerminalAttackCutscene.ResetForNewRaid();
			TerminalLights.ResetForNewRaid();
			TerminalFlares.ResetForRaid();
			TerminalCullingDriver.ResetForNewRaid();
			TerminalSoundRig.ResetForNewRaid();
			TerminalAIPlaces.ResetForNewRaid();
			TerminalGatesExplosion.ResetForRaid();
			TerminalCraneFalling.ResetForRaid();
			TerminalFinalExit.ResetForRaid();
			TerminalArtillery.ResetForRaid();
			TerminalPumpStation.ResetForRaid();
			TerminalEndingCutscene.ResetForRaid();
			TerminalEpilogueScreen.ResetForRaid();
			TerminalWater.ResetForRaid();
			TerminalLootBind.ResetForRaid();
			TerminalDryPlanes.ResetForRaid();
			TerminalShoreAudio.ResetForRaid();
			TerminalRainAudio.ResetForRaid();
			TerminalMissionWeather.ResetForRaid();
			TerminalWeatherLifecycle.ResetForRaid();
			TerminalWeather.ResetForRaid();
			TerminalHoldLock.ResetForRaid();
			TerminalPerfWatch.ResetForRaid();
			TerminalShadowGuard.ResetForRaid();
			TerminalSceneScrub.ResetForRaid();
			CivilianFleeState.ClearAll();
			CivilianUnstickHelper.ClearAll();
			CivilianMeleeEnforcer.ClearAll();
			Civilian.Patches.CivilianMotionMatchingPatch.ClearAll();
			TerminalCrewJobs.Reset();
			TerminalShaderRebind.ResetForRaid();
			TerminalRigFill.ResetForRaid();
			TerminalRuafNeutral.ResetForRaid();
			TerminalSpawnGate.ResetForRaid();
			TerminalPopulationDirector.ResetForRaid();
			TerminalStageDirector.ResetForRaid();
			if (PendingLocationId == "Terminal")
			{
				TerminalArtillery.InjectConfig();
				TerminalBossRoll.Roll(location);
			}
			Plugin.Log.LogInfo("[TerminalGate] raid location: '" + (PendingLocationId ?? "<null>") + "'");
		}
	}

	internal const string LocationId = "Terminal";

	internal static string PendingLocationId;
	internal static bool RaidEnded;

	internal static void MarkRaidEnded()
	{
		RaidEnded = true;
		_cachedFrame = -1;
		_cachedOn = false;
	}

	private static int _cachedFrame = -1;

	private static bool _cachedOn;

	internal static bool On
	{
		get
		{
			if (RaidEnded) return false;
			int num;
			try
			{
				num = Time.frameCount;
			}
			catch
			{
				num = -1;
			}
			if (num >= 0 && num == _cachedFrame)
			{
				return _cachedOn;
			}
			bool flag = false;
			try
			{
				GameWorld instance = Singleton<GameWorld>.Instance;
				flag = ((!(instance != null) || string.IsNullOrEmpty(instance.LocationId)) ? (!string.IsNullOrEmpty(PendingLocationId) && string.Equals(PendingLocationId, "Terminal", StringComparison.OrdinalIgnoreCase)) : string.Equals(instance.LocationId, "Terminal", StringComparison.OrdinalIgnoreCase));
			}
			catch
			{
				flag = !string.IsNullOrEmpty(PendingLocationId) && string.Equals(PendingLocationId, "Terminal", StringComparison.OrdinalIgnoreCase);
			}
			_cachedFrame = num;
			_cachedOn = flag;
			return flag;
		}
	}
}
