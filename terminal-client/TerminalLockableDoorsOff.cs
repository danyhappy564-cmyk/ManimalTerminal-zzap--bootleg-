using System;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalLockableDoorsOff
{
	private const string PluginGuid = "Jehree.LockableDoors";

	private static bool _tried;

	private static readonly (string type, string method)[] Targets = new (string, string)[3]
	{
		("LockableDoors.Patches.GameStartedPatch", "PatchPrefix"),
		("LockableDoors.Patches.GetAvailableActionsPatch", "PatchPostfix"),
		("LockableDoors.Patches.GameEndedPatch", "Postfix")
	};

	private static bool _swallowLogged;

	internal static void TryPatch(Harmony h)
	{
		if (_tried)
		{
			return;
		}
		_tried = true;
		try
		{
			HarmonyMethod harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(TerminalLockableDoorsOff), "SkipOnTerminal"));
			HarmonyMethod harmonyMethod2 = new HarmonyMethod(AccessTools.Method(typeof(TerminalLockableDoorsOff), "SwallowOnTerminal"));
			int num = 0;
			(string, string)[] targets = Targets;
			for (int i = 0; i < targets.Length; i++)
			{
				var (text, text2) = targets[i];
				try
				{
					Type type = AccessTools.TypeByName(text);
					MethodInfo methodInfo = ((type != null) ? AccessTools.Method(type, text2) : null);
					if (methodInfo == null)
					{
						Plugin.Log.LogWarning("[LockableDoors] " + text + "." + text2 + " not found — mod updated? terminal's doors may lose their prompts until this shim is refreshed");
					}
					else
					{
						h.Patch((MethodBase)methodInfo, harmonyMethod, (HarmonyMethod)null, (HarmonyMethod)null, harmonyMethod2, (HarmonyMethod)null);
						num++;
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[LockableDoors] couldn't neutralise " + text + "." + text2 + ": " + ex.Message);
				}
			}
			if (num > 0)
			{
				Plugin.Log.LogWarning($"[LockableDoors] detected — suppressed on terminal ({num} hook(s) gated); " + "the mod stays fully active on every other map.");
			}
			else
			{
				Plugin.Log.LogDebug("[LockableDoors] not installed (no patch types resolved) — nothing to gate");
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[LockableDoors] shim failed: " + ex2.Message);
		}
	}

	private static bool SkipOnTerminal()
	{
		if (!TerminalGate.On)
		{
			return !TerminalLoaded.Check();
		}
		return false;
	}

	private static Exception SwallowOnTerminal(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (!TerminalGate.On && !TerminalLoaded.Check())
		{
			return __exception;
		}
		if (!_swallowLogged)
		{
			_swallowLogged = true;
			Plugin.Log.LogWarning("[LockableDoors] swallowed its exception on terminal (" + __exception.GetType().Name + ": " + __exception.Message + ") — it keys settings by map name and has no 'terminal' entry. left unhandled this empties EVERY interaction prompt on the map.");
		}
		return null;
	}
}
