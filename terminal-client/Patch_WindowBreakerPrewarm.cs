using System;
using EFT.Interactive;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(WindowBreakerManager), "GenerateStuckPieces")]
internal static class Patch_WindowBreakerPrewarm
{
	private static Exception Finalizer(Exception __exception, WindowBreakerManager __instance)
	{
		if (__exception == null)
		{
			return null;
		}
		bool flag;
		try
		{
			flag = __instance != null && __instance.gameObject.scene.name != null && __instance.gameObject.scene.name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			flag = false;
		}
		if (!flag && !TerminalGate.On)
		{
			return __exception;
		}
		Plugin.Log.LogWarning("[RaidFix] swallowed WindowBreakerManager.method_0: " + __exception.Message);
		return null;
	}
}
