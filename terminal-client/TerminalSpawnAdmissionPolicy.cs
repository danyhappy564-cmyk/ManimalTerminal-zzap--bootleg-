namespace Manimal.Terminal;

internal static class TerminalSpawnAdmissionPolicy
{
	internal readonly struct TotalDecision
	{
		internal bool Allowed { get; }

		internal bool CapBypassed { get; }

		internal int Wanted { get; }

		internal int Headroom { get; }

		internal TotalDecision(bool allowed, bool capBypassed, int wanted, int headroom)
		{
			Allowed = allowed;
			CapBypassed = capBypassed;
			Wanted = wanted;
			Headroom = headroom;
		}
	}

	internal static TotalDecision EvaluateTotal(int wanted, int living, int pending, int maxAliveBots, bool ordinaryScav, bool authoredIgnoreMaxBots)
	{
		int num = ((wanted > 0) ? wanted : 0);
		if (num == 0)
		{
			return new TotalDecision(allowed: true, capBypassed: false, 0, int.MaxValue);
		}
		bool flag = !ordinaryScav & authoredIgnoreMaxBots;
		if (flag || maxAliveBots <= 0)
		{
			return new TotalDecision(allowed: true, flag, num, int.MaxValue);
		}
		int num2 = ((living > 0) ? living : 0);
		int num3 = ((pending > 0) ? pending : 0);
		long num4 = (long)num2 + (long)num3;
		int num5 = (int)((num4 < maxAliveBots) ? (maxAliveBots - num4) : 0);
		return new TotalDecision(num <= num5, capBypassed: false, num, num5);
	}
}
