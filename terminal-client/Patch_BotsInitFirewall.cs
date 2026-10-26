using System;
using EFT;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(BotsController), "Init")]
internal static class Patch_BotsInitFirewall
{
	private static void Prefix()
	{
		if (TerminalGate.On)
		{
			RaidFirewall.WrapForeignPostfixes(AccessTools.Method(typeof(BotsController), "Init"));
			LateWaypointsPatch.Apply();
			TerminalOrbitFirewall.Apply();
		}
	}

	private static Exception Finalizer(Exception __exception)
	{
		return RaidFirewall.Swallow(__exception, "BotsController.Init");
	}
}
