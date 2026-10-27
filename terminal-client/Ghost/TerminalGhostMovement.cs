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
		internal bool Pending, Moving, Hunt;
	}

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
			for (int j = 0; j < squad.Members.Count && j < TerminalGhostDirector.MaxMembers; j++)
			{
				BotOwner member = squad.Members[j].Bot;
				Vector3 offset = member ? member.Position - record.Bot.Position : Vector3.zero;
				// Keep a compact formation; SamplePosition/raycast validates every member.
				offset.y = 0f;
				squad.Members[j].GhostOffset = Vector3.ClampMagnitude(offset, 6f);
			}
			return;
		}
	}

	internal static void Stop(TerminalGhostDirector.Squad squad)
	{
		State state = squad.Motion;
		state.Pending = state.Moving = state.Hunt = false;
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
		if (!leader || leader.IsDead || !state.Leader.Dormant
			|| !TerminalCrewJobs.ByProfile.TryGetValue(leader.ProfileId, out var current) || !ReferenceEquals(current, state.Order))
		{
			TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move);
			return;
		}
		float now = Time.time;
		if (!Destination(leader, current, humans, out Vector3 goal, out bool hunt)) { Stop(squad); return; }
		if (state.Moving && !hunt && (goal - state.Goal).sqrMagnitude > 1f)
		{
			TerminalGhostDirector.WakeSquad(squad, TerminalGhostDirector.WakeReason.Move); // changed fixed destination belongs to the live crew layer
			return;
		}
		if (squad.Fight != null) { state.LastStep = now; return; }
		if (state.Pending || (hunt && (now >= state.NextPath || !state.Hunt)))
		{
			if (pathsLeft <= 0) { state.LastStep = now; return; }
			pathsLeft--;
			state.Pending = false;
			state.Hunt = hunt;
			state.Goal = goal;
			state.NextPath = now + 20f;
			if (!NavMesh.SamplePosition(leader.Position, out NavMeshHit start, 1.5f, NavMesh.AllAreas)
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
			state.Moving = true;
			state.LastStep = now;
			state.NextStep = now + 0.5f;
		}
		if (!state.Moving || now < state.NextStep || movesLeft <= 0) return;
		movesLeft--;
		float travel = Mathf.Clamp(now - state.LastStep, 0f, 0.5f) * Plugin.GhostMovementSpeed.Value;
		state.LastStep = now;
		state.NextStep = now + 0.5f;
		Vector3 next = leader.Position;
		int corner = state.Corner;
		for (int steps = 0; steps < MaxCorners && corner < state.Count && travel > 0f; steps++)
		{
			Vector3 point = state.Corners[corner];
			float length = Vector3.Distance(next, point);
			if (length <= travel) { next = point; travel -= length; corner++; }
			else { next = Vector3.MoveTowards(next, point, travel); travel = 0f; }
		}
		// Prepare the whole formation before teleporting ANY member. Failed samples
		// do not split the squad or advance the path. No retry/catch-up in this tick.
		for (int i = 0; i < squad.Members.Count && i < TerminalGhostDirector.MaxMembers; i++)
		{
			var record = squad.Members[i];
			BotOwner bot = record.Bot;
			if (!bot || bot.IsDead || !record.Dormant) continue;
			Vector3 desired = next + record.GhostOffset;
			if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
			{
				if (++state.SampleFailures >= 5) GiveBack(squad, "five sample failures");
				return;
			}
			if (Mathf.Abs(hit.position.y - desired.y) > 1.5f)
			{
				GiveBack(squad, $"height delta={Mathf.Abs(hit.position.y - desired.y):F2}m");
				return;
			}
			if (NavMesh.Raycast(bot.Position, hit.position, out _, NavMesh.AllAreas))
			{
				GiveBack(squad, "NavMesh.Raycast");
				return;
			}
			// Already one metre above BOTH endpoints; keep the obstruction guard.
			if (Physics.Linecast(bot.Position + Vector3.up, hit.position + Vector3.up, out RaycastHit obstacle,
				LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore))
			{
				GiveBack(squad, $"Linecast@1m collider={obstacle.collider?.name} layer={obstacle.collider?.gameObject.layer} point={obstacle.point}");
				return;
			}
			if (CrossesIndoor(bot.Position, hit.position, out bool sampleLimit))
			{
				GiveBack(squad, sampleLimit ? "CrossesIndoor sample limit (>12m)" : "CrossesIndoor volume");
				return;
			}
			// This check also covers a compacted follower offset crossing the wake boundary.
			if (TerminalGhostDirector.WakeAtDestination(squad, hit.position)) return;
			state.Destinations[i] = hit.position;
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
		if (corner >= state.Count) Stop(squad);
	}

	private static void GiveBack(TerminalGhostDirector.Squad squad, string reason)
	{
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
