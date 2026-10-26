using System;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(GrenadeFlashScreenEffect), "Awake")]
internal static class Patch_GrenadeFlashPrismRef
{
	[HarmonyPrefix]
	private static void Prefix(GrenadeFlashScreenEffect __instance)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		try
		{
			if (__instance.PrismEffects == null)
			{
				__instance.PrismEffects = __instance.GetComponent<PrismEffects>();
				if (__instance.PrismEffects != null)
				{
					Plugin.Log.LogDebug("[RaidFix] GrenadeFlash PrismEffects ref healed from sibling component");
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[RaidFix] grenade flash heal failed: " + ex.Message);
		}
	}
}
