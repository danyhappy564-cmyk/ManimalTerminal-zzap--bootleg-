using System;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalCrashGuard
{
	private const string TargetTypeName = "EFT.EftClientBackendSession";

	private const string TargetMethodName = "LocalRaidStarted";

	internal static void TryPatch(Harmony h)
	{
		Type type = AccessTools.TypeByName("EFT.EftClientBackendSession");
		if (type == null)
		{
			Plugin.Log.LogError("[CrashGuard] could not resolve type 'EFT.EftClientBackendSession' — obfuscated name likely drifted on this game build. This crash site is UNGUARDED until fixed.");
			return;
		}
		MethodInfo methodInfo = AccessTools.Method(type, "LocalRaidStarted");
		if (methodInfo == null)
		{
			Plugin.Log.LogError("[CrashGuard] resolved type 'EFT.EftClientBackendSession' but method 'LocalRaidStarted' not found on it (overload/rename?). This crash site is UNGUARDED until fixed.");
			return;
		}
		h.Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(TerminalCrashGuard), "Finalizer"), (HarmonyMethod)null);
		Plugin.Log.LogInfo("[CrashGuard] guarding EFT.EftClientBackendSession.LocalRaidStarted on Terminal");
	}

	private static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
	{
		if (__exception == null)
		{
			return null;
		}
		if (!TerminalGate.On)
		{
			return __exception;
		}
		Plugin.Log.LogError("[CrashGuard] swallowed exception in " + __originalMethod?.DeclaringType?.FullName + "." + $"{__originalMethod?.Name} during Terminal raid start: {__exception}");
		return null;
	}
}
