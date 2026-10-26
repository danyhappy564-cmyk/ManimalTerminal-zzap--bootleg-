using System;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(AIStationaryController), "Init")]
internal static class Patch_StationaryInit
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
		Plugin.Log.LogWarning("[RaidFix] swallowed AIStationaryController.Init: " + __exception.Message);
		return null;
	}
}
