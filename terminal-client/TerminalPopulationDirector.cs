using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Systems.Effects;
using Comfort.Common;
using Diz.Binding;
using EFT;
using EFT.Game.Spawning;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalPopulationDirector
{
	internal sealed class Entry
	{
		internal string ProfileId;

		internal BotOwner Bot;

		internal Player Player;

		internal WildSpawnType Role;

		internal bool OrdinaryScav;

		internal string OriginalZone;

		internal string LogicalZone;

		internal int OriginalStage;

		internal int LogicalStage;

		internal int LogicalTier;

		internal bool Alive;

		internal bool Removed;

		internal float SpawnAt;

		internal float DeathAt = -1f;

		internal float LastRecycledAt = -1f;

		internal int RecycleCount;

		internal float VisualRepairUntil = -1f;

		internal List<Renderer> VisualRepairRenderers;

		internal List<SkinnedMeshRenderer> VisualRepairSkins;
	}

	[HarmonyPatch(typeof(BotsController), "Init")]
	internal static class Patch_Attach
	{
		[HarmonyPostfix]
		private static void Postfix(BotsController __instance)
		{
			if (TerminalGate.On && TerminalCoop.Authority && __instance?.BotSpawner != null)
			{
				ResetForRaid();
				_spawner = __instance.BotSpawner;
				_spawner.OnBotCreated += OnBotCreated;
				_spawner.OnBotRemoved += OnBotRemoved;
				_host = new GameObject("Terminal_PopulationDirector");
				_host.AddComponent<Host>();
				Plugin.Log.LogInfo("[Population] ledger armed (event-driven births/deaths, scav-priority corpse cleanup); " + $"recycling scav={Plugin.ScavRecycler.Value} RUAF={Plugin.RuafRecycler.Value} BD={Plugin.BlackDivisionRecycler.Value}; " + $"corpse retention scav={Plugin.ScavCorpseLifetime.Value:0}s/{Plugin.ScavCorpseCleanupDistance.Value:0}m " + $"special={Plugin.SpecialCorpseLifetime.Value:0}s/{Plugin.SpecialCorpseCleanupDistance.Value:0}m");
			}
		}
	}

	[HarmonyPatch(typeof(AITaskManager), "UpdateSimpleTasks")]
	internal static class Patch_DropDestroyedBotTasks
	{
		[HarmonyPrefix]
		private static void Prefix(AITaskManager __instance)
		{
			if (!TerminalGate.On || __instance?._simpleTasks == null)
			{
				return;
			}
			foreach (AITaskManager.AISimpleTaskData simpleTask in __instance._simpleTasks)
			{
				if (simpleTask != null && (object)simpleTask.Bot != null && !simpleTask.Bot)
				{
					simpleTask.IsCancelRequested = true;
				}
			}
		}
	}

	[HarmonyPatch(typeof(EffectsCommutator), "UpdatePlayersBleedings")]
	internal static class Patch_DropDestroyedBleedingPlayers
	{
		private static readonly FieldInfo BleedingPlayers = AccessTools.Field(typeof(EffectsCommutator), "_playerBleedingDecalTimes");

		// zzap perf: this runs every frame; GetType().GetProperty("Key") per entry was a
		// reflection lookup per bleeding player per frame — cache it per element type
		private static Type _keyOwner;

		private static PropertyInfo _keyProp;

		private static object KeyOf(object entry)
		{
			if (entry == null)
			{
				return null;
			}
			Type type = entry.GetType();
			if (type != _keyOwner)
			{
				_keyOwner = type;
				_keyProp = type.GetProperty("Key");
			}
			return _keyProp?.GetValue(entry);
		}

		[HarmonyPrefix]
		private static void Prefix(EffectsCommutator __instance)
		{
			if (!TerminalGate.On || __instance == null || !(BleedingPlayers?.GetValue(__instance) is IList list))
			{
				return;
			}
			for (int num = list.Count - 1; num >= 0; num--)
			{
				object obj = list[num];
				object obj2 = KeyOf(obj);
				if (obj2 == null || (obj2 is UnityEngine.Object obj3 && !obj3))
				{
					list.RemoveAt(num);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Corpse), "Init", new Type[]
	{
		typeof(string),
		typeof(InventoryEquipment),
		typeof(BodyCustomization),
		typeof(bool),
		typeof(GameWorld),
		typeof(EPlayerSide),
		typeof(Vector3),
		typeof(Transform),
		typeof(bool),
		typeof(BindableState<Item>),
		typeof(bool),
		typeof(ContainerCollectionView),
		typeof(MongoID)
	})]
	internal static class Patch_AuditCorpseIdentity
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix(out TerminalTickProfiler.Scope __state)
		{
			__state = TerminalGate.On ? TerminalTickProfiler.Measure("Patch.CorpseInit.total") : default;
		}
		[HarmonyFinalizer]
		private static void Finalizer(TerminalTickProfiler.Scope __state) { __state.Dispose(); }

		[HarmonyPostfix]
		private static void Postfix(Corpse __instance, string playerProfileID, InventoryEquipment equipment)
		{
			if (!TerminalGate.On || !__instance || string.IsNullOrEmpty(playerProfileID))
			{
				return;
			}
			try
			{
				using var scope = TerminalTickProfiler.Measure("Population.CorpseIdentity");
				Entries.TryGetValue(playerProfileID, out var value);
				bool flag = value != null && (bool)value.Player && value.Player.Equipment == equipment;
				int num = __instance.gameObject.GetComponents<Corpse>().Length;
				Plugin.Log.LogInfo("[CorpseIdentity] created profile=" + playerProfileID + " role=" + ((value != null) ? value.Role.ToString() : "untracked") + string.Format(" equipment={0} playerEquipmentMatch={1}", (equipment != null) ? equipment.Id.ToString() : "NULL", flag) + $" corpseComponentsOnObject={num} recycled={value?.RecycleCount ?? 0}");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[CorpseIdentity] creation audit failed: " + ex.Message);
			}
		}
	}

	private enum RecyclePool
	{
		Scav,
		Ruaf,
		BlackDivision
	}

	private enum RecycleReject
	{
		Invalid,
		WrongFaction,
		Progress,
		TooYoung,
		NearOrVisible,
		Inactive,
		Combat,
		Error,
		Reserved
	}

	private sealed class Host : MonoBehaviour
	{
		private float _nextCleanup;

		private float _nextHeartbeat;

		private IEnumerator _cleanupSweep;

		private void Update()
		{
			using (TerminalTickProfiler.Measure("Population.Host.Update"))
			{
				if (!TerminalGate.On)
				{
					foreach (Entry value in Entries.Values)
					{
						FinishBodyVisibilityRepair(value);
					}
					_cleanupSweep = null;
					UnityEngine.Object.Destroy(gameObject);
					return;
				}
				ReconcileNativeBots();
				foreach (Entry value2 in Entries.Values)
				{
					if (!(value2.VisualRepairUntil < 0f))
					{
						if (value2.Alive && !value2.Removed && (bool)value2.Player && value2.Player.HealthController != null && value2.Player.HealthController.IsAlive && value2.VisualRepairUntil >= Time.time)
						{
							RepairRecycledBodyVisibility(value2, out var _, out var _);
						}
						else
						{
							FinishBodyVisibilityRepair(value2);
						}
					}
				}
				bool flag = Plugin.ScavCorpseCleanupKey.Value.IsDown();
				if (flag || (_cleanupSweep == null && Plugin.ScavCorpseCleanup.Value && Time.time >= _nextCleanup))
				{
					_nextCleanup = Time.time + 10f;
					_cleanupSweep = SweepCorpses(flag);
				}
				if (_cleanupSweep != null && !_cleanupSweep.MoveNext())
				{
					_cleanupSweep = null;
				}
				if (!(Time.time >= _nextHeartbeat))
				{
					return;
				}
				_nextHeartbeat = Time.time + 60f;
				int num = 0;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				foreach (Entry value3 in Entries.Values)
				{
					if (value3.Alive && !value3.Removed)
					{
						num++;
						if (value3.OrdinaryScav)
						{
							num2++;
						}
					}
					else if (!value3.Removed && value3.OrdinaryScav)
					{
						num3++;
					}
					else if (!value3.Removed)
					{
						num4++;
					}
				}
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				int num8 = 0;
				int num9 = 0;
				try
				{
					BotSpawner obj = ((!Singleton<IBotGame>.Instantiated) ? null : Singleton<IBotGame>.Instance.BotsController?.BotSpawner);
					num5 = obj?._allBotsCount ?? 0;
					num6 = obj?._inSpawnProcess ?? 0;
					num7 = (obj?._botCreator?.BotsLoading).GetValueOrDefault();
					num8 = BotCreationData.ProfilesLoadingProcess;
					num9 = (obj?.SpawnDelaysService?.WaitCount).GetValueOrDefault();
				}
				catch
				{
				}
				Plugin.Log.LogInfo($"[Population] ledgerAlive={num} livingScavs={num2} pendingScavs={TerminalSpawnGate.PendingScavCount} nativeAlive={num5} activating={num6} botCreatorLoading={num7} profilesLoading={num8} delayed={num9} scavCorpses={num3} specialCorpses={num4} " + $"created={_created} died={_died} recovered={_recoveredBirths} recycled={_recycledPlacements} avoided={_avoidedCreations} retired={_retiredCorpses}");
			}
		}

		private static IEnumerator SweepCorpses(bool manual)
		{
			GameWorld world = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
			if (!(world ? world.MainPlayer : null))
			{
				yield break;
			}
			int limit = (manual ? int.MaxValue : Plugin.ScavCorpseCleanupPerSweep.Value);
			int removed = 0;
			List<Entry> list = new List<Entry>(Entries.Values);
			list.Sort((Entry a, Entry b) =>
			{
				int num3 = b.OrdinaryScav.CompareTo(a.OrdinaryScav);
				return (num3 == 0) ? a.DeathAt.CompareTo(b.DeathAt) : num3;
			});
			foreach (Entry item in list)
			{
				if (!TerminalGate.On || !world)
				{
					yield break;
				}
				if (removed >= limit)
				{
					break;
				}
				if (item.Alive || item.Removed || !item.Player)
				{
					continue;
				}
				float num = (item.OrdinaryScav ? Plugin.ScavCorpseLifetime.Value : Plugin.SpecialCorpseLifetime.Value);
				float num2 = (item.OrdinaryScav ? Plugin.ScavCorpseCleanupDistance.Value : Plugin.SpecialCorpseCleanupDistance.Value);
				if (!manual && (item.DeathAt < 0f || Time.time - item.DeathAt < num))
				{
					continue;
				}
				Corpse component = item.Player.GetComponent<Corpse>();
				if ((bool)component && !IsNearAnyHuman(world, component.transform.position, num2 * num2))
				{
					IEnumerator retirement = RetireCorpse(world, item, component);
					while (true)
					{
						bool pending;
						using (TerminalTickProfiler.Measure("Population.RetireCorpse")) pending = retirement.MoveNext();
						if (!pending) break;
						yield return null;
					}
					if (item.Removed) removed++;
					yield return null;
				}
			}
			if (manual)
			{
				Plugin.Log.LogWarning($"[CorpseCleanup] manual sweep retired {removed} distance-eligible AI corpse(s), scav priority first");
			}
		}

		private static bool IsNearAnyHuman(GameWorld world, Vector3 corpsePosition, float distanceSq)
		{
			bool flag = false;
			try
			{
				List<IPlayer> registeredPlayers = world.RegisteredPlayers;
				for (int i = 0; i < registeredPlayers.Count; i++)
				{
					IPlayer player = registeredPlayers[i];
					if (player != null && !player.IsAI)
					{
						flag = true;
						if ((player.Position - corpsePosition).sqrMagnitude < distanceSq)
						{
							return true;
						}
					}
				}
			}
			catch
			{
			}
			Player player2 = (world ? world.MainPlayer : null);
			if (!flag && (bool)player2)
			{
				return (player2.Position - corpsePosition).sqrMagnitude < distanceSq;
			}
			return false;
		}

		// A sweep already retired at most one corpse per frame. Split ONE corpse's
		// independent teardown stages too; never hold a profiler scope across yield.
		private static IEnumerator RetireCorpse(GameWorld world, Entry e, Corpse corpse)
		{
			if (!world || !corpse || !e.Player || e.Alive || e.Removed) yield break;
			{
				FinishBodyVisibilityRepair(e);
				Player player = e.Player;
				int num = 0;
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.Bleeding");
					if (Singleton<Effects>.Instantiated)
					{
						Singleton<Effects>.Instance.EffectsCommutator?.StopBleedingForPlayer(player);
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] bleeding unregister failed for " + e.ProfileId + ": " + ex.Message);
				}
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.Tasks");
					num = CancelDelayedBotTasks(e.Bot);
				}
				catch (Exception ex2)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] delayed-AI purge failed for " + e.ProfileId + ": " + ex2.Message);
				}
				yield return null;
				if (!world || !corpse || !player || player.HealthController?.IsAlive == true) yield break;
				float distance = e.OrdinaryScav ? Plugin.ScavCorpseCleanupDistance.Value : Plugin.SpecialCorpseCleanupDistance.Value;
				if (IsNearAnyHuman(world, corpse.transform.position, distance * distance)) yield break;
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.BodyLedger");
					DeadBodiesController deadBodiesController = ((!e.Bot) ? null : e.Bot.BotsGroup?.DeadBodiesController);
					if (deadBodiesController != null)
					{
						for (int num2 = deadBodiesController.Bodies.Count - 1; num2 >= 0; num2--)
						{
							DeadBody deadBody = deadBodiesController.Bodies[num2];
							if (deadBody != null && (object)deadBody.Player == player)
							{
								deadBodiesController.RemoveBody(deadBody);
							}
						}
					}
				}
				catch (Exception ex3)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] body-ledger removal failed for " + e.ProfileId + ": " + ex3.Message);
				}
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.DestroyInfo");
					e.Bot?.BotsController?.DestroyInfo(player);
				}
				catch (Exception ex4)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] bot metadata unregister failed for " + e.ProfileId + ": " + ex4.Message);
				}
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.LocalRegistries");
					RemoveFromLocalGameRegistries(e.ProfileId, player);
				}
				catch (Exception ex5)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] local-game unregister failed for " + e.ProfileId + ": " + ex5.Message);
				}
				yield return null;
				if (!world || !corpse || !player || player.HealthController?.IsAlive == true) yield break;
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.WorldUnregister");
					if ((bool)world)
					{
						world.UnregisterPlayer(player);
					}
				}
				catch (Exception ex6)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] world unregister failed for " + e.ProfileId + ": " + ex6.Message);
				}
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.RetainedRegistries");
					RemoveFromGameWorldRetainedRegistries(world, e.ProfileId, player);
				}
				catch (Exception ex7)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] retained-world unregister failed for " + e.ProfileId + ": " + ex7.Message);
				}
				yield return null;
				if (!world || !corpse || !player || player.HealthController?.IsAlive == true) yield break;
				try
				{
					using var scope = TerminalTickProfiler.Measure("Population.RetireCorpse.Hands");
					player.method_118();
				}
				catch (Exception ex8)
				{
					Plugin.Log.LogWarning("[CorpseCleanup] hands release failed for " + e.ProfileId + ": " + ex8.Message);
				}
				yield return null;
				if (!world || !corpse || !player || player.HealthController?.IsAlive == true) yield break;
				try
				{
					e.Removed = true;
					using (TerminalTickProfiler.Measure("Population.RetireCorpse.Kill")) corpse.Kill();
					try
					{
						if ((bool)corpse)
						{
							using (TerminalTickProfiler.Measure("Population.RetireCorpse.DestroyComponent")) UnityEngine.Object.Destroy(corpse);
						}
					}
					catch (Exception ex9)
					{
						Plugin.Log.LogWarning("[CorpseIdentity] retired component cleanup failed for " + e.ProfileId + ": " + ex9.Message);
					}
					_retiredCorpses++;
					Plugin.Log.LogInfo(string.Format("[CorpseCleanup] safely retired {0} {1} role={2} age={3:0}s aiTasksCancelled={4}", e.OrdinaryScav ? "ordinary scav" : "remote special", e.ProfileId, e.Role, Time.time - e.DeathAt, num));
				}
				catch (Exception arg)
				{
					e.Removed = false;
					Plugin.Log.LogError($"[CorpseCleanup] Corpse.Kill failed for {e.ProfileId}; object left in place: {arg}");
				}
			}
		}

		private static int CancelDelayedBotTasks(BotOwner bot)
		{
			if (!bot || bot.AITaskManager == null || bot.AITaskManager._simpleTasks == null)
			{
				return 0;
			}
			int num = 0;
			foreach (AITaskManager.AISimpleTaskData item in new List<AITaskManager.AISimpleTaskData>(bot.AITaskManager._simpleTasks))
			{
				if (item != null && (object)item.Bot == bot)
				{
					bot.AITaskManager.CancelDelayedTask(item.Id);
					num++;
				}
			}
			return num;
		}

		private static void RemoveFromGameWorldRetainedRegistries(GameWorld world, string profileId, Player player)
		{
			if (!world || string.IsNullOrEmpty(profileId) || !player)
			{
				return;
			}
			Type type = world.GetType();
			while (type != null)
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				for (int i = 0; i < fields.Length; i++)
				{
					object value = fields[i].GetValue(world);
					IObserverToPlayerBridge value3;
					if (value is IDictionary<string, Player> dictionary)
					{
						if (dictionary.TryGetValue(profileId, out var value2) && (object)value2 == player)
						{
							dictionary.Remove(profileId);
						}
					}
					else if (value is IDictionary<string, IObserverToPlayerBridge> dictionary2 && dictionary2.TryGetValue(profileId, out value3) && value3 != null && value3.iPlayer == player)
					{
						try
						{
							value3.Dispose();
						}
						catch
						{
						}
						dictionary2.Remove(profileId);
					}
				}
				type = type.BaseType;
			}
		}

		private static void RemoveFromLocalGameRegistries(string profileId, Player player)
		{
			if (string.IsNullOrEmpty(profileId) || player == null || !Singleton<AbstractGame>.Instantiated)
			{
				return;
			}
			object instance = Singleton<AbstractGame>.Instance;
			Type type = instance.GetType();
			while (type != null)
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (typeof(IDictionary<string, Player>).IsAssignableFrom(fieldInfo.FieldType) && fieldInfo.GetValue(instance) is IDictionary<string, Player> dictionary && dictionary.TryGetValue(profileId, out var value) && (object)value == player)
					{
						dictionary.Remove(profileId);
					}
				}
				type = type.BaseType;
			}
		}
	}

	private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

	// zzap: Ghost must not capture the recycler's in-progress visibility repair.
	internal static bool GhostPlacementBusy(BotOwner bot)
	{
		return bot != null && bot.ProfileId != null && Entries.TryGetValue(bot.ProfileId, out var entry)
			&& (entry.VisualRepairUntil >= Time.time || (entry.LastRecycledAt >= 0f && Time.time - entry.LastRecycledAt < 15f));
	}

	private static GameObject _host;
	private static BotSpawner _spawner;

	private static int _highestProgressTier = -1;

	private static int _created;

	private static int _died;

	private static int _retiredCorpses;

	private static int _recycledPlacements;

	private static int _avoidedCreations;

	private static int _recoveredBirths;

	private static float _nextNativeReconcile;

	private static readonly Dictionary<string, float> RecycleDiagnosticTimes = new Dictionary<string, float>();

	private static readonly HashSet<string> Stage1Zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Zone1BD1HangarAmbush11", "Zone1BD1HangarBD11", "Zone1BD1PortAmbush1", "Zone1BDGateAmbush13", "Zone1ScavEnterStorm4", "Zone1ScavEnterStorm7", "Zone1ScavHangarStorm14", "Zone1ScavMiddleAmbush5", "Zone1ScavMiddleAmbush8", "Zone1ScavPortStorm6",
		"Zone1ScavStoreStorm9", "Zone1VSRF1Spawn3", "Zone1VSRFSnipeRoofPort2", "Zone1VSRFStoreAmbush10"
	};

	private static readonly Dictionary<string, int> Stage2Tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["Zone2VSRFPassMiddle15"] = 0,
		["Zone2ScavsWarehouse16"] = 0,
		["Zone2ScavContainers17"] = 0,
		["Zone2BDWarehouse18"] = 0,
		["Zone2BDWarehouse19"] = 0,
		["Zone2CivGate36"] = 0,
		["Zone2ScavsColumns20"] = 1,
		["Zone2ScavsColumns21"] = 1,
		["Zone2VSRFColumns22"] = 1,
		["Zone2CivPump35"] = 1,
		["Zone2VSRFContainers24"] = 2,
		["Zone2ScavContainers25"] = 2,
		["Zone2ScavContainers26"] = 2,
		["Zone2BDColumns23"] = 3,
		["Zone2BDPort28"] = 4,
		["Zone2ScavPort29"] = 4,
		["Zone2BDPort30"] = 4,
		["Zone2BDPort31"] = 4
	};

	private static readonly HashSet<string> Stage3Zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Zone2BDPort32", "Zone2VSRFExit33", "Zone2CivShip34" };

	internal static int LivingCount
	{
		get
		{
			ReconcileNativeBots();
			int num = 0;
			foreach (Entry value in Entries.Values)
			{
				if (value.Alive && !value.Removed && (bool)value.Player && value.Player.HealthController != null && value.Player.HealthController.IsAlive)
				{
					num++;
				}
			}
			try
			{
				num = Math.Max(num, ((Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance.BotsController : null)?.BotSpawner?._allBotsCount).GetValueOrDefault());
			}
			catch
			{
			}
			return num;
		}
	}

	internal static int LivingOrdinaryScavCount
	{
		get
		{
			ReconcileNativeBots();
			int num = 0;
			foreach (Entry value in Entries.Values)
			{
				if (value.OrdinaryScav && value.Alive && !value.Removed && (bool)value.Player && value.Player.HealthController != null && value.Player.HealthController.IsAlive)
				{
					num++;
				}
			}
			return num;
		}
	}

	internal static int OrdinaryScavsCreatedCount
	{
		get
		{
			ReconcileNativeBots();
			int num = 0;
			foreach (Entry value in Entries.Values)
			{
				if (value.OrdinaryScav)
				{
					num++;
				}
			}
			return num;
		}
	}

	internal static int ResidentOrdinaryScavCount
	{
		get
		{
			ReconcileNativeBots();
			int num = 0;
			foreach (Entry value in Entries.Values)
			{
				if (value.OrdinaryScav && !value.Removed)
				{
					num++;
				}
			}
			return num;
		}
	}

	private static void Unlisten()
	{
		BotSpawner spawner = _spawner;
		_spawner = null;
		if (spawner == null) return;
		try { spawner.OnBotCreated -= OnBotCreated; } catch (Exception) { }
		try { spawner.OnBotRemoved -= OnBotRemoved; } catch (Exception) { }
	}

	internal static int ReleaseAfterRaid()
	{
		int count = Entries.Count;
		Unlisten();
		Entries.Clear(); // no visibility repair on already disposed players
		RecycleDiagnosticTimes.Clear();
		GameObject host = _host;
		_host = null;
		TerminalRaidCleanup.DestroyOwned(host);
		return count;
	}

	internal static void ResetForRaid()
	{
		Unlisten();
		foreach (Entry value in Entries.Values)
		{
			FinishBodyVisibilityRepair(value);
		}
		Entries.Clear();
		_highestProgressTier = -1;
		_created = (_died = (_retiredCorpses = (_recycledPlacements = (_avoidedCreations = (_recoveredBirths = 0)))));
		_nextNativeReconcile = 0f;
		RecycleDiagnosticTimes.Clear();
		if ((bool)_host)
		{
			UnityEngine.Object.Destroy(_host);
		}
		_host = null;
	}

	internal static bool IsOrdinaryScav(WildSpawnType role)
	{
		if (role != WildSpawnType.assault && role != WildSpawnType.assaultGroup && role != WildSpawnType.cursedAssault)
		{
			return role == WildSpawnType.marksman;
		}
		return true;
	}

	internal static bool IsRuaf(WildSpawnType role)
	{
		return TerminalRoles.IsRuaf(role);
	}

	internal static bool IsBlackDivision(WildSpawnType role)
	{
		return TerminalRoles.Name(role).IndexOf("blackdiv", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string ZoneName(BotOwner bot)
	{
		try
		{
			if (!bot)
			{
				return "";
			}
			BotZone botZone = bot.BotsGroup?.BotZone;
			return botZone ? botZone.name : "";
		}
		catch
		{
			return "";
		}
	}

	internal static int StageOf(string zone)
	{
		if (string.IsNullOrEmpty(zone))
		{
			return 0;
		}
		if (Stage1Zones.Contains(zone))
		{
			return 1;
		}
		if (Stage3Zones.Contains(zone))
		{
			return 3;
		}
		if (Stage2Tiers.ContainsKey(zone))
		{
			return 2;
		}
		return 0;
	}

	internal static int TierOf(string zone)
	{
		if (Stage1Zones.Contains(zone))
		{
			return -1;
		}
		if (Stage2Tiers.TryGetValue(zone ?? "", out var value))
		{
			return value;
		}
		if (Stage3Zones.Contains(zone ?? ""))
		{
			return 5;
		}
		return int.MinValue;
	}

	internal static void NoteProgressEvent(string eventName)
	{
		if (!string.IsNullOrEmpty(eventName) && eventName.Length >= 2 && char.ToUpperInvariant(eventName[0]) == 'T' && !eventName.StartsWith("TB", StringComparison.OrdinalIgnoreCase))
		{
			int num = eventName[1] - 48;
			if (num >= 0 && num <= 5 && num > _highestProgressTier)
			{
				_highestProgressTier = num;
				Plugin.Log.LogInfo($"[Population] progression reached T{num}; authored event recorded");
			}
		}
	}

	internal static bool HasReachedTier(int tier)
	{
		return _highestProgressTier >= tier;
	}

	internal static void EnsureProgressWavesActivated(string eventName)
	{
		if (!TryProgressTier(eventName, out var tier))
		{
			return;
		}
		try
		{
			BossSpawnScenario bossSpawnScenario = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance : null)?.BossSpawnScenario;
			BossLocationSpawn[] array = bossSpawnScenario?.BossSpawnWaves;
			if (bossSpawnScenario == null || array == null)
			{
				return;
			}
			int num = 0;
			BossLocationSpawn[] array2 = array;
			foreach (BossLocationSpawn bossLocationSpawn in array2)
			{
				if (bossLocationSpawn != null && !bossLocationSpawn.Activated && bossLocationSpawn.ShallSpawn && bossLocationSpawn.TriggerType == SpawnTriggerType.botEvent && string.Equals(bossLocationSpawn.TriggerId, "T" + tier, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						bossSpawnScenario.ActivateWave(bossLocationSpawn);
						num++;
					}
					catch (Exception ex)
					{
						Plugin.Log.LogWarning($"[StageDirector] T{tier} fallback could not activate '{bossLocationSpawn.BossName}' in '{bossLocationSpawn.BossZone}': {ex.Message}");
					}
				}
			}
			if (num > 0)
			{
				Plugin.Log.LogWarning($"[StageDirector] native T{tier} event left {num} authored wave(s) dormant; activated them through BossSpawnScenario fallback");
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[StageDirector] progression-wave audit failed: " + ex2.Message);
		}
	}

	private static bool TryProgressTier(string eventName, out int tier)
	{
		tier = -1;
		if (string.IsNullOrEmpty(eventName) || eventName.Length < 2)
		{
			return false;
		}
		if (char.ToUpperInvariant(eventName[0]) != 'T' || eventName.StartsWith("TB", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		tier = eventName[1] - 48;
		if (tier >= 0)
		{
			return tier <= 5;
		}
		return false;
	}

	private static void OnBotCreated(BotOwner bot)
	{
		using (TerminalTickProfiler.Measure("Population.OnBotCreated"))
		{
			ScrubPooledCorpseComponents(bot);
			RecordBot(bot, recovered: false);
		}
	}

	private static void ScrubPooledCorpseComponents(BotOwner bot)
	{
		using var scope = TerminalTickProfiler.Measure("Population.ScrubPooledCorpse");
		try
		{
			Player player = (bot ? bot.GetPlayer : null);
			if (!player)
			{
				return;
			}
			Corpse[] components = player.gameObject.GetComponents<Corpse>();
			if (components == null || components.Length == 0)
			{
				return;
			}
			Corpse[] array = components;
			foreach (Corpse corpse in array)
			{
				if ((bool)corpse)
				{
					UnityEngine.Object.DestroyImmediate(corpse);
				}
			}
			Plugin.Log.LogWarning($"[CorpseIdentity] removed {components.Length} stale pooled corpse component(s) before activating {bot.ProfileId} role={bot.Profile?.Info?.Settings?.Role}");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[CorpseIdentity] pooled corpse scrub failed: " + ex.Message);
		}
	}

	private static void RecordBot(BotOwner bot, bool recovered)
	{
		using var scope = TerminalTickProfiler.Measure("Population.RecordBot");
		try
		{
			if (!bot)
			{
				return;
			}
			TerminalRuafNeutral.OnBotCreated(bot);
			string profileId = bot.ProfileId;
			Player getPlayer = bot.GetPlayer;
			ProfileSettings profileSettings = bot.Profile?.Info?.Settings;
			if (string.IsNullOrEmpty(profileId) || !getPlayer || profileSettings == null)
			{
				return;
			}
			if (Entries.TryGetValue(profileId, out var value))
			{
				value.Bot = bot;
				value.Player = getPlayer;
				return;
			}
			string text = ZoneName(bot);
			int num = StageOf(text);
			Entries[profileId] = new Entry
			{
				ProfileId = profileId,
				Bot = bot,
				Player = getPlayer,
				Role = profileSettings.Role,
				OrdinaryScav = IsOrdinaryScav(profileSettings.Role),
				OriginalZone = text,
				LogicalZone = text,
				OriginalStage = num,
				LogicalStage = num,
				LogicalTier = TierOf(text),
				Alive = true,
				SpawnAt = Time.time
			};
			_created++;
			TerminalSpawnGate.NoteBotCreated(IsOrdinaryScav(profileSettings.Role));
			if (recovered)
			{
				_recoveredBirths++;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Population] birth record failed: " + ex.Message);
		}
	}

	private static void ReconcileNativeBots()
	{
		if (Time.time < _nextNativeReconcile)
		{
			return;
		}
		_nextNativeReconcile = Time.time + 1f;
		using var scope = TerminalTickProfiler.Measure("Population.ReconcileNativeBots");
		try
		{
			BotsList botsList = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance : null)?.BotsController?.BotSpawner?._bots;
			if (botsList == null)
			{
				return;
			}
			int recoveredBirths = _recoveredBirths;
			foreach (BotOwner botOwner in botsList.BotOwners)
			{
				// Also retries groups whose first member had not finished activation
				// at OnBotCreated. Successful groups take the cached fast path.
				TerminalRuafNeutral.OnBotCreated(botOwner);
				if ((bool)botOwner && !string.IsNullOrEmpty(botOwner.ProfileId) && !Entries.ContainsKey(botOwner.ProfileId))
				{
					RecordBot(botOwner, recovered: true);
				}
			}
			int num = _recoveredBirths - recoveredBirths;
			if (num > 0)
			{
				Plugin.Log.LogWarning($"[Population] recovered {num} living bot(s) omitted by OnBotCreated (native-list reconciliation)");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Population] native reconciliation failed: " + ex.Message);
		}
	}

	private static void OnBotRemoved(BotOwner bot)
	{
		using (TerminalTickProfiler.Measure("Population.OnBotRemoved"))
		{
			try
			{
				if (!bot)
				{
					return;
				}
				TerminalStageWait.Release(bot);
				string profileId = bot.ProfileId;
				if (!string.IsNullOrEmpty(profileId))
				{
					if (!Entries.TryGetValue(profileId, out var value))
					{
						WildSpawnType role = bot.Profile.Info.Settings.Role;
						string text = ZoneName(bot);
						value = new Entry
						{
							ProfileId = profileId,
							Bot = bot,
							Player = bot.GetPlayer,
							Role = role,
							OrdinaryScav = IsOrdinaryScav(role),
							OriginalZone = text,
							LogicalZone = text,
							OriginalStage = StageOf(text),
							LogicalStage = StageOf(text),
							LogicalTier = TierOf(text),
							SpawnAt = Time.time
						};
						Entries[profileId] = value;
					}
					FinishBodyVisibilityRepair(value);
					if (value.Alive)
					{
						value.Alive = false;
						value.DeathAt = Time.time;
						_died++;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Population] death record failed: " + ex.Message);
			}
		}
	}

	internal static int DespawnAllForEnding()
	{
		int num = 0;
		foreach (Entry item in new List<Entry>(Entries.Values))
		{
			if (item != null && item.Alive && !item.Removed && (bool)item.Bot)
			{
				try
				{
					FinishBodyVisibilityRepair(item);
					item.Removed = true;
					TerminalCrewJobs.ByProfile.Remove(item.ProfileId);
					item.Bot.LeaveData.RemoveFromMap();
					num++;
				}
				catch (Exception ex)
				{
					item.Removed = false;
					Plugin.Log.LogWarning($"[Ending] bot despawn failed for {item.ProfileId} role={item.Role}: {ex.Message}");
				}
			}
		}
		Plugin.Log.LogInfo($"[Ending] despawned {num} living bot(s) before the ending cutscene");
		return num;
	}

	internal static int SeenInStage(int stage)
	{
		ReconcileNativeBots();
		int num = 0;
		foreach (Entry value in Entries.Values)
		{
			if (value.OriginalStage == stage || value.LogicalStage == stage)
			{
				num++;
			}
		}
		return num;
	}

	internal static List<BotOwner> LivingInLogicalStage(int stage)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		ReconcileNativeBots();
		return ValueEnumerableExtensions.ToList<WhereSelect<FromEnumerable<Entry>, Entry, BotOwner>, BotOwner>(ValueEnumerableExtensions.Select<FromEnumerable<Entry>, Entry, BotOwner>(ValueEnumerableExtensions.Where<FromEnumerable<Entry>, Entry>(ValueEnumerable.AsValueEnumerable<Entry>((IEnumerable<Entry>)Entries.Values), (Func<Entry, bool>)((Entry e) => e.Alive && !e.Removed && e.LogicalStage == stage && (bool)e.Bot)), (Func<Entry, BotOwner>)((Entry e) => e.Bot)));
	}

	internal static List<BotOwner> LivingInSpawnZone(string zone)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		ReconcileNativeBots();
		return ValueEnumerableExtensions.ToList<WhereSelect<FromEnumerable<Entry>, Entry, BotOwner>, BotOwner>(ValueEnumerableExtensions.Select<FromEnumerable<Entry>, Entry, BotOwner>(ValueEnumerableExtensions.Where<FromEnumerable<Entry>, Entry>(ValueEnumerable.AsValueEnumerable<Entry>((IEnumerable<Entry>)Entries.Values), (Func<Entry, bool>)((Entry e) => e.Alive && !e.Removed && (bool)e.Bot && !e.Bot.IsDead && (bool)e.Bot.SpawnBotZone && string.Equals(e.Bot.SpawnBotZone.NameZone, zone, StringComparison.Ordinal))), (Func<Entry, BotOwner>)((Entry e) => e.Bot)));
	}

	internal static List<BotOwner> LivingInSpawnZones(ISet<BotZone> zones)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		ReconcileNativeBots();
		return ValueEnumerableExtensions.ToList<WhereSelect<FromEnumerable<Entry>, Entry, BotOwner>, BotOwner>(ValueEnumerableExtensions.Select<FromEnumerable<Entry>, Entry, BotOwner>(ValueEnumerableExtensions.Where<FromEnumerable<Entry>, Entry>(ValueEnumerable.AsValueEnumerable<Entry>((IEnumerable<Entry>)Entries.Values), (Func<Entry, bool>)((Entry e) => e.Alive && !e.Removed && (bool)e.Bot && !e.Bot.IsDead && (bool)e.Bot.SpawnBotZone && zones.Contains(e.Bot.SpawnBotZone))), (Func<Entry, BotOwner>)((Entry e) => e.Bot)));
	}

	internal static string LogicalZoneOf(BotOwner bot)
	{
		if (!bot || bot.ProfileId == null || !Entries.TryGetValue(bot.ProfileId, out var value))
		{
			return "";
		}
		return value.LogicalZone;
	}

	internal static void MarkLogicalZone(BotOwner bot, string zone)
	{
		if ((bool)bot && bot.ProfileId != null && Entries.TryGetValue(bot.ProfileId, out var value))
		{
			value.LogicalZone = zone ?? "";
			value.LogicalStage = StageOf(value.LogicalZone);
			value.LogicalTier = TierOf(value.LogicalZone);
		}
	}

	internal static bool TryPreparePlainWave(BotsController controller, ref SpawnWave wave)
	{
		if (wave == null || !Plugin.ScavRecycler.Value || !IsOrdinaryScav(wave.WildSpawnType))
		{
			return false;
		}
		BotZone botZone = FindZone(controller, wave.SpawnAreaName);
		if (!botZone || wave.BotsCount <= 0)
		{
			return false;
		}
		int num = RecycleInto(botZone, wave.BotsCount, push: false, RecyclePool.Scav);
		if (num <= 0)
		{
			return false;
		}
		if (num >= wave.BotsCount)
		{
			_avoidedCreations += wave.BotsCount;
			Plugin.Log.LogInfo($"[Recycler] fulfilled plain scav wave {wave.BotsCount}/{wave.BotsCount} in '{botZone.name}' from living survivors; no profiles created");
			return true;
		}
		int num2 = wave.BotsCount - num;
		_avoidedCreations += num;
		wave = new SpawnWave
		{
			BotsCount = num2,
			Side = wave.Side,
			SpawnAreaName = wave.SpawnAreaName,
			Time = wave.Time,
			WildSpawnType = wave.WildSpawnType,
			IsPlayers = wave.IsPlayers,
			Difficulty = wave.Difficulty,
			ChanceGroup = wave.ChanceGroup,
			WithCheckMinMax = wave.WithCheckMinMax,
			KeepZoneOnSpawn = wave.KeepZoneOnSpawn
		};
		Plugin.Log.LogInfo($"[Recycler] fulfilled {num} scav slot(s) in '{botZone.name}' from survivors; spawning only {num2}");
		return false;
	}

	internal static bool TryPrepareBossWave(BotsController controller, ref BossLocationSpawn wave)
	{
		if (wave == null || !Plugin.ScavRecycler.Value || wave.Supports != null)
		{
			return false;
		}
		if (!IsOrdinaryScav(wave.BossType))
		{
			return false;
		}
		if (wave.EscortCount > 0 && !IsOrdinaryScav(wave.EscortType))
		{
			return false;
		}
		string name = ((!string.IsNullOrEmpty(wave.BornZone)) ? wave.BornZone : (wave.BossZone ?? "").Split(',')[0]);
		BotZone botZone = FindZone(controller, name);
		int num = TerminalWaveRoster.Count(wave);
		if (!botZone || num <= 0)
		{
			return false;
		}
		bool push = wave.TriggerType == SpawnTriggerType.botEvent || !string.IsNullOrEmpty(wave.TriggerId);
		int num2 = RecycleInto(botZone, num, push, RecyclePool.Scav);
		if (num2 <= 0)
		{
			return false;
		}
		if (num2 >= num)
		{
			_avoidedCreations += num;
			Plugin.Log.LogInfo($"[Recycler] fulfilled scav wave {num}/{num} in '{botZone.name}' from living survivors; no profiles created");
			return true;
		}
		int num3 = num - num2;
		BossLocationSpawn bossLocationSpawn = wave.Copy();
		bossLocationSpawn.BossChance = 100f;
		bossLocationSpawn.BossEscortAmount = Math.Max(0, num3 - 1).ToString();
		bossLocationSpawn.Supports = null;
		bossLocationSpawn.PerfectPos = wave.PerfectPos;
		bossLocationSpawn.Init();
		bossLocationSpawn.ShallSpawn = true;
		bossLocationSpawn.Activated = wave.Activated;
		wave = bossLocationSpawn;
		_avoidedCreations += num2;
		Plugin.Log.LogInfo($"[Recycler] fulfilled {num2} scav slot(s) in '{botZone.name}' from survivors; spawning only {num3}");
		return false;
	}

	internal static bool TryPrepareRuafWave(BotsController controller, ref BossLocationSpawn wave)
	{
		if (wave == null || !Plugin.RuafRecycler.Value || !IsRuaf(wave.BossType))
		{
			return false;
		}
		string name = ((!string.IsNullOrEmpty(wave.BornZone)) ? wave.BornZone : (wave.BossZone ?? "").Split(',')[0]);
		BotZone botZone = FindZone(controller, name);
		int num = TerminalWaveRoster.Count(wave);
		if (!botZone || num <= 0)
		{
			return false;
		}
		int num2 = RecycleInto(botZone, num, push: false, RecyclePool.Ruaf);
		if (num2 <= 0)
		{
			return false;
		}
		if (num2 >= num)
		{
			_avoidedCreations += num;
			Plugin.Log.LogInfo($"[Recycler] fulfilled RUAF wave {num}/{num} in '{botZone.name}' from idle RUAF survivors; no profiles created");
			return true;
		}
		int num3 = num - num2;
		BossLocationSpawn bossLocationSpawn = wave.Copy();
		bossLocationSpawn.BossChance = 100f;
		bossLocationSpawn.BossEscortAmount = Math.Max(0, num3 - 1).ToString();
		bossLocationSpawn.Supports = null;
		bossLocationSpawn.PerfectPos = wave.PerfectPos;
		bossLocationSpawn.Init();
		bossLocationSpawn.ShallSpawn = true;
		bossLocationSpawn.Activated = wave.Activated;
		wave = bossLocationSpawn;
		_avoidedCreations += num2;
		Plugin.Log.LogInfo($"[Recycler] moved {num2} idle RUAF survivor(s) into '{botZone.name}'; spawning only {num3} new RUAF instead of {num}");
		return false;
	}

	internal static bool TryPrepareBlackDivisionWave(BotsController controller, ref BossLocationSpawn wave)
	{
		if (wave == null || !Plugin.BlackDivisionRecycler.Value || !IsBlackDivision(wave.BossType))
		{
			return false;
		}
		string name = ((!string.IsNullOrEmpty(wave.BornZone)) ? wave.BornZone : (wave.BossZone ?? "").Split(',')[0]);
		BotZone botZone = FindZone(controller, name);
		int num = TerminalWaveRoster.Count(wave);
		if (!botZone || num <= 0)
		{
			return false;
		}
		int num2 = RecycleInto(botZone, num, push: false, RecyclePool.BlackDivision);
		if (num2 <= 0)
		{
			return false;
		}
		if (num2 >= num)
		{
			_avoidedCreations += num;
			Plugin.Log.LogInfo($"[Recycler] fulfilled Black Division wave {num}/{num} in '{botZone.name}' from idle BD survivors; no profiles created");
			return true;
		}
		int num3 = num - num2;
		BossLocationSpawn bossLocationSpawn = wave.Copy();
		bossLocationSpawn.BossChance = 100f;
		bossLocationSpawn.BossEscortAmount = Math.Max(0, num3 - 1).ToString();
		bossLocationSpawn.Supports = null;
		bossLocationSpawn.PerfectPos = wave.PerfectPos;
		bossLocationSpawn.Init();
		bossLocationSpawn.ShallSpawn = true;
		bossLocationSpawn.Activated = wave.Activated;
		wave = bossLocationSpawn;
		_avoidedCreations += num2;
		Plugin.Log.LogInfo($"[Recycler] moved {num2} idle Black Division survivor(s) into '{botZone.name}'; spawning only {num3} new BD instead of {num}");
		return false;
	}

	private static BotZone FindZone(BotsController controller, string name)
	{
		if (controller?.BotSpawner == null || string.IsNullOrEmpty(name))
		{
			return null;
		}
		try
		{
			return controller.BotSpawner.GetZoneByName(name);
		}
		catch
		{
			return null;
		}
	}

	private static int RecycleInto(BotZone targetZone, int wanted, bool push, RecyclePool pool)
	{
		if (!targetZone || wanted <= 0)
		{
			return 0;
		}
		int num = StageOf(targetZone.name);
		int num2 = TierOf(targetZone.name);
		if (pool == RecyclePool.Scav && (num < 2 || num2 < 0 || !HasReachedTier(num2)))
		{
			return 0;
		}
		Player player = TerminalCoop.NearestHuman(targetZone.transform.position);
		if (!player)
		{
			return 0;
		}
		float minDistSq = Plugin.ScavRecycleMinDistance.Value * Plugin.ScavRecycleMinDistance.Value;
		float minDistSq2 = Plugin.ScavRecycleDestinationDistance.Value * Plugin.ScavRecycleDestinationDistance.Value;
		List<Vector3> list = EligibleTargetPositions(targetZone, player.Position, minDistSq2);
		if (list.Count == 0)
		{
			LogRecycleBlock(pool, targetZone.name, "no safe destination marker (all too close, visible, or off NavMesh)");
			return 0;
		}
		List<Entry> list2 = new List<Entry>();
		int[] array = new int[9];
		foreach (Entry value in Entries.Values)
		{
			if (CanRecycle(value, num, num2, targetZone.name, player.Position, minDistSq, pool, out var reason))
			{
				list2.Add(value);
			}
			else
			{
				array[(int)reason]++;
			}
		}
		if (list2.Count == 0)
		{
			LogRecycleBlock(pool, targetZone.name, $"no eligible survivor among {Entries.Count}: faction={array[1]}, invalid/dead={array[0]}, protectedAssignment={array[2]}, young={array[3]}, nearOrVisible={array[4]}, inactive={array[5]}, combat={array[6]}, error={array[7]}");
		}
		list2.Sort((Entry a, Entry b) => a.SpawnAt.CompareTo(b.SpawnAt));
		int num3 = Math.Min(Math.Min(wanted, list.Count), list2.Count);
		int num4 = 0;
		for (int num5 = 0; num5 < num3; num5++)
		{
			Entry entry = list2[num5];
			Vector3 vector = list[num4 % list.Count];
			try
			{
				BotOwner bot = entry.Bot;
				TerminalGhostDirector.WakeNow(bot, TerminalGhostDirector.WakeReason.Move);
				if (!entry.Player.gameObject.activeSelf)
				{
					entry.Player.gameObject.SetActive(value: true);
				}
				bot.BotState = EBotState.Active;
				bot.StandBy?.Activate();
				bot.Memory?.Spotted(byHit: false);
				bot.Mover.Stop();
				bot.Mover.Teleport(vector);
				bot.Transform.position = vector;
				bot.SpawnBotZone = targetZone;
				TerminalGhostDirector.PlacementChanged(bot);
				bot.LookSensor?.UpdateZoneValue(targetZone);
				BeginBodyVisibilityRepair(entry);
				RepairRecycledBodyVisibility(entry, out var renderers, out var forcedBackOn);
				TerminalCrewJobs.ByProfile[entry.ProfileId] = new TerminalCrewJobs.Rec
				{
					Job = ((!push) ? TerminalCrewJobs.Job.Guard : TerminalCrewJobs.Job.Hunt),
					Zone = new Bounds(vector, new Vector3(30f, 10f, 30f)),
					RushUntil = 0f
				};
				entry.LogicalZone = targetZone.name;
				entry.LogicalStage = num;
				entry.LogicalTier = num2;
				entry.LastRecycledAt = Time.time;
				entry.RecycleCount++;
				num4++;
				_recycledPlacements++;
				Plugin.Log.LogDebug($"[Recycler] {PoolLabel(pool)} {entry.ProfileId} '{entry.Role}' -> {targetZone.name} at {vector} (reuse #{entry.RecycleCount})");
				Plugin.Log.LogDebug($"[Recycler] body visibility refreshed for {entry.ProfileId}: {renderers} renderer(s), {forcedBackOn} stale force-off flag(s) cleared");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Recycler] failed moving " + entry.ProfileId + " to '" + targetZone.name + "': " + ex.Message);
			}
		}
		return num4;
	}

	private static void BeginBodyVisibilityRepair(Entry entry)
	{
		FinishBodyVisibilityRepair(entry);
		if (!entry.Player || !entry.Player.PlayerBody)
		{
			return;
		}
		Entry entry2 = entry;
		if (entry2.VisualRepairRenderers == null)
		{
			entry2.VisualRepairRenderers = new List<Renderer>(128);
		}
		entry2 = entry;
		if (entry2.VisualRepairSkins == null)
		{
			entry2.VisualRepairSkins = new List<SkinnedMeshRenderer>(16);
		}
		entry.Player.PlayerBody.GetRenderersNonAlloc(entry.VisualRepairRenderers);
		entry.VisualRepairUntil = Time.time + 3f;
		foreach (Renderer visualRepairRenderer in entry.VisualRepairRenderers)
		{
			if (visualRepairRenderer is SkinnedMeshRenderer skinnedMeshRenderer && (bool)skinnedMeshRenderer && !skinnedMeshRenderer.updateWhenOffscreen)
			{
				entry.VisualRepairSkins.Add(skinnedMeshRenderer);
				skinnedMeshRenderer.updateWhenOffscreen = true;
			}
		}
	}

	private static void FinishBodyVisibilityRepair(Entry entry)
	{
		entry.VisualRepairUntil = -1f;
		if (entry.VisualRepairSkins != null)
		{
			foreach (SkinnedMeshRenderer visualRepairSkin in entry.VisualRepairSkins)
			{
				if ((bool)visualRepairSkin)
				{
					visualRepairSkin.updateWhenOffscreen = false;
				}
			}
			entry.VisualRepairSkins.Clear();
		}
		entry.VisualRepairRenderers?.Clear();
	}

	private static void RepairRecycledBodyVisibility(Entry entry, out int renderers, out int forcedBackOn)
	{
		renderers = 0;
		forcedBackOn = 0;
		try
		{
			if (entry == null || !entry.Player || !entry.Player.PlayerBody || TerminalStageWait.OwnsVisibility(entry.Bot) || TerminalGhostDirector.IsDormant(entry.Bot))
			{
				return;
			}
			List<Renderer> visualRepairRenderers = entry.VisualRepairRenderers;
			if (visualRepairRenderers == null)
			{
				return;
			}
			renderers = visualRepairRenderers.Count;
			foreach (Renderer item in visualRepairRenderers)
			{
				if ((bool)item && item.forceRenderingOff)
				{
					item.forceRenderingOff = false;
					forcedBackOn++;
				}
			}
		}
		catch
		{
		}
	}

	private static string PoolLabel(RecyclePool pool)
	{
		return pool switch
		{
			RecyclePool.BlackDivision => "Black Division", 
			RecyclePool.Ruaf => "RUAF", 
			_ => "scav", 
		};
	}

	private static void LogRecycleBlock(RecyclePool pool, string targetZone, string reason)
	{
		string key = pool.ToString() + ":" + targetZone;
		if (!RecycleDiagnosticTimes.TryGetValue(key, out var value) || !(Time.realtimeSinceStartup - value < 30f))
		{
			RecycleDiagnosticTimes[key] = Time.realtimeSinceStartup;
			Plugin.Log.LogInfo("[RecyclerDiag] " + PoolLabel(pool) + " -> '" + targetZone + "' blocked: " + reason);
		}
	}

	private static bool CanRecycle(Entry e, int targetStage, int targetTier, string targetZone, Vector3 playerPos, float minDistSq, RecyclePool pool, out RecycleReject reason)
	{
		reason = RecycleReject.Invalid;
		if (e == null || !e.Alive || e.Removed || !e.Bot || !e.Player)
		{
			return false;
		}
		if (pool == RecyclePool.Ruaf && !IsRuaf(e.Role))
		{
			reason = RecycleReject.WrongFaction;
			return false;
		}
		if (pool == RecyclePool.BlackDivision && !IsBlackDivision(e.Role))
		{
			reason = RecycleReject.WrongFaction;
			return false;
		}
		if (pool == RecyclePool.Scav && !e.OrdinaryScav)
		{
			reason = RecycleReject.WrongFaction;
			return false;
		}
		bool flag = string.Equals(e.LogicalZone, targetZone, StringComparison.OrdinalIgnoreCase);
		bool flag2 = pool == RecyclePool.Scav && (e.LogicalStage > targetStage || e.LogicalTier > targetTier);
		if (flag | flag2)
		{
			reason = RecycleReject.Progress;
			return false;
		}
		if (Time.time - Math.Max(e.SpawnAt, e.LastRecycledAt) < Plugin.ScavRecycleMinAge.Value)
		{
			reason = RecycleReject.TooYoung;
			return false;
		}
		Player player = TerminalCoop.NearestHuman(e.Bot.Position);
		if (player == null || (e.Bot.Position - player.Position).sqrMagnitude < minDistSq || IsInCamera(e.Bot.Position))
		{
			reason = RecycleReject.NearOrVisible;
			return false;
		}
		try
		{
			if (e.Bot.Mover == null)
			{
				reason = RecycleReject.Inactive;
				return false;
			}
			if (e.Player.HealthController == null || !e.Player.HealthController.IsAlive)
			{
				return false;
			}
			if (e.Bot.Memory == null || e.Bot.Memory.IsUnderFire)
			{
				reason = RecycleReject.Combat;
				return false;
			}
			EnemyInfo goalEnemy = e.Bot.Memory.GoalEnemy;
			if (goalEnemy != null && (goalEnemy.IsVisible || goalEnemy.CanShoot || Time.time - goalEnemy.PersonalLastSeenTime < 10f))
			{
				reason = RecycleReject.Combat;
				return false;
			}
		}
		catch
		{
			reason = RecycleReject.Error;
			return false;
		}
		return true;
	}

	private static List<Vector3> EligibleTargetPositions(BotZone zone, Vector3 playerPos, float minDistSq)
	{
		List<Vector3> list = new List<Vector3>();
		List<Vector3> list2 = new List<Vector3>();
		try
		{
			if (zone.SpawnPointMarkers == null)
			{
				return list;
			}
			float num = Mathf.Min(minDistSq, 400f);
			foreach (SpawnPointMarker spawnPointMarker in zone.SpawnPointMarkers)
			{
				if (!spawnPointMarker)
				{
					continue;
				}
				Vector3 position = spawnPointMarker.Position;
				if (IsInCamera(position) || !NavMesh.SamplePosition(position, out var hit, 6f, -1) || IsInCamera(hit.position))
				{
					continue;
				}
				Player player = TerminalCoop.NearestHuman(hit.position);
				if (!(player == null))
				{
					float sqrMagnitude = (hit.position - player.Position).sqrMagnitude;
					if (sqrMagnitude >= minDistSq)
					{
						list.Add(hit.position);
					}
					else if (sqrMagnitude >= num)
					{
						list2.Add(hit.position);
					}
				}
			}
			if (list.Count == 0 && list2.Count > 0)
			{
				list2.Sort((Vector3 a, Vector3 b) => (b - playerPos).sqrMagnitude.CompareTo((a - playerPos).sqrMagnitude));
				list.AddRange(list2);
				Plugin.Log.LogDebug($"[Recycler] '{zone.name}' using out-of-view destination fallback ({Mathf.Sqrt((list2[0] - playerPos).sqrMagnitude):F0}m)");
			}
		}
		catch
		{
		}
		return list;
	}

	private static bool IsInCamera(Vector3 position)
	{
		try
		{
			Camera main = Camera.main;
			if (!main)
			{
				return false;
			}
			Vector3 vector = main.WorldToViewportPoint(position + Vector3.up);
			return vector.z > 0f && vector.x >= 0f && vector.x <= 1f && vector.y >= 0f && vector.y <= 1f;
		}
		catch
		{
			return false;
		}
	}
}
