using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

// zzap perf/compat: a cached stand-in for AccessTools.TypeByName.
//
// TypeByName falls back to enumerating every type of every loaded assembly on a miss.
// With 140+ mods that is slow on its own, and any plugin built against a missing mod
// (e.g. a Fika add-on without Fika) makes each enumeration throw and log a
// ReflectionTypeLoadException — 225 of those in the 10/26 Terminal raid, ~10s per
// ambient-audio attempt. Here:
//  - full names ask each assembly directly (Assembly.GetType: no type enumeration),
//  - only short names (no '.') fall back to TypeByName's full scan,
//  - answers are cached; a "not found" is retried only after more assemblies load
//    (other plugins can load after us — see LateWaypointsPatch).
internal static class TerminalTypes
{
	private static readonly Dictionary<string, Type> _found = new Dictionary<string, Type>();

	private static readonly Dictionary<string, int> _missedAtAssemblyCount = new Dictionary<string, int>();

	private static readonly Dictionary<string, Type> _shortFound = new Dictionary<string, Type>();

	private static readonly Dictionary<string, int> _shortMissedAt = new Dictionary<string, int>();

	internal static Type ByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		if (_found.TryGetValue(name, out var cached))
		{
			return cached;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		if (_missedAtAssemblyCount.TryGetValue(name, out var count) && count == assemblies.Length)
		{
			return null;
		}
		Type type = null;
		try
		{
			type = Type.GetType(name, throwOnError: false);
		}
		catch
		{
		}
		if (type == null)
		{
			foreach (Assembly assembly in assemblies)
			{
				try
				{
					type = assembly.GetType(name, throwOnError: false);
				}
				catch
				{
				}
				if (type != null)
				{
					break;
				}
			}
		}
		if (type == null && name.IndexOf('.') < 0)
		{
			// short name: only a full scan can match Type.Name
			type = AccessTools.TypeByName(name);
		}
		if (type != null)
		{
			_found[name] = type;
			_missedAtAssemblyCount.Remove(name);
		}
		else
		{
			_missedAtAssemblyCount[name] = assemblies.Length;
		}
		return type;
	}

	// ByName, then a match on the bare Type.Name or FullName across every assembly
	// (what TerminalAcoustics.ResolveType did on each call) — cached the same way.
	internal static Type ByNameOrShort(string name)
	{
		Type type = ByName(name);
		if (type != null || string.IsNullOrEmpty(name))
		{
			return type;
		}
		if (_shortFound.TryGetValue(name, out var cached))
		{
			return cached;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		if (_shortMissedAt.TryGetValue(name, out var count) && count == assemblies.Length)
		{
			return null;
		}
		foreach (Assembly assembly in assemblies)
		{
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch
			{
				continue; // same as before: an assembly that cannot list its types is skipped
			}
			if (types == null)
			{
				continue;
			}
			foreach (Type candidate in types)
			{
				if (candidate != null && (candidate.Name == name || candidate.FullName == name))
				{
					_shortFound[name] = candidate;
					_shortMissedAt.Remove(name);
					return candidate;
				}
			}
		}
		_shortMissedAt[name] = assemblies.Length;
		return null;
	}
}
