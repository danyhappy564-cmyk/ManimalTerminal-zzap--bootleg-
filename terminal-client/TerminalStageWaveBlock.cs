using System;
using System.Collections.Generic;
using Comfort.Common;
using ZLinq;

namespace Manimal.Terminal;

internal static class TerminalStageWaveBlock
{
	internal readonly struct Result
	{
		internal string Zone { get; }

		internal bool Accepted { get; }

		internal bool ScenarioFound { get; }

		internal int MatchedRows { get; }

		internal int NewlyBlockedRows { get; }

		internal bool AlreadyBlocked { get; }

		internal bool Changed
		{
			get
			{
				if (NewlyBlockedRows <= 0)
				{
					if (Accepted)
					{
						return !AlreadyBlocked;
					}
					return false;
				}
				return true;
			}
		}

		internal Result(string zone, bool accepted, bool scenarioFound, int matchedRows, int newlyBlockedRows, bool alreadyBlocked)
		{
			Zone = zone;
			Accepted = accepted;
			ScenarioFound = scenarioFound;
			MatchedRows = matchedRows;
			NewlyBlockedRows = newlyBlockedRows;
			AlreadyBlocked = alreadyBlocked;
		}

		public override string ToString()
		{
			return $"zone='{Zone}' accepted={Accepted} scenario={ScenarioFound} matched={MatchedRows} " + $"new={NewlyBlockedRows} already={AlreadyBlocked}";
		}
	}

	private static readonly HashSet<string> BlockedZones = new HashSet<string>(StringComparer.Ordinal);

	private static readonly HashSet<BossLocationSpawn> BlockedRows = new HashSet<BossLocationSpawn>();

	private static bool InScope
	{
		get
		{
			if (TerminalGate.On)
			{
				return TerminalCoop.Authority;
			}
			return false;
		}
	}

	internal static void ResetForRaid()
	{
		BlockedZones.Clear();
		BlockedRows.Clear();
	}

	internal static Result BlockZone(string sourceZone)
	{
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		if (string.IsNullOrEmpty(sourceZone))
		{
			return new Result(sourceZone, accepted: false, scenarioFound: false, 0, 0, alreadyBlocked: false);
		}
		if (!InScope)
		{
			return new Result(sourceZone, accepted: false, scenarioFound: false, 0, 0, BlockedZones.Contains(sourceZone));
		}
		bool flag = !BlockedZones.Add(sourceZone);
		BossLocationSpawn[] array = null;
		try
		{
			array = ((!Singleton<IBotGame>.Instantiated) ? null : Singleton<IBotGame>.Instance?.BossSpawnScenario)?.BossSpawnWaves;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[StageDirector] native wave block lookup failed for '" + sourceZone + "': " + ex.Message);
		}
		if (array == null)
		{
			Plugin.Log.LogDebug("[StageDirector] native wave block armed before BossSpawnScenario was available for '" + sourceZone + "'");
			return new Result(sourceZone, accepted: true, scenarioFound: false, 0, 0, flag);
		}
		BossLocationSpawn[] array2 = ValueEnumerableExtensions.ToArray<BossLocationSpawn>(ValueEnumerableExtensions.Where<BossLocationSpawn>(ValueEnumerable.AsValueEnumerable<BossLocationSpawn>(array), (Func<BossLocationSpawn, bool>)((BossLocationSpawn wave) => wave != null && string.Equals(wave.BossZone, sourceZone, StringComparison.Ordinal))));
		int num = 0;
		BossLocationSpawn[] array3 = array2;
		foreach (BossLocationSpawn bossLocationSpawn in array3)
		{
			if (BlockedRows.Add(bossLocationSpawn))
			{
				num++;
			}
			bossLocationSpawn.BossChance = -1f;
			bossLocationSpawn.CalculateChance();
		}
		if (num > 0 || (!flag && array2.Length != 0))
		{
			Plugin.Log.LogInfo($"[StageDirector] native wave block {new Result(sourceZone, accepted: true, scenarioFound: true, array2.Length, num, flag)}");
		}
		return new Result(sourceZone, accepted: true, scenarioFound: true, array2.Length, num, flag);
	}

	internal static bool IsBlockedZone(string zone)
	{
		if (InScope && !string.IsNullOrEmpty(zone))
		{
			return BlockedZones.Contains(zone);
		}
		return false;
	}

	internal static bool IsBlocked(BossLocationSpawn wave)
	{
		if (InScope && wave != null)
		{
			if (!BlockedRows.Contains(wave))
			{
				return IsBlockedZone(wave.BossZone);
			}
			return true;
		}
		return false;
	}
}
