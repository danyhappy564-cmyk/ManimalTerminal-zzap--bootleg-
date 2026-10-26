using System;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class LateWaypointsPatch
{
	private static bool _done;

	internal static void Apply()
	{
		if (_done)
		{
			return;
		}
		_done = true;
		Type type = TerminalTypes.ByName("DrakiaXYZ.Waypoints.Patches.DoorLinkPatch");
		if (!(type == null))
		{
			MethodInfo methodInfo = AccessTools.Method(type, "PatchPostfix");
			if (methodInfo == null)
			{
				Plugin.Log.LogWarning("[RaidFix] Waypoints DoorLinkPatch found but PatchPostfix missing — layout changed?");
				return;
			}
			new Harmony("com.manimal.terminal.raidfix-late").Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(LateWaypointsPatch), "SwallowFinalizer"), (HarmonyMethod)null);
			Plugin.Log.LogDebug("[RaidFix] late-patched Waypoints DoorLinkPatch with finalizer");
		}
	}

	private static Exception SwallowFinalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (!TerminalGate.On)
		{
			return __exception;
		}
		Plugin.Log.LogWarning("[RaidFix] swallowed Waypoints DoorLinkPatch: " + __exception.Message);
		return null;
	}
}
