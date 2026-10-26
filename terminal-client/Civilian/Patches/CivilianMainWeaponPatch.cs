using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianMainWeaponPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(BotWeaponSelector), "SetSlotItem", new Type[2]
		{
			typeof(Callback<IHandsController>),
			typeof(bool)
		});
	}

	[PatchPrefix]
	private static bool PatchPrefix(BotWeaponSelector __instance, Callback<IHandsController> onSpawn)
	{
		BotOwner owner = __instance._owner;
		if ((object)owner == null || owner.BotState != EBotState.Active || owner.Profile?.Info?.Settings == null || !CivilianConstants.IsCivilian(owner.Profile.Info.Settings.Role))
		{
			return true;
		}
		Player getPlayer = owner.GetPlayer;
		Item item = getPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem;
		if (item == null)
		{
			return true;
		}
		__instance.FindItemsInSlots(anyway: false);
		__instance.IsChanging = true;
		__instance._mainWeapon = EquipmentSlot.Scabbard;
		if (getPlayer.HandsController?.Item == item)
		{
			onSpawn(new Result<IHandsController>(getPlayer.HandsController));
		}
		else
		{
			getPlayer.SetSlotItem(EquipmentSlot.Scabbard, onSpawn);
		}
		return false;
	}
}
