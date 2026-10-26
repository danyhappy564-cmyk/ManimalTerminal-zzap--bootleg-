using System;
using System.Reflection;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalSainCompat
{
	private const string LocationTypeName = "SAIN.Components.LocationClass";

	private static readonly object Sync = new object();

	private static Harmony _harmony;

	private static FieldInfo _foundLocation;

	private static bool _detected;

	private static bool _installed;

	private static object _lastAppliedInstance;

	internal static bool Detected => _detected;

	internal static void Install(Harmony harmony)
	{
		_harmony = harmony;
		TryInstall();
	}

	internal static void TryInstall()
	{
		if (_installed || _harmony == null)
		{
			return;
		}
		lock (Sync)
		{
			if (_installed)
			{
				return;
			}
			Type type = AccessTools.TypeByName("SAIN.Components.LocationClass");
			if (type == null)
			{
				return;
			}
			_detected = true;
			try
			{
				MethodInfo methodInfo = AccessTools.Method(type, "parseLocation");
				if (methodInfo == null)
				{
					Plugin.Log.LogWarning("[SainCompat] SAIN detected but LocationClass.parseLocation was not found — compatibility layout changed");
					return;
				}
				_foundLocation = AccessTools.Field(type, "_foundLocation");
				_harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(AccessTools.Method(typeof(TerminalSainCompat), "ParseLocationPrefix")), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				_installed = true;
				string text = type.Assembly.GetName().Version?.ToString() ?? "unknown";
				Plugin.Log.LogWarning("[SainCompat] SAIN " + text + " detected — Terminal will use SAIN's Customs location preset; real map ID remains Terminal");
			}
			catch (Exception arg)
			{
				Plugin.Log.LogWarning($"[SainCompat] SAIN location compatibility patch failed: {arg}");
			}
		}
	}

	private static bool ParseLocationPrefix(object __instance, ref object __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		try
		{
			Type type = __result?.GetType() ?? AccessTools.TypeByName("SAIN.Preset.Shared.Enums.ELocation") ?? AccessTools.TypeByName("SAIN.ELocation") ?? AccessTools.TypeByName("ELocation");
			if (type == null || !type.IsEnum)
			{
				return true;
			}
			__result = Enum.Parse(type, "Customs");
			_foundLocation?.SetValue(__instance, true);
			if (_lastAppliedInstance != __instance)
			{
				_lastAppliedInstance = __instance;
				Plugin.Log.LogWarning("[SainCompat] Terminal resolved as Customs inside SAIN for this raid");
			}
			return false;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[SainCompat] Customs substitution failed; using SAIN's native location: " + ex.Message);
			return true;
		}
	}
}
