using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Manimal.Terminal;

internal static class TerminalShoreAuthoring
{
	internal static HashSet<long> ConfiguredPlayerIds(JObject sound)
	{
		HashSet<long> hashSet = new HashSet<long>();
		if (!(sound?["AmbientSoundPlayerGroup"] is JArray jArray))
		{
			return hashSet;
		}
		foreach (JToken item in jArray)
		{
			if (!(item.Value<string>("go") ?? "").EndsWith("/AmbientSplineEmitterSeaGroup", StringComparison.Ordinal) || !(item["fields"]?["_soundPlayers"] is JArray jArray2))
			{
				continue;
			}
			foreach (JToken item2 in jArray2)
			{
				long valueOrDefault = item2.Value<long?>("ref").GetValueOrDefault();
				if (valueOrDefault != 0L)
				{
					hashSet.Add(valueOrDefault);
				}
			}
		}
		return hashSet;
	}
}
