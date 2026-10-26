using System;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalGateAmbushTime
{
	[HarmonyPatch(typeof(BossLocationSpawn), "ParseMainTypesTypes")]
	internal static class Patch_Retime
	{
		[HarmonyPostfix]
		private static void Postfix(BossLocationSpawn __instance)
		{
			try
			{
				if (!(__instance.BossZone != "Zone1BDGateAmbush13"))
				{
					float value = Plugin.GateAmbushTime.Value;
					if (!(value < 0f) && !Mathf.Approximately(value, __instance.Time))
					{
						Plugin.Log.LogWarning($"[GateAmbush] Zone1BDGateAmbush13 wave retimed {__instance.Time:0}s -> {value:0}s " + "(authored 950 assumes a slow fight through the armory)");
						__instance.Time = value;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[GateAmbush] retime failed: " + ex.Message);
			}
		}
	}
}
