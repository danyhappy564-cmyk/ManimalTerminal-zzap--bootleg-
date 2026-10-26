using System;
using System.Collections.Generic;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: raid-local registry, squad transitions and a bounded round-robin sleep scan.
internal sealed class TerminalGhostDirector : MonoBehaviour
{
	internal sealed class BotRecord
	{
		internal BotOwner Bot;
		internal Squad Squad;
		internal float CreatedAt;
		internal float LastDamageAt = float.NegativeInfinity;
		internal float BlockUntil;
		internal bool Dormant;
		internal EBotState SavedState;
		internal object SainController;
		internal AICoreAgent<BotLogicDecision> Agent;
		internal BotZone SpawnZone;
		internal string SpawnZoneName;
		internal readonly TerminalGhostBody Body = new TerminalGhostBody();
	}

	internal sealed class Squad
	{
		internal BotsGroup Group;
		internal readonly List<BotRecord> Members = new List<BotRecord>(16);
		internal bool Dormant;
		internal bool Queued;
		internal int CombatSlot = -1;
		internal TerminalGhostCombat.Fight Fight;
	}

	private static TerminalGhostDirector _instance;
	private static BotSpawner _spawner;
	private readonly Dictionary<BotOwner, BotRecord> _bots = new Dictionary<BotOwner, BotRecord>(512);
	private readonly Dictionary<BotsGroup, Squad> _byGroup = new Dictionary<BotsGroup, Squad>(128);
	private readonly Dictionary<AICoreAgent<BotLogicDecision>, BotRecord> _agents = new Dictionary<AICoreAgent<BotLogicDecision>, BotRecord>(512);
	private readonly List<Squad> _groups = new List<Squad>(128);
	private readonly List<Player> _humans = new List<Player>(16);
	private readonly Queue<Squad> _wakeQueue = new Queue<Squad>(128);
	private readonly List<BotOwner> _retired = new List<BotOwner>(512);
	private readonly TerminalGhostCombat _combat = new TerminalGhostCombat();
	private int _cursor;
	private float _nextScan;
	private float _nextReport;
	private int _sleeps;
	private int _wakes;
	private int _samples;
	private double _sumMs;
	private double _maxMs;
	private double _pendingHookMs;
	private bool _inMeasuredPass;
	private bool _listening;
	private bool _restoring;

	internal static bool Running => _instance && TerminalGate.On && Plugin.GhostMode.Value
		&& !TerminalGhostPatches.FikaPresent && TerminalGhostPatches.CoreReady;

	internal static void Attach(BotSpawner spawner)
	{
		if (_spawner == spawner) return;
		ResetForRaid();
		_spawner = spawner;
	}

	internal static void EnsureForRaid()
	{
		if (_instance || !TerminalGate.On || TerminalGhostPatches.FikaPresent || !TerminalGhostPatches.CoreReady) return;
		_instance = new GameObject("Terminal_GhostDirector").AddComponent<TerminalGhostDirector>();
		_instance._nextReport = Time.unscaledTime + 30f;
		if (_spawner == null)
			_spawner = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance : null)?.BotsController?.BotSpawner;
		if (_spawner == null) return;
		_spawner.OnBotCreated += _instance.Register;
		_spawner.OnBotRemoved += _instance.Remove;
		_instance._listening = true;
		// Existing births are rare at OnGameStarted; conservatively give them 15s.
		if (_spawner._bots != null)
			foreach (BotOwner bot in _spawner._bots.BotOwners) _instance.Register(bot);
	}

	private void Register(BotOwner bot)
	{
		if (!bot || bot.IsDead || bot.BotsGroup == null || _bots.ContainsKey(bot)) return;
		try
		{
			Squad squad = GetSquad(bot.BotsGroup);
			Wake(squad); // a new live member invalidates the whole squad's distance test
			var record = new BotRecord { Bot = bot, Squad = squad, CreatedAt = Time.time, Agent = bot.Brain?.Agent };
			record.SpawnZone = bot.SpawnBotZone;
			record.SpawnZoneName = record.SpawnZone ? record.SpawnZone.name : null;
			if (bot.Profile?.Info?.Settings != null) TerminalRoles.IsRuaf(bot.Profile.Info.Settings.Role);
			_bots.Add(bot, record);
			squad.Members.Add(record);
			if (record.Agent != null) _agents[record.Agent] = record;
		}
		catch (Exception ex) { Fault(bot, ex); }
	}

	private Squad GetSquad(BotsGroup group)
	{
		if (_byGroup.TryGetValue(group, out var squad)) return squad;
		squad = new Squad { Group = group };
		_byGroup.Add(group, squad);
		_groups.Add(squad);
		_combat.Register(squad);
		return squad;
	}

	private void Remove(BotOwner bot)
	{
		// Unity's destroyed-object bool can be false while the registry key is valid.
		if (ReferenceEquals(bot, null) || !_bots.TryGetValue(bot, out var record)) return;
		if (record.Dormant) Wake(record.Squad);
		try { Restore(record); }
		catch (Exception ex) { Plugin.Log.LogWarning("[Ghost] removed bot restore failed: " + ex.Message); }
		if (record.Agent != null) _agents.Remove(record.Agent);
		_bots.Remove(bot);
		Squad squad = record.Squad;
		squad.Members.Remove(record);
		if (squad.Members.Count == 0)
		{
			_combat.Unregister(squad);
			_byGroup.Remove(squad.Group);
			_groups.Remove(squad);
			squad.Dormant = false;
		}
	}

	internal static void GroupChanged(BotOwner bot)
	{
		if (!_instance || !bot) return;
		float lastDamage = float.NegativeInfinity;
		if (_instance._bots.TryGetValue(bot, out var previous)) lastDamage = previous.LastDamageAt;
		_instance.Remove(bot);
		_instance.Register(bot);
		if (_instance._bots.TryGetValue(bot, out var current)) current.LastDamageAt = lastDamage;
	}

	internal static void PlacementChanged(BotOwner bot)
	{
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record)) return;
		record.SpawnZone = bot.SpawnBotZone;
		record.SpawnZoneName = record.SpawnZone ? record.SpawnZone.name : null;
		record.BlockUntil = Time.time + 15f;
	}

	private void Update()
	{
		long start = Stopwatch.GetTimestamp();
		_inMeasuredPass = true;
		try
		{
			if (!TerminalGate.On)
			{
				ResetForRaid();
				return;
			}
			if (!Running || !Plugin.GhostBrainThrottle.Value)
			{
				// Full stop is the stage-1 safety contract. Turning its guard off wakes
				// our bots instead of leaving invisible bots with a live action node.
				RestoreAll();
				return;
			}
			TerminalCoop.CollectHumans(_humans);
			_retired.Clear();
			foreach (BotRecord record in _bots.Values)
				if (!record.Bot || record.Bot.IsDead || (record.Bot.HealthController != null && !record.Bot.HealthController.IsAlive)) _retired.Add(record.Bot);
			for (int i = 0; i < _retired.Count; i++) Remove(_retired[i]);
			float wake = Mathf.Min(Plugin.GhostWakeDistance.Value, Plugin.GhostSleepDistance.Value - 1f);
			float wakeSq = wake * wake;
			// Proximity and exclusions are urgent, independent of the sleep budget.
			for (int i = 0; i < _groups.Count; i++)
			{
				Squad squad = _groups[i];
				if (!squad.Dormant) continue;
				for (int j = 0; j < squad.Members.Count; j++)
				{
					BotRecord record = squad.Members[j];
					BotOwner bot = record.Bot;
					try
					{
						if (!bot || bot.IsDead || bot.HealthController == null || !bot.HealthController.IsAlive
							|| _humans.Count == 0 || bot.BotsGroup != squad.Group || TerminalGhostPolicy.Protected(record)
							|| bot.BotState != EBotState.NonActive || bot.Brain?.Agent != record.Agent || TerminalGhostPolicy.NearHuman(bot, _humans, wakeSq))
						{
							Wake(squad);
							break;
						}
					}
					catch (Exception ex) { Fault(bot, ex); break; }
				}
			}
			// Ordinary enemy-selection wakes: at most ONE squad per rendered frame.
			while (_wakeQueue.Count > 0)
			{
				Squad squad = _wakeQueue.Dequeue();
				squad.Queued = false;
				if (!squad.Dormant) continue;
				Wake(squad);
				break;
			}
			if (Time.time >= _nextScan)
			{
				_nextScan = Time.time + 0.5f;
				int budget = Math.Min(4, _groups.Count);
				for (int i = 0; i < budget; i++)
				{
					if (_cursor >= _groups.Count) _cursor = 0;
					Evaluate(_groups[_cursor++]);
				}
			}
			_combat.Tick(Time.time, _humans);
		}
		catch (Exception ex)
		{
			RestoreAll();
			Plugin.Log.LogError("[Ghost] director recovered all owned bots: " + ex.Message);
		}
		finally
		{
			RecordCost(start);
			_inMeasuredPass = false;
			Report();
		}
	}

	private void Evaluate(Squad squad)
	{
		if (squad.Dormant || squad.Queued || _humans.Count == 0 || TerminalGhostPolicy.Cutscene) return;
		float now = Time.time;
		float sleep = Plugin.GhostSleepDistance.Value;
		float sleepSq = sleep * sleep;
		bool any = false;
		// Test the ACTUAL group, including any member missing from our registry.
		for (int i = 0; i < squad.Group.MembersCount; i++)
		{
			BotOwner bot = squad.Group.Member(i);
			if (!bot || bot.IsDead || bot.HealthController == null || !bot.HealthController.IsAlive) continue;
			try
			{
				if (!_bots.TryGetValue(bot, out var record) || record.Squad != squad || now < record.BlockUntil
					|| !TerminalGhostPolicy.CanSleep(record, now) || TerminalGhostPolicy.NearHuman(bot, _humans, sleepSq)) return;
				// Agent can be created after the birth callback; dictionary assignment reuses storage.
				if (record.Agent != bot.Brain.Agent)
				{
					if (record.Agent != null) _agents.Remove(record.Agent);
					record.Agent = bot.Brain.Agent;
					_agents[record.Agent] = record;
				}
				any = true;
			}
			catch (Exception ex) { Fault(bot, ex); return; }
		}
		if (!any) return;
		// A missed membership event must never put a bot from another group to sleep.
		for (int i = 0; i < squad.Members.Count; i++)
		{
			BotOwner member = squad.Members[i].Bot;
			if (member && !member.IsDead && (member.BotsGroup != squad.Group || !squad.Group.Contains(member))) return;
		}
		squad.Dormant = true;
		for (int i = 0; i < squad.Members.Count; i++)
		{
			BotRecord record = squad.Members[i];
			BotOwner bot = record.Bot;
			if (!bot || bot.IsDead) continue;
			try
			{
				record.SavedState = bot.BotState;
				record.Dormant = true;
				bot.ShootData?.EndShoot();
				bot.Sprint(false);
				bot.Mover.Stop();
				bot.GetPlayer.Move(Vector2.zero);
				bot.DecisionQueue?.Clear();
				TerminalGhostPatches.ClearSain(record);
				bot.Memory.GoalEnemy = null;
				bot.Memory.GoalTarget?.Clear();
				bot.BotState = EBotState.NonActive;
				record.Body.Hide(bot.GetPlayer);
			}
			catch (Exception ex) { Fault(bot, ex); return; }
		}
		ClearIncomingTargets(squad);
		if (squad.Dormant) _sleeps++;
	}

	private void ClearIncomingTargets(Squad squad)
	{
		foreach (BotRecord record in _bots.Values)
		{
			BotOwner bot = record.Bot;
			if (!bot || record.Dormant) continue;
			try
			{
				BotOwner target = bot.Memory?.GoalEnemy?.Person?.AIData?.BotOwner;
				if (!target || target.BotsGroup != squad.Group) continue;
				bot.ShootData?.EndShoot();
				TerminalGhostPatches.ClearSain(record);
				bot.Memory.GoalEnemy = null;
			}
			catch (Exception ex) { Fault(bot, ex); Wake(squad); }
		}
	}

	private void LateUpdate()
	{
		if (!Running) return;
		long start = Stopwatch.GetTimestamp();
		_inMeasuredPass = true;
		foreach (BotRecord record in _bots.Values)
		{
			if (!record.Dormant) continue;
			try { record.Body.Enforce(); }
			catch (Exception ex) { Fault(record.Bot, ex); }
		}
		// Include late visibility maintenance in the same frame sample.
		double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency + _pendingHookMs;
		_pendingHookMs = 0;
		_inMeasuredPass = false;
		_sumMs += ms;
		_frameMs += ms;
		if (_frameMs > _maxMs) _maxMs = _frameMs;
	}

	private double _frameMs;

	private void RecordCost(long start)
	{
		_frameMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency + _pendingHookMs;
		_pendingHookMs = 0;
		_sumMs += _frameMs;
		if (_frameMs > _maxMs) _maxMs = _frameMs;
		_samples++;
	}

	private void Report()
	{
		if (Time.unscaledTime < _nextReport) return;
		_nextReport = Time.unscaledTime + 30f;
		if (Plugin.GhostDiagnostics.Value && Plugin.GhostMode.Value)
		{
			int dormant = 0, total = 0;
			foreach (BotRecord record in _bots.Values)
			{
				if (!record.Bot || record.Bot.IsDead) continue;
				total++;
				if (record.Dormant) dormant++;
			}
			Plugin.Log.LogInfo($"[Ghost] dormant {dormant}/total {total}, sleeps {_sleeps}/wakes {_wakes}, fights {_combat.ActiveCount}, shots {_combat.Shots}, last30s avg {(_samples == 0 ? 0 : _sumMs / _samples):F3} max {_maxMs:F3} ms");
		}
		_sleeps = _wakes = _samples = 0;
		_combat.ResetShots();
		_sumMs = _maxMs = 0;
	}

	private void Restore(BotRecord record)
	{
		if (!record.Dormant) return;
		// Clear the guard BEFORE restoring health/activation-dependent behavior.
		record.Dormant = false;
		try { record.Body.Restore(); }
		finally
		{
			BotOwner bot = record.Bot;
			if (bot && !bot.IsDead && bot.HealthController != null && bot.HealthController.IsAlive && bot.BotState == EBotState.NonActive)
				bot.BotState = record.SavedState;
		}
	}

	private void Wake(Squad squad)
	{
		_combat.Cancel(squad);
		if (!squad.Dormant) return;
		long start = _inMeasuredPass ? 0 : Stopwatch.GetTimestamp();
		squad.Dormant = false;
		for (int i = 0; i < squad.Members.Count; i++)
		{
			BotRecord record = squad.Members[i];
			record.BlockUntil = Time.time + 2f;
			try { Restore(record); }
			catch (Exception ex) { Plugin.Log.LogError("[Ghost] body restore failed: " + ex.Message); }
		}
		_wakes++;
		if (start != 0) _pendingHookMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
	}

	private void RestoreAll()
	{
		if (_restoring) return;
		_restoring = true;
		try
		{
			_combat.CancelAll();
			for (int i = 0; i < _groups.Count; i++)
			{
				Wake(_groups[i]);
				_groups[i].Queued = false;
			}
			_wakeQueue.Clear();
		}
		finally { _restoring = false; }
	}

	internal static bool IsDormant(BotOwner bot)
	{
		return _instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record) && record.Dormant;
	}

	internal static bool IsDormantAgent(AICoreAgent<BotLogicDecision> agent)
	{
		return _instance && agent != null && _instance._agents.TryGetValue(agent, out var record) && record.Dormant;
	}

	internal static void WakeNow(BotOwner bot)
	{
		if (_instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record)) _instance.Wake(record.Squad);
	}

	// Combat alone may restore one member without waking its surviving squad.
	internal static void RestoreForCombatDeath(BotRecord record)
	{
		if (!_instance || !record.Dormant || !record.Bot
			|| !_instance._bots.TryGetValue(record.Bot, out var current) || current != record)
			throw new InvalidOperationException("Ghost casualty is no longer owned/dormant");
		_instance.Restore(record);
	}

	internal static void WakeSquad(Squad squad)
	{
		if (_instance && squad != null) _instance.Wake(squad);
	}

	internal static void Damage(Player player)
	{
		if (TerminalGhostCombatDamage.IsSimulated(player)) return;
		BotOwner bot = player?.AIData?.BotOwner;
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record)) return;
		record.LastDamageAt = Time.time;
		_instance.Wake(record.Squad);
	}

	internal static void ForgetTarget(BotOwner bot)
	{
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record)) return;
		try { TerminalGhostPatches.ClearSain(record); }
		catch (Exception ex) { Fault(bot, ex); }
	}

	internal static bool AllowTarget(BotOwner observer, IPlayer person)
	{
		if (IsDormant(observer)) return false;
		BotOwner target = person?.AIData?.BotOwner;
		if (!_instance || !target || !_instance._bots.TryGetValue(target, out var record) || !record.Dormant) return true;
		if (observer && !observer.IsDead && observer.BotState == EBotState.Active && !record.Squad.Queued)
		{
			record.Squad.Queued = true;
			_instance._wakeQueue.Enqueue(record.Squad);
		}
		return false; // never accept a still-hidden target while waiting for the budget
	}

	internal static void WeaponParented(WeaponPrefab weapon, IPlayer player)
	{
		BotOwner bot = player?.AIData?.BotOwner;
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record) || !record.Dormant) return;
		try { record.Body.Capture(weapon.transform); record.Body.Enforce(); }
		catch (Exception ex) { Fault(bot, ex); }
	}

	internal static void WeaponReturning(WeaponPrefab weapon)
	{
		if (!_instance) return;
		foreach (BotRecord record in _instance._bots.Values)
		{
			if (!record.Dormant) continue;
			try { record.Body.ReleaseWeapon(weapon.transform); }
			catch (Exception ex) { Fault(record.Bot, ex); }
		}
	}

	internal static void Fault(BotOwner bot, Exception ex)
	{
		WakeNow(bot);
		if (_instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record)) record.BlockUntil = float.PositiveInfinity;
		Plugin.Log.LogWarning("[Ghost] bot restored and excluded after exception: " + ex.Message);
	}

	internal static void ResetForRaid()
	{
		if (_instance)
		{
			_instance.RestoreAll();
			_instance.Unlisten();
			Destroy(_instance.gameObject);
			_instance = null;
		}
		_spawner = null;
	}

	private void Unlisten()
	{
		if (!_listening || _spawner == null) return;
		_spawner.OnBotCreated -= Register;
		_spawner.OnBotRemoved -= Remove;
		_listening = false;
	}

	private void OnDisable() { RestoreAll(); }
	private void OnDestroy()
	{
		RestoreAll();
		Unlisten();
		if (_instance == this) _instance = null;
	}
}
