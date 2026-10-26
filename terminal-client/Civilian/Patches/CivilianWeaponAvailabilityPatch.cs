using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianWeaponAvailabilityPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(BotWeaponSelector), "FindItemsInSlots", new Type[1] { typeof(bool) });
	}

	[PatchPostfix]
	private static void PatchPostfix(BotWeaponSelector __instance)
	{
		WildSpawnType? wildSpawnType = __instance._owner?.Profile?.Info?.Settings?.Role;
		if (wildSpawnType.HasValue && CivilianConstants.IsCivilian(wildSpawnType.Value))
		{
			__instance.CanChangeToMeleeWeapons = true;
			__instance._canChangeToSupportWeapons = false;
			__instance._canChangeToSecondWeapons = false;
		}
	}
}
