using System.Collections.Generic;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalMigrationProbe
{
	internal enum DecisionReason
	{
		Unavailable,
		SameZone,
		NoSpawn,
		MissingCore,
		NearPlayer,
		VisibleToPlayer,
		EligibleProjection
	}

	internal readonly struct Result
	{
		internal DecisionReason Reason { get; }

		internal Vector3 TargetPosition { get; }

		internal int? CorePointId { get; }

		internal int SampledSpawnIndex { get; }

		internal int PlayersChecked { get; }

		internal bool IsEligibleProjection => Reason == DecisionReason.EligibleProjection;

		internal string ReasonCode => Reason switch
		{
			DecisionReason.SameZone => "same-zone", 
			DecisionReason.NoSpawn => "no-spawn", 
			DecisionReason.MissingCore => "missing-core", 
			DecisionReason.NearPlayer => "near-player", 
			DecisionReason.VisibleToPlayer => "visible-to-player", 
			DecisionReason.EligibleProjection => "eligible-projection", 
			_ => "unavailable", 
		};

		internal Result(DecisionReason reason, Vector3 targetPosition, int? corePointId, int sampledSpawnIndex, int playersChecked)
		{
			Reason = reason;
			TargetPosition = targetPosition;
			CorePointId = corePointId;
			SampledSpawnIndex = sampledSpawnIndex;
			PlayersChecked = playersChecked;
		}

		public override string ToString()
		{
			return string.Format("reason={0} target={1} core={2} ", ReasonCode, TargetPosition, CorePointId.HasValue ? CorePointId.Value.ToString() : "<none>") + $"sample={SampledSpawnIndex} players={PlayersChecked} projection=true";
		}
	}

	private static int _sampleCursor;

	internal static void ResetForRaid()
	{
		_sampleCursor = 0;
	}

	internal static Result Evaluate(BotOwner bot, BotZone destination)
	{
		if (!TerminalMigrationSelection.Evaluate(bot, destination, SelectDiagnostic, out var _, out var diagnostic))
		{
			return diagnostic;
		}
		return diagnostic;
	}

	private static TerminalMigrationSelection.Candidate SelectDiagnostic(IReadOnlyList<ISpawnPoint> points)
	{
		int index = NextSampleIndex(points.Count);
		return new TerminalMigrationSelection.Candidate(index, points[index]);
	}

	private static int NextSampleIndex(int count)
	{
		int num = _sampleCursor++;
		if (num < 0)
		{
			num = 0;
		}
		return num % count;
	}

	private static Result Unavailable(int sampledSpawnIndex = -1)
	{
		return new Result(DecisionReason.Unavailable, default, null, sampledSpawnIndex, 0);
	}
}
