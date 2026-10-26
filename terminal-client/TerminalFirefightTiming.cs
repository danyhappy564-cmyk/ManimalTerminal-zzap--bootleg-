using System;

namespace Manimal.Terminal;

internal static class TerminalFirefightTiming
{
	internal const float MinimumPeriod = 0.01f;

	internal static float Fade(float value, int type)
	{
		if (type == 3)
		{
			if (!(value < 0.5f))
			{
				return 1f - 0.5f * (-2f * value + 2f) * (-2f * value + 2f);
			}
			return 2f * value * value;
		}
		return value;
	}

	internal static float Phase(double dspTime, float period)
	{
		if (!(period <= 0.01f))
		{
			return (float)(dspTime % (double)period / (double)period);
		}
		return 0f;
	}

	internal static float Density(float curveValue, float period, float baseDensity, float localDensity)
	{
		return Clamp01(((period <= 0.01f) ? 1f : Clamp01(curveValue)) * baseDensity * localDensity);
	}

	internal static float GrenadeProbability(float ratePerMinute, float density, float dt)
	{
		return ratePerMinute * density * dt / 60f;
	}

	internal static float Cooldown(float density, float atZero, float atMaximum, float profileGap, float jitter)
	{
		return Lerp(atZero, atMaximum, density) + profileGap + jitter;
	}

	internal static float Backoff(float density, float atZero, float atMaximum)
	{
		return Lerp(atZero, atMaximum, density);
	}

	internal static float Beat(float loopLength, float rpm)
	{
		if (!(rpm > 0f))
		{
			if (!(loopLength > 0.01f))
			{
				return 0f;
			}
			return loopLength / 16f;
		}
		return 1f / Math.Max(1f, rpm / 60f);
	}

	internal static int BurstMinimum(float value)
	{
		return Math.Max(1, (int)Math.Floor(value));
	}

	internal static int BurstMaximum(float value, int minimum)
	{
		return Math.Max(minimum, (int)Math.Ceiling(value));
	}

	internal static void Distribute(float[] density, float total, int maximum, int start, int[] slots)
	{
		Array.Clear(slots, 0, slots.Length);
		if (!(total <= 0f) && density.Length != 0)
		{
			float num = 0f;
			int num2 = 0;
			for (int i = 0; i < density.Length; i++)
			{
				int num3 = (start + i) % density.Length;
				num += density[num3] / total * (float)maximum;
				slots[num3] = Math.Max(0, Math.Min(maximum - num2, (int)num - num2));
				num2 += slots[num3];
			}
		}
	}

	private static float Lerp(float a, float b, float t)
	{
		return a + (b - a) * Clamp01(t);
	}

	private static float Clamp01(float value)
	{
		return Math.Max(0f, Math.Min(1f, value));
	}
}
