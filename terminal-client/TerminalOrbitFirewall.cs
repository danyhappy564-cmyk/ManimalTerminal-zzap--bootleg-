using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalOrbitFirewall
{
	private static bool _done;

	private static bool _logged;

	private static bool _layerLogged;

	internal static void Apply()
	{
		if (_done)
		{
			return;
		}
		(string, string)[] array = new (string, string)[16]
		{
			("Orbit.Patches.OrbitInitPatch", "Postfix"),
			("Orbit.Patches.OrbitTickPatch", "Postfix"),
			("Orbit.Patches.DoorCarverShrinkPatch", "Patch"),
			("Orbit.Patches.DoorUnlockTracePatch", "Patch"),
			("Orbit.Patches.SoftTeleportTracePatch", "Patch"),
			("Orbit.Patches.HardTeleportTracePatch", "Patch"),
			("Orbit.Patches.BotVaultingPatch", "Patch"),
			("Orbit.Patches.MovementContextHumanizePatch", "Patch"),
			("Orbit.Patches.ManualFixedUpdateSkipPatch", "PatchPrefix"),
			("Orbit.Patches.AirdropLandedPatch", "Postfix"),
			("Orbit.Patches.InventoryChangePatch", "Postfix"),
			("Orbit.Patches.CorpseRegistrationPatch", "Postfix"),
			("Orbit.Patches.RescueInterceptPatch", "Prefix"),
			("Orbit.Patches.AssaultEnemyFarBypassPatch", "Patch"),
			("Orbit.Patches.ExfilLayerBypassPatch", "Patch"),
			("Orbit.Patches.PtrlBirdEyeBypassPatch", "Patch")
		};
		List<MethodInfo> list = new List<MethodInfo>();
		(string, string)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple = array2[i];
			Type type = TerminalTypes.ByName(tuple.Item1);
			MethodInfo methodInfo = ((type != null) ? AccessTools.Method(type, tuple.Item2) : null);
			if (methodInfo != null)
			{
				list.Add(methodInfo);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		Type type2 = TerminalTypes.ByName("Orbit.Brain.OrbitBrainLayer");
		MethodInfo methodInfo2 = ((type2 != null) ? AccessTools.Method(type2, "IsExcludedRole", new Type[1] { typeof(BotOwner) }) : null);
		if (methodInfo2 == null)
		{
			Plugin.Log.LogWarning("[OrbitFirewall] ORBIT hooks found but OrbitBrainLayer exclusion was not; firewall deferred until next BotsController.Init");
			return;
		}
		Harmony harmony = new Harmony("com.manimal.terminal.orbit-firewall");
		HarmonyMethod harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(TerminalOrbitFirewall), "VoidHookPrefix"));
		HarmonyMethod harmonyMethod2 = new HarmonyMethod(AccessTools.Method(typeof(TerminalOrbitFirewall), "BoolHookPrefix"));
		foreach (MethodInfo item in list)
		{
			harmony.Patch((MethodBase)item, (item.ReturnType == typeof(bool)) ? harmonyMethod2 : harmonyMethod, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		}
		harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(AccessTools.Method(typeof(TerminalOrbitFirewall), "ForceExcludedPrefix")), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		_done = true;
		Plugin.Log.LogInfo($"[OrbitFirewall] armed layer exclusion + {list.Count}/{array.Length} Terminal-only ORBIT hook gates");
	}

	private static bool VoidHookPrefix()
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		LogRuntimeBlockOnce();
		return false;
	}

	private static bool BoolHookPrefix(ref bool __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		LogRuntimeBlockOnce();
		__result = true;
		return false;
	}

	private static bool ForceExcludedPrefix(ref bool __result)
	{
		if (!TerminalGate.On)
		{
			return true;
		}
		__result = true;
		if (!_layerLogged)
		{
			_layerLogged = true;
			Plugin.Log.LogWarning("[OrbitFirewall] ORBIT brain layer made inert on Terminal before manager/agent construction");
		}
		return false;
	}

	private static void LogRuntimeBlockOnce()
	{
		if (!_logged)
		{
			_logged = true;
			Plugin.Log.LogWarning("[OrbitFirewall] ORBIT runtime disabled on Terminal — stage director and native bot movement retain ownership");
		}
	}
}
