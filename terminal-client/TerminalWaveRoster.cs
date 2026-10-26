using System;
using System.Collections.Generic;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalWaveRoster
{
	internal static int Count(BossLocationSpawn wave)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		if (wave == null)
		{
			return 0;
		}
		List<BossLocationSpawnSubData> escors = wave.GetEscors();
		int num = ((escors != null) ? ValueEnumerableExtensions.Sum<FromList<BossLocationSpawnSubData>, BossLocationSpawnSubData, int>(ValueEnumerable.AsValueEnumerable<BossLocationSpawnSubData>(escors), (Func<BossLocationSpawnSubData, int>)((BossLocationSpawnSubData s) => Math.Max(0, s.BossEscortAmount))) : Math.Max(0, wave.EscortCount));
		return 1 + num;
	}

	internal static void Normalize(BossLocationSpawn wave)
	{
		if (wave != null && wave.GetEscors() != null)
		{
			int num = Count(wave) - 1;
			if (wave.EscortCount != num)
			{
				int escortCount = wave.EscortCount;
				wave.EscortCount = num;
				Plugin.Log.LogInfo("[WaveRoster] '" + wave.TriggerId + "' '" + wave.BossZone + "': " + $"escort budget {escortCount} -> {num}; authored squad={num + 1}");
			}
		}
	}
}
