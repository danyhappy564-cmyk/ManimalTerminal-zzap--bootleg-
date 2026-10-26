using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalHoldLock
{
	[HarmonyPatch(typeof(Player), "Move")]
	internal static class Patch_FreezeDuringHold
	{
		[HarmonyPrefix]
		private static void Prefix(Player __instance, ref Vector2 direction)
		{
			if (_holds > 0 && __instance != null && __instance.IsYourPlayer)
			{
				direction = Vector2.zero;
			}
		}
	}

	private static int _holds;

	internal static bool Active => _holds > 0;

	internal static void Acquire()
	{
		_holds++;
	}

	internal static void Release()
	{
		_holds--;
		if (_holds < 0)
		{
			_holds = 0;
		}
	}

	internal static void ResetForRaid()
	{
		_holds = 0;
	}
}
