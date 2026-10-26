using System;
using System.Collections.Generic;

namespace Manimal.Terminal;

internal static class TerminalStagePolicy
{
	internal readonly struct StageSnapshot
	{
		internal int Stage { get; }

		internal int LiveCount { get; }

		internal int RetainedCount { get; }

		internal int InitialSurplus { get; }

		internal StageSnapshot(int stage, int liveCount, int retainedCount)
		{
			Stage = stage;
			LiveCount = liveCount;
			RetainedCount = retainedCount;
			InitialSurplus = CalculateMigrationLimit(liveCount, retainedCount);
		}
	}

	internal const int WaitPriority = 150;

	internal const string StageOneTrigger = "T0";

	private static readonly Dictionary<string, int> StageByEvent = new Dictionary<string, int>(StringComparer.Ordinal) { ["T0"] = 1 };

	internal static int RushPriority(bool sainMode)
	{
		if (!sainMode)
		{
			return 149;
		}
		return 75;
	}

	internal static bool ShouldHold(int currentStage, int authoredBlockStage, bool cutscene)
	{
		if (!cutscene)
		{
			if (authoredBlockStage > 0)
			{
				return authoredBlockStage - 1 > currentStage;
			}
			return false;
		}
		return true;
	}

	internal static bool TryGetNextStage(string eventName, out int nextStage)
	{
		if (eventName == null)
		{
			nextStage = 0;
			return false;
		}
		return StageByEvent.TryGetValue(eventName, out nextStage);
	}

	internal static bool ShouldAdvance(int currentStage, int nextStage)
	{
		return nextStage > currentStage;
	}

	internal static int CalculateRetainedCount(int liveCount, float remainPercent, int minimumCount)
	{
		liveCount = Math.Max(0, liveCount);
		if (!(remainPercent > 0f))
		{
			return 0;
		}
		int num = (int)((1f - remainPercent) * (float)liveCount);
		return Math.Max(liveCount - num, minimumCount);
	}

	internal static int CalculateMigrationLimit(int liveCount, int retainedCount)
	{
		return Math.Max(0, liveCount - Math.Max(0, retainedCount));
	}

	internal static StageSnapshot Snapshot(int stage, int liveCount, float remainPercent, int minimumCount)
	{
		return new StageSnapshot(stage, liveCount, CalculateRetainedCount(liveCount, remainPercent, minimumCount));
	}
}
