using System;
using System.Collections.Generic;
using EFT;
using EFT.CameraControl;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal sealed class TerminalFirefightDirector : IDisposable
{
	private sealed class Preset
	{
		internal Profile[] Profiles;

		internal AnimationCurve Curve;

		internal float Period;

		internal float Density;

		internal float Grenades;

		internal float CdZero;

		internal float CdMax;

		internal float NoProfile;

		internal float BackoffZero;

		internal float BackoffMax;

		internal float ResponseProbability;

		internal Vector2 Initial;

		internal Vector2 Jitter;

		internal Vector2 ResponseDelay;

		internal Vector2 ResponseShots;

		internal Vector2 ResponseCooldown;

		internal bool Respond;
	}

	private sealed class Profile
	{
		internal SoundBank[] Singles;

		internal SoundBank[] Grenades;

		internal LoopSet[] Loops;

		internal Vector2 Burst;

		internal Vector2 Volume;

		internal Vector2 Pitch;

		internal Vector2 StartJitter;

		internal Vector2 SingleGap;

		internal Vector2 BurstGap;

		internal float AutoBias;
	}

	private sealed class LoopSet
	{
		internal SoundBank Loop;

		internal SoundBank Tail;

		internal float Rpm;
	}

	private sealed class Area
	{
		internal Transform Transform;

		internal Preset Preset;

		internal float Radius;

		internal float LocalDensity;

		internal float Aperture;

		internal float Cooldown;

		internal Vector2 ForwardOffset;

		internal bool Sector;

		internal double BusyUntil;

		internal double NextResponse;

		internal readonly List<double> Ends = new List<double>();

		internal int Slots;

		internal bool Saturated
		{
			get
			{
				if (Slots > 0)
				{
					return Ends.Count >= Slots;
				}
				return false;
			}
		}
	}

	private sealed class SinglesJob
	{
		internal Area Area;

		internal Profile Profile;

		internal SoundBank Bank;

		internal Vector3 Position;

		internal float Distance;

		internal float Volume;

		internal float Due;

		internal int Remaining;
	}

	private readonly TerminalFirefightAssets _assets;

	private readonly TerminalFirefightPlayback _playback;

	private readonly Area[] _areas;

	private readonly List<SinglesJob> _singles = new List<SinglesJob>();

	private readonly float[] _density;

	private readonly int[] _slots;

	private readonly int _maximum;

	private readonly float _interval;

	private int _roundRobin;

	private float _nextTick;

	private bool _running;

	private bool _disposed;

	private bool _mixerReported;

	internal bool Ready
	{
		get
		{
			if (!_disposed)
			{
				return _areas.Length != 0;
			}
			return false;
		}
	}

	internal string Status => "retail profiles/strategies armed";

	internal string ReadinessSummary => $"{_areas.Length} areas, {_maximum} sequence slots, {_interval:0.00}s ticks";

	internal TerminalFirefightDirector(string directory, Dictionary<string, List<Transform>> paths)
	{
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_049c: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_033f: Unknown result type (might be due to invalid IL or missing references)
		TerminalFirefightDirector terminalFirefightDirector = this;
		_assets = TerminalFirefightAssets.Load(directory);
		_playback = new TerminalFirefightPlayback(_assets.FadeTypes);
		try
		{
			JObject spec = _assets.Spec;
			if (spec.Value<string>("startCutscene") != "Final_trkv_2")
			{
				throw new InvalidOperationException("Unknown firefight cutscene binding");
			}
			JObject jObject = (JObject)spec["director"];
			_maximum = jObject.Value<int>("_maxConcurrentSequencesTotal");
			_interval = jObject.Value<float>("_updateIntervalSec");
			if (_maximum < 1 || _interval <= 0f)
			{
				throw new InvalidOperationException("Invalid firefight director timing");
			}
			Dictionary<string, Profile> profiles = new Dictionary<string, Profile>();
			foreach (KeyValuePair<string, JToken> item in (JObject)spec["profiles"])
			{
				JObject jObject2 = (JObject)item.Value;
				JObject jObject3 = (JObject)spec["firearms"][jObject2.Value<string>("AudioPreset")];
				profiles.Add(item.Key, new Profile
				{
					Singles = Banks(jObject3["SingleShots"]),
					Grenades = Banks(jObject3["GrenadeBanks"]),
					Loops = ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, LoopSet>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, LoopSet>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)jObject3["LoopTailSets"]), (Func<JToken, LoopSet>)((JToken t) => new LoopSet
					{
						Loop = terminalFirefightDirector.Bank(t["Loop"]),
						Tail = terminalFirefightDirector.Bank(t["Tail"]),
						Rpm = t.Value<float>("RPM")
					}))),
					Burst = Range(jObject2, "BurstSizeRange"),
					Volume = Range(jObject2, "VolumeJitter"),
					Pitch = Range(jObject2, "PitchJitter"),
					StartJitter = Range(jObject2, "SingleShotStartJitterSec"),
					SingleGap = Range(jObject2, "SingleShotGapRangeSec"),
					BurstGap = Range(jObject2, "BurstSequenceGapRangeSec"),
					AutoBias = jObject2.Value<float>("AutoFireBias")
				});
			}
			Dictionary<string, Preset> presets = new Dictionary<string, Preset>();
			foreach (KeyValuePair<string, JToken> item2 in (JObject)spec["presets"])
			{
				JObject jObject4 = (JObject)item2.Value;
				Keyframe[] keys = ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, Keyframe>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, Keyframe>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)jObject4["DensityCurve"]["m_Curve"]), (Func<JToken, Keyframe>)((JToken t) => new Keyframe(t.Value<float>("time"), t.Value<float>("value"), t.Value<float>("inSlope"), t.Value<float>("outSlope"), t.Value<float>("inWeight"), t.Value<float>("outWeight"))
				{
					weightedMode = (WeightedMode)t.Value<int>("weightedMode")
				})));
				presets.Add(item2.Key, new Preset
				{
					Profiles = ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, Profile>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, Profile>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)jObject4["WeaponProfiles"]), (Func<JToken, Profile>)((JToken t) => profiles[t.Value<string>()]))),
					Curve = new AnimationCurve(keys),
					Period = jObject4.Value<float>("DensityPeriodSec"),
					Density = jObject4.Value<float>("BaseDensity"),
					Grenades = jObject4.Value<float>("GrenadeEventsPerShooterPerMinuteAtMaxDensity"),
					Initial = Range(jObject4, "InitialCooldownRangeSec"),
					CdZero = jObject4.Value<float>("BaseCooldownAtZeroDensitySec"),
					CdMax = jObject4.Value<float>("BaseCooldownAtMaxDensitySec"),
					Jitter = Range(jObject4, "CooldownJitterRangeSec"),
					NoProfile = jObject4.Value<float>("NoProfileRetryCooldownSec"),
					BackoffZero = jObject4.Value<float>("BackoffAtZeroDensitySec"),
					BackoffMax = jObject4.Value<float>("BackoffAtMaxDensitySec"),
					Respond = (jObject4.Value<int>("EnableCallAndResponse") != 0),
					ResponseProbability = jObject4.Value<float>("ResponseProbability"),
					ResponseDelay = Range(jObject4, "ResponseDelayRange"),
					ResponseShots = Range(jObject4, "ResponseShotsRange"),
					ResponseCooldown = Range(jObject4, "ResponseCooldownRange")
				});
			}
			_areas = ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, Area>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, Area>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)spec["areas"]), (Func<JToken, Area>)((JToken t) =>
			{
				string text = t.Value<string>("path");
				if (!paths.TryGetValue(text, out var value) || value.Count != 1 || !value[0])
				{
					throw new InvalidOperationException("Missing/ambiguous firefight area: " + text);
				}
				JObject jObject5 = (JObject)t["fields"];
				return new Area
				{
					Transform = value[0],
					Preset = presets[jObject5.Value<string>("AudioPreset")],
					Radius = jObject5.Value<float>("Radius"),
					LocalDensity = jObject5.Value<float>("LocalDensity"),
					Sector = (jObject5.Value<int>("UseSector") != 0),
					Aperture = jObject5.Value<float>("SectorApertureDeg"),
					ForwardOffset = Range(jObject5, "ForwardOffsetPercentRange")
				};
			})));
			_density = new float[_areas.Length];
			_slots = new int[_areas.Length];
		}
		catch
		{
			_assets.Dispose();
			throw;
		}
	}

	private SoundBank Bank(JToken key)
	{
		if (key != null && key.Type != JTokenType.Null)
		{
			return _assets.Banks[key.Value<string>()];
		}
		return null;
	}

	private SoundBank[] Banks(JToken array)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		return ValueEnumerableExtensions.ToArray<FromEnumerable<JToken>, JToken, SoundBank>(ValueEnumerableExtensions.Select<FromEnumerable<JToken>, JToken, SoundBank>(ValueEnumerable.AsValueEnumerable<JToken>((IEnumerable<JToken>)(JArray)array), (Func<JToken, SoundBank>)Bank));
	}

	private static Vector2 Range(JObject obj, string name)
	{
		return new Vector2(obj[name].Value<float>("x"), obj[name].Value<float>("y"));
	}

	private static float Sample(Vector2 range)
	{
		return UnityEngine.Random.Range(range.x, range.y);
	}

	private static float Gap(Vector2 range)
	{
		float num = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
		return UnityEngine.Random.Range(num, Mathf.Max(num, Mathf.Max(range.x, range.y)));
	}

	private static T Pick<T>(T[] values) where T : class
	{
		if (values.Length != 0)
		{
			return values[UnityEngine.Random.Range(0, values.Length)];
		}
		return null;
	}

	private static int Burst(Vector2 range)
	{
		int num = TerminalFirefightTiming.BurstMinimum(range.x);
		return UnityEngine.Random.Range(num, TerminalFirefightTiming.BurstMaximum(range.y, num) + 1);
	}

	internal void LogContentReadiness()
	{
		Plugin.Log.LogInfo("[Firefight] " + ReadinessSummary);
	}

	internal void Tick(float realtime, bool active)
	{
		if (_disposed)
		{
			return;
		}
		try
		{
			TickCore(realtime, active);
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[Firefight] playback failed; releasing sources for this raid: {arg}");
			Dispose();
		}
	}

	private void TickCore(float realtime, bool active)
	{
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		if (_disposed)
		{
			return;
		}
		if (!active)
		{
			if (_running)
			{
				StopAll();
			}
		}
		else
		{
			if (!CameraManager.Exist || !_playback.Ready)
			{
				return;
			}
			if (!_mixerReported)
			{
				_mixerReported = true;
				Plugin.Log.LogInfo("[Firefight] native sources routed through the settings-controlled firefight mixer");
			}
			if (!_running)
			{
				_running = true;
				Area[] areas = _areas;
				foreach (Area obj in areas)
				{
					obj.Cooldown = Sample(obj.Preset.Initial);
				}
				_nextTick = realtime;
				Plugin.Log.LogInfo("[Firefight] retail scenario started at attack-cutscene start");
			}
			TickSingles(realtime);
			if (!(realtime < _nextTick))
			{
				_nextTick = realtime + _interval;
				double dspTime = AudioSettings.dspTime;
				for (int j = 0; j < _areas.Length; j++)
				{
					Preset preset = _areas[j].Preset;
					_density[j] = TerminalFirefightTiming.Density(preset.Curve.Evaluate(TerminalFirefightTiming.Phase(dspTime, preset.Period)), preset.Period, preset.Density, _areas[j].LocalDensity);
				}
				TerminalFirefightTiming.Distribute(_density, ValueEnumerableExtensions.Sum<FromArray<float>, float>(ValueEnumerable.AsValueEnumerable<float>(_density)), _maximum, _roundRobin, _slots);
				_roundRobin = (_roundRobin + 1) % _areas.Length;
				for (int k = 0; k < _areas.Length; k++)
				{
					_areas[k].Slots = _slots[k];
				}
				for (int l = 0; l < _areas.Length; l++)
				{
					TickArea(_areas[l], _density[l], dspTime, realtime);
				}
			}
		}
	}

	private void TickArea(Area area, float density, double now, float realtime)
	{
		if (!area.Transform || density <= 0f)
		{
			return;
		}
		for (int num = area.Ends.Count - 1; num >= 0; num--)
		{
			if (now >= area.Ends[num])
			{
				area.Ends.RemoveAt(num);
			}
		}
		Preset preset = area.Preset;
		if (!area.Saturated && UnityEngine.Random.value < TerminalFirefightTiming.GrenadeProbability(preset.Grenades, density, _interval))
		{
			PlayGrenade(area, Pick(preset.Profiles), now);
		}
		now = AudioSettings.dspTime;
		if (now < area.BusyUntil)
		{
			return;
		}
		area.Cooldown -= _interval;
		if (area.Cooldown > 0f)
		{
			return;
		}
		Profile profile = Pick(preset.Profiles);
		if (profile == null)
		{
			area.Cooldown = preset.NoProfile;
			return;
		}
		int num2 = Burst(profile.Burst);
		Vector3 position = Position(area);
		if (area.Saturated)
		{
			area.Cooldown = TerminalFirefightTiming.Backoff(density, preset.BackoffZero, preset.BackoffMax);
			return;
		}
		Sequence(area, profile, num2, position, 0.0, now, realtime);
		area.Cooldown = TerminalFirefightTiming.Cooldown(density, preset.CdZero, preset.CdMax, Gap((num2 > 1) ? profile.BurstGap : profile.SingleGap), Sample(preset.Jitter));
		if (!preset.Respond)
		{
			return;
		}
		Area[] areas = _areas;
		foreach (Area area2 in areas)
		{
			if (area2 != area)
			{
				Respond(area2, now, realtime);
			}
		}
	}

	private void Respond(Area area, double now, float realtime)
	{
		Preset preset = area.Preset;
		if ((bool)area.Transform && preset.Respond && !(UnityEngine.Random.value > preset.ResponseProbability) && !(now < area.NextResponse) && !(now < area.BusyUntil) && !area.Saturated)
		{
			int shots = UnityEngine.Random.Range((int)preset.ResponseShots.x, (int)preset.ResponseShots.y);
			Profile profile = Pick(preset.Profiles);
			if (profile != null)
			{
				Sequence(area, profile, shots, Position(area), Sample(preset.ResponseDelay), now, realtime);
				float num = Mathf.Max(0.01f, Sample(preset.ResponseCooldown));
				area.NextResponse = now + (double)num;
				area.Cooldown = Mathf.Max(area.Cooldown, num);
			}
		}
	}

	private void Sequence(Area area, Profile profile, int shots, Vector3 position, double delay, double now, float realtime)
	{
		bool flag = profile.Singles.Length != 0;
		if (shots > 1 && profile.Loops.Length != 0 && (!flag || UnityEngine.Random.value < Mathf.Clamp01(profile.AutoBias)))
		{
			LoopSet loopSet = Pick(profile.Loops);
			float distance = CameraManager.Instance.Distance(position);
			TerminalFirefightPlayback.Selection selection = _playback.Pick(loopSet.Loop, distance);
			if (selection == null)
			{
				return;
			}
			float num = TerminalFirefightTiming.Beat(selection.Length, loopSet.Rpm);
			if (num <= 0f)
			{
				return;
			}
			double num2 = now + Math.Max(0.0, delay);
			double num3 = num2 + (double)Mathf.Max(num, (float)shots * num);
			if (_playback.Play(selection, position, num2, num3, 1f, 1f, loop: true, distance))
			{
				TerminalFirefightPlayback.Selection selection2 = _playback.Pick(loopSet.Tail, distance);
				if (selection2 != null)
				{
					_playback.Play(selection2, position, num3, num3 + (double)selection2.Length, 1f, 1f, loop: false, distance);
				}
				ReportEnd(area, num3);
			}
		}
		else if (flag)
		{
			SinglesJob singlesJob = new SinglesJob
			{
				Area = area,
				Profile = profile,
				Bank = Pick(profile.Singles),
				Position = position,
				Distance = CameraManager.Instance.Distance(position),
				Volume = Sample(profile.Volume),
				Remaining = Mathf.Max(1, shots),
				Due = realtime + (float)Math.Max(0.0, delay + (double)Sample(profile.StartJitter))
			};
			_singles.Add(singlesJob);
			if (singlesJob.Due <= realtime)
			{
				PlaySingle(singlesJob, realtime);
			}
		}
	}

	private void TickSingles(float realtime)
	{
		for (int num = _singles.Count - 1; num >= 0; num--)
		{
			SinglesJob singlesJob = _singles[num];
			if (singlesJob.Remaining > 0 && singlesJob.Due <= realtime)
			{
				PlaySingle(singlesJob, realtime);
			}
			if (singlesJob.Remaining <= 0 || !singlesJob.Area.Transform)
			{
				_singles.RemoveAt(num);
			}
		}
	}

	private void PlaySingle(SinglesJob job, float realtime)
	{
		TerminalFirefightPlayback.Selection selection = _playback.Pick(job.Bank, job.Distance);
		double dspTime = AudioSettings.dspTime;
		if (selection != null && _playback.Play(selection, job.Position, dspTime, dspTime + (double)Mathf.Max(0.01f, selection.Length), job.Volume, Sample(job.Profile.Pitch), loop: false, job.Distance, scheduled: false))
		{
			job.Area.BusyUntil = Math.Max(job.Area.BusyUntil, dspTime + (double)Mathf.Max(0.01f, selection.Length));
		}
		job.Remaining--;
		if (job.Remaining > 0)
		{
			job.Due = realtime + Gap(job.Profile.SingleGap);
		}
	}

	private void PlayGrenade(Area area, Profile profile, double now)
	{
		if (profile == null)
		{
			return;
		}
		SoundBank soundBank = Pick(profile.Grenades);
		if (!soundBank)
		{
			return;
		}
		Vector3 position = Position(area);
		float distance = CameraManager.Instance.Distance(position);
		TerminalFirefightPlayback.Selection selection = _playback.Pick(soundBank, distance);
		if (selection != null)
		{
			double end = now + (double)selection.Length;
			if (_playback.Play(selection, position, now, end, soundBank.RandomVolume, soundBank.RandomPitch, loop: false, distance, scheduled: false, stereo: true))
			{
				ReportEnd(area, end);
			}
		}
	}

	private static void ReportEnd(Area area, double end)
	{
		area.Ends.Add(end);
		area.BusyUntil = Math.Max(area.BusyUntil, end);
	}

	private static Vector3 Position(Area area)
	{
		Vector3 position = area.Transform.position;
		if (!area.Sector)
		{
			Vector2 vector = UnityEngine.Random.insideUnitCircle * area.Radius;
			return position + new Vector3(vector.x, 0f, vector.y);
		}
		Vector3 forward = area.Transform.forward;
		forward.y = 0f;
		forward = ((forward.magnitude > 1E-05f) ? forward.normalized : Vector3.forward);
		position += forward * (Mathf.Clamp01(Sample(area.ForwardOffset)) * area.Radius);
		float num = Mathf.Clamp(area.Aperture, 0f, 360f) * 0.5f;
		Vector3 a = Quaternion.AngleAxis(0f - num, Vector3.up) * forward;
		Vector3 b = Quaternion.AngleAxis(num, Vector3.up) * forward;
		Vector3 normalized = Vector3.Lerp(a, b, UnityEngine.Random.value).normalized;
		return position + normalized * (UnityEngine.Random.value * area.Radius);
	}

	internal void StopAll()
	{
		_playback.StopAll();
		_singles.Clear();
		Area[] areas = _areas;
		foreach (Area obj in areas)
		{
			obj.Ends.Clear();
			obj.BusyUntil = (obj.NextResponse = 0.0);
		}
		_roundRobin = 0;
		_running = false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			StopAll();
			_assets.Dispose();
			_disposed = true;
		}
	}
}
