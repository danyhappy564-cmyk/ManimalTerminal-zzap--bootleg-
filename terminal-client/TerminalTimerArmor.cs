using System;
using EFT.UI.BattleTimer;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalTimerArmor
{
	[HarmonyPatch(typeof(TimerPanel), "SetTimerText")]
	internal static class Patch_TimerTextArmor
	{
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception != null && !_reported)
			{
				_reported = true;
				Plugin.Log.LogWarning("[TimerArmor] SetTimerText threw (suppressed from here on): " + __exception.Message);
			}
			return null;
		}
	}

	private static bool _reported;
}
