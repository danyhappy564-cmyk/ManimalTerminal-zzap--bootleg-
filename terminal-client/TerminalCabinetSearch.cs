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

		// zzap: both hooks used to call IsItemKnown on EVERY SetItemAsKnown, on every map.
		// LootInstantSearch prefixes IsItemKnown and calls SetItemAsKnown from it, so the
		// two mods called each other until the stack overflowed (raid crash while
		// shooting/looting). Now: only items GearTax actually parked in Pending are looked
		// at (Pending is empty outside Terminal), and a re-entry guard stops the loop when
		// a foreign IsItemKnown hook calls back into SetItemAsKnown. Pending items keep the
		// exact original behaviour.
		[ThreadStatic]
		private static int _depth;

		[PatchPrefix]
		internal static void Before(ActiveSearchController __instance, Item item, bool raiseEvents, out bool __state)
		{
			__state = false;
			if (_depth > 0 || !raiseEvents || !IsPending(__instance, item, out var parent))
			{
				return;
			}
			_depth++;
			try
			{
				__state = parent != null && parent.Equals(item.Parent) &&__instance._discoveredItems.Contains(item) && !__instance.IsItemKnown(item);
			}
			finally
			{
				_depth--;
			}
		}

		[PatchPostfix]
		internal static void After(ActiveSearchController __instance, Item item, bool __state)
		{
			if (_depth > 0 || !IsPending(__instance, item, out _))
			{
				return;
			}
			_depth++;
			try
			{
				if (__instance.IsItemKnown(item))
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
			finally
			{
				_depth--;
			}
		}

		private static bool IsPending(ActiveSearchController search, Item item, out ItemAddress parent)
		{
			parent = null;
			return search != null && item != null && Pending.TryGetValue(search, out var value) && value.TryGetValue(item, out parent);
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
