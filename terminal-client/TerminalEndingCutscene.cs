using System;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalEndingCutscene
{
	[HarmonyPatch(typeof(LocalGame), "Stop")]
	internal static class Patch_InterceptExtraction
	{
		[HarmonyPrefix]
		internal static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance, string profileId, ExitStatus exitStatus, string exitName, float delay)
		{
			try
			{
				if (PassThrough)
				{
					return true;
				}
				if (!TerminalGate.On || !Plugin.EndingCutscene.Value)
				{
					return true;
				}
				Player player = Singleton<GameWorld>.Instance?.MainPlayer;
				if (!TerminalCoop.IsHuman(player) || player.ProfileId != profileId)
				{
					return true;
				}
				if (PlayingNow)
				{
					Plugin.Log.LogWarning($"[Ending] swallowed a {exitStatus} stop during the take — the runner owns the raid end");
					return false;
				}
				if (_played)
				{
					return true;
				}
				if (exitStatus != ExitStatus.Survived || exitName != "Terminal_Zubr_Exit")
				{
					return true;
				}
				if (!Available)
				{
					Plugin.Log.LogInfo("[Ending] ending scene not in bundle — extracting without the cutscene");
					return true;
				}
				Player player2 = Singleton<GameWorld>.Instance?.MainPlayer;
				if (player2 == null || player2.ProfileId != profileId)
				{
					return true;
				}
				_played = true;
				Plugin.Log.LogInfo("[Ending] Zubr extraction — holding raid end for the ending cutscene");
				if (!TerminalCoop.Active)
				{
					TerminalSpawnGate.StopForEnding();
					TerminalPopulationDirector.DespawnAllForEnding();
				}
				EndingRunner endingRunner = new GameObject("Terminal_EndingCutscene").AddComponent<EndingRunner>();
				endingRunner.Game = __instance;
				endingRunner.ProfileId = profileId;
				endingRunner.ExitName = exitName;
				return false;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Ending] intercept failed, extracting normally: " + ex.Message);
				return true;
			}
		}
	}

	internal const string SceneName = "Terminal_area_PortCutscene_02_Ending_00";

	private static bool _played;

	internal static bool PassThrough;

	internal static bool PlayingNow;

	internal static bool Available
	{
		get
		{
			try
			{
				return Application.CanStreamedLevelBeLoaded("Terminal_area_PortCutscene_02_Ending_00");
			}
			catch
			{
				return false;
			}
		}
	}

	internal static void ResetForRaid()
	{
		_played = false;
		PassThrough = false;
		PlayingNow = false;
	}
}
