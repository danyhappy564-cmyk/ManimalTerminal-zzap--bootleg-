using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianKnifePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(BotWeaponSelector), "TryChangeToSlot");
	}

	[PatchPrefix]
	private static void PatchPrefix(BotWeaponSelector __instance, ref EquipmentSlot slot, ref bool changeToMain)
	{
		try
		{
			WildSpawnType? wildSpawnType = __instance._owner?.Profile?.Info?.Settings?.Role;
			if (wildSpawnType.HasValue && CivilianConstants.IsCivilian(wildSpawnType.Value))
			{
				slot = EquipmentSlot.Scabbard;
				changeToMain = false;
			}
		}
		catch
		{
		}
	}
}
