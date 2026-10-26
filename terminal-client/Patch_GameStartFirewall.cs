using System;
using EFT;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
internal static class Patch_GameStartFirewall
{
	private static void Prefix()
	{
		RaidFirewall.WrapForeignPostfixes(AccessTools.Method(typeof(GameWorld), "OnGameStarted"));
	}

	private static Exception Finalizer(Exception __exception)
	{
		return RaidFirewall.Swallow(__exception, "GameWorld.OnGameStarted");
	}
}
