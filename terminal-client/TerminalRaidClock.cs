using System;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalRaidClock
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_PinRaidClock
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On && !(Plugin.RaidStartHour.Value < 0f))
			{
				new GameObject("Terminal_RaidClock").AddComponent<ClockHost>();
			}
		}
	}

	internal class ClockHost : MonoBehaviour
	{
		private float _started;

		private float _next;

		private bool _worldDone;

		private bool _skyDone;

		private void Start()
		{
			_started = Time.realtimeSinceStartup;
		}

		private void Update()
		{
			if (Time.realtimeSinceStartup < _next)
			{
				return;
			}
			_next = Time.realtimeSinceStartup + 0.5f;
			float value = Plugin.RaidStartHour.Value;
			int num = (int)value;
			int num2 = (int)((value - (float)num) * 60f);
			if (!_worldDone)
			{
				_worldDone = TryAnchor(Singleton<GameWorld>.Instance?.GameDateTime, num, num2, "GameWorld");
			}
			if (!_skyDone)
			{
				GameDateTime gameDateTime = TODSkyProvider.Instance?.CurrentTime?.GameDateTime;
				if (gameDateTime != null && _worldDone && gameDateTime == Singleton<GameWorld>.Instance?.GameDateTime)
				{
					_skyDone = true;
				}
				else if (gameDateTime != null)
				{
					_skyDone = TryAnchor(gameDateTime, num, num2, "sky/TODSkyProvider");
				}
			}
			if (_worldDone && _skyDone)
			{
				Plugin.Log.LogInfo($"[RaidClock] both clocks anchored to {num:00}:{num2:00} — night raid, still ticking");
				UnityEngine.Object.Destroy(gameObject);
			}
			else if (Time.realtimeSinceStartup - _started > 20f)
			{
				Plugin.Log.LogWarning($"[RaidClock] gave up after 20s (world={_worldDone}, sky={_skyDone}) — " + "whichever missed keeps natural time");
				UnityEngine.Object.Destroy(gameObject);
			}
		}

		private static bool TryAnchor(GameDateTime gdt, int h, int m, string label)
		{
			if (gdt == null)
			{
				return false;
			}
			try
			{
				DateTime dateTime = gdt.Calculate();
				DateTime gameDateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, h, m, 0);
				gdt.Reset(gameDateTime);
				Plugin.Log.LogDebug($"[RaidClock] {label} clock anchored to {h:00}:{m:00}");
				return true;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[RaidClock] " + label + " clock anchor failed: " + ex.Message);
				return true;
			}
		}
	}
}
