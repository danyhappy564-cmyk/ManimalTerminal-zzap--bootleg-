using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianArtilleryPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(AvoidDangerLayer), "CanFightUnderArtillery", Type.EmptyTypes);
	}

	[PatchPrefix]
	private static bool Prefix(AvoidDangerLayer __instance, ref bool __result)
	{
		WildSpawnType? wildSpawnType = __instance._owner?.Profile?.Info?.Settings?.Role;
		if (!TerminalGate.On || !wildSpawnType.HasValue || !CivilianConstants.IsCivilian(wildSpawnType.Value))
		{
			return true;
		}
		__result = false;
		return false;
	}
}
