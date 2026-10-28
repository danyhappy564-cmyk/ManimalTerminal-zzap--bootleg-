using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Manimal.Terminal;

// zzap: fixed raid-local storage. Only Ghost-owned dormant squads participate.
// No coroutines, closures, reflection, collection growth or event objects in Tick.
internal sealed class TerminalGhostCombat
{
	private const int MaxSquads = 256;
	internal const int MaxFights = 32;
	internal const int MaxMembers = 32; // per side; oversized squads remain asleep without simulation
	private const int PairsPerFrame = 4;
	private const int EventsPerFrame = 8;

	internal sealed class Fight
	{
		internal TerminalGhostDirector.Squad A, B;
		internal bool Active;
		internal bool Executing, Ending;
		internal float EndAt;
		internal string EndReason;
		internal float Started, Ends, Distance, Ratio;
		internal int CountA, CountB, DeathsA, DeathsB, WoundsA, WoundsB, EventCount, Pending;
		internal string KeyA, KeyB;
		internal WildSpawnType RoleA, RoleB;
		internal readonly TerminalGhostDirector.BotRecord[] Members = new TerminalGhostDirector.BotRecord[MaxMembers * 2];
		internal readonly CombatEvent[] Events = new CombatEvent[MaxMembers * 2];
		internal readonly TerminalGhostAudio.State Audio = new TerminalGhostAudio.State();
	}

	internal struct CombatEvent
	{
		internal TerminalGhostDirector.BotRecord Victim;
		internal float At, Damage;
		internal bool Death, Done, SideA;
	}

	private readonly TerminalGhostDirector.Squad[] _slots = new TerminalGhostDirector.Squad[MaxSquads];
	private readonly int[] _occupied = new int[MaxSquads];
	private readonly float[] _cooldowns = new float[MaxSquads * MaxSquads];
	private readonly Fight[] _fights = new Fight[MaxFights];
	private readonly TerminalGhostAudio _audio = new TerminalGhostAudio();
	private int _count, _scanA, _scanB, _pairsLeft, _pumpCursor;
	private float _nextSweep;
	internal int ActiveCount { get; private set; }
	// zzap: 10/28 raid showed fights 0 in every 30s report. ActiveCount is only the
	// current number, so a short 8-20s fight between reports was invisible; these
	// count every contact attempt and where it stopped (reset by the director report).
	internal int FightsStarted, ContactTries, ContactIneligible, ContactNotHostile, ContactFar, ContactChance, ContactSight;
	internal void ResetContactStats() { ContactTries = ContactIneligible = ContactNotHostile = ContactFar = ContactChance = ContactSight = 0; }
	internal int Shots => _audio.Shots;
	internal void ResetShots() { _audio.ResetShots(); }

	internal TerminalGhostCombat()
	{
		for (int i = 0; i < _fights.Length; i++) _fights[i] = new Fight();
	}

	internal void Register(TerminalGhostDirector.Squad squad)
	{
		for (int i = 0; i < MaxSquads; i++)
		{
			if (_slots[i] != null) continue;
			_slots[i] = squad;
			squad.CombatSlot = i;
			_occupied[_count++] = i;
			_pairsLeft = 0;
			return;
		}
		// Capacity is a safety bound, never a reason to grow arrays during a frame.
	}

	internal void Unregister(TerminalGhostDirector.Squad squad)
	{
		int slot = squad.CombatSlot;
		if (slot < 0) return;
		_slots[slot] = null;
		for (int i = 0; i < _count && i < MaxSquads; i++)
			if (_occupied[i] == slot) { _occupied[i] = _occupied[--_count]; break; }
		for (int i = 0; i < MaxSquads; i++)
		{
			_cooldowns[slot * MaxSquads + i] = 0f;
			_cooldowns[i * MaxSquads + slot] = 0f;
		}
		squad.CombatSlot = -1;
		_pairsLeft = 0;
		// A synchronous simulated last death can remove the group here. Keep its
		// Fight until Pump returns from the death callback and records that casualty.
	}

	internal void Tick(float now, List<Player> humans)
	{
		if (!Plugin.GhostCombat.Value) { CancelAll(); return; }
		Pump(now);
		_audio.Tick(now, _fights, humans);
		if (TerminalGhostPolicy.Cutscene || _count < 2) return;
		if (_pairsLeft == 0)
		{
			if (now < _nextSweep) return;
			_nextSweep = now + 2f;
			_scanA = 0;
			_scanB = 1;
			_pairsLeft = _count * (_count - 1) / 2;
		}
		for (int i = 0; i < PairsPerFrame && _pairsLeft > 0; i++)
		{
			var a = _slots[_occupied[_scanA]];
			var b = _slots[_occupied[_scanB]];
			_pairsLeft--;
			if (++_scanB >= _count) { _scanA++; _scanB = _scanA + 1; }
			if (a.Waking || b.Waking || a.Queued || b.Queued) continue;
			if (a.CombatSlot < 0 || b.CombatSlot < 0
				|| now < _cooldowns[a.CombatSlot * MaxSquads + b.CombatSlot]) continue;
			if (a.Dormant != b.Dormant)
			{
				var dormant = a.Dormant ? a : b;
				var awake = a.Dormant ? b : a;
				try
				{
					if (!InEncounterRange(dormant, awake)) continue;
					if (!TerminalGhostDirector.TryGhostEncounter(dormant, awake))
					{
						// Proximity is not a target selection. A failed conversion must
						// leave the hidden squad asleep; AllowTarget owns real threats.
						Cooldown(a, b, now + RetryCooldown);
						continue;
					}
				}
				catch (Exception ex) { Fail(a, b, ex, now); continue; }
			}
			if (!a.Dormant || !b.Dormant || a.Fight != null || b.Fight != null || a.CombatSlot < 0 || b.CombatSlot < 0) continue;
			if (now < _cooldowns[a.CombatSlot * MaxSquads + b.CombatSlot]) continue;
			try { Contact(a, b, now); }
			catch (Exception ex) { Fail(a, b, ex, now); }
		}
	}

	private static bool Alive(TerminalGhostDirector.BotRecord member)
	{
		BotOwner bot = member?.Bot;
		return bot && !bot.IsDead && bot.GetPlayer && bot.HealthController != null && bot.HealthController.IsAlive;
	}

	private static bool Eligible(TerminalGhostDirector.Squad squad)
	{
		if (!squad.Dormant || squad.Queued || squad.Waking || squad.Members.Count > MaxMembers || squad.Group.MembersCount > MaxMembers) return false;
		int living = 0;
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
		{
			var member = squad.Members[i];
			if (!Alive(member)) continue;
			if (!member.Dormant || member.Bot.BotsGroup != squad.Group || !squad.Group.Contains(member.Bot)
				|| member.Bot.BotState != EBotState.NonActive || TerminalGhostPolicy.Protected(member)) return false;
			living++;
		}
		if (living == 0 || living > MaxMembers) return false;
		int actual = 0;
		for (int i = 0; i < squad.Group.MembersCount && i < MaxMembers; i++)
		{
			BotOwner bot = squad.Group.Member(i);
			if (bot && !bot.IsDead && bot.HealthController != null && bot.HealthController.IsAlive) actual++;
		}
		return living == actual;
	}

	internal static bool NearestHostile(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b,
		out TerminalGhostDirector.BotRecord nearestA, out TerminalGhostDirector.BotRecord nearestB, out float nearestSq)
	{
		nearestA = nearestB = null;
		nearestSq = float.PositiveInfinity;
		if (a == null || b == null || a == b || a.Members.Count > MaxMembers || b.Members.Count > MaxMembers) return false;
		for (int i = 0; i < a.Members.Count && i < MaxMembers; i++)
		{
			var ma = a.Members[i];
			if (!Alive(ma)) continue;
			for (int j = 0; j < b.Members.Count && j < MaxMembers; j++)
			{
				var mb = b.Members[j];
				if (!Alive(mb)) continue;
				float d = (ma.Bot.Position - mb.Bot.Position).sqrMagnitude;
				if (d >= nearestSq) continue;
				nearestSq = d;
				nearestA = ma;
				nearestB = mb;
			}
		}
		if (nearestA == null || nearestB == null) return false;
		var pa = nearestA.Bot.GetPlayer;
		var pb = nearestB.Bot.GetPlayer;
		return a.Group.IsEnemy(pb) || b.Group.IsEnemy(pa) || a.Group.IsPlayerEnemy(pb) || b.Group.IsPlayerEnemy(pa);
	}

	internal static bool InEncounterRange(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b)
	{
		float range = Plugin.GhostEncounterDistance.Value;
		return NearestHostile(a, b, out _, out _, out float distanceSq) && distanceSq <= range * range;
	}

	private static float Frequency(float low, float normal, float high)
	{
		switch (Plugin.GhostFightFrequency.Value)
		{
			case TerminalGhostSettings.FightFrequency.Low: return low;
			case TerminalGhostSettings.FightFrequency.High: return high;
			default: return normal;
		}
	}
	private static float RetryCooldown => Frequency(60f, 30f, 15f);
	private static float FightCooldown => Frequency(360f, 180f, 90f);

	internal void Encounter(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b, float now)
	{
		if (a.Fight != null || b.Fight != null || a.CombatSlot < 0 || b.CombatSlot < 0
			|| now < _cooldowns[a.CombatSlot * MaxSquads + b.CombatSlot]) return;
		try { Contact(a, b, now, Plugin.GhostEncounterDistance.Value); }
		catch (Exception ex) { Fail(a, b, ex, now); }
	}

	private void Contact(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b, float now, float encounterRange = 0f)
	{
		ContactTries++;
		if (!Eligible(a) || !Eligible(b)) { ContactIneligible++; return; }
		if (!NearestHostile(a, b, out var nearestA, out var nearestB, out float nearestSq)) { ContactNotHostile++; return; }
		// An 85m conversion must get a contact attempt at 85m; otherwise two static
		// squads could sleep forever outside the ordinary 60m contact range.
		float range = Mathf.Clamp(Mathf.Max(Plugin.GhostCombatRange.Value, encounterRange), 10f, 150f);
		if (nearestSq > range * range) { ContactFar++; return; }
		// Installed BotsGroup.IsEnemy(IPlayer): live Enemies list (existing grudges).
		// IsPlayerEnemy(IPlayer): Settings friendly/warn/enemy bot ROLE lists, then
		// side/loyalty rules. Same Savage side alone does not establish friendship.
		// A hostile direction suffices: its opponent may defend itself. No AddEnemy.
		var pa = nearestA.Bot.GetPlayer;
		var pb = nearestB.Bot.GetPlayer;
		float distance = Mathf.Sqrt(nearestSq);
		Cooldown(a, b, now + RetryCooldown);
		float chance = distance <= 20f ? 1f : Mathf.Lerp(0.85f, 0.25f, Mathf.InverseLerp(20f, range, distance));
		chance = Mathf.Min(1f, chance * Frequency(0.5f, 1f, 1.5f));
		if (chance < 1f && Random.value >= chance) { ContactChance++; return; }
		// Within 30m live bots engage by sound through walls and corners; Terminal's
		// industrial blocks rarely give a clean 60m line, so sight gates only farther pairs.
		if (distance > 30f && !ClearSight(pa.Position + Vector3.up * 1.5f, pb.Position + Vector3.up * 1.5f)) { ContactSight++; return; }
		for (int i = 0; i < _fights.Length; i++)
		{
			if (_fights[i].Active) continue;
			Begin(_fights[i], a, b, nearestA, nearestB, distance, now);
			return;
		}
	}

	private static bool ClearSight(Vector3 from, Vector3 to)
	{
		Vector3 right = Vector3.Cross(to - from, Vector3.up).normalized * 0.3f;
		if (right.sqrMagnitude < 0.001f) right = Vector3.right * 0.3f;
		// EFT's world occlusion mask: terrain + high-poly world colliders, excluding
		// character hitboxes and triggers. No names/custom layer numbers to drift.
		int mask = LayersMaskController.HighPolyWithTerrainMask;
		return !Physics.Linecast(from, to, mask, QueryTriggerInteraction.Ignore)
			&& !Physics.Linecast(from + right, to + right, mask, QueryTriggerInteraction.Ignore)
			&& !Physics.Linecast(from - right, to - right, mask, QueryTriggerInteraction.Ignore);
	}

	private static float Score(TerminalGhostDirector.BotRecord member, float distance)
	{
		BotOwner bot = member.Bot;
		var hp = bot.HealthController.GetBodyPartHealth(EBodyPart.Common);
		float health = hp.Maximum > 0f ? Mathf.Clamp(hp.Current / hp.Maximum, 0.35f, 1f) : 0.35f;
		float difficulty;
		switch (bot.Profile.Info.Settings.BotDifficulty)
		{
			case BotDifficulty.easy: difficulty = 0.85f; break;
			case BotDifficulty.hard: difficulty = 1.15f; break;
			case BotDifficulty.impossible: difficulty = 1.3f; break;
			default: difficulty = 1f; break;
		}
		// Weapon.WeapClass reads WeaponTemplate.weapClass in the installed EFT DLL.
		// The value is a template string; no item-name/role heuristics or ToString.
		Weapon weapon = bot.GetPlayer.HandsController?.Item as Weapon;
		float t = Mathf.InverseLerp(20f, 150f, distance);
		float suitability;
		switch (weapon?.WeapClass)
		{
			case "pistol":
			case "shotgun": suitability = Mathf.Lerp(1f, 0.3f, t); break;
			case "smg": suitability = Mathf.Lerp(1f, 0.45f, t); break;
			case "sniperRifle": suitability = Mathf.Lerp(0.3f, 1f, t); break;
			case "marksmanRifle": suitability = Mathf.Lerp(0.55f, 1f, t); break;
			case null: suitability = 0.3f; break;
			default: suitability = Mathf.Lerp(0.95f, 0.75f, t); break;
		}
		return health * suitability * difficulty;
	}

	private void Begin(Fight fight, TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b,
		TerminalGhostDirector.BotRecord leadA, TerminalGhostDirector.BotRecord leadB, float distance, float now)
	{
		fight.A = a;
		fight.B = b;
		fight.Active = true;
		a.Fight = b.Fight = fight; // establish cancellation ownership before any fallible setup
		ActiveCount++;
		FightsStarted++;
		fight.Started = now;
		fight.Ends = now + Random.Range(8f, 20f);
		fight.Distance = distance;
		fight.Ratio = 1f;
		fight.KeyA = leadA.Bot.ProfileId;
		fight.KeyB = leadB.Bot.ProfileId;
		fight.RoleA = leadA.Bot.Profile.Info.Settings.Role;
		fight.RoleB = leadB.Bot.Profile.Info.Settings.Role;
		fight.CountA = Collect(a, fight.Members, 0);
		fight.CountB = Collect(b, fight.Members, fight.CountA);
		float scoreA = 0f, scoreB = 0f;
		for (int i = 0; i < fight.CountA && i < MaxMembers; i++) scoreA += Score(fight.Members[i], distance);
		for (int i = fight.CountA; i < fight.CountA + fight.CountB && i < MaxMembers * 2; i++) scoreB += Score(fight.Members[i], distance);
		scoreA *= Random.Range(0.7f, 1.3f);
		scoreB *= Random.Range(0.7f, 1.3f);
		bool aWins = scoreA >= scoreB;
		fight.Ratio = Mathf.Max(scoreA, scoreB) / Mathf.Max(0.0001f, Mathf.Min(scoreA, scoreB));
		float lethality = Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(50f, 150f, distance));
		float casualtyScale = Mathf.Clamp(Plugin.GhostCombatLethality.Value, 0f, 3f);
		float losses = (1 + (fight.Ratio > 1.6f ? 1 : 0) + (fight.Ratio > 2.5f ? 1 : 0)) * lethality * casualtyScale;
		int loserDeaths = Mathf.FloorToInt(losses);
		if (Random.value < losses - loserDeaths) loserDeaths++;
		int winnerCount = aWins ? fight.CountA : fight.CountB;
		int winnerDeaths = fight.Ratio < 1.2f && winnerCount >= 2 && Random.value < 0.3f ? 1 : 0;
		float winnerLosses = winnerDeaths * casualtyScale;
		winnerDeaths = Mathf.FloorToInt(winnerLosses);
		if (winnerLosses > winnerDeaths && Random.value < winnerLosses - winnerDeaths) winnerDeaths++;
		winnerDeaths = Math.Min(winnerDeaths, Math.Max(0, winnerCount - 1)); // retain a live killer for the losing side's events
		// One draw per side, then independent draws for its surviving members.
		float winnerWounds = Random.Range(0.1f, 0.6f);
		float loserWounds = Random.Range(0.5f, 0.9f);
		Schedule(fight, 0, fight.CountA, aWins ? winnerDeaths : loserDeaths, aWins ? winnerWounds : loserWounds, true, aWins);
		Schedule(fight, fight.CountA, fight.CountB, aWins ? loserDeaths : winnerDeaths, aWins ? loserWounds : winnerWounds, false, !aWins);
		SortEvents(fight);
		Cooldown(a, b, now + FightCooldown);
	}

	private static int Collect(TerminalGhostDirector.Squad squad, TerminalGhostDirector.BotRecord[] members, int start)
	{
		int count = 0;
		for (int i = 0; i < squad.Members.Count && i < MaxMembers; i++)
			if (Alive(squad.Members[i])) members[start + count++] = squad.Members[i];
		// In-place shuffle: casualty choice has no bias towards the representative.
		for (int i = Math.Min(count, MaxMembers) - 1; i > 0; i--)
		{
			int other = start + Random.Range(0, i + 1);
			var member = members[start + i];
			members[start + i] = members[other];
			members[other] = member;
		}
		return count;
	}

	private static void Schedule(Fight fight, int start, int count, int deaths, float wounds, bool sideA, bool winner)
	{
		deaths = Math.Min(deaths, count);
		for (int i = 0; i < count && i < MaxMembers; i++)
		{
			bool death = i < deaths;
			if (!death && Random.value >= wounds) continue;
			ref CombatEvent evt = ref fight.Events[fight.EventCount++];
			evt.Victim = fight.Members[start + i];
			evt.SideA = sideA;
			evt.Death = death;
			evt.Done = false;
			evt.Damage = winner ? Random.Range(10f, 35f) : Random.Range(25f, 55f);
			evt.At = Random.Range(fight.Started + 0.25f, fight.Ends - 0.25f);
			// If both sides suffer fatalities, keep a losing-side killer alive until
			// the winner's casualty has occurred. All events still lie inside 8-20s.
			if (death)
			{
				float split = Mathf.Lerp(fight.Started, fight.Ends, 0.35f);
				evt.At = winner ? Random.Range(fight.Started + 0.25f, split)
					: Random.Range(split + 0.25f, fight.Ends - 0.25f);
			}
			fight.Pending++;
		}
	}

	private static void SortEvents(Fight fight)
	{
		// Small bounded insertion sort; process deadlines in order even on a slow
		// frame where several events become due together. No comparer/delegate.
		for (int i = 1; i < fight.EventCount && i < MaxMembers * 2; i++)
		{
			CombatEvent evt = fight.Events[i];
			int j = i - 1;
			while (j >= 0 && j < MaxMembers * 2 && fight.Events[j].At > evt.At)
			{
				fight.Events[j + 1] = fight.Events[j];
				j--;
			}
			fight.Events[j + 1] = evt;
		}
	}

	private void Pump(float now)
	{
		int budget = EventsPerFrame;
		int first = _pumpCursor;
		_pumpCursor = (_pumpCursor + 1) % MaxFights;
		for (int i = 0; i < MaxFights; i++)
		{
			Fight fight = _fights[(first + i) % MaxFights];
			if (!fight.Active) continue;
			var a = fight.A;
			var b = fight.B;
			try
			{
				if (!TerminalGhostDirector.Running || !Plugin.GhostBrainThrottle.Value || !Plugin.GhostCombat.Value
					|| !(fight.DeathsA == fight.CountA || Eligible(fight.A))
					|| !(fight.DeathsB == fight.CountB || Eligible(fight.B))) { End(fight, now, "cancelled"); continue; }
				// Never apply an overdue hit after the chosen fight duration. A stall
				// or event budget exhaustion drops the remaining reservations safely.
				if (now >= fight.Ends) { End(fight, now, fight.Pending == 0 ? "finished" : "deadline"); continue; }
				for (int j = 0; j < fight.EventCount && j < MaxMembers * 2 && budget > 0 && fight.Active; j++)
				{
					CombatEvent evt = fight.Events[j]; // copy: synchronous death callbacks may cancel the entire pool slot
					if (evt.Done || now < evt.At) continue;
					fight.Events[j].Done = true;
					fight.Pending--;
					budget--;
					fight.Executing = true;
					try { Execute(fight, evt); }
					finally
					{
						fight.Executing = false;
						if (fight.Ending) Release(fight, fight.EndAt, fight.EndReason);
					}
				}
			}
			catch (Exception ex) { Fail(a, b, ex, now); }
		}
	}

	private static void Execute(Fight fight, CombatEvent evt)
	{
		if (!Alive(evt.Victim) || !evt.Victim.Dormant) return;
		bool changed;
		if (evt.Death)
		{
			TerminalGhostAudio.StopShooter(fight, evt.Victim);
			var enemy = evt.SideA ? fight.B : fight.A;
			TerminalGhostDirector.BotRecord killer = null;
			int count = 0;
			for (int i = 0; i < enemy.Members.Count && i < MaxMembers; i++)
			{
				var member = enemy.Members[i];
				if (!Alive(member) || !(member.Bot.GetPlayer.HandsController?.Item is Weapon)) continue;
				if (Random.Range(0, ++count) == 0) killer = member;
			}
			if (killer == null) throw new InvalidOperationException("Ghost fight has no living armed opponent");
			changed = TerminalGhostCombatDamage.Kill(evt.Victim, killer);
		}
		else changed = TerminalGhostCombatDamage.Wound(evt.Victim, evt.Damage);
		if (!changed) return;
		if (evt.Death) { if (evt.SideA) fight.DeathsA++; else fight.DeathsB++; }
		else { if (evt.SideA) fight.WoundsA++; else fight.WoundsB++; }
	}

	private void Cooldown(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b, float until)
	{
		if (a.CombatSlot < 0 || b.CombatSlot < 0) return;
		until = Mathf.Max(until, _cooldowns[a.CombatSlot * MaxSquads + b.CombatSlot]);
		_cooldowns[a.CombatSlot * MaxSquads + b.CombatSlot] = until;
		_cooldowns[b.CombatSlot * MaxSquads + a.CombatSlot] = until;
	}

	internal void Cancel(TerminalGhostDirector.Squad squad)
	{
		if (squad.Fight != null) End(squad.Fight, Time.time, "cancelled");
	}

	internal void CancelAll()
	{
		_audio.ReleaseSources(true);
		for (int i = 0; i < _fights.Length; i++)
			if (_fights[i].Active) End(_fights[i], Time.time, "cancelled");
		_pairsLeft = 0;
	}

	private void End(Fight fight, float now, string reason)
	{
		if (!fight.Active) return;
		fight.Active = false; // detach BEFORE logging/waking or callbacks can re-enter
		TerminalGhostAudio.Reset(fight); // no pending body or tail survives cancellation
		fight.A.Fight = fight.B.Fight = null;
		ActiveCount--;
		Cooldown(fight.A, fight.B, now + FightCooldown);
		if (fight.Executing)
		{
			// Cancellation is immediate, but a synchronous death callback may be
			// inside Execute. Preserve its event/counters until that call unwinds.
			fight.Ending = true;
			fight.EndAt = now;
			fight.EndReason = reason;
			return;
		}
		Release(fight, now, reason);
	}

	private static void Release(Fight fight, float now, string reason)
	{
		try
		{
			if (Plugin.GhostDiagnostics.Value)
				Plugin.Log.LogInfo($"[GhostFight] A={fight.KeyA}({TerminalRoles.Name(fight.RoleA)},{fight.CountA}명) vs B={fight.KeyB}({TerminalRoles.Name(fight.RoleB)},{fight.CountB}명) {fight.Distance:F1}m r={fight.Ratio:F2} 사망 {fight.DeathsA}/{fight.DeathsB} 부상 {fight.WoundsA}/{fight.WoundsB} 시간 {now - fight.Started:F1}s {reason}");
		}
		finally
		{
			Array.Clear(fight.Members, 0, fight.Members.Length);
			Array.Clear(fight.Events, 0, fight.Events.Length);
			fight.A = fight.B = null;
			fight.KeyA = fight.KeyB = null;
			fight.CountA = fight.CountB = fight.EventCount = fight.Pending = 0;
			fight.DeathsA = fight.DeathsB = fight.WoundsA = fight.WoundsB = 0;
			fight.Ending = false;
			fight.EndReason = null;
		}
	}

	private void Fail(TerminalGhostDirector.Squad a, TerminalGhostDirector.Squad b, Exception ex, float now)
	{
		bool started = a?.Fight != null || b?.Fight != null;
		if (a?.Fight != null) End(a.Fight, now, "exception");
		if (b?.Fight != null) End(b.Fight, now, "exception");
		if (a != null && b != null) Cooldown(a, b, now + (started ? FightCooldown : RetryCooldown));
		TerminalGhostDirector.WakeSquad(a, TerminalGhostDirector.WakeReason.Fault);
		TerminalGhostDirector.WakeSquad(b, TerminalGhostDirector.WakeReason.Fault);
		Plugin.Log.LogWarning("[GhostFight] cancelled; both squads restored: " + ex.Message);
	}
}
