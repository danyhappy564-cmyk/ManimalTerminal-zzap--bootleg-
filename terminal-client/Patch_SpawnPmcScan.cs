using System;
using EFT.Game.Spawning;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(SpawnPointsCollection), "CalculateCanSpawnPmcBots")]
internal static class Patch_SpawnPmcScan
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
		Plugin.Log.LogWarning("[RaidFix] swallowed EFT.Game.Spawning.SpawnPointsCollection.smethod_3: " + __exception.Message);
		return null;
	}
}
