using System;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(RainController), "CameraChanged")]
internal static class Patch_RainScreenOnCam2
{
	private static Exception Finalizer(RainController __instance, Exception __exception)
	{
		if (!TerminalGate.On)
		{
			return __exception;
		}
		RainRehookHost.Observe(__instance, __exception);
		return null;
	}
}
