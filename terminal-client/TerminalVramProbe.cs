using System;
using EFT.CameraControl;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalVramProbe
{
	private const ulong Mib = 1048576uL;

	private static float _nextRead;

	private static bool _valid;

	private static bool _warned;

	private static long _totalMb;

	private static long _budgetMb;

	private static long _usedMb;

	internal static void ResetForRaid()
	{
		_nextRead = 0f;
		_valid = false;
		_warned = false;
		_totalMb = (_budgetMb = (_usedMb = -1L));
	}

	internal static bool Read(out long totalMb, out long budgetMb, out long usedMb)
	{
		if (Time.realtimeSinceStartup >= _nextRead)
		{
			_nextRead = Time.realtimeSinceStartup + 0.25f;
			try
			{
				CameraManager instance = CameraManager.Instance;
				if (instance != null)
				{
					instance.GetVRamUsage(out var totalVRam, out var localBudget, out var localCurrentUsage);
					_totalMb = (long)(totalVRam / 1048576);
					_budgetMb = (long)(localBudget / 1048576);
					_usedMb = (long)(localCurrentUsage / 1048576);
					_valid = totalVRam != 0 && localBudget != 0;
				}
			}
			catch (Exception ex)
			{
				_valid = false;
				if (!_warned)
				{
					_warned = true;
					Plugin.Log.LogWarning("[VRAM] EFT memory query unavailable: " + ex.Message);
				}
			}
		}
		totalMb = _totalMb;
		budgetMb = _budgetMb;
		usedMb = _usedMb;
		return _valid;
	}
}
