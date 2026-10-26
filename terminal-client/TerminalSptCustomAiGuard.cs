using System;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalSptCustomAiGuard
{
	internal static void TryPatch(Harmony harmony)
	{
		Type type = TerminalTypes.ByName("SPT.Custom.CustomAI.AIBrainSpawnWeightAdjustment");
		if (type == null)
		{
			return;
		}
		int num = 0;
		string[] array = new string[3] { "GetAssaultScavWildSpawnType", "GetRandomisedPlayerScavType", "GetPmcWildSpawnType" };
		foreach (string name in array)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, name);
			if (!(methodInfo == null))
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(TerminalSptCustomAiGuard), "ReturnAuthoredRole"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				num++;
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[CustomAIGuard] {num} SPT brain-weight lookup(s) guarded for Terminal's missing map cache entry");
		}
	}

	private static bool ReturnAuthoredRole(object[] __args, ref WildSpawnType __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		ProfileSettings profileSettings = ((__args != null && __args.Length != 0) ? (__args[0] as BotOwner) : null)?.Profile?.Info?.Settings;
		if (profileSettings == null)
		{
			return true;
		}
		__result = profileSettings.Role;
		return false;
	}
}
