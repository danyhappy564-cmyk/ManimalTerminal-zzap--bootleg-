using System;
using System.Collections.Generic;
using System.Globalization;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Manimal.Terminal.Server;

// recovered from the 0.2.20 release build (source was never published).
// a BossLocationSpawn whose Supports rosters already sum to its BossEscortAmount
// would get the parent escort count on top of the supports — zero the parent so
// the supports alone define the squad.
internal static class TerminalWaveRoster
{
    public static int NormalizeDuplicateEscortAmounts(LocationBase location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var normalized = 0;
        foreach (var wave in location.BossLocationSpawn ?? new List<BossLocationSpawn>())
        {
            if (wave?.Supports == null || !TryParseCount(wave.BossEscortAmount, out var parent) || parent <= 0)
                continue;

            long supportTotal = 0;
            var supportCount = 0;
            var unreadable = false;
            foreach (var support in wave.Supports)
            {
                if (support is null || !TryParseCount(support.BossEscortAmount, out var count))
                {
                    unreadable = true;
                    break;
                }
                supportTotal += count;
                supportCount++;
                if (supportTotal > int.MaxValue)
                {
                    unreadable = true;
                    break;
                }
            }

            if (!unreadable && supportCount != 0 && supportTotal != 0 && supportTotal == parent)
            {
                wave.BossEscortAmount = "0";
                normalized++;
            }
        }
        return normalized;
    }

    private static bool TryParseCount(string? value, out int count)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count);
}
