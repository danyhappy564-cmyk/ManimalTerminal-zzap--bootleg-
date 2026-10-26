using System;
using System.Collections.Generic;
using System.IO;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalContainerTemplates
{
	private static Dictionary<string, string>? _templates;

	internal static bool Apply(LootableContainer? container)
	{
		if (container == null || string.IsNullOrEmpty(container.Id))
		{
			return false;
		}
		if (_templates == null)
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_container_templates.json");
			try
			{
				_templates = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty container template map");
			}
			catch (Exception ex)
			{
				_templates = new Dictionary<string, string>();
				Plugin.Log.LogError("[Containers] template map could not load: " + ex.Message);
			}
		}
		if (!_templates.TryGetValue(container.Id, out string value) || container.Template == value)
		{
			return false;
		}
		container.Template = value;
		return true;
	}

	internal static void ApplyLoadedContainers()
	{
		int num = 0;
		LootableContainer[] array = UnityEngine.Object.FindObjectsOfType<LootableContainer>(includeInactive: true);
		for (int i = 0; i < array.Length; i++)
		{
			if (Apply(array[i]))
			{
				num++;
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[Containers] {num} container template(s) restored by exact server ID");
		}
	}
}
