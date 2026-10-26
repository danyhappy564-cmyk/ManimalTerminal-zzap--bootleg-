using System;
using System.Collections.Generic;
using EFT;

namespace Manimal.Terminal;

// zzap perf: WildSpawnType.ToString() allocates a new string on every call, and the
// RUAF checks ran it per bot per brain tick (TerminalRuafDefenseLayer.IsActive) and
// per AddEnemy. Role names cannot change during a session (the prepatchers register
// custom roles before the game loads), so name and the ruaf/vsrf test are cached.
internal static class TerminalRoles
{
	private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();

	private static readonly Dictionary<int, bool> _ruaf = new Dictionary<int, bool>();

	internal static string Name(WildSpawnType role)
	{
		int key = (int)role;
		if (!_names.TryGetValue(key, out var name))
		{
			name = role.ToString();
			_names[key] = name;
		}
		return name;
	}

	// same test as before: the role name contains "ruaf" or "vsrf", any case
	internal static bool IsRuaf(WildSpawnType role)
	{
		int key = (int)role;
		if (!_ruaf.TryGetValue(key, out var ruaf))
		{
			string text = Name(role);
			ruaf = text.IndexOf("ruaf", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("vsrf", StringComparison.OrdinalIgnoreCase) >= 0;
			_ruaf[key] = ruaf;
		}
		return ruaf;
	}
}
