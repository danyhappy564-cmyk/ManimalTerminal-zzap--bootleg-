using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal;

internal static class TerminalCabinetSearch
{
	internal sealed class RevealPatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(ActiveSearchController), "SetItemAsKnown", new Type[2]
			{
				typeof(Item),
				typeof(bool)
			});
		}

		[PatchPrefix]
		internal static void Before(ActiveSearchController __instance, Item item, bool raiseEvents, out bool __state)
		{
			__state = raiseEvents && item != null && Pending.TryGetValue(__instance, out var value) && value.TryGetValue(item, out var value2) && value2.Equals(item.Parent) && __instance._discoveredItems.Contains(item) && !__instance.IsItemKnown(item);
		}

		[PatchPostfix]
		internal static void After(ActiveSearchController __instance, Item item, bool __state)
		{
			if (item != null && __instance.IsItemKnown(item))
			{
				if (Pending.TryGetValue(__instance, out var value))
				{
					value.Remove(item);
				}
				if (__state)
				{
					__instance.InvokeItemFound(item);
				}
			}
		}
	}

	private static readonly ConditionalWeakTable<ActiveSearchController, Dictionary<Item, ItemAddress>> Pending = new ConditionalWeakTable<ActiveSearchController, Dictionary<Item, ItemAddress>>();

	internal static void ForgetDeposit(ActiveSearchController search, Item item)
	{
		Pending.GetOrCreateValue(search)[item] = item.Parent;
		search.ForgetItem(item);
		search.RemoveItemFromTemporaryKnown(item);
	}
}
