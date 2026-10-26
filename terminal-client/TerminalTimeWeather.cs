using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalTimeWeather
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_Arm
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On)
			{
				ResetForRaid();
				new GameObject("Terminal_TimeWeather").AddComponent<Host>();
			}
		}
	}

	internal class Host : MonoBehaviour
	{
		private float _next;

		private float _deadline;

		private bool _skyWired;

		private bool _timeSet;

		private bool _weatherSeeded;

		private float _weatherReleaseAt;

		private WeatherDebug _seededWeatherDebug;

		private bool _reportedMissingNativeWeather;

		private void Start()
		{
			_deadline = Time.realtimeSinceStartup + 60f;
		}

		private void Update()
		{
			if (_done)
			{
				UnityEngine.Object.Destroy(gameObject);
			}
			else if (_weatherSeeded)
			{
				WeatherController instance = WeatherController.Instance;
				if (Plugin.ForceWeather.Value && Time.realtimeSinceStartup >= _weatherReleaseAt && !TerminalWeatherLifecycle.HasNativeWeather(instance))
				{
					if (!_reportedMissingNativeWeather)
					{
						_reportedMissingNativeWeather = true;
						Plugin.Log.LogWarning("[TimeWeather] opening override cannot hand off: native weather nodes have not arrived; keeping the selected override until weather is ready or ForceWeather is disabled");
					}
				}
				else if (!Plugin.ForceWeather.Value || Time.realtimeSinceStartup >= _weatherReleaseAt)
				{
					ReleaseStartupWeather();
				}
			}
			else
			{
				if (Time.realtimeSinceStartup < _next)
				{
					return;
				}
				_next = Time.realtimeSinceStartup + 0.5f;
				if (Time.realtimeSinceStartup > _deadline)
				{
					Plugin.Log.LogWarning("[TimeWeather] gave up after 60s — " + $"sky clock wired={_skyWired}, weather stack up={(bool)WeatherController.Instance}");
					UnityEngine.Object.Destroy(gameObject);
					return;
				}
				GameWorld instance2 = Singleton<GameWorld>.Instance;
				GameDateTime gameDateTime = (instance2 ? instance2.GameDateTime : null);
				if (gameDateTime == null)
				{
					return;
				}
				if (!_skyWired)
				{
					_skyWired = WireSkyClock(gameDateTime);
				}
				if (!_skyWired)
				{
					return;
				}
				if (!_timeSet && Plugin.RaidStartHour.Value >= 0f && (!Plugin.RetailMissionWeather.Value || Plugin.ForceWeather.Value))
				{
					SetTime(gameDateTime);
					_timeSet = true;
				}
				if (!SeedStartupWeather())
				{
					TerminalWeather.TryStage();
				}
				else if (!_weatherSeeded)
				{
					if (!TerminalWeatherLifecycle.HasNativeWeather(WeatherController.Instance))
					{
						Plugin.Log.LogWarning("[TimeWeather] native weather was not initialized; see WeatherLifecycle diagnostics");
					}
					_done = true;
					UnityEngine.Object.Destroy(gameObject);
				}
			}
		}

		private static bool WireSkyClock(GameDateTime gdt)
		{
			try
			{
				TOD_Time tOD_Time = UnityEngine.Object.FindObjectOfType<TOD_Time>();
				if (!tOD_Time)
				{
					return false;
				}
				FieldInfo fieldInfo = AccessTools.Field(typeof(TOD_Time), "GameDateTime");
				if (fieldInfo == null)
				{
					return false;
				}
				GameDateTime gameDateTime = fieldInfo.GetValue(tOD_Time) as GameDateTime;
				if (gameDateTime != gdt)
				{
					fieldInfo.SetValue(tOD_Time, gdt);
					Plugin.Log.LogWarning("[TimeWeather] TOD_Time.GameDateTime rewired to the raid clock (was " + ((gameDateTime == null) ? "NULL" : "a different instance") + ")");
				}
				if (tOD_Time.enabled)
				{
					tOD_Time.enabled = false;
					Plugin.Log.LogWarning("[TimeWeather] TOD_Time disabled — it seeds the sky from the REAL clock and re-stomps it every frame; the raid clock owns the sky now");
				}
				return true;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[TimeWeather] sky clock wiring failed: " + ex.Message);
				return false;
			}
		}

		private static void SetTime(GameDateTime gdt)
		{
			try
			{
				float value = Plugin.RaidStartHour.Value;
				int num = (int)value;
				int num2 = (int)((value - (float)num) * 60f);
				DateTime dateTime = gdt.Calculate();
				DateTime dateTime2 = dateTime.AddHours(num - dateTime.Hour).AddMinutes(num2 - dateTime.Minute);
				gdt.Reset(dateTime2);
				Plugin.Log.LogWarning($"[TimeWeather] time set to {dateTime2:HH:mm} (was {dateTime:HH:mm}) — retail terminal runs 21:00-06:00");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[TimeWeather] time set failed: " + ex.Message);
			}
		}

		private bool SeedStartupWeather()
		{
			try
			{
				if (!Plugin.ForceWeather.Value)
				{
					return !Plugin.RetailMissionWeather.Value || TerminalMissionWeather.TryApplyOpening();
				}
				WeatherController instance = WeatherController.Instance;
				if (!instance)
				{
					return false;
				}
				WeatherDebug weatherDebug = instance.WeatherDebug;
				if (weatherDebug == null)
				{
					Plugin.Log.LogDebug("[TimeWeather] no WeatherDebug — weather left alone");
					return true;
				}
				weatherDebug.isEnabled = true;
				weatherDebug.Rain = Plugin.WeatherRain.Value;
				weatherDebug.CloudDensity = Plugin.WeatherClouds.Value;
				weatherDebug.Fog = Plugin.WeatherFog.Value;
				weatherDebug.WindMagnitude = Plugin.WeatherWind.Value;
				weatherDebug.LightningThunderProbability = Plugin.WeatherThunder.Value;
				_seededWeatherDebug = weatherDebug;
				_weatherSeeded = true;
				float num = Mathf.Clamp(Plugin.WeatherStartHoldSeconds.Value, 5f, 300f);
				_weatherReleaseAt = Time.realtimeSinceStartup + num;
				Plugin.Log.LogWarning($"[TimeWeather] startup weather seeded for {num:0}s — " + $"rain={weatherDebug.Rain:0.00} clouds={weatherDebug.CloudDensity:0.00} fog={weatherDebug.Fog:0.000} " + $"wind={weatherDebug.WindMagnitude:0.00} thunder={weatherDebug.LightningThunderProbability:0.00}; " + "native weather will resume afterward");
				return true;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[TimeWeather] weather set failed: " + ex.Message);
				return true;
			}
		}

		private void ReleaseStartupWeather()
		{
			try
			{
				if (_seededWeatherDebug != null)
				{
					_seededWeatherDebug.Enabled = false;
				}
				WeatherController instance = WeatherController.Instance;
				if ((bool)instance)
				{
					IWeatherCurve weatherCurve = instance.WeatherCurve;
					Plugin.Log.LogWarning("[TimeWeather] startup weather released — native weather curve resumed: " + $"rain={weatherCurve.Rain:0.00} clouds={weatherCurve.Cloudiness:0.00} fog={weatherCurve.Fog:0.000} " + $"wind={weatherCurve.Wind.magnitude:0.00} thunder={weatherCurve.LightningThunderProbability:0.00}");
				}
				else
				{
					Plugin.Log.LogWarning("[TimeWeather] startup weather released; WeatherController was already gone");
				}
			}
			catch (Exception ex)
			{
				if (_seededWeatherDebug != null)
				{
					_seededWeatherDebug.Enabled = false;
				}
				Plugin.Log.LogWarning("[TimeWeather] startup weather release completed with a diagnostic error: " + ex.Message);
			}
			finally
			{
				_weatherSeeded = false;
				_seededWeatherDebug = null;
				_done = true;
				UnityEngine.Object.Destroy(gameObject);
			}
		}
	}

	private static bool _done;

	internal static void ResetForRaid()
	{
		_done = false;
	}
}
