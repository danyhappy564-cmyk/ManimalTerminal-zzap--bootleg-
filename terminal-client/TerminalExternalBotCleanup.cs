using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalExternalBotCleanup
{
	internal static void ResetForRaid()
	{
		int num = ClearStaticDictionary("RUAFComeHome.Controllers.BotRuafManagerFactory", "managers");
		int num2 = ClearSingletonDictionary("RUAFComeHome.Components.RuafCheckpointManager", "ZoneCheckpoints");
		int num3 = ClearSingletonDictionary("MoreBotsAPI.Components.HuntManager", "huntGroups");
		if (num + num2 + num3 > 0)
		{
			Plugin.Log.LogWarning("[RaidCleanup] released persistent external AI state: " + $"RUAF managers={num}, checkpoint zones={num2}, hunt groups={num3}");
		}
	}

	private static int ClearStaticDictionary(string typeName, string fieldName)
	{
		try
		{
			Type type = TerminalTypes.ByName(typeName);
			return Clear(((type == null) ? null : AccessTools.Field(type, fieldName))?.GetValue(null));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[RaidCleanup] " + typeName + "." + fieldName + ": " + ex.Message);
			return 0;
		}
	}

	private static int ClearSingletonDictionary(string typeName, string fieldName)
	{
		try
		{
			Type type = TerminalTypes.ByName(typeName);
			if (type == null)
			{
				return 0;
			}
			FieldInfo fieldInfo = AccessTools.Field(type, fieldName);
			if (fieldInfo == null)
			{
				return 0;
			}
			int num = 0;
			UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(type);
			foreach (UnityEngine.Object obj in array)
			{
				num += Clear(fieldInfo.GetValue(obj));
			}
			return num;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[RaidCleanup] " + typeName + "." + fieldName + ": " + ex.Message);
			return 0;
		}
	}

	private static int Clear(object collection)
	{
		if (!(collection is IDictionary { Count: var count } dictionary))
		{
			return 0;
		}
		dictionary.Clear();
		return count;
	}
}
