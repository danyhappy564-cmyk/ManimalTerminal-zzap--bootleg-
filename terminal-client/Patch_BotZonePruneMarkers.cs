using System.Collections.Generic;
using EFT.Game.Spawning;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(BotZone), "Init")]
internal static class Patch_BotZonePruneMarkers
{
	private static void Prefix(BotZone __instance)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		if (__instance.PatrolWays == null)
		{
			__instance.PatrolWays = new PatrolWay[0];
		}
		List<SpawnPointMarker> spawnPointMarkers = __instance.SpawnPointMarkers;
		if (spawnPointMarkers == null)
		{
			__instance.SpawnPointMarkers = new List<SpawnPointMarker>();
			return;
		}
		int num = spawnPointMarkers.RemoveAll((SpawnPointMarker m) =>
		{
			if (m == null)
			{
				return true;
			}
			try
			{
				return m.SpawnPoint == null;
			}
			catch
			{
				return true;
			}
		});
		if (num > 0)
		{
			Plugin.Log.LogDebug($"[RaidFix] pruned {num} dead spawn markers from BotZone '{__instance.name}' ({spawnPointMarkers.Count} left)");
		}
	}
}
