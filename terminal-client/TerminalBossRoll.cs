using System;
using System.Collections.Generic;
using JsonType;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalBossRoll
{
	private static readonly string[] Candidates = new string[5] { "bossGluhar", "bossKilla", "bossBully", "bossSanitar", "bossTagilla" };

	internal static void Roll(LocationSettings.Location location)
	{
		try
		{
			BossLocationSpawn[] array = location?.BossLocationSpawn;
			if (array == null || array.Length == 0)
			{
				return;
			}
			if (Plugin.DisableAllBots != null && Plugin.DisableAllBots.Value)
			{
				location.BossLocationSpawn = Array.Empty<BossLocationSpawn>();
				Plugin.Log.LogWarning($"[BotControl] DIAGNOSTIC BOTLESS — removed all {array.Length} authored boss/event/wave rows");
				return;
			}
			List<int> list = new List<int>();
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i]?.BossName;
				string[] candidates = Candidates;
				foreach (string text2 in candidates)
				{
					if (text == text2)
					{
						list.Add(i);
						break;
					}
				}
			}
			if (list.Count == 0)
			{
				return;
			}
			if (Plugin.TerminalBosses != null && !Plugin.TerminalBosses.Value)
			{
				List<BossLocationSpawn> list2 = new List<BossLocationSpawn>(array.Length - list.Count);
				for (int k = 0; k < array.Length; k++)
				{
					if (!list.Contains(k))
					{
						list2.Add(array[k]);
					}
				}
				location.BossLocationSpawn = list2.ToArray();
				Plugin.Log.LogWarning($"[BossRoll] DIAGNOSTIC CONTROL — removed all {list.Count} Terminal boss candidates; regular event/T4 waves remain enabled");
			}
			else
			{
				if (list.Count == 1)
				{
					return;
				}
				int num = list[UnityEngine.Random.Range(0, list.Count)];
				List<BossLocationSpawn> list3 = new List<BossLocationSpawn>(array.Length);
				for (int l = 0; l < array.Length; l++)
				{
					if (l == num || !list.Contains(l))
					{
						list3.Add(array[l]);
					}
				}
				location.BossLocationSpawn = list3.ToArray();
				Plugin.Log.LogInfo($"[BossRoll] {list.Count} candidates -> '{array[num].BossName}' " + "(escort " + array[num].BossEscortAmount + "x " + array[num].BossEscortType + ") spawns this raid");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[BossRoll] failed: " + ex.Message);
		}
	}
}
