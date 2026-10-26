using System;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(EffectsController), "Init")]
internal static class Patch_EffectsControllerInit
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
		Plugin.Log.LogWarning("[RaidFix] swallowed EffectsController.method_3: " + __exception.Message + "\n" + __exception.StackTrace);
		return null;
	}
}
