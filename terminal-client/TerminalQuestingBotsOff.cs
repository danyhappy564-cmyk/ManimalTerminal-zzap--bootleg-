using System;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalQuestingBotsOff
{
	internal static void TryPatch(Harmony h)
	{
		Type type = TerminalTypes.ByName("QuestingBots.Components.Spawning.PMCGenerator");
		Type type2 = TerminalTypes.ByName("QuestingBots.Components.Spawning.PScavGenerator");
		if (type == null && type2 == null)
		{
			return;
		}
		int num = 0;
		Type[] array = new Type[2] { type, type2 };
		foreach (Type type3 in array)
		{
			if (!(type3 == null))
			{
				MethodInfo methodInfo = AccessTools.DeclaredMethod(type3, "CanSpawnBots");
				if (methodInfo != null)
				{
					h.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(TerminalQuestingBotsOff), "SkipFalse"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
				}
				MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type3, "GetNumberOfBotsAllowedToSpawn");
				if (methodInfo2 != null)
				{
					h.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(TerminalQuestingBotsOff), "SkipZero"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					num++;
				}
			}
		}
		Type type4 = TerminalTypes.ByName("QuestingBots.Patches.Spawning.ActivateBossesByWavePatch");
		MethodInfo methodInfo3 = ((type4 == null) ? null : AccessTools.DeclaredMethod(type4, "shouldSuppressBossWave"));
		if (methodInfo3 != null)
		{
			h.Patch((MethodBase)methodInfo3, new HarmonyMethod(typeof(TerminalQuestingBotsOff), "SkipFalse"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			num++;
		}
		Type type5 = TerminalTypes.ByName("QuestingBots.Patches.Spawning.GameStartPatch");
		MethodInfo methodInfo4 = ((type5 == null) ? null : AccessTools.PropertyGetter(type5, "IsDelayingGameStart"));
		if (methodInfo4 != null)
		{
			h.Patch((MethodBase)methodInfo4, new HarmonyMethod(typeof(TerminalQuestingBotsOff), "SkipFalse"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			num++;
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[QuestingBotsOff] detected — {num} method(s) gated (spawn generators + boss suppression + start delay); QB muted on terminal ONLY");
		}
	}

	private static bool SkipFalse(ref bool __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static bool SkipZero(ref int __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		__result = 0;
		return false;
	}
}
