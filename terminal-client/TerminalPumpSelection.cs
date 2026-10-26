using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Manimal.Terminal;

internal static class TerminalPumpSelection
{
	internal static string HumanKey(bool isAi, string nickname, string profileId, int playerId)
	{
		if (isAi || (nickname != null && nickname.StartsWith("headless_", StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		if (!string.IsNullOrEmpty(profileId))
		{
			return profileId;
		}
		return "player:" + playerId;
	}

	internal static JObject Configuration(JArray rows, int playerCount)
	{
		JArray jArray = null;
		foreach (JToken row in rows)
		{
			if (row.Value<string>("cls") == "ElectricalCabinetSelector")
			{
				jArray = row["fields"]?["_groupConfigurations"] as JArray;
				break;
			}
		}
		JObject jObject = null;
		JObject jObject2 = null;
		foreach (JObject item in jArray ?? new JArray())
		{
			int num = item.Value<int>("MaxGroupSize");
			if (jObject2 == null || num > jObject2.Value<int>("MaxGroupSize"))
			{
				jObject2 = item;
			}
			if (num >= Math.Max(1, playerCount) && (jObject == null || num < jObject.Value<int>("MaxGroupSize")))
			{
				jObject = item;
			}
		}
		return jObject ?? jObject2 ?? throw new InvalidOperationException("Pump group configurations missing");
	}

	internal static List<string> Pool(JObject configuration)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (JToken item in (configuration["AvailableCabinets"] as JArray) ?? new JArray())
		{
			string text = item["$ref"]?.Value<string>("path");
			if (string.IsNullOrEmpty(text))
			{
				throw new InvalidOperationException("Pump pool contains an empty cabinet reference");
			}
			if (hashSet.Add(text))
			{
				list.Add(text);
			}
		}
		return list;
	}

	internal static List<string> Choose(IReadOnlyList<string> pool, int count, Random random)
	{
		List<string> list = new List<string>(pool);
		if (count < 1 || count > list.Count)
		{
			throw new InvalidOperationException($"Cannot break {count} cabinets from a {list.Count}-cabinet pool");
		}
		List<string> list2 = new List<string>(count);
		for (int i = 0; i < count; i++)
		{
			int index = random.Next(list.Count);
			list2.Add(list[index]);
			list.RemoveAt(index);
		}
		return list2;
	}

	internal static int RaidSeed(string raidKey)
	{
		uint num = 2166136261u;
		string text = "TerminalPumpCabinets|" + raidKey;
		foreach (char c in text)
		{
			num = (num ^ c) * 16777619;
		}
		return (int)num;
	}

	internal static Dictionary<string, bool> InitialStates(JArray rows, string cabinetPath, bool broken)
	{
		JObject jObject = null;
		foreach (JToken row in rows)
		{
			if (row.Value<string>("cls") == "ElectricalCabinetController" && row.Value<string>("path") == cabinetPath)
			{
				jObject = row["fields"] as JObject;
			}
		}
		if (jObject == null)
		{
			throw new InvalidOperationException("Pump controller missing: " + cabinetPath);
		}
		string text = jObject.Value<string>(broken ? "_triggerSetActive" : "_triggerSetDisabled");
		string text2 = jObject.Value<string>("_onCabinetFixedTriggerId");
		Dictionary<string, bool> dictionary = new Dictionary<string, bool>(StringComparer.Ordinal);
		foreach (JToken row2 in rows)
		{
			JToken jToken = row2["fields"];
			if (jToken == null)
			{
				continue;
			}
			if (row2.Value<string>("cls") == "HandlerGameObjectState" && jToken.Value<string>("_triggerId") == text)
			{
				foreach (JToken item in (jToken["_controlledGameObjects"] as JArray) ?? new JArray())
				{
					dictionary[item["$ref"].Value<string>("path")] = jToken.Value<int>("_targetState") != 0;
				}
			}
			if (!broken || !(row2.Value<string>("cls") == "HandlerGOState"))
			{
				continue;
			}
			string text3 = jToken["_target"]?["$ref"]?.Value<string>("path");
			if (text3 != null)
			{
				if (jToken.Value<string>("_triggerIdEnable") == text2)
				{
					dictionary[text3] = false;
				}
				if (jToken.Value<string>("_triggerIdDisable") == text2)
				{
					dictionary[text3] = true;
				}
			}
		}
		dictionary[cabinetPath] = broken;
		return dictionary;
	}
}
