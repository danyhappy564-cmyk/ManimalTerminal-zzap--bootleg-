using System;
using System.Collections.Generic;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using UnityEngine;
using BlockReason = Manimal.Terminal.TerminalGhostPolicy.BlockReason;

namespace Manimal.Terminal;

// zzap: raid-local registry, squad transitions and a bounded round-robin sleep scan.
internal sealed class TerminalGhostDirector : MonoBehaviour
{
	internal const int MaxBots = 512, MaxSquads = 256, MaxMembers = 32, MaxHumans = 16;
	internal enum WakeReason { Near, Near50, Scope, Target, Damage, Protect, Move, Encounter, Fault, Other, Count }
	internal sealed class BotRecord
	{
		internal BotOwner Bot;
		internal Squad Squad;
		internal float CreatedAt;
		internal float LastDamageAt = float.NegativeInfinity;
		internal float BlockUntil;
		internal TerminalGhostPolicy.HandsReason HandsState;
		internal float HandsSince;
		internal bool Dormant;
		internal bool SainThrottled;
		internal bool SainWakePending;
		internal int SainWakeDeadline;
		internal bool FaultReported;
		internal EBotState SavedState;
		internal object SainController;
		internal AICoreAgent<BotLogicDecision> Agent;
		internal BotZone SpawnZone;
		internal string SpawnZoneName;
		internal Vector3 GhostOffset;
		internal readonly TerminalGhostBody Body = new TerminalGhostBody();
	}

	internal sealed class Squad
	{
		internal BotsGroup Group;
		internal readonly List<BotRecord> Members = new List<BotRecord>(16);
		internal bool Dormant;
		internal bool Queued;
		internal bool Waking;
		internal BotOwner Observer;
		internal int WakeCursor;
		internal WakeReason WakeCause;
		internal readonly TerminalGhostMovement.State Motion = new TerminalGhostMovement.State();
		internal int CombatSlot = -1;
		internal TerminalGhostCombat.Fight Fight;
		internal BlockReason Blocked;
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
	private readonly TerminalGhostScope _scope = new TerminalGhostScope();
	private readonly int[] _blocked = new int[(int)BlockReason.Count];
	private readonly int[] _wokeBy = new int[(int)WakeReason.Count];
	private int _cursor;
	private float _nextScan;
	private float _nextReport;
	private int _sleeps;
	private int _wakes;
	private int _samples;
	private int _sainWakePending;
	private int _scopeCursor, _wakeCursor, _motionCursor;
	private int _scopeWakes, _staggered, _ghostedAwake;
	private float _nextMemberWake;
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
		if (_spawner == null)
			_spawner = (Singleton<IBotGame>.Instantiated ? Singleton<IBotGame>.Instance : null)?.BotsController?.BotSpawner;
		if (_spawner == null) return;
		_instance = new GameObject("Terminal_GhostDirector").AddComponent<TerminalGhostDirector>();
		_instance._nextReport = Time.unscaledTime + 30f;
		_spawner.OnBotCreated += _instance.Register;
		_spawner.OnBotRemoved += _instance.Remove;
		_instance._listening = true;
		// Existing births are rare at OnGameStarted; conservatively give them 15s.
		if (_spawner._bots != null)
		{
			int examined = 0;
			foreach (BotOwner bot in _spawner._bots.BotOwners)
			{
				if (++examined > MaxBots) break;
				_instance.Register(bot);
			}
		}
	}

	private void Register(BotOwner bot)
	{
		if (!bot || bot.IsDead || bot.BotsGroup == null || _bots.ContainsKey(bot)) return;
		if (_bots.Count >= MaxBots || (!_byGroup.ContainsKey(bot.BotsGroup) && _groups.Count >= MaxSquads)) return;
		try
		{
			Squad squad = GetSquad(bot.BotsGroup);
			Wake(squad, WakeReason.Protect); // a new member invalidates the distance test
			var record = new BotRecord { Bot = bot, Squad = squad, CreatedAt = Time.time, Agent = bot.Brain?.Agent };
			record.SpawnZone = bot.SpawnBotZone;
			record.SpawnZoneName = record.SpawnZone ? record.SpawnZone.name : null;
			if (bot.Profile?.Info?.Settings != null)
			{
				TerminalRoles.IsRuaf(bot.Profile.Info.Settings.Role);
				TerminalGhostPolicy.PrepareRole(bot.Profile.Info.Settings.Role);
			}
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
		if (record.Dormant || record.Squad.Waking) Wake(record.Squad, WakeReason.Protect);
		try { Restore(record); }
		catch (Exception ex) { Plugin.Log.LogWarning("[Ghost] removed bot restore failed: " + ex.Message); }
		if (record.Agent != null) _agents.Remove(record.Agent);
		FinishSainWake(bot);
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
		float blockUntil = 0f;
		bool faultReported = false, refreshSain = false;
		if (_instance._bots.TryGetValue(bot, out var previous))
		{
			lastDamage = previous.LastDamageAt;
			blockUntil = previous.BlockUntil;
			faultReported = previous.FaultReported;
			refreshSain = previous.SainThrottled || previous.SainWakePending;
		}
		_instance.Remove(bot);
		_instance.Register(bot);
		if (_instance._bots.TryGetValue(bot, out var current))
		{
			current.LastDamageAt = lastDamage;
			current.BlockUntil = Mathf.Max(current.BlockUntil, blockUntil);
			current.FaultReported = faultReported;
			if (refreshSain && TerminalGhostSain.NeedsWakeRefresh)
			{
				current.SainWakePending = true;
				current.SainWakeDeadline = Time.frameCount + 2;
				_instance._sainWakePending++;
			}
		}
	}

	internal static void PlacementChanged(BotOwner bot)
	{
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record)) return;
		_instance.Wake(record.Squad, WakeReason.Move); // external placements only
		record.SpawnZone = bot.SpawnBotZone;
		record.SpawnZoneName = record.SpawnZone ? record.SpawnZone.name : null;
		record.BlockUntil = Mathf.Max(record.BlockUntil, Time.time + 15f);
	}

	private void Update()
	{
		TerminalGhostSain.BeginFrame();
		long start = Stopwatch.GetTimestamp();
		_inMeasuredPass = true;
		try
		{
			if (!TerminalGate.On)
			{
				ResetForRaid();
				return;
			}
			// Wake cleanup must also finish or fail open after GhostMode/brain throttle
			// is switched OFF; those switches take the early restore path below.
			if (_sainWakePending > 0)
			{
				int botLimit1 = 0;
				foreach (BotRecord record in _bots.Values)
				{
					if (++botLimit1 > MaxBots) break;
					if (!record.SainWakePending) continue;
					if (!record.Bot || record.Bot.IsDead || record.Bot.HealthController?.IsAlive != true) FinishSainWake(record.Bot);
					else if (Time.frameCount > record.SainWakeDeadline)
						Fault(record.Bot, new InvalidOperationException("SAIN wake update did not complete within two frames"));
				}
			}
			if (!Running || !Plugin.GhostBrainThrottle.Value)
			{
				// Full stop is the stage-1 safety contract. Turning its guard off wakes
				// our bots instead of leaving invisible bots with a live action node.
				RestoreAll();
				return;
			}
			CollectHumans();
			_retired.Clear();
			int botLimit2 = 0;
			foreach (BotRecord record in _bots.Values)
			{
				if (++botLimit2 > MaxBots) break;
				if (!record.Bot || record.Bot.IsDead || (record.Bot.HealthController != null && !record.Bot.HealthController.IsAlive)) _retired.Add(record.Bot);
			}
			for (int i = 0; i < _retired.Count && i < MaxBots; i++) Remove(_retired[i]);
			float wake = Mathf.Min(Plugin.GhostWakeDistance.Value, Plugin.GhostSleepDistance.Value - 1f);
			float wakeSq = wake * wake;
			_scope.Capture(_humans, wake);
			// Proximity and exclusions are urgent, independent of the sleep budget.
			for (int i = 0; i < _groups.Count && i < MaxSquads; i++)
			{
				Squad squad = _groups[i];
				if (!squad.Dormant && !squad.Waking) continue;
				for (int j = 0; j < squad.Members.Count && j < MaxMembers; j++)
				{
					BotRecord record = squad.Members[j];
					BotOwner bot = record.Bot;
					try
					{
						if (!bot || bot.IsDead || bot.HealthController == null || !bot.HealthController.IsAlive
							|| _humans.Count == 0 || bot.BotsGroup != squad.Group || TerminalGhostPolicy.Protected(record)
							|| TerminalGhostPolicy.Cutscene
							|| (record.Dormant && (bot.BotState != EBotState.NonActive || bot.Brain?.Agent != record.Agent)))
						{
							Wake(squad, WakeReason.Protect);
							break;
						}
						if (TerminalGhostPolicy.NearHuman(bot, _humans, 2500f))
						{
							Wake(squad, WakeReason.Near50);
							break;
						}
						if (squad.Dormant && TerminalGhostPolicy.NearHuman(bot, _humans, wakeSq))
						{
							Wake(squad, WakeReason.Near, false);
							break;
						}
					}
					catch (Exception ex) { Fault(bot, ex); break; }
				}
			}
			// Ordinary enemy-selection wakes: at most ONE squad per rendered frame.
			for (int dequeued = 0; dequeued < MaxSquads && _wakeQueue.Count > 0; dequeued++)
			{
				Squad squad = _wakeQueue.Dequeue();
				squad.Queued = false;
				BotOwner observer = squad.Observer;
				squad.Observer = null;
				if (!squad.Dormant) continue;
				if (!observer || !_bots.TryGetValue(observer, out var other)
					|| !TerminalGhostCombat.InEncounterRange(squad, other.Squad)
					|| !TryGhostEncounter(squad, other.Squad)) Wake(squad, WakeReason.Target, false);
				break;
			}
			ScanScope();
			PumpWakes();
			if (Time.time >= _nextScan)
			{
				_nextScan = Time.time + 0.5f;
				int budget = Math.Min(4, _groups.Count);
				for (int i = 0; i < budget && i < 4; i++)
				{
					if (_cursor >= _groups.Count) _cursor = 0;
					TrySleep(_groups[_cursor++]);
				}
			}
			int pathsLeft = 1, movesLeft = 4;
			int motionFirst = _motionCursor;
			_motionCursor = (_motionCursor + 1) % Math.Max(1, _groups.Count);
			for (int i = 0, count = Math.Min(_groups.Count, MaxSquads); i < count; i++)
			{
				Squad squad = _groups[(motionFirst + i) % count];
				try { TerminalGhostMovement.Tick(squad, _humans, ref pathsLeft, ref movesLeft); }
				catch (Exception ex)
				{
					BotOwner failed = squad.Motion.Leader?.Bot;
					Wake(squad, WakeReason.Fault);
					if (failed) Fault(failed, ex);
					else Plugin.Log.LogWarning("[GhostMove] restored squad: " + ex.Message);
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

	private void CollectHumans()
	{
		_humans.Clear();
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		if (TerminalCoop.IsHuman(world?.MainPlayer)) _humans.Add(world.MainPlayer);
		if (world?.RegisteredPlayers == null) return;
		int examined = 0;
		foreach (IPlayer candidate in world.RegisteredPlayers)
		{
			if (++examined > MaxBots || _humans.Count >= MaxHumans) break;
			if (candidate is Player player && TerminalCoop.IsHuman(player) && !_humans.Contains(player)) _humans.Add(player);
		}
	}

	private void ScanScope()
	{
		int count = Math.Min(_groups.Count, MaxSquads);
		int wakeBudget = 2;
		for (int i = 0; i < count && i < 16 && wakeBudget > 0; i++)
		{
			if (_scopeCursor >= count) _scopeCursor = 0;
			Squad squad = _groups[_scopeCursor++];
			if (!squad.Dormant) continue;
			for (int j = 0; j < squad.Members.Count && j < MaxMembers; j++)
			{
				BotOwner bot = squad.Members[j].Bot;
				if (!bot || bot.IsDead || !_scope.Contains(bot.Position)) continue;
				_scopeWakes++;
				wakeBudget--;
				Wake(squad, WakeReason.Scope, false);
				break;
			}
		}
	}

	private void PumpWakes()
	{
		if (Plugin.GhostWakeDelayMs.Value > 0 && Time.time < _nextMemberWake) return;
		int count = Math.Min(_groups.Count, MaxSquads);
		for (int i = 0; i < count && i < MaxSquads; i++)
		{
			if (_wakeCursor >= count) _wakeCursor = 0;
			Squad squad = _groups[_wakeCursor++];
			if (!squad.Waking) continue;
			if (Plugin.GhostWakeDelayMs.Value == 0) { Wake(squad, squad.WakeCause); continue; }
			for (int checkedMembers = 0; checkedMembers < MaxMembers && squad.WakeCursor < squad.Members.Count; checkedMembers++)
			{
				BotRecord record = squad.Members[squad.WakeCursor++];
				if (!record.Dormant) continue;
				DelayResleep(record);
				try { Restore(record); }
				catch (Exception ex) { Fault(record.Bot, ex); }
				_staggered++;
				_nextMemberWake = Time.time + Plugin.GhostWakeDelayMs.Value * 0.001f;
				if (squad.WakeCursor >= squad.Members.Count) squad.Waking = false;
				return; // global one member per rendered frame, no overdue catch-up
			}
			squad.Waking = false;
		}
	}

	internal static bool WakeAtDestination(Squad squad, Vector3 position)
	{
		if (!_instance) return true;
		float wake = Mathf.Min(Plugin.GhostWakeDistance.Value, Plugin.GhostSleepDistance.Value - 1f);
		for (int i = 0; i < _instance._humans.Count && i < MaxHumans; i++)
		{
			float distanceSq = (position - _instance._humans[i].Position).sqrMagnitude;
			if (distanceSq > wake * wake && distanceSq > 2500f) continue;
			_instance.Wake(squad, distanceSq <= 2500f ? WakeReason.Near50 : WakeReason.Near, distanceSq <= 2500f);
			return true;
		}
		if (_instance._scope.Contains(position))
		{
			_instance._scopeWakes++;
			_instance.Wake(squad, WakeReason.Scope, false);
			return true;
		}
		return false;
	}

	internal static bool TryGhostEncounter(Squad dormant, Squad awake)
	{
		if (!Running || !Plugin.GhostCombat.Value || !Plugin.GhostSleepAwake.Value
			|| dormant == awake || dormant == null || awake == null || !dormant.Dormant || awake.Waking) return false;
		if (awake.Dormant) return true;
		if (!_instance.TrySleep(awake)) return false;
		_instance._ghostedAwake++;
		_instance._combat.Encounter(dormant, awake, Time.time);
		return dormant.Dormant && awake.Dormant;
	}

	private bool TrySleep(Squad squad)
	{
		if (squad.Dormant || squad.Waking) return false;
		if (squad.Members.Count > MaxMembers || squad.Group.MembersCount > MaxMembers) { squad.Blocked = BlockReason.Invalid; return false; }
		squad.Blocked = BlockReason.None;
		if (squad.Queued) { squad.Blocked = BlockReason.Block; return false; }
		if (_humans.Count == 0) { squad.Blocked = BlockReason.NoHuman; return false; }
		if (TerminalGhostPolicy.Cutscene) { squad.Blocked = BlockReason.Cutscene; return false; }
		float now = Time.time;
		float sleep = Plugin.GhostSleepDistance.Value;
		float sleepSq = sleep * sleep;
		bool any = false;
		// Test the ACTUAL group, including any member missing from our registry.
		for (int i = 0; i < squad.Group.MembersCount && i < MaxMembers; i++)
		{
			BotOwner bot = squad.Group.Member(i);
			if (!bot || bot.IsDead || bot.HealthController == null || !bot.HealthController.IsAlive) continue;
			try
			{
				if (!_bots.TryGetValue(bot, out var record) || record.Squad != squad) { squad.Blocked = BlockReason.Invalid; return false; }
				if (now < record.BlockUntil || record.SainWakePending) { squad.Blocked = BlockReason.Block; return false; }
				squad.Blocked = TerminalGhostPolicy.SleepBlock(record, now);
				if (squad.Blocked != BlockReason.None) return false;
				if (TerminalGhostPolicy.NearHuman(bot, _humans, sleepSq)) { squad.Blocked = BlockReason.Near; return false; }
				if (_scope.Contains(bot.Position)) { squad.Blocked = BlockReason.Scope; return false; }
				// Agent can be created after the birth callback; dictionary assignment reuses storage.
				if (record.Agent != bot.Brain.Agent)
				{
					if (record.Agent != null) _agents.Remove(record.Agent);
					record.Agent = bot.Brain.Agent;
					_agents[record.Agent] = record;
				}
				any = true;
			}
			catch (Exception ex) { squad.Blocked = BlockReason.Block; Fault(bot, ex); return false; }
		}
		if (!any) { squad.Blocked = BlockReason.Invalid; return false; }
		// A missed membership event must never put a bot from another group to sleep.
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
		{
			BotOwner member = squad.Members[i].Bot;
			if (member && !member.IsDead && (member.BotsGroup != squad.Group || !squad.Group.Contains(member))) { squad.Blocked = BlockReason.Invalid; return false; }
		}
		squad.Dormant = true;
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
		{
			BotRecord record = squad.Members[i];
			BotOwner bot = record.Bot;
			if (!bot || bot.IsDead) continue;
			try
			{
				record.SavedState = bot.BotState;
				record.Dormant = true;
				TerminalGhostSain.DormancyChanged();
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
			catch (Exception ex) { Fault(bot, ex); return false; }
		}
		ClearIncomingTargets(squad);
		if (squad.Dormant)
		{
			_sleeps++;
			TerminalGhostMovement.Begin(squad, _humans);
		}
		return squad.Dormant;
	}

	private void ClearIncomingTargets(Squad squad)
	{
		int botLimit3 = 0;
		foreach (BotRecord record in _bots.Values)
		{
			if (++botLimit3 > MaxBots) break;
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
			catch (Exception ex) { Fault(bot, ex); Wake(squad, WakeReason.Fault); }
		}
	}

	private void LateUpdate()
	{
		if (!Running) return;
		RefreshOverlay();
		long start = Stopwatch.GetTimestamp();
		_inMeasuredPass = true;
		int botLimit4 = 0;
		foreach (BotRecord record in _bots.Values)
		{
			if (++botLimit4 > MaxBots) break;
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

	// zzap: on-screen status so the player can see Ghost working without reading logs
	// (10/28 user: "잘 작동되는지 모르겠음"). Counts refresh once per second; OnGUI only
	// draws the cached string.
	private float _nextOverlay;
	private string _overlayText;
	private GUIStyle _overlayStyle;

	private void RefreshOverlay()
	{
		if (!Plugin.GhostOverlay.Value || Time.unscaledTime < _nextOverlay) return;
		_nextOverlay = Time.unscaledTime + 1f;
		int dormant = 0, total = 0, moving = 0, examined = 0;
		foreach (BotRecord record in _bots.Values)
		{
			if (++examined > MaxBots) break;
			if (!record.Bot || record.Bot.IsDead) continue;
			total++;
			if (record.Dormant) dormant++;
		}
		for (int i = 0; i < _groups.Count && i < MaxSquads; i++)
			if (_groups[i].Dormant && _groups[i].Motion.Moving) moving++;
		_overlayText = $"유령 모드: 잠든 봇 {dormant}/{total} · 숨긴 이동 {moving}분대 · 모의 싸움 {_combat.ActiveCount}(누적 {_combat.FightsStarted}) · 총성 {_combat.Shots}";
	}

	private void OnGUI()
	{
		if (!Plugin.GhostOverlay.Value || !Running || string.IsNullOrEmpty(_overlayText)) return;
		if (_overlayStyle == null)
		{
			_overlayStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight };
			_overlayStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.9f);
		}
		// Top-right (10/28 user request); right-aligned so the line grows leftwards.
		GUI.Label(new Rect(Screen.width - 912f, 8f, 900f, 24f), _overlayText, _overlayStyle);
	}

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
		TerminalGhostSain.ReadWindow(out double sainMs, out long sainSkipped);
		if (Plugin.GhostDiagnostics.Value)
		{
			// One most recent first-block reason per awake squad, not per bot/scan attempt.
			Array.Clear(_blocked, 0, _blocked.Length);
			for (int i = 0; i < _groups.Count && i < MaxSquads; i++)
				if (!_groups[i].Dormant) _blocked[(int)_groups[i].Blocked]++;
			int dormant = 0, total = 0, moving = 0;
			int handsLong = 0, cannotInteract = 0, interaction = 0, changing = 0, medicine = 0, missing = 0;
			for (int i = 0; i < _groups.Count && i < MaxSquads; i++)
				if (_groups[i].Dormant && _groups[i].Motion.Moving) moving++;
			int botLimit5 = 0;
			foreach (BotRecord record in _bots.Values)
			{
				if (++botLimit5 > MaxBots) break;
				if (!record.Bot || record.Bot.IsDead) continue;
				total++;
				if (record.Dormant) dormant++;
				if (!record.Dormant && record.Squad.Blocked == BlockReason.Hands
					&& record.HandsState != TerminalGhostPolicy.HandsReason.None && Time.time - record.HandsSince >= 20f)
				{
					handsLong++;
					var flags = record.HandsState;
					if ((flags & TerminalGhostPolicy.HandsReason.CannotInteract) != 0) cannotInteract++;
					if ((flags & TerminalGhostPolicy.HandsReason.Interaction) != 0) interaction++;
					if ((flags & TerminalGhostPolicy.HandsReason.Changing) != 0) changing++;
					if ((flags & TerminalGhostPolicy.HandsReason.MedicineUsing) != 0) medicine++;
					if ((flags & (TerminalGhostPolicy.HandsReason.Missing | TerminalGhostPolicy.HandsReason.SelectorMissing
						| TerminalGhostPolicy.HandsReason.MedicineMissing)) != 0) missing++;
				}
			}
			Plugin.Log.LogInfo($"[Ghost] dormant {dormant}/total {total}, sleeps {_sleeps}/wakes {_wakes}, fights {_combat.ActiveCount}, shots {_combat.Shots}, moving {moving}, ghosted-awake {_ghostedAwake}, scopeWakes {_scopeWakes}, staggered {_staggered}, last30s avg {(_samples == 0 ? 0 : _sumMs / _samples):F3} max {_maxMs:F3} ms, sain {sainMs:F2}ms/frame (skipped {sainSkipped} calls)"
				+ $", wokeBy: near {_wokeBy[(int)WakeReason.Near]}, near50 {_wokeBy[(int)WakeReason.Near50]}, scope {_wokeBy[(int)WakeReason.Scope]}, target {_wokeBy[(int)WakeReason.Target]}, damage {_wokeBy[(int)WakeReason.Damage]}, protect {_wokeBy[(int)WakeReason.Protect]}, move {_wokeBy[(int)WakeReason.Move]}, encounter {_wokeBy[(int)WakeReason.Encounter]}, fault {_wokeBy[(int)WakeReason.Fault]}, other {_wokeBy[(int)WakeReason.Other]}"
				+ $", blocked: boss {_blocked[(int)BlockReason.Boss]}, crew {_blocked[(int)BlockReason.Crew]}, zone {_blocked[(int)BlockReason.Zone]}, stage {_blocked[(int)BlockReason.Stage]}, young {_blocked[(int)BlockReason.Young]}, damaged {_blocked[(int)BlockReason.Damaged]}, notActive {_blocked[(int)BlockReason.NotActive]}, hands {_blocked[(int)BlockReason.Hands]}, humanFight {_blocked[(int)BlockReason.HumanFight]}, sain {_blocked[(int)BlockReason.Sain]}, near {_blocked[(int)BlockReason.Near]}, block {_blocked[(int)BlockReason.Block]}, cutscene {_blocked[(int)BlockReason.Cutscene]}, civilian {_blocked[(int)BlockReason.Civilian]}, ruaf {_blocked[(int)BlockReason.Ruaf]}, placement {_blocked[(int)BlockReason.Placement]}, invalid {_blocked[(int)BlockReason.Invalid]}, noHuman {_blocked[(int)BlockReason.NoHuman]}, type {_blocked[(int)BlockReason.Type]}, scope {_blocked[(int)BlockReason.Scope]}");
			Plugin.Log.LogInfo($"[GhostRoam] last30s started {RoamStarted}, stopped {RoamFailed}");
			Plugin.Log.LogInfo($"[GhostShoot] last30s shots blocked: dormant {ShootBlockedDormant}, sainWake {ShootBlockedSain}, hiddenTarget {ShootBlockedTarget}; RUAF neutral enemy-add blocked {TerminalRuafNeutral.BlockedAdds}, RUAF player hits ignored {TerminalRuafNeutral.Patch_RuafInvulnerable.Blocked}");
			TerminalRuafNeutral.Patch_RuafInvulnerable.Blocked = 0;
			ShootBlockedDormant = ShootBlockedSain = ShootBlockedTarget = 0;
			TerminalRuafNeutral.BlockedAdds = 0;
			RoamStarted = RoamFailed = 0;
			Plugin.Log.LogInfo($"[GhostContact] last30s tries {_combat.ContactTries}, ineligible {_combat.ContactIneligible}, notHostile {_combat.ContactNotHostile}, far {_combat.ContactFar}, chance {_combat.ContactChance}, sight {_combat.ContactSight}; fights started total {_combat.FightsStarted}");
			_combat.ResetContactStats();
			if (handsLong > 0)
				Plugin.Log.LogInfo($"[GhostHands] same flags >=20s: bots {handsLong}, cannotInteract {cannotInteract}, interaction {interaction}, changing {changing}, medicine {medicine}, missing {missing}");
		}
		Array.Clear(_wokeBy, 0, _wokeBy.Length);
		_sleeps = _wakes = _samples = 0;
		_scopeWakes = _staggered = _ghostedAwake = 0;
		_combat.ResetShots();
		_sumMs = _maxMs = 0;
	}

	private void Restore(BotRecord record)
	{
		if (!record.Dormant) return;
		if (record.SainThrottled && TerminalGhostSain.NeedsWakeRefresh && record.Bot && !record.Bot.IsDead
			&& record.Bot.HealthController != null && record.Bot.HealthController.IsAlive && !record.SainWakePending)
		{
			record.SainWakePending = true;
			record.SainWakeDeadline = Time.frameCount + 2;
			_sainWakePending++;
		}
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

	private static void DelayResleep(BotRecord record)
	{
		record.BlockUntil = Mathf.Max(record.BlockUntil, Time.time + Plugin.GhostResleepDelay.Value);
	}

	private void Wake(Squad squad, WakeReason reason, bool immediate = true)
	{
		var fight = squad.Fight;
		Squad opponent = fight == null ? null : (fight.A == squad ? fight.B : fight.A);
		_combat.Cancel(squad);
		// Cancel detaches both Fight references before entering the opponent's wake.
		// Thus at most one extra call is possible, and no reserved hit survives.
		if (opponent != null) Wake(opponent, reason, immediate);
		TerminalGhostMovement.Stop(squad);
		if (!immediate && Plugin.GhostWakeDelayMs.Value > 0)
		{
			if (squad.Waking || !squad.Dormant) return;
			squad.Dormant = false;
			squad.Waking = true;
			squad.WakeCursor = 0;
			squad.WakeCause = reason;
			_wokeBy[(int)reason]++;
			_wakes++;
			return;
		}
		if (!squad.Dormant && !squad.Waking) return;
		long start = _inMeasuredPass ? 0 : Stopwatch.GetTimestamp();
		bool counted = squad.Waking;
		squad.Dormant = squad.Waking = false;
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
		{
			BotRecord record = squad.Members[i];
			DelayResleep(record);
			try { Restore(record); }
			catch (Exception ex) { Plugin.Log.LogError("[Ghost] body restore failed: " + ex.Message); }
		}
		if (!counted) { _wakes++; _wokeBy[(int)reason]++; }
		if (start != 0) _pendingHookMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
	}

	private void RestoreAll()
	{
		if (_restoring) return;
		_restoring = true;
		try
		{
			_combat.CancelAll();
			for (int i = 0; i < _groups.Count && i < MaxSquads; i++)
			{
				Wake(_groups[i], WakeReason.Other);
				_groups[i].Queued = false;
			}
			_wakeQueue.Clear();
			_nextMemberWake = 0f;
		}
		finally { _restoring = false; }
	}

	internal static int RoamStarted, RoamFailed;
	internal static int ShootBlockedDormant, ShootBlockedSain, ShootBlockedTarget;

	// TerminalGhostMovement roaming: nearest dormant hostile squad within maxDistance
	// whose position is far from every human. None if a hostile one is already within
	// arrive range (the contact sweep owns that pair).
	internal static bool FindRoamTarget(Squad squad, float maxDistance, float arrive, out BotRecord leader, out Squad target)
	{
		leader = null;
		target = null;
		if (!_instance || !squad.Dormant || squad.Waking || squad.Queued || squad.Fight != null) return false;
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
		{
			var record = squad.Members[i];
			if (record.Bot && !record.Bot.IsDead && record.Dormant) { leader = record; break; }
		}
		if (leader == null) return false;
		float best = maxDistance * maxDistance;
		for (int i = 0; i < _instance._groups.Count && i < MaxSquads; i++)
		{
			Squad other = _instance._groups[i];
			if (other == squad || !other.Dormant || other.Waking || other.Queued || other.Fight != null) continue;
			if (!TerminalGhostCombat.NearestHostile(squad, other, out _, out var otherLead, out float nearSq)) continue;
			if (nearSq <= arrive * arrive) return false;
			if (nearSq >= best || NearHuman(otherLead.Bot.Position)) continue;
			best = nearSq;
			target = other;
		}
		if (target == null) return false;
		RoamStarted++;
		return true;
	}

	internal static bool RoamGoal(Squad squad, Squad target, float arrive, out Vector3 goal)
	{
		goal = default;
		if (!_instance || target == null || !target.Dormant || target.Waking || target.Queued || target.Fight != null || squad.Fight != null) return false;
		if (!TerminalGhostCombat.NearestHostile(squad, target, out _, out var otherLead, out float nearSq) || nearSq <= arrive * arrive) return false;
		goal = otherLead.Bot.Position;
		return !NearHuman(goal);
	}

	private static bool NearHuman(Vector3 position)
	{
		float keep = Mathf.Min(Plugin.GhostWakeDistance.Value, Plugin.GhostSleepDistance.Value - 1f) + 30f;
		for (int i = 0; i < _instance._humans.Count && i < MaxHumans; i++)
			if ((position - _instance._humans[i].Position).sqrMagnitude <= keep * keep) return true;
		return false;
	}

	// OfflinePlayerCulling.ApplyVisibleState postfix: keep a dormant body hidden.
	internal static void EnforceBody(BotOwner bot)
	{
		if (_instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record) && record.Dormant)
			record.Body.Enforce();
	}

	internal static bool IsDormant(BotOwner bot)
	{
		return _instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record) && record.Dormant;
	}

	internal static bool IsDormantAgent(AICoreAgent<BotLogicDecision> agent)
	{
		return _instance && agent != null && _instance._agents.TryGetValue(agent, out var record) && record.Dormant;
	}

	internal static bool IsBrainPausedAgent(AICoreAgent<BotLogicDecision> agent)
	{
		return _instance && agent != null && _instance._agents.TryGetValue(agent, out var record) && (record.Dormant || record.SainWakePending);
	}

	internal static bool HasSainWakePending => _instance && _instance._sainWakePending > 0;
	internal static bool SainWakePending(BotOwner bot)
	{
		return _instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record) && record.SainWakePending;
	}

	internal static void SuspendSain(BotOwner bot, object activation)
	{
		if (!_instance || ReferenceEquals(bot, null) || !_instance._bots.TryGetValue(bot, out var record) || record.SainThrottled) return;
		TerminalGhostPatches.SuspendSain(record, activation);
		record.SainThrottled = true;
	}

	internal static void FinishSainWake(BotOwner bot)
	{
		if (!_instance || ReferenceEquals(bot, null) || !_instance._bots.TryGetValue(bot, out var record)) return;
		if (record.SainWakePending) _instance._sainWakePending--;
		record.SainWakePending = false;
		record.SainThrottled = false;
	}

	internal static void WakeNow(BotOwner bot, WakeReason reason)
	{
		if (_instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record)) _instance.Wake(record.Squad, reason);
	}

	// Combat alone may restore one member without waking its surviving squad.
	internal static void RestoreForCombatDeath(BotRecord record)
	{
		if (!_instance || !record.Dormant || !record.Bot
			|| !_instance._bots.TryGetValue(record.Bot, out var current) || current != record)
			throw new InvalidOperationException("Ghost casualty is no longer owned/dormant");
		_instance.Restore(record);
	}

	internal static void WakeSquad(Squad squad, WakeReason reason, bool immediate = true)
	{
		if (_instance && squad != null) _instance.Wake(squad, reason, immediate);
	}

	internal static void Damage(Player player, bool byHuman)
	{
		if (TerminalGhostCombatDamage.IsSimulated(player)) return;
		BotOwner bot = player?.AIData?.BotOwner;
		if (!_instance || !bot || !_instance._bots.TryGetValue(bot, out var record)) return;
		// zzap: only human damage holds the squad awake for 30s. Bot-vs-bot hits used to
		// block every distant fighting squad, so no fight ever reached the simulation
		// (10/28 15시: fights 0, ghosted-awake 0 all raid). A real hit still wakes now.
		if (byHuman) record.LastDamageAt = Time.time;
		_instance.Wake(record.Squad, WakeReason.Damage);
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
		if (observer && !observer.IsDead && observer.BotState == EBotState.Active && !record.Squad.Queued && !record.Squad.Waking)
		{
			if (_instance._wakeQueue.Count >= MaxSquads) { _instance.Wake(record.Squad, WakeReason.Target); return false; }
			record.Squad.Queued = true;
			record.Squad.Observer = observer;
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
		int examined = 0;
		foreach (BotRecord record in _instance._bots.Values)
		{
			if (++examined > MaxBots) break;
			if (!record.Dormant) continue;
			try { record.Body.ReleaseWeapon(weapon.transform); }
			catch (Exception ex) { Fault(record.Bot, ex); }
		}
	}

	internal static void Fault(BotOwner bot, Exception ex)
	{
		WakeNow(bot, WakeReason.Fault);
		FinishSainWake(bot);
		if (_instance && !ReferenceEquals(bot, null) && _instance._bots.TryGetValue(bot, out var record))
		{
			record.BlockUntil = float.PositiveInfinity;
			if (record.FaultReported) return;
			record.FaultReported = true;
		}
		Plugin.Log.LogWarning("[Ghost] bot restored and excluded after exception: " + ex.Message);
	}

	internal static void ResetForRaid()
	{
		TerminalGhostDirector director = _instance;
		try
		{
			if (ReferenceEquals(director, null)) return;
			try { if (director) director.RestoreAll(); }
			catch (Exception ex) { Plugin.Log.LogWarning("[Ghost] raid-end restore skipped: " + ex.Message); }
			finally
			{
				director.Unlisten();
				director._bots.Clear();
				director._agents.Clear();
				director._byGroup.Clear();
				director._groups.Clear();
				director._humans.Clear();
				director._wakeQueue.Clear();
				director._retired.Clear();
				_instance = null;
				if (director) TerminalRaidCleanup.DestroyOwned(director.gameObject);
			}
		}
		finally
		{
			_spawner = null;
			TerminalGhostSain.ResetForRaid();
		}
	}

	private void Unlisten()
	{
		if (!_listening || _spawner == null) return;
		try { _spawner.OnBotCreated -= Register; } catch (Exception) { }
		try { _spawner.OnBotRemoved -= Remove; } catch (Exception) { }
		_listening = false;
	}

	private void OnDisable()
	{
		try { RestoreAll(); }
		catch (Exception ex) { Plugin.Log.LogWarning("[Ghost] disable restore skipped: " + ex.Message); }
	}
	private void OnDestroy()
	{
		try { RestoreAll(); }
		catch (Exception ex) { Plugin.Log.LogWarning("[Ghost] destroyed director restore skipped: " + ex.Message); }
		finally
		{
			Unlisten();
			if (ReferenceEquals(_instance, this)) { _instance = null; _spawner = null; }
		}
	}
}
