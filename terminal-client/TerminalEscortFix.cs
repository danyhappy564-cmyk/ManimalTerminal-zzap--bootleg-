using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalEscortFix
{
	[HarmonyPatch]
	internal static class Patch_EscortsHonourIgnoreMaxBots
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			string[] array = new string[2] { "SpawnBossSupports", "SpawnEscort" };
			foreach (string name in array)
			{
				MethodInfo methodInfo = AccessTools.Method(typeof(BotBossSpawn), name);
				if (methodInfo != null)
				{
					yield return methodInfo;
				}
			}
		}

		[HarmonyPrefix]
		private static void Prefix(BossLocationSpawn wave, ref bool forceSpawn)
		{
			try
			{
				if (!(!TerminalGate.On | forceSpawn) && wave != null && wave.IgnoreMaxBots)
				{
					forceSpawn = true;
					Plugin.Log.LogDebug("[EscortFix] '" + wave.BossName + "' escorts into '" + wave.BossZone + "' forced " + $"(wave IgnoreMaxBots) — {wave.EscortCount} slot(s) reserved");
				}
			}
			catch
			{
			}
		}
	}
}
