using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;

namespace Manimal.Terminal;

// One-way migration: the new section wins after the first load; old values are
// read only as defaults. In particular an existing GhostMode=false stays false.
internal static class TerminalGhostSettings
{
	internal enum FightFrequency { Low, Normal, High }
	private sealed class LegacyDisplay { public bool Browsable = false; }
	private static readonly HashSet<string> CurrentKeys = new HashSet<string>(StringComparer.Ordinal);
	private static bool _readKeys;
	private static bool _readFailed;

	private static void ReadKeys(ConfigFile file)
	{
		if (_readKeys) return;
		_readKeys = true;
		if (!File.Exists(file.ConfigFilePath)) return;
		try
		{
			bool currentSection = false;
			int lines = 0;
			foreach (string raw in File.ReadLines(file.ConfigFilePath))
			{
				if (++lines > 20000) throw new InvalidDataException("config line limit");
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
				if (line[0] == '[') { currentSection = line == "[Terminal Ghost]"; continue; }
				int equals = line.IndexOf('=');
				if (currentSection && equals > 0) CurrentKeys.Add(line.Substring(0, equals).Trim());
			}
		}
		catch (Exception ex)
		{
			_readFailed = true;
			Plugin.Log.LogWarning("[GhostConfig] legacy-default fallback: " + ex.Message);
		}
	}

	internal static ConfigEntry<T> Legacy<T>(ConfigFile file, string key, T value, string description, AcceptableValueBase range = null)
	{
		// Snapshot key presence before Bind's automatic Save can create new entries.
		ReadKeys(file);
		var legacy = file.Bind("Terminal", key, value,
			new ConfigDescription("Legacy migration source. Use Terminal Ghost instead.", range, new LegacyDisplay()));
		if (_readFailed) return file.Bind("Terminal Ghost", key, legacy.Value, new ConfigDescription(description, range));
		var current = file.Bind("Terminal Ghost", key, value, new ConfigDescription(description, range));
		if (!CurrentKeys.Contains(key)) current.Value = legacy.Value;
		return current;
	}

	// zzap 10/28 (user): "유령 모드 200m가 너무 넓은 거 같음 거의 시가전 중시인데 125m 혹은
	// 100m가 나을듯". Bind keeps a saved value, so existing configs would stay at 260/210;
	// move them once to the new defaults, then any later F12 choice is kept.
	internal const int DistanceVersion = 2;

	internal static void MigrateDistances(ConfigFile file, ConfigEntry<float> sleep, ConfigEntry<float> wake, float sleepDefault, float wakeDefault)
	{
		var version = file.Bind("Terminal Ghost", "GhostDistanceVersion", 0,
			new ConfigDescription("Internal: distance-default migration marker.", null, new LegacyDisplay()));
		if (version.Value >= DistanceVersion) return;
		sleep.Value = sleepDefault;
		wake.Value = wakeDefault;
		version.Value = DistanceVersion;
		Plugin.Log.LogInfo($"[GhostConfig] ghost distances moved to new defaults: sleep {sleepDefault:0}m, wake {wakeDefault:0}m");
	}

	internal static ConfigEntry<T> Bind<T>(ConfigFile file, string key, T value, string description, AcceptableValueBase range = null)
	{
		return file.Bind("Terminal Ghost", key, value, new ConfigDescription(description, range));
	}
}
