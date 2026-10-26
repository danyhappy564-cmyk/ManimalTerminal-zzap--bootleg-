using System;
using System.Collections.Generic;
using System.Reflection;
using Audio.AmbientSubsystem;
using Audio.AmbientSubsystem.Data;
using Comfort.Common;
using EFT;
using EFT.Settings.Sound;
using EFT.Weather;
using UnityEngine;
using UnityEngine.Audio;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalRainAudio
{
	internal sealed class RainBlendProfile
	{
		internal float CrossfadeDuration = 1f;

		internal float BlendSpeed = 1f;

		internal float DynamicCrossfadeDuration = 1f;

		internal float DynamicBlendSpeed = 1f;

		internal float MaxVolumeCheckDelta = 0.05f;
	}

	private static bool _nativeActive;

	private static float _nextStageProbeAt;

	private static PrecipitationAmbientBlender[] _raidBlenders = new PrecipitationAmbientBlender[0];

	private static readonly Dictionary<AudioSource, bool> NativeMuteStates = new Dictionary<AudioSource, bool>();

	private const BindingFlags NativeFields = BindingFlags.Instance | BindingFlags.NonPublic;

	private static readonly FieldInfo SoundBlendersField = typeof(AmbientAudioSystem).GetField("_soundBlenders", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo DisposedField = typeof(AmbientAudioSystem).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo InitializedField = typeof(PrecipitationAmbientBlender).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo AmbientDataField = typeof(PrecipitationAmbientBlender).GetField("_ambientSoundData", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly ESeasonStatus[] WarmSeasons = new ESeasonStatus[5]
	{
		ESeasonStatus.Summer,
		ESeasonStatus.Autumn,
		ESeasonStatus.Spring,
		ESeasonStatus.AutumnLate,
		ESeasonStatus.SpringEarly
	};

	private static readonly RainController.ERainIntensity[] RainTiers = new RainController.ERainIntensity[3]
	{
		RainController.ERainIntensity.Low,
		RainController.ERainIntensity.Med,
		RainController.ERainIntensity.High
	};

	internal static void ResetForRaid()
	{
		_nativeActive = false;
		_nextStageProbeAt = 0f;
		_raidBlenders = new PrecipitationAmbientBlender[0];
		RestoreNativeSources();
		TerminalRainBed.ResetForRaid();
	}

	internal static void TryStage()
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		if (!TerminalGate.On || Time.unscaledTime < _nextStageProbeAt)
		{
			return;
		}
		_nextStageProbeAt = Time.unscaledTime + 1f;
		AmbientAudioSystem ambientAudioSystem = (MonoBehaviourSingleton<AmbientAudioSystem>.Instantiated ? MonoBehaviourSingleton<AmbientAudioSystem>.Instance : null);
		if (_raidBlenders.Length == 0 || ValueEnumerableExtensions.Any<PrecipitationAmbientBlender>(ValueEnumerable.AsValueEnumerable<PrecipitationAmbientBlender>(_raidBlenders), (Func<PrecipitationAmbientBlender, bool>)((PrecipitationAmbientBlender blender) => !blender)))
		{
			_raidBlenders = UnityEngine.Object.FindObjectsOfType<PrecipitationAmbientBlender>(includeInactive: true);
		}
		PrecipitationAmbientBlender[] raidBlenders = _raidBlenders;
		if (ambientAudioSystem == null && raidBlenders.Length == 0)
		{
			return;
		}
		if (ambientAudioSystem != null && ambientAudioSystem.Initialized)
		{
			bool flag = false;
			try
			{
				flag = TryActivateNative(ambientAudioSystem, raidBlenders);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[RainAudio] native catalog activation failed; using recovered catalog: " + ex.Message);
			}
			if (flag)
			{
				_nativeActive = true;
				RestoreNativeSources();
				TerminalRainBed.ResetForRaid();
				return;
			}
		}
		_nativeActive = false;
		MuteNativeSources(raidBlenders);
		TerminalRainBed.Ensure(raidBlenders);
	}

	private static bool TryActivateNative(AmbientAudioSystem sys, PrecipitationAmbientBlender[] blenders)
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		if (sys == null || !sys.Initialized || !sys.isActiveAndEnabled || DisposedField == null || (bool)DisposedField.GetValue(sys) || blenders == null || blenders.Length == 0)
		{
			return false;
		}
		if (ValueEnumerableExtensions.Any<PrecipitationAmbientBlender>(ValueEnumerable.AsValueEnumerable<PrecipitationAmbientBlender>(blenders), (Func<PrecipitationAmbientBlender, bool>)((PrecipitationAmbientBlender precipitationAmbientBlender) => !precipitationAmbientBlender)) || !HasFullCatalog(sys.AmbientSoundData))
		{
			return false;
		}
		ISoundBlender[] array = SoundBlendersField?.GetValue(sys) as ISoundBlender[];
		if (InitializedField == null || AmbientDataField == null || array == null)
		{
			return false;
		}
		AudioMixer masterMixer = SoundSettingsController.MasterMixer;
		if (!masterMixer)
		{
			return false;
		}
		AudioMixerGroup audioMixerGroup = null;
		AudioMixerGroup audioMixerGroup2 = null;
		foreach (PrecipitationAmbientBlender blender in blenders)
		{
			if (!ValueEnumerableExtensions.Any<ISoundBlender>(ValueEnumerable.AsValueEnumerable<ISoundBlender>(array), (Func<ISoundBlender, bool>)((ISoundBlender item) => item == blender)) || !blender.isActiveAndEnabled || !(bool)InitializedField.GetValue(blender) || AmbientDataField.GetValue(blender) != sys.AmbientSoundData || !blender._outputMixerGroup || blender._outputMixerGroup.audioMixer != masterMixer || !NativeSourceReady(blender._precipitationSource, blender._outputMixerGroup) || !NativeSourceReady(blender._precipitationMixSource, blender._outputMixerGroup))
			{
				return false;
			}
			if (RainController.IntensityType != RainController.ERainIntensity.None && (!blender._precipitationSource.isPlaying || !blender._precipitationSource.clip) && (!blender._precipitationMixSource.isPlaying || !blender._precipitationMixSource.clip))
			{
				return false;
			}
			if (blender._environment == EnvironmentType.Indoor)
			{
				audioMixerGroup = blender._outputMixerGroup;
			}
			else if (blender._environment == EnvironmentType.Outdoor)
			{
				audioMixerGroup2 = blender._outputMixerGroup;
			}
		}
		if (!audioMixerGroup || !audioMixerGroup2 || audioMixerGroup == audioMixerGroup2)
		{
			return false;
		}
		if (!_nativeActive)
		{
			Plugin.Log.LogInfo($"[RainAudio] healthy native precipitation catalog active: {blenders.Length} blender(s), " + "indoor='" + audioMixerGroup.name + "', outdoor='" + audioMixerGroup2.name + "'");
		}
		return true;
	}

	private static bool NativeSourceReady(AudioSource source, AudioMixerGroup group)
	{
		bool value;
		if ((bool)source && source.isActiveAndEnabled && source.outputAudioMixerGroup == group)
		{
			return !(NativeMuteStates.TryGetValue(source, out value) ? value : source.mute);
		}
		return false;
	}

	private static bool HasFullCatalog(SeasonAmbientSoundDataSO data)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		if (!data)
		{
			return false;
		}
		try
		{
			return ValueEnumerableExtensions.All<ESeasonStatus>(ValueEnumerable.AsValueEnumerable<ESeasonStatus>(WarmSeasons), (Func<ESeasonStatus, bool>)((ESeasonStatus season) =>
			{
				//IL_0019: Unknown result type (might be due to invalid IL or missing references)
				return ValueEnumerableExtensions.All<RainController.ERainIntensity>(ValueEnumerable.AsValueEnumerable<RainController.ERainIntensity>(RainTiers), (Func<RainController.ERainIntensity, bool>)((RainController.ERainIntensity tier) => data.TryGetPrecipitationClip(season, tier, EnvironmentType.Indoor, out var clip) && (bool)clip && data.TryGetPrecipitationClip(season, tier, EnvironmentType.Outdoor, out var clip2) && (bool)clip2));
			}));
		}
		catch
		{
			return false;
		}
	}

	private static void MuteNativeSources(PrecipitationAmbientBlender[] blenders)
	{
		if (blenders == null)
		{
			return;
		}
		foreach (PrecipitationAmbientBlender precipitationAmbientBlender in blenders)
		{
			if ((bool)precipitationAmbientBlender)
			{
				MuteNativeSource(precipitationAmbientBlender._precipitationSource);
				MuteNativeSource(precipitationAmbientBlender._precipitationMixSource);
			}
		}
	}

	private static void MuteNativeSource(AudioSource source)
	{
		if ((bool)source)
		{
			if (!NativeMuteStates.ContainsKey(source))
			{
				NativeMuteStates[source] = source.mute;
			}
			source.mute = true;
		}
	}

	private static void RestoreNativeSources()
	{
		foreach (KeyValuePair<AudioSource, bool> nativeMuteState in NativeMuteStates)
		{
			if ((bool)nativeMuteState.Key)
			{
				nativeMuteState.Key.mute = nativeMuteState.Value;
			}
		}
		NativeMuteStates.Clear();
	}

	internal static RainBlendProfile ReadBlendProfile(PrecipitationAmbientBlender[] blenders, EnvironmentType environment)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		RainBlendProfile rainBlendProfile = new RainBlendProfile();
		if (blenders == null || blenders.Length == 0)
		{
			return rainBlendProfile;
		}
		PrecipitationAmbientBlender precipitationAmbientBlender = ValueEnumerableExtensions.FirstOrDefault<FromArray<PrecipitationAmbientBlender>, PrecipitationAmbientBlender>(ValueEnumerable.AsValueEnumerable<PrecipitationAmbientBlender>(blenders), (Func<PrecipitationAmbientBlender, bool>)((PrecipitationAmbientBlender blender) => (bool)blender && blender._environment == environment));
		if ((bool)precipitationAmbientBlender)
		{
			ApplyBlendFields(rainBlendProfile, precipitationAmbientBlender);
			return rainBlendProfile;
		}
		PrecipitationAmbientBlender precipitationAmbientBlender2 = ValueEnumerableExtensions.FirstOrDefault<FromArray<PrecipitationAmbientBlender>, PrecipitationAmbientBlender>(ValueEnumerable.AsValueEnumerable<PrecipitationAmbientBlender>(blenders), (Func<PrecipitationAmbientBlender, bool>)((PrecipitationAmbientBlender blender) => blender));
		if ((bool)precipitationAmbientBlender2)
		{
			ApplyBlendFields(rainBlendProfile, precipitationAmbientBlender2);
		}
		return rainBlendProfile;
	}

	private static void ApplyBlendFields(RainBlendProfile profile, PrecipitationAmbientBlender blender)
	{
		profile.CrossfadeDuration = PositiveOrDefault(blender._crossfadeDuration, profile.CrossfadeDuration);
		profile.BlendSpeed = PositiveOrDefault(blender._defaultBlendSpeed, profile.BlendSpeed);
		profile.DynamicCrossfadeDuration = PositiveOrDefault(blender._dynamicCrossfadeDuration, profile.DynamicCrossfadeDuration);
		profile.DynamicBlendSpeed = PositiveOrDefault(blender._dynamicBlendSpeed, profile.DynamicBlendSpeed);
		profile.MaxVolumeCheckDelta = PositiveOrDefault(blender._maxVolumeCheckDelta, profile.MaxVolumeCheckDelta);
	}

	private static float PositiveOrDefault(float value, float fallback)
	{
		if (!(value > 0f) || float.IsNaN(value) || float.IsInfinity(value))
		{
			return fallback;
		}
		return value;
	}

	internal static ESeasonStatus CurrentSeason(AmbientAudioSystem ambient)
	{
		if (ambient != null && ambient.Initialized)
		{
			return ambient.CurrentSeasonStatus;
		}
		try
		{
			RainController.ERainControllerStatus? eRainControllerStatus = (WeatherController.Instance?.RainController)?.State?.Status;
			if (eRainControllerStatus.HasValue)
			{
				switch (eRainControllerStatus.GetValueOrDefault())
				{
				case RainController.ERainControllerStatus.Autumn:
					return ESeasonStatus.Autumn;
				case RainController.ERainControllerStatus.AutumnLate:
					return ESeasonStatus.AutumnLate;
				case RainController.ERainControllerStatus.Spring:
					return ESeasonStatus.Spring;
				case RainController.ERainControllerStatus.SpringEarly:
					return ESeasonStatus.SpringEarly;
				}
			}
		}
		catch
		{
		}
		return ESeasonStatus.Summer;
	}

	internal static bool IsWinter(AmbientAudioSystem ambient)
	{
		if (ambient != null && ambient.Initialized)
		{
			if (ambient.CurrentSeasonStatus != ESeasonStatus.Winter)
			{
				return ambient.CurrentSeasonStatus == ESeasonStatus.Storm;
			}
			return true;
		}
		try
		{
			RainController.ERainControllerStatus? eRainControllerStatus = WeatherController.Instance?.RainController?.State?.Status;
			return eRainControllerStatus == RainController.ERainControllerStatus.Winter || eRainControllerStatus == RainController.ERainControllerStatus.WinterStorm || eRainControllerStatus == RainController.ERainControllerStatus.WinterStormReconnect;
		}
		catch
		{
			return false;
		}
	}

	internal static float GetRainMultiplier(ESeasonStatus season, EnvironmentType environment, RainController.ERainIntensity intensity)
	{
		if (intensity == RainController.ERainIntensity.None)
		{
			return 0f;
		}
		try
		{
			if (!Singleton<ClientSettingsConfig>.Instantiated)
			{
				return 1f;
			}
			return (Singleton<ClientSettingsConfig>.Instance?.AudioSettings?.EnvironmentSettings)?.GetSeasonSettings(season)?.GetRainMult(environment, intensity) ?? 1f;
		}
		catch
		{
			return 1f;
		}
	}
}
