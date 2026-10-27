using System;
using EFT;
using HarmonyLib;
using Manimal.Terminal.Civilian;
using UnityEngine;

namespace Manimal.Terminal;

// Release raid roots before menu UnloadUnusedAssets, including failed Dispose calls.
internal static class TerminalRaidCleanup
{
	[HarmonyPatch(typeof(GameWorld), "Dispose")]
	internal static class Patch_EndRaid
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix(GameWorld __instance, out bool __state)
		{
			__state = !TerminalGate.RaidEnded && (string.Equals(__instance.LocationId, TerminalGate.LocationId,
				StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(__instance.LocationId) && TerminalGate.On));
		}

		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state && !TerminalGate.RaidEnded) ReleaseAfterRaid();
			return __exception;
		}
	}

	private static void ReleaseAfterRaid()
	{
		// Deferred destruction must not let a surviving host recreate raid caches.
		TerminalGate.MarkRaidEnded();
		int clips = 0, ambient = 0, bots = 0, failed = 0;
		if (!Release("ghost", TerminalGhostDirector.ResetForRaid)) failed++;
		if (!Release("population", () => bots = TerminalPopulationDirector.ReleaseAfterRaid())) failed++;
		if (!Release("stager", TerminalStageDirector.ReleaseAfterRaid)) failed++;
		if (!Release("shore loader", TerminalShoreAudio.ReleaseAfterRaid)) failed++;
		if (!Release("acoustics", () => TerminalAcoustics.ReleaseAfterRaid(out clips, out ambient))) failed++;
		if (!Release("ambient effects", () => TerminalAmbientEffectsAssets.Reset(immediate: false))) failed++;
		if (!Release("ambient seasons", () => TerminalAmbientSeasonAssets.ResetForRaid(immediate: false))) failed++;
		Release("civilians", () => { CivilianFleeState.ClearAll(); CivilianUnstickHelper.ClearAll(); CivilianMeleeEnforcer.ClearAll(); CivilianTeleportProbe.ResetForRaid(); });
		Release("crew", TerminalCrewJobs.Reset);
		Release("RUAF neutrality", TerminalRuafNeutral.ResetForRaid);
		Release("spawn gate", TerminalSpawnGate.ResetForRaid);
		Release("sound rig", TerminalSoundRig.ResetForNewRaid);
		Release("rain", TerminalRainAudio.ReleaseAfterRaid);
		Release("weather", TerminalWeather.ResetForRaid);
		Release("mission weather", TerminalMissionWeather.ReleaseAfterRaid);
		Release("weather lifecycle", TerminalWeatherLifecycle.ResetForRaid);
		Release("water", TerminalWater.ReleaseAfterRaid);
		Release("culling", TerminalCullingDriver.ReleaseAfterRaid);
		Release("LOD", TerminalLodCullFloor.ReleaseAfterRaid);
		Release("loot bounds", TerminalLootBounds.ReleaseAfterRaid);
		Release("lights", TerminalLights.ResetForNewRaid);
		Release("shadows", TerminalShadowGuard.ResetForRaid);
		Release("performance", TerminalPerfWatch.ResetForRaid);
		Release("render profiler", TerminalRenderProfiler.Disable);
		Release("frame profiler", TerminalFramePhase.Disable);
		Release("player loop", TerminalLoopProbe.Restore);
		Release("AI bake", TerminalAIBake.ReleaseAfterRaid);
		Release("AI places", TerminalAIPlaces.ResetForNewRaid);
		Release("rig", TerminalRigFill.ResetForRaid);
		Release("path diagnostics", TerminalPathDiagnostics.ResetForRaid);
		Release("artillery", TerminalArtillery.ResetForRaid);
		Release("gates", TerminalGatesExplosion.ResetForRaid);
		Release("pump", TerminalPumpStation.ResetForRaid);
		Release("exit", TerminalFinalExit.ResetForRaid);
		Release("SAIN location", TerminalSainCompat.ReleaseAfterRaid);
		Release("grass", TerminalGrass.ReleaseAfterRaid);
		Release("vegetation", TerminalVegetation.ReleaseAfterRaid);
		Release("trigger visuals", TerminalTriggerViz.ReleaseAfterRaid);
		Release("subtitles", TerminalSubtitles.ReleaseAfterRaid);
		Release("camera", TerminalCameraDonor.ReleaseAfterRaid);
		Release("audio routing", TerminalAudioRouting.ReleaseAfterRaid);
		Release("FX clips", TerminalFxBundle.ReleaseAfterRaid);
		Release("human registry", TerminalCoop.ReleaseAfterRaid);
		// Epilogue.Pending belongs to the result screen; preserve it after extraction.
		Plugin.Log.LogInfo($"[Cleanup] released after raid: clips {clips}, ambient {ambient}, bots {bots}, primary failures {failed}");
	}

	private static bool Release(string name, Action cleanup)
	{
		try { cleanup(); return true; }
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Cleanup] " + name + " release skipped: " + ex.Message);
			return false;
		}
	}

	internal static void DestroyOwned(UnityEngine.Object asset)
	{
		try { if (asset) UnityEngine.Object.Destroy(asset); }
		catch (Exception) { } // scene teardown may already have destroyed it
	}
}
