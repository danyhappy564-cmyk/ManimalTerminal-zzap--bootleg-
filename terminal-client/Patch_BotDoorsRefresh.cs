using System;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(BotDoorsController), "RefreshData")]
internal static class Patch_BotDoorsRefresh
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
		Plugin.Log.LogWarning("[RaidFix] swallowed BotDoorsController.RefreshData: " + __exception.Message);
		return null;
	}
}
