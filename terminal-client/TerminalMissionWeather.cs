using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Weather;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalMissionWeather
{
	private sealed class Settings
	{
		[JsonProperty(Required = Required.Always)]
		public int schema { get; set; }

		[JsonProperty(Required = Required.Always)]
		public Profile opening { get; set; }

		[JsonProperty]
		public List<Preset> presets { get; set; }
	}

	private sealed class Preset
	{
		[JsonProperty("id", Required = Required.Always)]
		public string Id { get; set; }

		[JsonProperty("lockWeather", Required = Required.Always)]
		public int LockWeatherValue { get; set; }

		[JsonProperty("updateWeatherTime", Required = Required.Always)]
		public int UpdateWeatherTimeValue { get; set; }

		[JsonProperty("WeatherConfig", Required = Required.Always)]
		public Profile WeatherConfig { get; set; }

		internal bool LockWeather => LockWeatherValue == 1;

		internal bool UpdateWeatherTime => UpdateWeatherTimeValue == 1;

		internal bool Valid()
		{
			if (!string.IsNullOrWhiteSpace(Id) && (LockWeatherValue == 0 || LockWeatherValue == 1) && (UpdateWeatherTimeValue == 0 || UpdateWeatherTimeValue == 1) && WeatherConfig != null)
			{
				return WeatherConfig.Valid();
			}
			return false;
		}
	}

	internal sealed class Profile
	{
		[JsonProperty(Required = Required.Always)]
		public int TimeHour;

		[JsonProperty(Required = Required.Always)]
		public int TimeMinute;

		[JsonProperty(Required = Required.Always)]
		public float Cloudness;

		[JsonProperty(Required = Required.Always)]
		public float Wind;

		[JsonProperty(Required = Required.Always)]
		public int WindDirection;

		[JsonProperty(Required = Required.Always)]
		public float Rain;

		[JsonProperty(Required = Required.Always)]
		public float RainRandomness;

		[JsonProperty(Required = Required.Always)]
		public float ScaterringFogDensity;

		[JsonProperty(Required = Required.Always)]
		public float Temperature;

		[JsonProperty(Required = Required.Always)]
		public float AtmospherePressure;

		[JsonProperty(Required = Required.Always)]
		public float LyingWater;

		internal bool Valid()
		{
			//IL_010b: Unknown result type (might be due to invalid IL or missing references)
			if (TimeHour >= 0 && TimeHour <= 23 && TimeMinute >= 0 && TimeMinute <= 59 && WindDirection >= 0 && WindDirection < WeatherNode.WindDirections.Length && Rain >= 0f && Rain <= 1f && Wind >= 0f && Wind <= 1f && Cloudness >= -1f && Cloudness <= 1f && ScaterringFogDensity >= 0f)
			{
				return ValueEnumerableExtensions.All<float>(ValueEnumerable.AsValueEnumerable<float>(new float[8] { Rain, Wind, Cloudness, RainRandomness, ScaterringFogDensity, Temperature, AtmospherePressure, LyingWater }), (Func<float, bool>)((float value) => !float.IsNaN(value) && !float.IsInfinity(value)));
			}
			return false;
		}
	}

	private static readonly FieldInfo BackendCurve = AccessTools.Field(typeof(WeatherController), "backendWeatherCurve");

	private static Settings _settings;

	private static bool _settingsTried;

	private static bool _reportedFailure;

	private static WeatherController _owner;

	private static IWeatherCurve _previousCurve;

	private static WeatherCurve _curve;

	private static WeatherDebug _previousDebug;

	private static bool _previousDebugEnabled;

	private static GameDateTime _clock;

	private static TOD_Time _tod;

	private static bool _previousClockLock;

	private static bool _previousSkyLock;

	private static bool _expectedLock;

	private static int _applyFrame;

	private static bool _lockPending;

	private static DateTime _skyTime;

	private static bool _endingApplied;

	private static IWeatherCurve _endingPreviousCurve;

	private static WeatherDebug _endingPreviousDebug;

	private static bool _endingPreviousDebugEnabled;

	private static bool _endingPreviousClockLock;

	private static bool _endingPreviousSkyLock;

	private static bool _endingPreviousExpectedLock;

	private static bool _endingPreviousLockPending;

	private static DateTime _endingPreviousSkyTime;

	private static DateTime _endingPreviousClockTime;

	private static DateTime _endingPreviousCycleTime;

	private static DateTime _endingAppliedTime;

	private static DateTime _endingAppliedStatedGameTime;

	private static DateTime _endingAppliedStatedRealTime;

	private static float _endingAppliedRealtimeSinceStartup;

	private static float _endingAppliedTimeFactor;

	private static float _endingAppliedTimeFactorMod;

	private static bool Enabled
	{
		get
		{
			if (TerminalGate.On && Plugin.WeatherStack.Value && Plugin.RetailMissionWeather.Value)
			{
				return !Plugin.ForceWeather.Value;
			}
			return false;
		}
	}

	internal static bool Applied
	{
		get
		{
			if ((bool)_owner && (object)_owner == WeatherController.Instance)
			{
				return BackendCurve?.GetValue(_owner) == _curve;
			}
			return false;
		}
	}

	internal static bool EndingApplied => _endingApplied;

	internal static bool TryApplyOpening()
	{
		if (!Enabled)
		{
			return false;
		}
		WeatherController instance = WeatherController.Instance;
		if ((bool)_owner && (object)_owner == instance)
		{
			return true;
		}
		GameWorld instance2 = Singleton<GameWorld>.Instance;
		AbstractGame instance3 = Singleton<AbstractGame>.Instance;
		TOD_Time tOD_Time = (TODSkyProvider.IsAvailable ? TODSkyProvider.Instance.CurrentTime : null);
		if (!instance || !TerminalWeatherLifecycle.HasNativeWeather(instance) || instance3 == null || instance3.Status != GameStatus.Started || instance2?.GameDateTime == null || !tOD_Time || instance.WeatherDebug == null)
		{
			return false;
		}
		Settings settings = LoadSettings();
		if (settings == null)
		{
			return false;
		}
		try
		{
			if (BackendCurve == null)
			{
				throw new MissingFieldException("WeatherController.backendWeatherCurve");
			}
			ResetForRaid();
			_owner = instance;
			_clock = instance2.GameDateTime;
			_tod = tOD_Time;
			_previousClockLock = _clock.Locked;
			_previousSkyLock = _tod.LockCurrentTime;
			_previousCurve = (IWeatherCurve)BackendCurve.GetValue(instance);
			_previousDebug = instance.WeatherDebug.Clone(instance.WeatherDebug);
			_previousDebugEnabled = instance.WeatherDebug.Enabled;
			IWeatherCurve weatherCurve = instance.WeatherCurve;
			_skyTime = SelectTime(_clock.Calculate(), settings.opening);
			_clock.Unlock();
			_tod.LockCurrentTime = false;
			_expectedLock = false;
			_tod.GameDateTime = _clock;
			_clock.ResetForce(_skyTime);
			_curve = CreateCurve(weatherCurve, settings.opening, _skyTime.AddSeconds(1.0).Ticks);
			BackendCurve.SetValue(instance, _curve);
			instance.WeatherDebug.Enabled = false;
			instance.WeatherDebug.CopyParams(_curve);
			TODSkyProvider.Instance.Cycle.DateTime = _skyTime;
			_applyFrame = Time.frameCount;
			_lockPending = true;
			Plugin.Log.LogInfo($"[MissionWeather] retail opening applied: time={_skyTime:yyyy-MM-dd HH:mm:ss}, " + $"rain={settings.opening.Rain:0.###}, clouds={settings.opening.Cloudness:0.###}, " + $"wind={settings.opening.Wind:0.###}, fog={settings.opening.ScaterringFogDensity:0.####}; sky lock next frame, no timed release");
			return true;
		}
		catch (Exception error)
		{
			ResetForRaid();
			ReportFailure(error);
			return false;
		}
	}

	internal static void Pump()
	{
		if ((object)_owner != null)
		{
			if (!Enabled || !_owner || (object)_owner != WeatherController.Instance)
			{
				ResetForRaid();
			}
			else if (!Applied)
			{
				ResetForRaid();
			}
			else if (_lockPending && Time.frameCount > _applyFrame)
			{
				_clock.Lock();
				_tod.LockCurrentTime = true;
				_expectedLock = true;
				_lockPending = false;
			}
		}
	}

	internal static bool TryApplyEnding00()
	{
		if (!Enabled)
		{
			return false;
		}
		if (_endingApplied && (object)_owner == WeatherController.Instance)
		{
			return true;
		}
		if (!Applied)
		{
			ReportFailure(new InvalidOperationException("Ending00 weather requires the owned opening mission-weather curve"));
			return false;
		}
		Preset preset = FindPreset(LoadSettings(), "ending_weather_1");
		if (preset == null || !preset.Valid())
		{
			ReportFailure(new InvalidDataException("Missing or invalid ending_weather_1 profile"));
			return false;
		}
		WeatherController instance = WeatherController.Instance;
		GameWorld instance2 = Singleton<GameWorld>.Instance;
		AbstractGame instance3 = Singleton<AbstractGame>.Instance;
		TOD_Time tOD_Time = (TODSkyProvider.IsAvailable ? TODSkyProvider.Instance.CurrentTime : null);
		if (!instance || !TerminalWeatherLifecycle.HasNativeWeather(instance) || instance3 == null || instance3.Status != GameStatus.Started || instance2?.GameDateTime == null || !tOD_Time || instance.WeatherDebug == null)
		{
			ReportFailure(new InvalidOperationException("Ending00 weather prerequisites are not ready"));
			return false;
		}
		try
		{
			if (BackendCurve == null)
			{
				throw new MissingFieldException("WeatherController.backendWeatherCurve");
			}
			_endingPreviousCurve = (IWeatherCurve)BackendCurve.GetValue(instance);
			_endingPreviousDebug = instance.WeatherDebug.Clone(instance.WeatherDebug);
			_endingPreviousDebugEnabled = instance.WeatherDebug.Enabled;
			_endingPreviousClockLock = instance2.GameDateTime.Locked;
			_endingPreviousSkyLock = tOD_Time.LockCurrentTime;
			_endingPreviousExpectedLock = _expectedLock;
			_endingPreviousLockPending = _lockPending;
			_endingPreviousSkyTime = _skyTime;
			_endingPreviousClockTime = instance2.GameDateTime.Calculate();
			_endingPreviousCycleTime = TODSkyProvider.Instance.Cycle.DateTime;
			_endingApplied = true;
			IWeatherCurve weatherCurve = instance.WeatherCurve;
			Profile weatherConfig = preset.WeatherConfig;
			DateTime dateTime = instance2.GameDateTime.Calculate();
			DateTime dateTime2 = (preset.UpdateWeatherTime ? SelectTime(dateTime, weatherConfig) : dateTime);
			long ticks = dateTime2.AddSeconds(1.0).Ticks;
			_endingAppliedTime = dateTime2;
			instance2.GameDateTime.Unlock();
			tOD_Time.LockCurrentTime = false;
			_expectedLock = false;
			tOD_Time.GameDateTime = instance2.GameDateTime;
			if (preset.UpdateWeatherTime)
			{
				instance2.GameDateTime.ResetForce(dateTime2);
				TODSkyProvider.Instance.Cycle.DateTime = dateTime2;
				_skyTime = dateTime2;
			}
			CaptureEndingClockIdentity();
			_curve = CreateCurve(weatherCurve, weatherConfig, ticks);
			BackendCurve.SetValue(instance, _curve);
			instance.WeatherDebug.Enabled = false;
			instance.WeatherDebug.CopyParams(_curve);
			_applyFrame = Time.frameCount;
			_lockPending = preset.LockWeather;
			if (!_lockPending)
			{
				_expectedLock = false;
			}
			Plugin.Log.LogInfo($"[MissionWeather] Ending_00 t=0 weather applied: time={dateTime2:yyyy-MM-dd HH:mm:ss}, " + $"rain={weatherConfig.Rain:0.###}, clouds={weatherConfig.Cloudness:0.###}, " + $"wind={weatherConfig.Wind:0.###}, fog={weatherConfig.ScaterringFogDensity:0.####}, " + $"lock={preset.LockWeather}, updateTime={preset.UpdateWeatherTime}; " + "retail cloud asset resources.assets:47780 (texture resources.assets:1688) is unsupported, so the existing Terminal cloud compatibility fallback remains active");
			return true;
		}
		catch (Exception error)
		{
			ResetEndingWeather();
			ReportFailure(error);
			return false;
		}
	}

	internal static void ResetEndingWeather()
	{
		if (!_endingApplied)
		{
			return;
		}
		WeatherController owner = _owner;
		bool flag = (bool)owner && _curve != null && BackendCurve?.GetValue(owner) == _curve;
		GameWorld instance = Singleton<GameWorld>.Instance;
		bool flag2 = IsEndingClockIdentity();
		bool flag3 = ((flag && _clock != null && instance?.GameDateTime == _clock) & flag2) && _clock.Locked == _expectedLock;
		bool flag4 = flag2 && TODSkyProvider.Instance?.Cycle != null && TODSkyProvider.Instance.Cycle.DateTime == _endingAppliedTime;
		bool flag5 = ((flag && (bool)_tod && (object)TODSkyProvider.Instance?.CurrentTime == _tod) & flag4) && _tod.LockCurrentTime == _expectedLock;
		try
		{
			if (flag)
			{
				BackendCurve.SetValue(owner, _endingPreviousCurve);
				if (!owner.WeatherDebug.Enabled && _endingPreviousDebug != null)
				{
					owner.WeatherDebug.CopyParams(_endingPreviousDebug);
					owner.WeatherDebug.Enabled = _endingPreviousDebugEnabled;
				}
			}
			if (flag3)
			{
				_clock.ResetForce(_endingPreviousClockTime);
			}
			if (_clock != null && _clock.Locked == _expectedLock && (flag3 || (!flag & flag2)))
			{
				if (flag3)
				{
					if (_endingPreviousClockLock)
					{
						_clock.Lock();
					}
					else
					{
						_clock.Unlock();
					}
				}
				else
				{
					_clock.Unlock();
				}
			}
			if ((bool)_tod && _tod.LockCurrentTime == _expectedLock && (flag5 || (!flag & flag4)))
			{
				_tod.LockCurrentTime = flag5 && _endingPreviousSkyLock;
			}
			if (flag5 && TODSkyProvider.Instance?.Cycle != null)
			{
				TODSkyProvider.Instance.Cycle.DateTime = _endingPreviousCycleTime;
			}
		}
		finally
		{
			if ((flag & flag3 & flag5) && (bool)owner && _endingPreviousCurve != null)
			{
				_curve = _endingPreviousCurve as WeatherCurve;
				_skyTime = _endingPreviousSkyTime;
				_expectedLock = _endingPreviousExpectedLock;
				_lockPending = _endingPreviousLockPending;
			}
			else
			{
				_owner = null;
				_previousCurve = null;
				_previousDebug = null;
				_clock = null;
				_tod = null;
				_curve = null;
				_expectedLock = false;
				_lockPending = false;
			}
			_endingApplied = false;
			_endingPreviousCurve = null;
			_endingPreviousDebug = null;
			_endingPreviousClockTime = default;
			_endingAppliedTime = default;
			_endingAppliedStatedGameTime = default;
			_endingAppliedStatedRealTime = default;
			_endingAppliedRealtimeSinceStartup = 0f;
			_endingAppliedTimeFactor = 0f;
			_endingAppliedTimeFactorMod = 0f;
		}
	}

	private static void CaptureEndingClockIdentity()
	{
		_endingAppliedStatedGameTime = _clock.StatedGameDateTime;
		_endingAppliedStatedRealTime = _clock.StatedRealDateTime;
		_endingAppliedRealtimeSinceStartup = _clock._realtimeSinceStartup;
		_endingAppliedTimeFactor = _clock.TimeFactor;
		_endingAppliedTimeFactorMod = _clock.TimeFactorMod;
	}

	private static bool IsEndingClockIdentity()
	{
		if (_clock == null)
		{
			return false;
		}
		if (_clock.StatedGameDateTime == _endingAppliedStatedGameTime && _clock.StatedRealDateTime == _endingAppliedStatedRealTime && _clock._realtimeSinceStartup == _endingAppliedRealtimeSinceStartup && _clock.TimeFactor == _endingAppliedTimeFactor)
		{
			return _clock.TimeFactorMod == _endingAppliedTimeFactorMod;
		}
		return false;
	}

	internal static DateTime ResolveSkyTime(DateTime natural)
	{
		if (!Enabled || !Applied || !_tod || (!_lockPending && !_tod.LockCurrentTime))
		{
			return natural;
		}
		return _skyTime;
	}

	internal static void ResetForRaid()
	{
		ResetEndingWeather();
		try
		{
			if ((bool)_owner && _curve != null && BackendCurve?.GetValue(_owner) == _curve)
			{
				BackendCurve.SetValue(_owner, _previousCurve);
				if (!_owner.WeatherDebug.Enabled && _previousDebug != null)
				{
					_owner.WeatherDebug.CopyParams(_previousDebug);
					_owner.WeatherDebug.Enabled = _previousDebugEnabled;
				}
			}
			if (_clock != null && _clock.Locked == _expectedLock)
			{
				if (_previousClockLock)
				{
					_clock.Lock();
				}
				else
				{
					_clock.Unlock();
				}
			}
			if ((bool)_tod && _tod.LockCurrentTime == _expectedLock)
			{
				_tod.LockCurrentTime = _previousSkyLock;
			}
		}
		finally
		{
			_owner = null;
			_previousCurve = null;
			_curve = null;
			_previousDebug = null;
			_clock = null;
			_tod = null;
			_lockPending = false;
			_expectedLock = false;
		}
	}

	internal static DateTime SelectTime(DateTime current, Profile profile)
	{
		DateTime dateTime = new DateTime(current.Year, current.Month, current.Day, profile.TimeHour, profile.TimeMinute, current.Second, DateTimeKind.Utc);
		if (!(dateTime < current))
		{
			return dateTime;
		}
		return dateTime.AddDays(1.0);
	}

	internal static WeatherCurve CreateCurve(IWeatherCurve start, Profile profile, long ticks)
	{
		WeatherCurve weatherCurve = new WeatherCurve(new WeatherNode[1]
		{
			new WeatherNode
			{
				Time = ticks
			}
		});
		Vector2 vector = WeatherNode.WindDirections[profile.WindDirection] * profile.Wind;
		Vector2 topWind = start.TopWind;
		weatherCurve.windXCurve = AnimationCurve.Linear(0f, vector.x, 1f, vector.x);
		weatherCurve.windYCurve = AnimationCurve.Linear(0f, vector.y, 1f, vector.y);
		weatherCurve.topWindXCurve = AnimationCurve.Linear(0f, topWind.x, 1f, 0f);
		weatherCurve.topWindYCurve = AnimationCurve.Linear(0f, topWind.y, 1f, 0f);
		weatherCurve.rainCurve = AnimationCurve.Linear(0f, start.Rain, 1f, profile.Rain);
		weatherCurve.cloudinessCurve = AnimationCurve.Linear(0f, start.Cloudiness, 1f, profile.Cloudness);
		weatherCurve.fogCurve = AnimationCurve.Linear(0f, start.Fog, 1f, profile.ScaterringFogDensity);
		weatherCurve.temperatureCurve = AnimationCurve.Linear(0f, start.Temperature, 1f, profile.Temperature);
		return weatherCurve;
	}

	private static Settings LoadSettings()
	{
		if (_settingsTried)
		{
			return _settings;
		}
		_settingsTried = true;
		try
		{
			Settings settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_mission_weather.json")));
			if (settings == null || settings.schema != 1 || settings.opening == null || !settings.opening.Valid())
			{
				throw new InvalidDataException("Invalid mission weather schema or opening profile");
			}
			_settings = settings;
		}
		catch (Exception error)
		{
			ReportFailure(error);
		}
		return _settings;
	}

	private static Preset FindPreset(Settings settings, string id)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		if (settings?.presets == null || string.IsNullOrEmpty(id))
		{
			return null;
		}
		return ValueEnumerableExtensions.FirstOrDefault<FromList<Preset>, Preset>(ValueEnumerable.AsValueEnumerable<Preset>(settings.presets), (Func<Preset, bool>)((Preset p) => p != null && string.Equals(p.Id, id, StringComparison.Ordinal)));
	}

	private static void ReportFailure(Exception error)
	{
		if (!_reportedFailure)
		{
			_reportedFailure = true;
			Plugin.Log.LogWarning("[MissionWeather] cannot apply recovered opening; leaving forecast available: " + error.Message);
		}
	}
}
