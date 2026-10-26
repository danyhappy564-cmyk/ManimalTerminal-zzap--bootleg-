using System;
using EFT;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalRuafNeutral
{
	[HarmonyPatch(typeof(BotsGroup), "AddEnemy")]
	internal static class Patch_RuafNeutralToHumans
	{
		[HarmonyPrefix]
		private static bool Prefix(BotsGroup __instance, IPlayer person, EBotEnemyCause cause, ref bool __result)
		{
			try
			{
				if (!TerminalGate.On || !Plugin.RuafNeutral.Value)
				{
					return true;
				}
				if (person == null || person.IsAI)
				{
					return true;
				}
				string text = __instance?._initialBot?.Profile?.Info?.Settings?.Role.ToString() ?? "";
				if (text.IndexOf("ruaf", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("vsrf", StringComparison.OrdinalIgnoreCase) < 0)
				{
					return true;
				}
				if (cause == EBotEnemyCause.followGetHit || cause == EBotEnemyCause.byKill || cause == EBotEnemyCause.death)
				{
					return true;
				}
				Plugin.Log.LogDebug($"[RuafNeutral] blocked enemy-add: role='{text}' cause={cause} target=human");
				__result = false;
				return false;
			}
			catch
			{
				return true;
			}
		}
	}
}
