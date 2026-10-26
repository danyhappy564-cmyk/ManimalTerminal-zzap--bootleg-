using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(BotsController), "Init")]
internal static class Patch_EnsureStationaryController
{
	private static void Prefix()
	{
		if (!TerminalGate.On)
		{
			return;
		}
		if (UnityEngine.Object.FindObjectOfType<AIStationaryController>() == null)
		{
			new GameObject("Terminal_AIStationary_Fix").AddComponent<AIStationaryController>();
			Plugin.Log.LogWarning("[RaidFix] created missing AIStationaryController (no stationary weapons on map)");
		}
		if (!Singleton<ObservedCullingManager>.Instantiated)
		{
			new GameObject("Terminal_ObservedCullingManager_Fix").AddComponent<ObservedCullingManager>();
			Plugin.Log.LogWarning("[RaidFix] created missing ObservedCullingManager (bot body visibility)");
		}
		try
		{
			AICoversData aICoversData = AICoversData.CreateOrFind(undestandAtGame: true);
			int num = HealNullCollections(aICoversData);
			FieldInfo[] fields = typeof(AICoversData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (typeof(Component).IsAssignableFrom(fieldInfo.FieldType))
				{
					Component component = fieldInfo.GetValue(aICoversData) as Component;
					if (component != null)
					{
						num += HealNullCollections(component);
					}
				}
			}
			Plugin.Log.LogWarning($"[RaidFix] AI covers graph pre-healed: {num} null collection field(s) initialized (empty nav data until the AI bake port)");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[RaidFix] covers pre-heal failed: " + ex.Message);
		}
	}

	private static int HealNullCollections(object o)
	{
		int num = 0;
		FieldInfo[] fields = o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			Type fieldType = fieldInfo.FieldType;
			try
			{
				if (fieldType.IsArray && fieldType.GetArrayRank() == 1 && fieldInfo.GetValue(o) == null)
				{
					fieldInfo.SetValue(o, Array.CreateInstance(fieldType.GetElementType(), 0));
					num++;
				}
				else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) && fieldInfo.GetValue(o) == null)
				{
					fieldInfo.SetValue(o, Activator.CreateInstance(fieldType));
					num++;
				}
			}
			catch
			{
			}
		}
		return num;
	}
}
