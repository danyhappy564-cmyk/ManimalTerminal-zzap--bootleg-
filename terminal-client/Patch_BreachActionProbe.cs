using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[HarmonyPatch]
internal static class Patch_BreachActionProbe
{
	private static float _lastLog;

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type type = AccessTools.TypeByName("EFT.InteractionContextHelper");
		if (type == null)
		{
			yield break;
		}
		MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			ParameterInfo[] parameters = methodInfo.GetParameters();
			bool flag = false;
			ParameterInfo[] array = parameters;
			foreach (ParameterInfo parameterInfo in array)
			{
				if (typeof(Door).IsAssignableFrom(parameterInfo.ParameterType))
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				yield return methodInfo;
			}
		}
	}

	[HarmonyPostfix]
	private static void Postfix(object __result, MethodBase __originalMethod, object[] __args)
	{
		try
		{
			if (!TerminalGate.On || !Plugin.BreachDoorProbe.Value || Time.unscaledTime - _lastLog < 2f)
			{
				return;
			}
			Door door = null;
			for (int i = 0; i < __args.Length; i++)
			{
				if (__args[i] is Door door2)
				{
					door = door2;
					break;
				}
			}
			if (door == null)
			{
				return;
			}
			_lastLog = Time.unscaledTime;
			StringBuilder stringBuilder = new StringBuilder();
			if (AccessTools.Field(__result?.GetType(), "Actions")?.GetValue(__result) is IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					if (item == null)
					{
						continue;
					}
					Type type = item.GetType();
					string arg = AccessTools.Field(type, "Name")?.GetValue(item)?.ToString() ?? "?";
					object arg2 = AccessTools.Field(type, "Disabled")?.GetValue(item);
					stringBuilder.Append($"\n    action '{arg}' Disabled={arg2}");
					FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
					foreach (FieldInfo fieldInfo in fields)
					{
						if (fieldInfo.FieldType == typeof(string) && fieldInfo.Name != "Name")
						{
							stringBuilder.Append($"  {fieldInfo.Name}='{fieldInfo.GetValue(item)}'");
						}
					}
				}
			}
			else
			{
				stringBuilder.Append("\n    <no Actions list on the result>");
			}
			Plugin.Log.LogWarning($"[BreachDoor][probe] {__originalMethod.Name} on '{door.gameObject.name}':{stringBuilder}");
			TerminalBreachDoors.DumpBreachState(door, "probe");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[BreachDoor] probe threw: " + ex.Message);
		}
	}
}
