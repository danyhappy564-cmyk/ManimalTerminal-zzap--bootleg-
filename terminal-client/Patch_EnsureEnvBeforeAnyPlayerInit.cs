using Comfort.Common;
using EFT;
using EFT.EnvironmentEffect;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(Player), "Init")]
internal static class Patch_EnsureEnvBeforeAnyPlayerInit
{
	[HarmonyPrefix]
	private static void Prefix()
	{
		if (TerminalGate.On)
		{
			if (!Singleton<ObservedCullingManager>.Instantiated)
			{
				new GameObject("Terminal_ObservedCullingManager_Fix").AddComponent<ObservedCullingManager>();
				Plugin.Log.LogWarning("[RaidFix] created missing ObservedCullingManager (observed body visibility)");
			}
			if (!(EnvironmentManager.Instance != null))
			{
				new GameObject("Terminal_EnvManager_Fix").AddComponent<EnvironmentManager>();
				Plugin.Log.LogWarning("[RaidFix] created missing EnvironmentManager singleton (bare — no indoor triggers yet)");
			}
		}
	}
}
