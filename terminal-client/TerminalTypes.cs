using System;
using System.Collections.Generic;
using System.Reflection;

namespace Manimal.Terminal;

// zzap perf/compat: a cached stand-in for AccessTools.TypeByName.
//
// TypeByName falls back to enumerating every type of every loaded assembly on a miss.
// With 140+ mods that is slow on its own, and any plugin built against a missing mod
// (e.g. a Fika add-on without Fika) makes each enumeration throw and log a
// ReflectionTypeLoadException — 225 of those in the 10/26 Terminal raid, ~10s per
// ambient-audio attempt. Here:
//  - full names ask each assembly directly (Assembly.GetType: no type enumeration),
//  - short names (no '.') — the ambient sidecar asks for dozens of them — are looked
//    up in ONE name index built from a single pass over all types, instead of one
//    full scan per name (the 6.4s first ambient attempt on the 10/26 third raid),
//  - answers are cached; a "not found" is retried only after more assemblies load
//    (other plugins can load after us — see LateWaypointsPatch).
// Match order is TypeByName's: Type.GetType, then FullName, then Name, first hit in
// assembly/type order; types of an assembly that only partly loads are still seen.
internal static class TerminalTypes
{
	private static readonly Dictionary<string, Type> _found = new Dictionary<string, Type>();

	private static readonly Dictionary<string, int> _missedAtAssemblyCount = new Dictionary<string, int>();

	private static Dictionary<string, Type> _byFullName;

	private static Dictionary<string, Type> _byShortName;

	private static int _indexedAssemblyCount = -1;

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
			// short name: only a match on Type.Name can find it
			EnsureIndex(assemblies);
			if (!_byFullName.TryGetValue(name, out type))
			{
				_byShortName.TryGetValue(name, out type);
			}
		}
		Remember(name, type, assemblies.Length);
		return type;
	}

	// ByName, then a match on the bare Type.Name or FullName across every assembly
	// (what TerminalAcoustics.ResolveType did on each call) — answered from the index.
	internal static Type ByNameOrShort(string name)
	{
		Type type = ByName(name);
		if (type != null || string.IsNullOrEmpty(name))
		{
			return type;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		EnsureIndex(assemblies);
		if (!_byShortName.TryGetValue(name, out type))
		{
			_byFullName.TryGetValue(name, out type);
		}
		return type;
	}

	private static void Remember(string name, Type type, int assemblyCount)
	{
		if (type != null)
		{
			_found[name] = type;
			_missedAtAssemblyCount.Remove(name);
		}
		else
		{
			_missedAtAssemblyCount[name] = assemblyCount;
		}
	}

	// one pass over every type of every assembly (rebuilt only when assemblies are added)
	private static void EnsureIndex(Assembly[] assemblies)
	{
		if (_byFullName != null && _indexedAssemblyCount == assemblies.Length)
		{
			return;
		}
		var byFull = new Dictionary<string, Type>(StringComparer.Ordinal);
		var byShort = new Dictionary<string, Type>(StringComparer.Ordinal);
		foreach (Assembly assembly in assemblies)
		{
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types; // like HarmonyX: keep the types that did load
			}
			catch
			{
				continue;
			}
			if (types == null)
			{
				continue;
			}
			foreach (Type t in types)
			{
				if (t == null)
				{
					continue;
				}
				string full = t.FullName;
				if (full != null && !byFull.ContainsKey(full))
				{
					byFull[full] = t;
				}
				if (!byShort.ContainsKey(t.Name))
				{
					byShort[t.Name] = t;
				}
			}
		}
		_byFullName = byFull;
		_byShortName = byShort;
		_indexedAssemblyCount = assemblies.Length;
	}
}
