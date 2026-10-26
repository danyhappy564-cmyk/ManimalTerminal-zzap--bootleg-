using System;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalBallisticGlass
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_Sweep
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!TerminalGate.On || !Plugin.BallisticGlassPen.Value)
			{
				return;
			}
			try
			{
				int num = 0;
				int num2 = 0;
				BallisticCollider[] array = UnityEngine.Object.FindObjectsOfType<BallisticCollider>();
				foreach (BallisticCollider ballisticCollider in array)
				{
					if ((bool)ballisticCollider)
					{
						if (ballisticCollider.TypeOfMaterial == MaterialType.Chainfence)
						{
							ballisticCollider.PenetrationChance = 1f;
							ballisticCollider.PenetrationLevel = 0f;
							ballisticCollider.RicochetChance = 0f;
							num2++;
						}
						else if (ballisticCollider.TypeOfMaterial == MaterialType.Glass && ballisticCollider.name.IndexOf("BALLISTIC", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							ballisticCollider.PenetrationChance = 1f;
							ballisticCollider.PenetrationLevel = 0f;
							ballisticCollider.RicochetChance = 0f;
							num++;
						}
					}
				}
				Plugin.Log.LogWarning($"[Glass] {num} ballistic-glass + {num2} chainfence collider(s) made penetrable " + "(pen chance 1, level 0) — player and AI rounds pass through");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Glass] sweep failed: " + ex.Message);
			}
		}
	}
}
