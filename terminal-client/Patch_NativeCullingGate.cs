using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class Patch_NativeCullingGate
{
	internal static void TryPatch(Harmony harmony)
	{
		Type type = typeof(GameWorld).Assembly.GetType("Koenigz.PerfectCulling.EFT.PerfectCullingAdaptiveGrid");
		if (type == null)
		{
			Plugin.Log.LogDebug("[RaidFix] no native PerfectCullingAdaptiveGrid class — gate not needed");
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(type, "Awake");
		if (methodInfo == null)
		{
			Plugin.Log.LogWarning("[RaidFix] PerfectCullingAdaptiveGrid.Awake not found — native culling gate NOT armed");
			return;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(Patch_NativeCullingGate), "Prefix"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		Plugin.Log.LogInfo("[RaidFix] native adaptive-grid gate armed (Assembly-CSharp class, explicit bind)");
	}

	private static bool Prefix(MonoBehaviour __instance)
	{
		try
		{
			string text = ((__instance != null) ? __instance.gameObject.scene.name : null);
			if (text == null || !text.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch
		{
			return true;
		}
		Plugin.Log.LogWarning("[RaidFix] suppressed native PerfectCullingAdaptiveGrid.Awake (terminal scene — its packed bake cannot exist; sidecar culling owns this map)");
		return false;
	}
}
