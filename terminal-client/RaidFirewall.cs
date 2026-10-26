using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class RaidFirewall
{
	private const string OwnPrefix = "com.manimal.terminal";

	private static readonly HashSet<MethodBase> Wrapped = new HashSet<MethodBase>();

	internal static void WrapForeignPostfixes(MethodBase chokePoint)
	{
		if (chokePoint == null || !TerminalGate.On)
		{
			return;
		}
		try
		{
			Patches patchInfo = Harmony.GetPatchInfo(chokePoint);
			if (patchInfo?.Postfixes == null)
			{
				return;
			}
			Harmony harmony = new Harmony("com.manimal.terminal.postfixairbag");
			HarmonyMethod harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(RaidFirewall), "PostfixAirbag"));
			foreach (Patch postfix in patchInfo.Postfixes)
			{
				if (!postfix.owner.StartsWith("com.manimal.terminal", StringComparison.OrdinalIgnoreCase) && Wrapped.Add(postfix.PatchMethod))
				{
					try
					{
						harmony.Patch((MethodBase)postfix.PatchMethod, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, harmonyMethod, (HarmonyMethod)null);
						Plugin.Log.LogDebug("[Firewall] airbag on " + postfix.PatchMethod.DeclaringType?.FullName + "." + postfix.PatchMethod.Name + " (owner " + postfix.owner + ")");
					}
					catch (Exception ex)
					{
						Plugin.Log.LogDebug("[Firewall] couldnt wrap " + postfix.PatchMethod.Name + " of " + postfix.owner + ": " + ex.Message);
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Firewall] postfix sweep failed on " + chokePoint.Name + ": " + ex2.Message);
		}
	}

	private static Exception PostfixAirbag(Exception __exception, MethodBase __originalMethod)
	{
		if (__exception == null)
		{
			return null;
		}
		if (!TerminalGate.On)
		{
			return __exception;
		}
		string text = __originalMethod?.DeclaringType?.Assembly.GetName().Name ?? "unknown";
		Plugin.Log.LogError("[Firewall] " + text + "'s hook " + __originalMethod?.DeclaringType?.Name + "." + __originalMethod?.Name + " threw on terminal — swallowed; only THAT mod's hook is skipped, later mods' hooks still run (likely a per-map table with no 'Terminal' entry — report it to " + text + "'s author). inner: " + __exception.Message);
		return null;
	}

	internal static Exception Swallow(Exception ex, string site)
	{
		if (ex == null)
		{
			return null;
		}
		if (!TerminalGate.On)
		{
			return ex;
		}
		string text = "unknown";
		string[] array = (ex.StackTrace ?? "").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			Match match = Regex.Match(array[i], "at (?!EFT\\.|System\\.|UnityEngine\\.|Manimal\\.|DMD|\\(wrapper)([A-Za-z_][\\w]*)[\\w.]*\\.");
			if (match.Success)
			{
				text = match.Groups[1].Value;
				break;
			}
		}
		Plugin.Log.LogError("[Firewall] a mod threw inside " + site + " on terminal — swallowed so the raid can start; that mod is INACTIVE this raid. culprit: " + text + " (likely a per-map table with no 'Terminal' entry — report it to that mod's author). " + $"FULL: {ex}");
		return null;
	}
}
