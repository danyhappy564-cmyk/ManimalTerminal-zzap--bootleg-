using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalBreachDoors
{
	private class MemberSetter
	{
		private readonly FieldInfo _f;

		private readonly PropertyInfo _p;

		internal readonly string Shape;

		internal bool Ok
		{
			get
			{
				if (!(_f != null))
				{
					if (_p != null)
					{
						return _p.CanWrite;
					}
					return false;
				}
				return true;
			}
		}

		internal MemberSetter(Type t, string name)
		{
			_f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (_f == null)
			{
				_p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			Shape = ((_f != null) ? "field" : ((_p != null) ? "property" : "MISSING"));
		}

		internal void Set(object target, bool v)
		{
			if (_f != null)
			{
				_f.SetValue(target, v);
			}
			else
			{
				_p?.SetValue(target, v, null);
			}
		}
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_RollBreachableDoor
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!TerminalGate.On)
			{
				return;
			}
			int value = Plugin.BreachableDoors.Value;
			if (value <= 0)
			{
				return;
			}
			try
			{
				Roll(value);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[BreachDoor] roll failed: " + ex.Message);
			}
		}
	}

	private static readonly string[] Candidates = new string[7] { "door_Terminal_Area_07_OilStorage_Courtyard_00001", "door_Terminal_Area_07_OilStorage_Courtyard_00002", "door_Terminal_Area_07_OilStorage_Courtyard_00003", "door_Terminal_Area_07_OilStorage_Courtyard_00004", "door_Terminal_Area_07_OilStorage_Courtyard_00005", "door_Terminal_Area_07_OilStorage_Courtyard_00006", "door_Terminal_Area_07_OilStorage_Courtyard_00012" };

	private static MemberSetter _canBeBreached;

	private static MemberSetter _canInteractWithBreach;

	private static bool _resolved;

	private static PropertyInfo _idProp;

	private static FieldInfo _idField;

	private static bool _idResolved;

	private static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		Type[] array = new Type[2]
		{
			typeof(Door),
			typeof(WorldInteractiveObject)
		};
		foreach (Type type in array)
		{
			MemberSetter memberSetter = new MemberSetter(type, "CanBeBreached");
			MemberSetter memberSetter2 = new MemberSetter(type, "CanInteractWithBreach");
			if (memberSetter.Ok)
			{
				_canBeBreached = memberSetter;
				_canInteractWithBreach = (memberSetter2.Ok ? memberSetter2 : null);
				Plugin.Log.LogInfo("[BreachDoor] flags resolved on " + type.Name + ": CanBeBreached=" + memberSetter.Shape + ", CanInteractWithBreach=" + (memberSetter2.Ok ? memberSetter2.Shape : "MISSING"));
				return;
			}
		}
		Plugin.Log.LogWarning("[BreachDoor] could not resolve CanBeBreached on Door or WorldInteractiveObject — breach roll disabled this session (BSG renamed the member?)");
	}

	internal static string SafeId(Component c)
	{
		try
		{
			if (!_idResolved)
			{
				_idResolved = true;
				_idProp = typeof(WorldInteractiveObject).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (_idProp == null)
				{
					_idField = typeof(WorldInteractiveObject).GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
			}
			return (_idProp?.GetValue(c, null) ?? _idField?.GetValue(c))?.ToString();
		}
		catch
		{
			return null;
		}
	}

	private static string SafeState(Component c)
	{
		try
		{
			return c.GetType().GetProperty("DoorState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c, null)?.ToString() ?? "?";
		}
		catch
		{
			return "?";
		}
	}

	private static void Roll(int want)
	{
		Resolve();
		if (_canBeBreached == null)
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(Candidates, StringComparer.OrdinalIgnoreCase);
		List<WorldInteractiveObject> list = new List<WorldInteractiveObject>();
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		WorldInteractiveObject[] array = UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(includeInactive: true);
		foreach (WorldInteractiveObject worldInteractiveObject in array)
		{
			if (worldInteractiveObject == null)
			{
				continue;
			}
			string text = SafeId(worldInteractiveObject);
			if (!string.IsNullOrEmpty(text) && hashSet.Contains(text))
			{
				list.Add(worldInteractiveObject);
			}
			else if (!string.IsNullOrEmpty(text) && text.IndexOf("OilStorage", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				num++;
				if (num <= 30)
				{
					stringBuilder.Append("\n    [" + worldInteractiveObject.GetType().Name + "] id='" + text + "' go='" + worldInteractiveObject.gameObject.name + "' state=" + SafeState(worldInteractiveObject));
				}
			}
		}
		if (list.Count == 0)
		{
			Plugin.Log.LogWarning($"[BreachDoor] none of the {Candidates.Length} candidate ID(s) matched. " + $"{num} OilStorage door ID(s) in scene:{stringBuilder}");
			return;
		}
		if (list.Count < Candidates.Length)
		{
			Plugin.Log.LogInfo($"[BreachDoor] {list.Count}/{Candidates.Length} candidate doors present in scene");
		}
		int num2 = 0;
		foreach (WorldInteractiveObject item in list)
		{
			if (item is Door door && !(door.HitClip == null) && door.HitClip.name.IndexOf("grate", StringComparison.OrdinalIgnoreCase) >= 0 && !(door.BreachSound == door.HitClip))
			{
				string text2 = ((door.BreachSound != null) ? door.BreachSound.name : "<null>");
				door.BreachSound = door.HitClip;
				num2++;
				Plugin.Log.LogInfo("[BreachDoor] grate breach audio repaired on id='" + SafeId(door) + "': " + text2 + " -> " + door.BreachSound.name);
			}
		}
		if (num2 > 0)
		{
			Plugin.Log.LogWarning($"[BreachDoor] repaired chain-fence breach audio on {num2} candidate door(s)");
		}
		int num3 = Mathf.Min(want, list.Count);
		for (int j = 0; j < num3; j++)
		{
			int index = UnityEngine.Random.Range(j, list.Count);
			WorldInteractiveObject value = list[j];
			list[j] = list[index];
			list[index] = value;
		}
		for (int k = 0; k < num3; k++)
		{
			WorldInteractiveObject worldInteractiveObject2 = list[k];
			_canBeBreached.Set(worldInteractiveObject2, v: true);
			_canInteractWithBreach?.Set(worldInteractiveObject2, v: true);
			Plugin.Log.LogWarning("[BreachDoor] id='" + SafeId(worldInteractiveObject2) + "' [" + worldInteractiveObject2.GetType().Name + "] go='" + worldInteractiveObject2.gameObject.name + "' " + $"is BREACHABLE this raid (state={SafeState(worldInteractiveObject2)}, {list.Count} candidate(s) in the pool)");
			DumpBreachState(worldInteractiveObject2, "post-roll");
		}
	}

	internal static void DumpBreachState(Component door, string tag)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			Type type = door.GetType();
			while (type != null && type != typeof(Component))
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					string text = fieldInfo.Name.ToLowerInvariant();
					if (text.Contains("breach") || text.Contains("operat") || text.Contains("broken") || text.Contains("lock") || text.Contains("keyid") || text.Contains("doorstate"))
					{
						object arg = null;
						try
						{
							arg = fieldInfo.GetValue(door);
						}
						catch
						{
						}
						stringBuilder.Append($"\n    [f] {type.Name}.{fieldInfo.Name} = {arg}");
					}
				}
				PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (PropertyInfo propertyInfo in properties)
				{
					string text2 = propertyInfo.Name.ToLowerInvariant();
					if ((text2.Contains("breach") || text2.Contains("operat") || text2.Contains("broken") || text2.Contains("lock") || text2.Contains("keyid") || text2.Contains("doorstate")) && propertyInfo.CanRead && propertyInfo.GetIndexParameters().Length == 0)
					{
						object obj2 = null;
						try
						{
							obj2 = propertyInfo.GetValue(door, null);
						}
						catch (Exception ex)
						{
							obj2 = "<throw: " + ex.GetBaseException().Message + ">";
						}
						stringBuilder.Append($"\n    [p] {type.Name}.{propertyInfo.Name} = {obj2}");
					}
				}
				type = type.BaseType;
			}
			Plugin.Log.LogWarning($"[BreachDoor][{tag}] '{door.gameObject.name}' breach state:{stringBuilder}");
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[BreachDoor] state dump failed: " + ex2.Message);
		}
	}
}
