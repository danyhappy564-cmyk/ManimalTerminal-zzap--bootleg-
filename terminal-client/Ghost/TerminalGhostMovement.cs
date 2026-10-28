using EFT;
using EFT.EnvironmentEffect;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

// Replays only an existing crew destination; never creates or extends an order.
internal static class TerminalGhostMovement
{
	internal const int MaxCorners = 128;
	internal sealed class State
	{
		internal readonly NavMeshPath Path = new NavMeshPath();
		internal readonly Vector3[] Corners = new Vector3[MaxCorners];
		internal readonly Vector3[] Destinations = new Vector3[TerminalGhostDirector.MaxMembers];
		internal TerminalGhostDirector.BotRecord Leader;
		internal TerminalCrewJobs.Rec Order;
		internal int Count, Corner, SampleFailures;
		internal float NextStep, LastStep, NextPath, NextOrder;
		internal Vector3 Goal;
		internal bool Pending, Moving, Hunt, Roam, FromOrigin;
		internal float NextRoamSearch;
		internal TerminalGhostDirector.Squad RoamTarget;
	}

	// zzap 10/28 2nd raid: 19 bots slept, 1262 contact tries in 30s, fights 0 - every
	// dormant pair was either friendly (55%) or beyond contact range (45%), and a
	// dormant squad without a crew rush/hunt order never moved. Roaming walks a
	// dormant squad (hidden, reduced speed) toward the nearest dormant HOSTILE squad
	// so the simulated fight can happen. Roam never wakes anyone on failure.
	internal const float RoamMaxDistance = 300f, RoamArrive = 25f, RoamSpeedFactor = 0.4f, RoamRetry = 60f;
	// zzap 10/28 4th raid: roaming worked (up to 11 squads moving, 9 fights) but the raid
	// fell 66 -> 62 fps, 1% low 48 -> 34. Every 0.5s step teleports each member, and
	// Player.Teleport runs EnvironmentManager.UpdateEnvironmentForPlayer plus a physics
	// transform sync. Roam now steps every RoamStepInterval (same speed, 4x fewer
	// teleports), at most MaxRoaming squads at once, a search every RoamSearchInterval
	// per squad, and a moving target only forces a new path after RoamReplanMove metres.
	internal const float RoamStepInterval = 2f, RoamSearchInterval = 5f, RoamReplanMove = 15f, RoamReplanEvery = 30f;
	internal const int MaxRoaming = 6;


	internal static bool CanRepresent(BotOwner bot, TerminalCrewJobs.Rec order)
	{
		if (!Plugin.GhostMovement.Value || order == null || order.Job == TerminalCrewJobs.Job.None) return false;
		return order.Job == TerminalCrewJobs.Job.Hunt
			|| (order.RushTo.HasValue && order.RushUntil > Time.time);
	}

	private static bool Destination(BotOwner bot, TerminalCrewJobs.Rec order, System.Collections.Generic.List<Player> humans,
		out Vector3 goal, out bool hunt)
	{
		goal = default;
		hunt = false;
		if (!CanRepresent(bot, order)) return false;
		if (order.RushTo.HasValue)
		{
			goal = order.RushTo.Value;
			return (goal - bot.Position).sqrMagnitude > 196f;
		}
		if (order.Job != TerminalCrewJobs.Job.Hunt) return false;
		float nearest = float.PositiveInfinity;
		for (int i = 0; i < humans.Count && i < TerminalGhostDirector.MaxHumans; i++)
		{
			float distance = (bot.Position - humans[i].Position).sqrMagnitude;
			if (distance >= nearest) continue;
			nearest = distance;
			goal = humans[i].Position;
			hunt = true;
		}
		return hunt;
	}

	internal static void Begin(TerminalGhostDirector.Squad squad, System.Collections.Generic.List<Player> humans)
	{
		Stop(squad);
		if (!Plugin.GhostMovement.Value) return;
		State state = squad.Motion;
		if (BeginOrder(squad, humans) || !Plugin.GhostCombat.Value || Time.time < state.NextRoamSearch) return;
		state.NextRoamSearch = Time.time + RoamSearchInterval;
		if (TerminalGhostDirector.RoamingCount() < MaxRoaming
			&& TerminalGhostDirector.FindRoamTarget(squad, RoamMaxDistance, RoamArrive, out var leader, out var target))
		{
			state.Leader = leader;
			state.Roam = true;
			state.RoamTarget = target;
			state.Pending = true;
			state.NextPath = Time.time;
			state.LastStep = Time.time;
			Formation(squad, leader);
		}
	}

	private static void Formation(TerminalGhostDirector.Squad squad, TerminalGhostDirector.BotRecord leader)
	{
		for (int j = 0; j < squad.Members.Count && j < TerminalGhostDirector.MaxMembers; j++)
		{
			BotOwner member = squad.Members[j].Bot;
			Vector3 offset = member ? member.Position - leader.Bot.Position : Vector3.zero;
			// Keep a compact formation; SamplePosition/raycast validates every member.
			offset.y = 0f;
			squad.Members[j].GhostOffset = Vector3.ClampMagnitude(offset, 6f);
		}
	}

	private static bool BeginOrder(TerminalGhostDirector.Squad squad, System.Collections.Generic.List<Player> humans)
	{
		State state = squad.Motion;
		// Representative is the first living member carrying a move order.
		for (int i = 0; i < squad.Members.Count && i < TerminalGhostDirector.MaxMembers; i++)
		{
			var record = squad.Members[i];
			if (!record.Bot || record.Bot.IsDead || !record.Dormant
				|| !TerminalCrewJobs.ByProfile.TryGetValue(record.Bot.ProfileId, out var order)
				|| !Destination(record.Bot, order, humans, out _, out _)) continue;
			state.Leader = record;
			state.Order = order;
			state.Pending = true;
			state.NextPath = Time.time;
			state.LastStep = Time.time;
			Formation(squad, record);
			return true;
		}
		return false;
	}

	internal static void Stop(TerminalGhostDirector.Squad squad)
	{
		State state = squad.Motion;
		state.Pending = state.Moving = state.Hunt = state.Roam = false;
		state.RoamTarget = null;
		state.Leader = null;
		state.Order = null;
		state.Count = state.Corner = state.SampleFailures = 0;
	}

	// The director shares a single path budget across ALL squads each frame.
	internal static void Tick(TerminalGhostDirector.Squad squad, System.Collections.Generic.List<Player> humans, ref int pathsLeft, ref int movesLeft)
	{
		State state = squad.Motion;
		if (!squad.Dormant || squad.Waking) { Stop(squad); return; }
		if (!Plugin.GhostMovement.Value)
		{
			if (state.Pending || state.Moving) TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move);
			return;
		}
		if (state.Leader == null)
		{
			if (Time.time < state.NextOrder) return;
			state.NextOrder = Time.time + 0.5f;
			Begin(squad, humans); // also notices new orders and movement switched ON
			if (state.Leader == null) return;
		}
		BotOwner leader = state.Leader.Bot;
		float now = Time.time;
		Vector3 goal;
		bool hunt;
		if (state.Roam)
		{
			// A crew order outranks roaming: drop the roam and let Begin pick the order.
			if (!leader || leader.IsDead || !state.Leader.Dormant
				|| (TerminalCrewJobs.ByProfile.TryGetValue(leader.ProfileId, out var order) && Destination(leader, order, humans, out _, out _)))
			{
				Stop(squad);
				return;
			}
			if (!TerminalGhostDirector.RoamGoal(squad, state.RoamTarget, RoamArrive, out goal))
			{
				Stop(squad); // arrived, target gone/awake/fighting, or goal near a human
				state.NextOrder = now + 10f;
				return;
			}
			hunt = true;
		}
		else
		{
			if (!leader || leader.IsDead || !state.Leader.Dormant
				|| !TerminalCrewJobs.ByProfile.TryGetValue(leader.ProfileId, out var current) || !ReferenceEquals(current, state.Order))
			{
				TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move);
				return;
			}
			if (!Destination(leader, current, humans, out goal, out hunt)) { Stop(squad); return; }
		}
		if (state.Moving && !hunt && (goal - state.Goal).sqrMagnitude > 1f)
		{
			TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move); // changed fixed destination belongs to the live crew layer
			return;
		}
		if (squad.Fight != null) { state.LastStep = now; return; }
		bool replan = state.Pending || (hunt && (now >= state.NextPath || !state.Hunt));
		// A roam target that has barely moved keeps the current path (CalculatePath over
		// a few hundred metres is the director's largest single cost).
		if (replan && !state.Pending && state.Roam && state.Moving && (goal - state.Goal).sqrMagnitude < RoamReplanMove * RoamReplanMove)
		{
			replan = false;
			state.NextPath = now + RoamReplanEvery;
		}
		if (replan)
		{
			if (pathsLeft <= 0) { state.LastStep = now; return; }
			pathsLeft--;
			state.Pending = false;
			state.Hunt = hunt;
			state.Goal = goal;
			state.NextPath = now + (state.Roam ? RoamReplanEvery : 20f);
			if (!NavMesh.SamplePosition(leader.Position, out NavMeshHit start, 3f, NavMesh.AllAreas)
				|| !NavMesh.SamplePosition(goal, out NavMeshHit end, 3f, NavMesh.AllAreas)
				|| !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, state.Path)
				|| state.Path.status != NavMeshPathStatus.PathComplete)
			{
				GiveBack(squad, "path unavailable");
				return;
			}
			state.Count = state.Path.GetCornersNonAlloc(state.Corners);
			if (state.Count < 2 || state.Count >= MaxCorners) { GiveBack(squad, "corner limit"); return; }
			state.Corner = 1;
			state.FromOrigin = true;
			state.Moving = true;
			state.LastStep = now;
			state.NextStep = now + 0.5f;
		}
		if (!state.Moving || now < state.NextStep || movesLeft <= 0) return;
		movesLeft--;
		float interval = state.Roam ? RoamStepInterval : 0.5f;
		float travel = Mathf.Clamp(now - state.LastStep, 0f, interval) * Plugin.GhostMovementSpeed.Value * (state.Roam ? RoamSpeedFactor : 1f);
		state.LastStep = now;
		state.NextStep = now + interval;
		// First step starts on the sampled path origin, not the (possibly off-mesh) body.
		Vector3 next = state.FromOrigin ? state.Corners[0] : leader.Position;
		int corner = state.Corner;
		for (int steps = 0; steps < MaxCorners && corner < state.Count && travel > 0f; steps++)
		{
			Vector3 point = state.Corners[corner];
			float length = Vector3.Distance(next, point);
			// Stop AT a corner: one step never cuts across it, so the straight
			// NavMesh.Raycast below stays inside the computed path.
			if (length <= travel) { next = point; corner++; break; }
			else { next = Vector3.MoveTowards(next, point, travel); travel = 0f; }
		}
		// Prepare the whole formation before teleporting ANY member. Failed samples
		// do not split the squad or advance the path. No retry/catch-up in this tick.
		for (int i = 0; i < squad.Members.Count && i < TerminalGhostDirector.MaxMembers; i++)
		{
			var record = squad.Members[i];
			BotOwner bot = record.Bot;
			if (!bot || bot.IsDead || !record.Dormant) continue;
			bool isLeader = record == state.Leader;
			string reason = Place(bot, next + (isLeader ? Vector3.zero : record.GhostOffset), isLeader ? PlaceMode.Path : PlaceMode.Offset, out Vector3 placed, out bool sampleMiss);
			// zzap 10/28: every [GhostMove] give-back was NavMesh.Raycast — a follower's
			// 6m offset point sits behind a wall/edge the leader's path never touches.
			// Collapse that follower onto the leader's own path point instead of waking.
			if (reason != null && !isLeader)
			{
				record.GhostOffset = Vector3.zero;
				reason = Place(bot, next, PlaceMode.Collapse, out placed, out sampleMiss);
			}
			if (reason != null) reason += isLeader ? " (leader)" : " (follower)";
			if (reason != null)
			{
				if (sampleMiss) { if (++state.SampleFailures >= 5) GiveBack(squad, reason); }
				else GiveBack(squad, reason);
				return;
			}
			// This check also covers a compacted follower offset crossing the wake boundary.
			if (TerminalGhostDirector.WakeAtDestination(squad, placed)) return;
			state.Destinations[i] = placed;
		}
		state.SampleFailures = 0;
		int memberCount = squad.Members.Count;
		for (int i = 0; i < squad.Members.Count && i < TerminalGhostDirector.MaxMembers && squad.Dormant; i++)
		{
			var record = squad.Members[i];
			BotOwner bot = record.Bot;
			if (!bot || bot.IsDead || !record.Dormant) continue;
			bot.Mover.Teleport(state.Destinations[i]);
			if (!squad.Dormant || squad.Members.Count != memberCount) { TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move); return; }
			bot.GetPlayer.Transform.Original.position = state.Destinations[i];
			// Do not call PlacementChanged: the spawn zone and placement ownership have
			// not changed. Native teleport must not silently change either one.
			if (bot.SpawnBotZone != record.SpawnZone) { TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move); return; }
			record.Body.Enforce();
		}
		state.Corner = corner;
		state.FromOrigin = false;
		if (corner >= state.Count) Stop(squad);
	}

	// Path: the leader's point on the CalculatePath result. Steps stop at corners, so the
	// segment is part of an already validated path; NavMesh.Raycast along a path that hugs
	// a mesh edge reports a hit (10/28 3rd raid: all 4 roams died on the first step).
	// Offset: a follower's formation point, fully checked. Collapse: a follower moved onto
	// the leader's path point after its offset failed; hidden, so only mesh + height.
	private enum PlaceMode { Path, Offset, Collapse }

	// Returns null when the member may be moved to `placed`, else the give-back reason.
	private static string Place(BotOwner bot, Vector3 desired, PlaceMode mode, out Vector3 placed, out bool sampleMiss)
	{
		placed = default;
		sampleMiss = false;
		if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
		{
			sampleMiss = true;
			return "five sample failures";
		}
		if (Mathf.Abs(hit.position.y - desired.y) > 1.5f) return $"height delta={Mathf.Abs(hit.position.y - desired.y):F2}m";
		if (mode == PlaceMode.Collapse)
		{
			placed = hit.position;
			return null;
		}
		if (mode == PlaceMode.Offset)
		{
			// The body stands slightly above/off the mesh; NavMesh.Raycast from an off-mesh
			// origin reports a hit at once. Start from the member's own mesh point.
			Vector3 from = NavMesh.SamplePosition(bot.Position, out NavMeshHit origin, 3f, NavMesh.AllAreas) ? origin.position : bot.Position;
			if (NavMesh.Raycast(from, hit.position, out _, NavMesh.AllAreas)) return "NavMesh.Raycast";
		}
		// Already one metre above BOTH endpoints; keep the obstruction guard.
		if (Physics.Linecast(bot.Position + Vector3.up, hit.position + Vector3.up, out RaycastHit obstacle,
			LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
			return $"Linecast@1m collider={obstacle.collider?.name} layer={obstacle.collider?.gameObject.layer} point={obstacle.point}";
		if (CrossesIndoor(bot.Position, hit.position, out bool sampleLimit))
			return sampleLimit ? "CrossesIndoor sample limit (>12m)" : "CrossesIndoor volume";
		placed = hit.position;
		return null;
	}

	private static void GiveBack(TerminalGhostDirector.Squad squad, string reason)
	{
		if (squad.Motion.Roam)
		{
			// Roaming is optional: stay asleep where we are and try again later.
			Stop(squad);
			squad.Motion.NextOrder = Time.time + RoamRetry;
			TerminalGhostDirector.RoamFailed++;
			if (Plugin.GhostDiagnostics.Value) Plugin.Log.LogInfo("[GhostMove] roam stopped (stays asleep, retry 60s): " + reason);
			return;
		}
		TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move);
		// Give live navigation time to leave the blocked/indoor segment, rather
		// than repeatedly hiding and waking at the same bad path every two seconds.
		for (int i = 0; i < squad.Members.Count && i < TerminalGhostDirector.MaxMembers; i++)
			squad.Members[i].BlockUntil = Mathf.Max(squad.Members[i].BlockUntil, Time.time + 30f);
		if (Plugin.GhostDiagnostics.Value) Plugin.Log.LogInfo("[GhostMove] live navigation for 30s: " + reason);
	}

	private static bool CrossesIndoor(Vector3 from, Vector3 to, out bool sampleLimit)
	{
		sampleLimit = false;
		EnvironmentManager environment = EnvironmentManager.Instance;
		if (!environment) return false;
		// Include the origin and sample at no more than 1m intervals. Long formation
		// jumps are rejected rather than doing an unbounded volume query.
		int samples = Mathf.CeilToInt(Vector3.Distance(from, to));
		if (samples > 12) { sampleLimit = true; return true; }
		for (int i = 0; i <= samples && i <= 12; i++)
			if (environment.TryFindTriggerByPos(Vector3.Lerp(from, to, samples == 0 ? 0f : (float)i / samples))) return true;
		return false;
	}
}
