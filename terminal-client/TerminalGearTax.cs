using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalGearTax
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_GearTax
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On && Plugin.GearConfiscation.Value && TerminalCoop.LocalHuman)
			{
				new GameObject("Terminal_GearTax").AddComponent<TaxHost>();
			}
		}
	}

	internal class TaxHost : MonoBehaviour
	{
		private void Start()
		{
			StartCoroutine(Run());
		}

		private IEnumerator Run()
		{
			while (TerminalCoop.Active && !TerminalCoop.RaidStarted)
			{
				yield return null;
			}
			yield return new WaitForSeconds(2f);
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			float deadline = Time.time + 15f;
			while (player == null && Time.time < deadline)
			{
				yield return new WaitForSeconds(0.5f);
				player = Singleton<GameWorld>.Instance?.MainPlayer;
			}
			if (player == null)
			{
				Done("no main player");
				yield break;
			}
			if (TerminalCoop.Active)
			{
				TerminalCoop.Request(TerminalEvent.GearCabinet);
				float grantDeadline = Time.realtimeSinceStartup + 60f;
				while ((!TerminalCoop.SyncReady || (!CoopFinished.Contains(player.ProfileId) && !CoopCabinets.ContainsKey(player.ProfileId))) && Time.realtimeSinceStartup < grantDeadline)
				{
					yield return null;
				}
				if (CoopFinished.Contains(player.ProfileId))
				{
					Done("already processed this raid");
					yield break;
				}
				if (!TerminalCoop.SyncReady)
				{
					Done("host state unavailable — gear untouched");
					yield break;
				}
				if (!CoopCabinets.ContainsKey(player.ProfileId))
				{
					Done("host cabinet reservation unavailable — gear untouched");
					yield break;
				}
			}
			LootableContainer safe = null;
			CompoundItem root = null;
			float bindDeadline = Time.time + 30f;
			while (Time.time < bindDeadline && !TryPickCabinet(out safe, out root))
			{
				yield return new WaitForSeconds(1f);
			}
			if (safe == null || root == null)
			{
				Done("no usable bound valberg safe in scene — gear and hands untouched");
				yield break;
			}
			if (TerminalCoop.Active)
			{
				float grantDeadline = Time.realtimeSinceStartup + 30f;
				while (!CoopPrepared.Contains(player.ProfileId) && Time.realtimeSinceStartup < grantDeadline)
				{
					yield return null;
				}
				if (!CoopPrepared.Contains(player.ProfileId))
				{
					Done("host did not acknowledge cabinet preparation — gear and hands untouched");
					yield break;
				}
			}
			else if (!TerminalCoop.Active)
			{
				Grid[] grids = root.Grids;
				for (int i = 0; i < grids.Length; i++)
				{
					grids[i].RemoveAll();
				}
			}
			bool handsCallback = false;
			if (!HandsSettled(player))
			{
				try
				{
					Plugin.Log.LogDebug("[GearTax] empty-hands requested from " + HandsState(player));
					player.SetEmptyHands((Result<IEmptyHandsController> _) =>
					{
						handsCallback = true;
					});
				}
				catch (Exception ex)
				{
					Done("could not request empty hands (" + ex.Message + ") — gear untouched");
					yield break;
				}
				float grantDeadline = Time.realtimeSinceStartup;
				float handsDeadline = grantDeadline + 30f;
				while (Time.realtimeSinceStartup < handsDeadline && (!handsCallback || !HandsSettled(player)))
				{
					yield return null;
				}
				if (!handsCallback || !HandsSettled(player))
				{
					Done($"empty-hands transition timed out after 30s ({HandsState(player)}, callback={handsCallback}) — gear untouched");
					yield break;
				}
				Plugin.Log.LogDebug($"[GearTax] empty hands settled in {Time.realtimeSinceStartup - grantDeadline:F2}s");
			}
			yield return StartCoroutine(Confiscate(player, safe, root));
			UnityEngine.Object.Destroy(gameObject);
		}

		internal static bool ApplyCoopPreparation(string profile, bool clearGeneratedLoot)
		{
			if (string.IsNullOrEmpty(profile) || !CoopCabinets.TryGetValue(profile, out var ownerId))
			{
				return false;
			}
			if (clearGeneratedLoot)
			{
				LootableContainer lootableContainer = null;
				try
				{
					lootableContainer = Array.Find(UnityEngine.Object.FindObjectsOfType<LootableContainer>(), (LootableContainer c) => c?.ItemOwner?.ID == ownerId);
				}
				catch
				{
				}
				if (!(lootableContainer?.ItemOwner?.RootItem is CompoundItem { Grids: not null } compoundItem))
				{
					return false;
				}
				int num = 0;
				Grid[] grids = compoundItem.Grids;
				for (int num2 = 0; num2 < grids.Length; num2++)
				{
					foreach (Item item in grids[num2].Items)
					{
						_ = item;
						num++;
					}
				}
				grids = compoundItem.Grids;
				for (int num2 = 0; num2 < grids.Length; num2++)
				{
					grids[num2].RemoveAll();
				}
				Plugin.Log.LogInfo($"[GearTax] synchronized preparation cleared {num} generated item(s) from reserved cabinet '{ownerId}'");
			}
			CoopPrepared.Add(profile);
			return true;
		}

		private static bool TryPickCabinet(out LootableContainer safe, out CompoundItem root)
		{
			safe = null;
			root = null;
			try
			{
				List<LootableContainer> list = new List<LootableContainer>();
				LootableContainer[] array = UnityEngine.Object.FindObjectsOfType<LootableContainer>();
				foreach (LootableContainer lootableContainer in array)
				{
					if (lootableContainer != null && lootableContainer.ItemOwner != null && (lootableContainer.name.IndexOf("valberg", StringComparison.OrdinalIgnoreCase) >= 0 || (lootableContainer.transform.parent != null && lootableContainer.transform.parent.name.IndexOf("valberg", StringComparison.OrdinalIgnoreCase) >= 0)) && lootableContainer.ItemOwner.RootItem is CompoundItem { Grids: not null } compoundItem && compoundItem.Grids.Length != 0)
					{
						list.Add(lootableContainer);
					}
				}
				if (list.Count == 0)
				{
					return false;
				}
				if (TerminalCoop.Active)
				{
					Player player = Singleton<GameWorld>.Instance?.MainPlayer;
					if (player == null || !CoopCabinets.TryGetValue(player.ProfileId, out var ownerId))
					{
						return false;
					}
					safe = list.Find((LootableContainer c) => c.ItemOwner.ID == ownerId);
					if (safe == null)
					{
						return false;
					}
				}
				else
				{
					safe = list[UnityEngine.Random.Range(0, list.Count)];
				}
				root = safe.ItemOwner.RootItem as CompoundItem;
				return root?.Grids != null && root.Grids.Length != 0;
			}
			catch
			{
			}
			return false;
		}

		private static bool HandsSettled(Player player)
		{
			if (player != null && player.HandsController is Player.EmptyHandsController)
			{
				return player.ProcessStatus == Player.EProcessStatus.None;
			}
			return false;
		}

		private static string HandsState(Player player)
		{
			try
			{
				return "controller=" + (player?.HandsController?.GetType().Name ?? "<null>") + ", process=" + (player?.ProcessStatus.ToString() ?? "<null>");
			}
			catch
			{
				return "hands state unavailable";
			}
		}

		private IEnumerator Confiscate(Player player, LootableContainer safe, CompoundItem root)
		{
			InventoryEquipment inventoryEquipment = player.Profile?.Inventory?.Equipment;
			InventoryController controller = player.InventoryController;
			if (inventoryEquipment == null || controller == null)
			{
				Done("no equipment controller");
				yield break;
			}
			List<Item> taken = new List<Item>();
			EquipmentSlot[] array = new EquipmentSlot[9]
			{
				EquipmentSlot.Headwear,
				EquipmentSlot.Earpiece,
				EquipmentSlot.FaceCover,
				EquipmentSlot.Eyewear,
				EquipmentSlot.FirstPrimaryWeapon,
				EquipmentSlot.SecondPrimaryWeapon,
				EquipmentSlot.Holster,
				EquipmentSlot.Scabbard,
				EquipmentSlot.Backpack
			};
			foreach (EquipmentSlot slotName in array)
			{
				Item item = inventoryEquipment.GetSlot(slotName)?.ContainedItem;
				if (item != null && !taken.Contains(item))
				{
					taken.Add(item);
				}
			}
			List<CompoundItem> list = new List<CompoundItem>();
			if (inventoryEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem is CompoundItem item2)
			{
				list.Add(item2);
			}
			if (inventoryEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem is CompoundItem item3)
			{
				list.Add(item3);
			}
			if (inventoryEquipment.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem is CompoundItem item4)
			{
				list.Add(item4);
			}
			foreach (Slot allSlot in inventoryEquipment.GetAllSlots())
			{
				if ((allSlot.ID ?? "").IndexOf("belt", StringComparison.OrdinalIgnoreCase) >= 0 && allSlot.ContainedItem is CompoundItem item5 && !list.Contains(item5))
				{
					list.Add(item5);
				}
			}
			foreach (CompoundItem item6 in list)
			{
				if (item6.Grids == null)
				{
					continue;
				}
				Grid[] grids = item6.Grids;
				for (int i = 0; i < grids.Length; i++)
				{
					foreach (Item item7 in new List<Item>(grids[i].Items))
					{
						if (!taken.Contains(item7))
						{
							taken.Add(item7);
						}
					}
				}
			}
			if (taken.Count == 0)
			{
				Done("nothing to confiscate");
				yield break;
			}
			Item last = player.LastEquippedWeaponOrKnifeItem;
			int boundBefore = CountTakenFastAccess(player, taken);
			IPlayerSearchController searchController = player.SearchController;
			if (searchController == null)
			{
				Done("no player search controller — gear untouched");
				yield break;
			}
			ActiveSearchController activeSearch = searchController as ActiveSearchController;
			SearchableItem searchableRoot = root as SearchableItem;
			bool wasSearched = searchableRoot != null && searchController.IsSearched(searchableRoot);
			try
			{
				try
				{
					if (!searchController.IsItemKnown(root))
					{
						searchController.SetItemAsKnown(root, raiseEvents: false);
					}
					if (root is SearchableItem searchableItem && !searchController.IsSearched(searchableItem))
					{
						searchController.SetItemAsSearched(searchableItem);
					}
				}
				catch (Exception ex)
				{
					Done("could not authorize cabinet transfer (" + ex.Message + ") — gear untouched");
					yield break;
				}
				int placed = 0;
				int failed = 0;
				List<Item> list2 = new List<Item>();
				foreach (Item item8 in taken)
				{
					int num = list2.Count;
					while (num > 0 && CellArea(list2[num - 1]) < CellArea(item8))
					{
						num--;
					}
					list2.Insert(num, item8);
				}
				foreach (Item it in list2)
				{
					GridItemAddress gridItemAddress = null;
					Grid[] grids = root.Grids;
					foreach (Grid grid in grids)
					{
						LocationInGrid locationInGrid = grid.FindFreeSpace(it);
						if (!(locationInGrid == null))
						{
							gridItemAddress = grid.CreateItemAddress(locationInGrid);
							break;
						}
					}
					if (gridItemAddress == null)
					{
						failed++;
						Plugin.Log.LogError("[GearTax] NO ROOM in cabinet for '" + it.LocalizedName() + "' — item remains on player");
						continue;
					}
					OperationResult<MoveResult> operationResult;
					try
					{
						operationResult = ItemManipulator.Move(it, gridItemAddress, controller, simulate: true);
					}
					catch (Exception ex2)
					{
						failed++;
						Plugin.Log.LogWarning("[GearTax] native move construction failed for '" + it.LocalizedName() + "': " + ex2.Message);
						continue;
					}
					if (operationResult.Failed)
					{
						failed++;
						Plugin.Log.LogWarning($"[GearTax] native move rejected for '{it.LocalizedName()}': {operationResult.Error}");
						continue;
					}
					Task<IResult> task;
					try
					{
						task = controller.TryRunNetworkTransaction(operationResult);
					}
					catch (Exception ex3)
					{
						failed++;
						Plugin.Log.LogWarning("[GearTax] native transaction start failed for '" + it.LocalizedName() + "': " + ex3.Message);
						continue;
					}
					float transactionDeadline = Time.realtimeSinceStartup + 30f;
					while (!task.IsCompleted && Time.realtimeSinceStartup < transactionDeadline)
					{
						yield return null;
					}
					if (!task.IsCompleted)
					{
						failed++;
						Plugin.Log.LogError("[GearTax] native transaction timed out for '" + it.LocalizedName() + "' — confiscation stopped to avoid overlapping inventory operations");
						while (!task.IsCompleted)
						{
							yield return null;
						}
						if (task.IsFaulted)
						{
							Plugin.Log.LogWarning("[GearTax] delayed transaction faulted: " + task.Exception?.GetBaseException().Message);
						}
						break;
					}
					if (task.IsFaulted || task.IsCanceled)
					{
						failed++;
						Plugin.Log.LogWarning("[GearTax] native transaction faulted for '" + it.LocalizedName() + "': " + (task.Exception?.GetBaseException().Message ?? "cancelled"));
						continue;
					}
					IResult result = task.Result;
					if (result == null || result.Failed)
					{
						failed++;
						Plugin.Log.LogWarning("[GearTax] native transaction failed for '" + it.LocalizedName() + "': " + (result?.Error?.ToString() ?? "no result"));
						continue;
					}
					if (!IsOwnedBy(it, safe.ItemOwner))
					{
						failed++;
						Plugin.Log.LogError("[GearTax] native transaction completed but cabinet does not own '" + it.LocalizedName() + "' — confiscation stopped");
						break;
					}
					placed++;
				}
				bool flag = last != null && IsOwnedBy(last, safe.ItemOwner);
				if (flag)
				{
					if (LastEquippedField != null)
					{
						LastEquippedField.SetValue(player, null);
					}
					else
					{
						Plugin.Log.LogWarning("[GearTax] could not clear stale last-equipped item field");
					}
				}
				int num2 = CountTakenFastAccess(player, taken);
				bool flag2 = failed == 0 && placed == taken.Count;
				foreach (Item item9 in taken)
				{
					flag2 &= IsOwnedBy(item9, safe.ItemOwner);
				}
				if (TerminalCoop.Active & flag2)
				{
					TerminalCoop.Request(TerminalEvent.GearDone);
				}
				Plugin.Log.LogInfo(string.Format("[GearTax] {0} {1}/{2} item(s) -> '{3}' via native transactions ", flag2 ? "confiscated" : "PARTIAL; will resume after reconnect", placed, taken.Count, safe.name) + $"(failed={failed}, quickBindingsCleared={Math.Max(0, boundBefore - num2)}, staleBindings={num2}, " + $"lastEquippedCleared={flag}) at {safe.transform.position}");
				if (!flag2)
				{
					yield break;
				}
				try
				{
					NotificationManager.DisplayMessageNotification("Port security has confiscated your equipment. It is locked in one of the terminal's equipment cabinets.", ENotificationDurationType.Long, ENotificationIconType.Alert, Color.yellow);
				}
				catch
				{
				}
			}
			finally
			{
				if (activeSearch != null)
				{
					foreach (Item item10 in taken)
					{
						if (IsOwnedBy(item10, safe.ItemOwner))
						{
							TerminalCabinetSearch.ForgetDeposit(activeSearch, item10);
						}
					}
					if (searchableRoot != null && !wasSearched)
					{
						activeSearch._searchedItems.Remove(searchableRoot);
					}
					Plugin.Log.LogInfo("[GearTax] cabinet transfer authorization released; deposited gear requires searching");
				}
			}
		}

		private static int CountTakenFastAccess(Player player, List<Item> taken)
		{
			try
			{
				InventoryController inventoryController = player.InventoryController;
				if (inventoryController?.Inventory?.FastAccess?.BoundItems == null)
				{
					return 0;
				}
				HashSet<Item> hashSet = new HashSet<Item>();
				foreach (Item value in inventoryController.Inventory.FastAccess.BoundItems.Values)
				{
					if (value != null && taken.Contains(value))
					{
						hashSet.Add(value);
					}
				}
				return hashSet.Count;
			}
			catch
			{
				return 0;
			}
		}

		private static bool IsOwnedBy(Item item, IItemOwner owner)
		{
			try
			{
				IItemOwner itemOwner = item?.Parent?.GetOwner();
				return itemOwner != null && owner != null && (itemOwner == owner || itemOwner.ID == owner.ID);
			}
			catch
			{
				return false;
			}
		}

		private static int CellArea(Item it)
		{
			try
			{
				IntVec2 intVec = it.CalculateCellSize();
				return intVec.X * intVec.Y;
			}
			catch
			{
				return 1;
			}
		}

		private void Done(string why)
		{
			Plugin.Log.LogInfo("[GearTax] " + why);
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	private const string CabinetTpl = "68a4c0ffee0000000000cab1";

	internal static readonly Dictionary<string, string> CoopCabinets = new Dictionary<string, string>();

	internal static readonly HashSet<string> CoopFinished = new HashSet<string>();

	internal static readonly HashSet<string> CoopPrepared = new HashSet<string>();

	private static readonly FieldInfo LastEquippedField = AccessTools.Field(typeof(Player), "_lastEquippedWeaponOrKnifeItem");
}
