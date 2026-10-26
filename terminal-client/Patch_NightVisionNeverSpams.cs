using System;
using BSG.CameraEffects;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(NightVision), "OnPreCull")]
internal static class Patch_NightVisionNeverSpams
{
	private static bool _logged;

	private static Exception Finalizer(Exception __exception, NightVision __instance)
	{
		if (__exception != null && !TerminalGate.On)
		{
			return __exception;
		}
		if (__exception != null && __instance != null)
		{
			__instance.enabled = false;
			try
			{
				if (__instance.TextureMask != null)
				{
					__instance.TextureMask.Mask = null;
					__instance.TextureMask.enabled = false;
				}
			}
			catch
			{
			}
			if (!_logged)
			{
				_logged = true;
				Plugin.Log.LogWarning($"[RaidFix] NightVision.OnPreCull threw — component disabled, mask cleared. FULL STACK (once): {__exception}");
			}
		}
		return null;
	}
}
