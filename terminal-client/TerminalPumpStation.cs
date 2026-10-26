using System;
using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalPumpStation
{
	internal class CabinetInfo
	{
		public string Path;

		public string Suffix;

		public Transform Root;
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_SelectAtRaidStart
	{
		private static void Postfix(GameWorld __instance)
		{
			if (!TerminalGate.On || _raidStarted || __instance == null)
			{
				return;
			}
			_raidStarted = true;
			if (TerminalCoop.Active)
			{
				return;
			}
			try
			{
				HashSet<string> humans = new HashSet<string>(StringComparer.Ordinal);
				if (__instance.RegisteredPlayers != null)
				{
					foreach (IPlayer registeredPlayer in __instance.RegisteredPlayers)
					{
						AddHuman(registeredPlayer);
					}
				}
				AddHuman(__instance.MainPlayer);
				_raidPlayerCount = Math.Max(1, humans.Count);
				_raidSeed = CreateRaidSeed();
				TryStage();
				void AddHuman(IPlayer player)
				{
					if (player != null)
					{
						string text = TerminalPumpSelection.HumanKey(player.IsAI, player.Profile?.Info?.Nickname, player.ProfileId, player.Id);
						if (text != null)
						{
							humans.Add(text);
						}
					}
				}
			}
			catch (Exception arg)
			{
				_staged = true;
				Plugin.Log.LogError($"[Pump] raid selection initialization failed: {arg}");
			}
		}
	}

	private const string BoxesFixedTrigger = "on_electric_boxes_fixed";

	private const string WaterRemoveTrigger = "Water_remove_1178689405";

	internal const float RepairSeconds = 5f;

	private const float UnsafeChance = 0.1f;

	private const string MultitoolTpl = "590c2e1186f77425357b6124";

	private static bool _staged;

	private static bool _raidStarted;

	private static int _raidPlayerCount;

	private static int _raidSeed;

	private static bool _hasCoopLayout;

	internal static bool PowerFixed;

	internal static bool WaterDrained;

	private static readonly HashSet<string> _broken = new HashSet<string>();

	private static readonly Dictionary<string, CabinetInfo> _cabinets = new Dictionary<string, CabinetInfo>();

	private static readonly HashSet<string> NativeSet = new HashSet<string> { "HandlerAnimator", "HandlerGOState", "HandlerGameObjectState", "HandlerPlaySoundAdvanced", "HandlerPlaySoundSequenced", "HandlerDamage" };

	internal static bool Ready => _staged;

	internal static void ResetForRaid()
	{
		_staged = false;
		_raidStarted = false;
		_raidPlayerCount = 0;
		_raidSeed = 0;
		_hasCoopLayout = false;
		PowerFixed = false;
		WaterDrained = false;
		_broken.Clear();
		_cabinets.Clear();
	}

	internal static void TryStage()
	{
		if (_staged || !_raidStarted || !Plugin.PumpStation.Value || !TerminalGate.On || (TerminalCoop.Active && !_hasCoopLayout))
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.TriggersEmitter == null)
		{
			return;
		}
		Transform transform = TerminalRigFill.FindRootNamed("Water_station");
		Transform transform2 = TerminalRigFill.FindRootNamed("Electric_box(AVR)_for_key_02");
		if (!transform || !transform2)
		{
			return;
		}
		try
		{
			JArray rows = JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_pump.json")))["rows"] as JArray;
			Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>();
			TerminalRigFill.IndexTree(transform, "Water_station", dictionary);
			TerminalRigFill.IndexTree(transform2, "Electric_box(AVR)_for_key_02", dictionary);
			TerminalRigFill.StageRows(rows, NativeSet, dictionary, "Pump");
			SelectBrokenCabinets(rows, dictionary);
			if (dictionary.TryGetValue("Water_station/Water/Interactive_Button", out var value))
			{
				value.gameObject.SetActive(value: false);
			}
			if (dictionary.TryGetValue("Water_station/Water/Interactive_Button_off", out var value2))
			{
				value2.gameObject.SetActive(value: true);
			}
			_staged = true;
			Plugin.Log.LogInfo($"[Pump] PUMP STATION RESTORED — {_broken.Count} cabinet(s) broken, repair to power the panel");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Pump] staging failed: {arg}");
			_staged = true;
		}
	}

	internal static void ApplyCoopLayout(int humans, int seed)
	{
		if (!_hasCoopLayout)
		{
			_hasCoopLayout = true;
			_raidPlayerCount = Math.Max(1, humans);
			_raidSeed = seed;
			TryStage();
		}
	}

	internal static bool ApplyCoopRepair(string path)
	{
		if (!_staged)
		{
			return false;
		}
		if (_cabinets.TryGetValue(path, out var value) && IsBroken(value))
		{
			OnRepaired(value);
		}
		return true;
	}

	internal static CabinetInfo FindBroken(string path)
	{
		if (!_cabinets.TryGetValue(path, out var value) || !IsBroken(value))
		{
			return null;
		}
		return value;
	}

	internal static void ApplyCoopShock(string path)
	{
		if (_cabinets.TryGetValue(path, out var value))
		{
			OnUnsafeRepairFailed(value);
		}
	}

	internal static void Drain()
	{
		if (PowerFixed && !WaterDrained)
		{
			if (TerminalCoop.Active && !TerminalCoop.Applying)
			{
				TerminalCoop.Request(TerminalEvent.PumpDrain);
				return;
			}
			WaterDrained = true;
			TerminalGatesExplosion.Emit("Water_remove_1178689405");
		}
	}

	private static int CreateRaidSeed()
	{
		AbstractGame abstractGame = (Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null);
		if (abstractGame != null && abstractGame.GetType().Name.IndexOf("Coop", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Type type = AccessTools.TypeByName("FikaBackendUtils");
			string text = ((type == null) ? null : AccessTools.Property(type, "RaidCode")?.GetValue(null)?.ToString());
			string text2 = ((type == null) ? null : AccessTools.Property(type, "ServerGuid")?.GetValue(null)?.ToString());
			if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2))
			{
				return TerminalPumpSelection.RaidSeed(text + "|" + text2);
			}
			Plugin.Log.LogWarning("[Pump] co-op match seed unavailable; cabinet choices cannot be synchronized across peers");
		}
		return TerminalPumpSelection.RaidSeed(Guid.NewGuid().ToString("N"));
	}

	private static void SelectBrokenCabinets(JArray rows, Dictionary<string, Transform> index)
	{
		JObject jObject = TerminalPumpSelection.Configuration(rows, _raidPlayerCount);
		List<string> list = TerminalPumpSelection.Pool(jObject);
		int count = jObject.Value<int>("CabinetsToBreak");
		foreach (JToken row in rows)
		{
			if (row.Value<string>("cls") != "HandlerPlantAction")
			{
				continue;
			}
			string text = row.Value<string>("path") ?? "";
			string text2 = row["fields"]?.Value<string>("_triggerId") ?? "";
			if (text2.StartsWith("start_safe_repair_electricity_terminal_"))
			{
				string text3 = text.Substring(0, text.IndexOf("/TriggersLogic", StringComparison.Ordinal));
				string suffix = text2.Substring("start_safe_repair_electricity_terminal_".Length);
				if (!_cabinets.ContainsKey(text3) && index.TryGetValue(text3, out var value))
				{
					_cabinets[text3] = new CabinetInfo
					{
						Path = text3,
						Suffix = suffix,
						Root = value
					};
				}
			}
		}
		foreach (string item in list)
		{
			if (!_cabinets.ContainsKey(item) || !index.ContainsKey(item + "/Electric_box_Switch"))
			{
				throw new InvalidOperationException($"[Pump] authored pool incomplete ({list.Count} expected): '{item}' or its repair switch is missing");
			}
		}
		foreach (string item2 in TerminalPumpSelection.Choose(list, count, new System.Random(_raidSeed)))
		{
			_broken.Add(item2);
		}
		foreach (CabinetInfo value3 in _cabinets.Values)
		{
			bool flag = _broken.Contains(value3.Path);
			Dictionary<string, bool> dictionary = TerminalPumpSelection.InitialStates(rows, value3.Path, flag);
			value3.Root.gameObject.SetActive(value: false);
			foreach (KeyValuePair<string, bool> item3 in dictionary)
			{
				if (!(item3.Key == value3.Path))
				{
					if (index.TryGetValue(item3.Key, out var value2))
					{
						value2.gameObject.SetActive(item3.Value);
					}
					else
					{
						Plugin.Log.LogWarning("[Pump] initial-state target missing: '" + item3.Key + "'");
					}
				}
			}
			value3.Root.gameObject.SetActive(flag);
		}
		Plugin.Log.LogInfo(string.Format("[Pump] selection: humans={0}, bracket={1},", _raidPlayerCount, jObject.Value<int>("MaxGroupSize")) + string.Format(" pool={0}/{1}, broken={2}, seed={3}; candidates=[{4}]; selected=[{5}]", list.Count, list.Count, _broken.Count, _raidSeed, string.Join(", ", list), string.Join(", ", _broken)));
	}

	internal static CabinetInfo CabinetForSwitch(Transform sw)
	{
		foreach (CabinetInfo value in _cabinets.Values)
		{
			if (sw.IsChildOf(value.Root))
			{
				return value;
			}
		}
		return null;
	}

	internal static bool IsBroken(CabinetInfo cab)
	{
		if (cab != null)
		{
			return _broken.Contains(cab.Path);
		}
		return false;
	}

	internal static bool HasMultitool(Player player)
	{
		if (player?.Profile?.Inventory == null)
		{
			return false;
		}
		foreach (Item allRealPlayerItem in player.Profile.Inventory.AllRealPlayerItems)
		{
			if (allRealPlayerItem.TemplateId == (MongoID)"590c2e1186f77425357b6124")
			{
				return true;
			}
		}
		return false;
	}

	internal static void OnRepaired(CabinetInfo cab)
	{
		if (!IsBroken(cab))
		{
			return;
		}
		if (TerminalCoop.Active && !TerminalCoop.Applying)
		{
			TerminalCoop.Request(TerminalEvent.PumpRepair, cab.Path);
			return;
		}
		_broken.Remove(cab.Path);
		TerminalGatesExplosion.Emit("repaired_electicity_terminal_" + cab.Suffix);
		Plugin.Log.LogInfo($"[Pump] cabinet repaired ({_broken.Count} left)");
		if (_broken.Count == 0 && !PowerFixed)
		{
			PowerFixed = true;
			TerminalGatesExplosion.Emit("on_electric_boxes_fixed");
			TerminalGatesExplosion.PlayAt(TerminalFxBundle.FindClip("amb_terminal_interactive_generator_start_electricity"), cab.Root.position, 30f);
			NotificationManager.DisplayMessageNotification("Power restored — the pump station panel is live", ENotificationDurationType.Long, ENotificationIconType.Default, Color.green);
		}
	}

	internal static void OnUnsafeRepairFailed(CabinetInfo cab)
	{
		TerminalGatesExplosion.Emit("repair_unsafe_failed_electicity_terminal_" + cab.Suffix);
		NotificationManager.DisplayMessageNotification("Repair failed — the cabinet shocked you", ENotificationDurationType.Default, ENotificationIconType.Alert, Color.red);
	}
}
