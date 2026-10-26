using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using CommonAssets.Scripts.ArtilleryShelling;
using EFT;
using EFT.GameTriggers;
using EFT.GlobalEvents;
using EFT.GlobalEvents.ArtilleryShellingEcents;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalArtillery
{
	internal sealed class UpdateOncePatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(ArtilleryShellingControllerServer), "OnUpdate");
		}

		[PatchPrefix]
		private static bool Prefix(ArtilleryShellingControllerServer __instance)
		{
			if (!TerminalGate.On || !TerminalCoop.Authority || !Plugin.Artillery.Value)
			{
				return true;
			}
			if (_lastUpdatedController == __instance && _lastUpdatedFrame == Time.frameCount)
			{
				return false;
			}
			_lastUpdatedController = __instance;
			_lastUpdatedFrame = Time.frameCount;
			try
			{
				ApplyPendingZoneChanges(__instance);
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[Artillery] zone transition failed; native projectile update retained: {arg}");
			}
			return true;
		}
	}

	internal sealed class GeometryPatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(ArtilleryShellingControllerServer), "GetShootFinalPointByGeometry");
		}

		[PatchPrefix]
		private static bool Prefix(Vector3 __0, ref Vector3 __1, ref bool __result)
		{
			if (!TerminalGate.On || !TerminalCoop.Authority || !Plugin.Artillery.Value)
			{
				return true;
			}
			Vector3 origin = new Vector3(__0.x, 300f, __0.z);
			__result = Physics.Raycast(origin, Vector3.down, out var hitInfo, 1000f, EFTHardSettings.Instance.MOVEMENT_MASK);
			__1 = (__result ? hitInfo.point : Vector3.zero);
			return false;
		}
	}

	internal sealed class RetiredProjectilePatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(ArtilleryShellingControllerServer), "PushExplosionEvents");
		}

		[PatchPrefix]
		private static bool Prefix(ArtilleryShellingControllerServer __instance, ArtilleryProjectileServer serverProjectile)
		{
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			if (!TerminalGate.On || !TerminalCoop.Authority || !Plugin.Artillery.Value || !ZoneAdapterEnabled)
			{
				return true;
			}
			string id = serverProjectile.zoneID;
			if (ValueEnumerableExtensions.Any<FromEnumerable<ArtilleryShellingZone>, ArtilleryShellingZone>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>((IEnumerable<ArtilleryShellingZone>)__instance.dictionary_0.Keys), (Func<ArtilleryShellingZone, bool>)((ArtilleryShellingZone z) => z.ID == id)) || ValueEnumerableExtensions.Any<FromEnumerable<ArtilleryShellingZone>, ArtilleryShellingZone>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>((IEnumerable<ArtilleryShellingZone>)__instance._zonesInEventShelling.Keys), (Func<ArtilleryShellingZone, bool>)((ArtilleryShellingZone z) => z.ID == id)) || !KnownZones.TryGetValue(id, out var value))
			{
				return true;
			}
			float safeMaxZoneRadius = __instance.GetSafeMaxZoneRadius(__instance.GetMaxZoneSide(value));
			GlobalEventsController.CreateEvent<ShellingProjectileExplosionEvent>().Invoke(id, safeMaxZoneRadius, value.Center);
			if (serverProjectile.firstInRoundForZone)
			{
				GlobalEventsController.CreateEvent<FirstShellingExplosionInRoundEvent>().Invoke(id, safeMaxZoneRadius, value.Center);
				__instance.method_2();
			}
			if (serverProjectile.lastInRoundForZone)
			{
				GlobalEventsController.CreateEvent<LastShellingExplosionInRoundEvent>().Invoke(id, safeMaxZoneRadius, value.Center);
				__instance.method_2();
			}
			if (serverProjectile.firstInShellingForZone)
			{
				GlobalEventsController.CreateEvent<FirstShellingExplosionZoneEvent>().Invoke(id, safeMaxZoneRadius, value.Center);
				__instance.method_2();
			}
			if (serverProjectile.lastInShellingForZone)
			{
				GlobalEventsController.CreateEvent<LastShellingExplosionZoneEvent>().Invoke(id, safeMaxZoneRadius, value.Center);
				__instance.method_2();
			}
			return false;
		}
	}

	internal sealed class PlannedZonesPatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(ArtilleryShellingControllerServer), "ShellingZones");
		}

		[PatchPostfix]
		private static void Postfix(ref IEnumerable<ArtilleryShellingZone> __result)
		{
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_0049: Unknown result type (might be due to invalid IL or missing references)
			if (TerminalGate.On && TerminalCoop.Authority && Plugin.Artillery.Value && _zonePolicy != null)
			{
				__result = ValueEnumerableExtensions.ToArray<FromEnumerable<ArtilleryShellingZone>, ArtilleryShellingZone>(ValueEnumerableExtensions.Where<FromEnumerable<ArtilleryShellingZone>, ArtilleryShellingZone>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>(__result), (Func<ArtilleryShellingZone, bool>)((ArtilleryShellingZone z) => _zonePolicy.Contains(z.ID))));
			}
		}
	}

	private static bool _staged;

	private static bool _injected;

	private static readonly HashSet<string> NativeSet = new HashSet<string> { "TriggerZone" };

	private static bool _stopped = true;

	internal static bool UpdateGuardEnabled;

	private static ArtilleryShellingControllerServer _lastUpdatedController;

	private static int _lastUpdatedFrame = -1;

	private static bool _pumpWarned;

	private static Type _bcTypeCached;

	private static PropertyInfo _bcZcProp;

	private static FieldInfo _bcZcField;

	private static Type _zcTypeCached;

	private static PropertyInfo _zcMapProp;

	private static FieldInfo _zcMapField;

	private static TerminalArtilleryPolicy _zonePolicy;

	private static readonly Queue<(string[] Added, string[] Removed)> ZoneChanges = new Queue<(string[], string[])>();

	private static readonly List<string> AddedZones = new List<string>();

	private static readonly List<string> RemovedZones = new List<string>();

	private static readonly Dictionary<string, ArtilleryShellingZone> KnownZones = new Dictionary<string, ArtilleryShellingZone>(StringComparer.Ordinal);

	internal static bool ZoneAdapterEnabled;

	internal static bool Pumping => !_stopped;

	internal static void ResetForRaid()
	{
		_staged = false;
		_stopped = true;
		_pumpWarned = false;
		_lastUpdatedController = null;
		_lastUpdatedFrame = -1;
		ResetPlannedZones();
	}

	internal static void InjectConfig()
	{
		if (_injected || !Plugin.Artillery.Value || !ZoneAdapterEnabled)
		{
			return;
		}
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_artillery.json");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[Artillery] terminal_artillery.json missing — no barrage");
				_injected = true;
				return;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(path));
			LoadPlannedZones(jObject);
			GlobalConfiguration.ArtilleryShellingGlobalSettings artilleryShellingGlobalSettings = Singleton<GlobalConfiguration>.Instance?.ArtilleryShelling;
			if (artilleryShellingGlobalSettings == null || artilleryShellingGlobalSettings.ArtilleryMapsConfigs == null)
			{
				Plugin.Log.LogWarning("[Artillery] backend ArtilleryShelling config absent — no barrage");
				return;
			}
			Type type = AccessTools.TypeByName("CommonAssets.Scripts.ArtilleryShelling.ArtilleryShellingMapConfiguration") ?? AccessTools.TypeByName("ArtilleryShellingMapConfiguration");
			if (type == null)
			{
				Plugin.Log.LogWarning("[Artillery] map-config type missing");
				_injected = true;
				return;
			}
			object obj = Activator.CreateInstance(type);
			JObject jObject2 = (JObject)jObject.DeepClone();
			foreach (JToken item in (JArray)jObject2["ShellingZones"])
			{
				((JObject)item).Remove("TriggerInclude");
				((JObject)item).Remove("TriggerExclude");
			}
			TerminalRigFill.FillObject(obj, jObject2, new Dictionary<string, Transform>());
			((IDictionary)artilleryShellingGlobalSettings.ArtilleryMapsConfigs)[(object)"Terminal"] = obj;
			_injected = true;
			Plugin.Log.LogInfo("[Artillery] Terminal shelling config injected — native controllers will arm at game start");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Artillery] config inject failed: {arg}");
		}
	}

	internal static void Pump()
	{
		if (!Plugin.Artillery.Value || !TerminalGate.On || !TerminalCoop.Authority || !UpdateGuardEnabled)
		{
			return;
		}
		try
		{
			ArtilleryShellingControllerServer artilleryShellingControllerServer = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null)?.ServerShellingController;
			if (artilleryShellingControllerServer != null)
			{
				if (artilleryShellingControllerServer.ShellingOn || artilleryShellingControllerServer.TargetShellingOn || artilleryShellingControllerServer.dictionary_0.Count > 0 || artilleryShellingControllerServer._zonesInEventShelling.Count > 0 || artilleryShellingControllerServer.ActiveProjectiles.Count > 0 || artilleryShellingControllerServer._notifyZoneShellingMessagesQueue.Count > 0 || ZoneChanges.Count > 0)
				{
					artilleryShellingControllerServer.OnUpdate();
					_stopped = false;
				}
				else if (!_stopped)
				{
					_stopped = true;
					Plugin.Log.LogInfo("[Artillery] native event rounds and projectiles finished; fallback pump idle");
				}
			}
		}
		catch (Exception arg)
		{
			if (!_pumpWarned)
			{
				_pumpWarned = true;
				Plugin.Log.LogWarning($"[Artillery] pump threw (once): {arg}");
			}
		}
	}

	internal static IDictionary ActiveZonesDict()
	{
		try
		{
			BotsController botsController = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance : null)?.BotsController;
			if (botsController == null)
			{
				return null;
			}
			Type type = botsController.GetType();
			if (_bcTypeCached != type)
			{
				_bcTypeCached = type;
				_bcZcProp = type.GetProperty("ArtilleryZonesController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				_bcZcField = type.GetField("ArtilleryZonesController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			object obj = _bcZcProp?.GetValue(botsController) ?? _bcZcField?.GetValue(botsController);
			if (obj == null)
			{
				return null;
			}
			Type type2 = obj.GetType();
			if (_zcTypeCached != type2)
			{
				_zcTypeCached = type2;
				_zcMapProp = type2.GetProperty("ActiveZonesOnMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				_zcMapField = type2.GetField("ActiveZonesOnMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			return (_zcMapProp?.GetValue(obj) ?? _zcMapField?.GetValue(obj)) as IDictionary;
		}
		catch
		{
			return null;
		}
	}

	internal static void TryStage()
	{
		if (_staged || !Plugin.Artillery.Value || !TerminalGate.On)
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.TriggersEmitter == null)
		{
			return;
		}
		Transform transform = TerminalRigFill.FindRootNamed("ArtilleryTriggers");
		if (!transform)
		{
			return;
		}
		try
		{
			JObject jObject = JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_endgame.json")));
			Dictionary<string, Transform> index = new Dictionary<string, Transform>();
			TerminalRigFill.IndexTree(transform, "ArtilleryTriggers", index);
			TerminalRigFill.StageRows(jObject["rows"] as JArray, NativeSet, index, "Artillery");
			if (gameWorld.ServerShellingController == null)
			{
				Plugin.Log.LogWarning("[Artillery] shelling controller NOT created — config injection missed the game-create window?");
			}
			for (int i = 1; i <= 4; i++)
			{
				string trigger = $"artillery_trigger_{i}";
				TriggersEmitter.Instance.Subscribe(trigger, () =>
				{
					OnZoneTripped(trigger);
				});
			}
			_staged = true;
			Plugin.Log.LogInfo("[Artillery] four authored triggers armed; planned barrage zone membership restored");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Artillery] staging failed: {arg}");
			_staged = true;
		}
	}

	private static void OnZoneTripped(string trigger)
	{
		if (!TerminalCoop.Authority)
		{
			return;
		}
		try
		{
			QueueZoneChanges(trigger);
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Artillery] shelling start failed: {arg}");
		}
	}

	private static void LoadPlannedZones(JObject config)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		_zonePolicy = new TerminalArtilleryPolicy(ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, TerminalArtilleryPolicy.Rule>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, TerminalArtilleryPolicy.Rule>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)config["ShellingZones"]), (Func<JToken, TerminalArtilleryPolicy.Rule>)((JToken z) => new TerminalArtilleryPolicy.Rule(z.Value<string>("ID"), z.Value<bool>("UsedInPlanedShelling"), z.Value<bool>("IsActive"), z.Value<string>("TriggerInclude"), z.Value<string>("TriggerExclude"))))));
	}

	private static void ResetPlannedZones()
	{
		_zonePolicy?.Reset();
		ZoneChanges.Clear();
		AddedZones.Clear();
		RemovedZones.Clear();
		KnownZones.Clear();
	}

	private static void ApplyPendingZoneChanges(ArtilleryShellingControllerServer controller)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		if (!ZoneAdapterEnabled || controller._currentMapConfiguration.ShellingZones == null)
		{
			return;
		}
		while (ZoneChanges.Count > 0)
		{
			(string[] Added, string[] Removed) change = ZoneChanges.Dequeue();
			ArtilleryShellingZone[] array = ValueEnumerableExtensions.ToArray<ArtilleryShellingZone>(ValueEnumerableExtensions.Where<ArtilleryShellingZone>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>(controller._currentMapConfiguration.ShellingZones), (Func<ArtilleryShellingZone, bool>)((ArtilleryShellingZone z) =>
			{
				//IL_0018: Unknown result type (might be due to invalid IL or missing references)
				//IL_0044: Unknown result type (might be due to invalid IL or missing references)
				return ValueEnumerableExtensions.Contains<FromArray<string>, string>(ValueEnumerable.AsValueEnumerable<string>(change.Added), z.ID) && !ValueEnumerableExtensions.Any<FromEnumerable<ArtilleryShellingZone>, ArtilleryShellingZone>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>((IEnumerable<ArtilleryShellingZone>)controller.dictionary_0.Keys), (Func<ArtilleryShellingZone, bool>)((ArtilleryShellingZone artilleryShellingZone) => artilleryShellingZone.ID == z.ID));
			})));
			int num = ((array.Length != 0) ? ((int)MyExtensions.Random(1f, array.Length)) : 0);
			ArtilleryShellingZone[] array2 = ((num >= array.Length) ? array : ValueEnumerableExtensions.ToArray<OrderBySkipTake<FromArray<ArtilleryShellingZone>, ArtilleryShellingZone, float>, ArtilleryShellingZone>(ValueEnumerableExtensions.Take<FromArray<ArtilleryShellingZone>, ArtilleryShellingZone, float>(ValueEnumerableExtensions.OrderBy<FromArray<ArtilleryShellingZone>, ArtilleryShellingZone, float>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone>(array), (Func<ArtilleryShellingZone, float>)((ArtilleryShellingZone _) => MyExtensions.Random(0f, 1f))), num)));
			for (int num2 = 0; num2 < array2.Length; num2++)
			{
				ArtilleryShellingZone zone = array2[num2];
				controller.FindShellingPointsByMapGeometryForZone(ref zone);
				ArtilleryBrigade nearestBrigade = controller.GetNearestBrigade(zone);
				controller.dictionary_0.Add(zone, new ZoneShellingProcess
				{
					roundsCountTarget = zone.ShellingRounds,
					shotsCountTarget = zone.ShotCount,
					shellingBrigadeID = nearestBrigade.ID,
					ShellingPoints = controller.GetRandomShellingPoints(zone),
					BoundsPoints = zone.ZoneBounds,
					pauseBetweenRoundOn = true
				});
				KnownZones[zone.ID] = zone;
				controller.AddNotifyShellingZOneToQueue(zone.ID);
			}
			KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess>[] array3 = ValueEnumerableExtensions.ToArray<FromDictionary<ArtilleryShellingZone, ZoneShellingProcess>, KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess>>(ValueEnumerableExtensions.Where<FromDictionary<ArtilleryShellingZone, ZoneShellingProcess>, KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess>>(ValueEnumerable.AsValueEnumerable<ArtilleryShellingZone, ZoneShellingProcess>(controller.dictionary_0), (Func<KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess>, bool>)((KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess> pair) =>
			{
				//IL_000b: Unknown result type (might be due to invalid IL or missing references)
				return ValueEnumerableExtensions.Contains<FromArray<string>, string>(ValueEnumerable.AsValueEnumerable<string>(change.Removed), pair.Key.ID);
			})));
			for (int num2 = 0; num2 < array3.Length; num2++)
			{
				KeyValuePair<ArtilleryShellingZone, ZoneShellingProcess> keyValuePair = array3[num2];
				KnownZones[keyValuePair.Key.ID] = keyValuePair.Key;
				foreach (int key in keyValuePair.Value.PlayersInAlertZone.Keys)
				{
					controller.SendPlayersAlertState(key, "", Vector3.zero);
				}
				keyValuePair.Value.RefreshProcess();
				controller.dictionary_0.Remove(keyValuePair.Key);
			}
			controller._needCheckPlayers = true;
		}
	}

	private static void QueueZoneChanges(string trigger)
	{
		if (_zonePolicy != null)
		{
			_zonePolicy.Apply(trigger, AddedZones, RemovedZones);
			if (AddedZones.Count != 0 || RemovedZones.Count != 0)
			{
				ZoneChanges.Enqueue((AddedZones.ToArray(), RemovedZones.ToArray()));
				Plugin.Log.LogInfo("[Artillery] " + trigger + ": include " + string.Join(",", AddedZones) + ", exclude " + string.Join(",", RemovedZones));
			}
		}
	}
}
