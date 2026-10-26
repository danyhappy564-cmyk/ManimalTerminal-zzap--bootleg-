using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using EFT;
using EFT.EnvironmentEffect;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal sealed class TerminalRainBed : MonoBehaviour
{
	private sealed class BedState
	{
		internal AudioSource Current;

		internal AudioSource Mix;

		internal RainController.ERainIntensity Intensity;

		internal RainController.ERainIntensity PendingIntensity;

		internal Coroutine Transition;

		internal float TargetVolume;
	}

	private static TerminalRainBed _instance;

	private AudioSource _outdoorA;

	private AudioSource _outdoorB;

	private AudioSource _indoorA;

	private AudioSource _indoorB;

	private BedState _outdoor;

	private BedState _indoor;

	private AudioClip _outdoorWeak;

	private AudioClip _outdoorMedium;

	private AudioClip _outdoorStrong;

	private AudioClip _indoorWeak;

	private AudioClip _indoorMedium;

	private AudioClip _indoorStrong;

	private readonly HashSet<AudioClip> _ownedClips = new HashSet<AudioClip>();

	private TerminalRainAudio.RainBlendProfile _outdoorProfile = new TerminalRainAudio.RainBlendProfile();

	private TerminalRainAudio.RainBlendProfile _indoorProfile = new TerminalRainAudio.RainBlendProfile();

	private Coroutine _loadRoutine;

	private UnityWebRequest _loadingRequest;

	private bool _realCatalog;

	private bool _emergencyCatalog;

	private bool _routingWarningLogged;

	private bool _reportedLive;

	private bool _selectorInitialised;

	private bool _resolvedIndoors;

	private bool _candidateIndoors;

	private float _candidateSince;

	private AmbientAudioSystem _ambientSystem;

	internal static bool Active
	{
		get
		{
			if ((bool)_instance)
			{
				if (!_instance._realCatalog)
				{
					return _instance._emergencyCatalog;
				}
				return true;
			}
			return false;
		}
	}

	internal static void ResetForRaid()
	{
		TerminalRainBed instance = _instance;
		_instance = null;
		if ((bool)instance)
		{
			instance.CleanupAndDestroy();
		}
	}

	internal static void Ensure(PrecipitationAmbientBlender[] blenders)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		if (!_instance)
		{
			GameObject gameObject = new GameObject("Terminal_RainAudioFallback");
			Scene sceneByName = SceneManager.GetSceneByName("Terminal_Scripts");
			if (sceneByName.IsValid() && sceneByName.isLoaded)
			{
				SceneManager.MoveGameObjectToScene(gameObject, sceneByName);
			}
			_instance = gameObject.AddComponent<TerminalRainBed>();
		}
		_instance.Configure(blenders);
	}

	private void Awake()
	{
		_outdoorA = MakeSource("OutdoorRainA");
		_outdoorB = MakeSource("OutdoorRainB");
		_indoorA = MakeSource("IndoorRainA");
		_indoorB = MakeSource("IndoorRainB");
		_outdoor = new BedState
		{
			Current = _outdoorA,
			Mix = _outdoorB
		};
		_indoor = new BedState
		{
			Current = _indoorA,
			Mix = _indoorB
		};
		_loadRoutine = StartCoroutine(LoadCatalog());
	}

	private void Configure(PrecipitationAmbientBlender[] blenders)
	{
		_outdoorProfile = TerminalRainAudio.ReadBlendProfile(blenders, EnvironmentType.Outdoor);
		_indoorProfile = TerminalRainAudio.ReadBlendProfile(blenders, EnvironmentType.Indoor);
	}

	private AudioSource MakeSource(string sourceName)
	{
		GameObject gameObject = new GameObject(sourceName);
		gameObject.transform.SetParent(transform, worldPositionStays: false);
		AudioSource audioSource = gameObject.AddComponent<AudioSource>();
		audioSource.playOnAwake = false;
		audioSource.loop = true;
		audioSource.spatialBlend = 0f;
		audioSource.volume = 0f;
		TryRoute(audioSource);
		return audioSource;
	}

	private IEnumerator LoadCatalog()
	{
		yield return LoadClip("amb_rain_outdoor_weak.wav", "amb_rain_outdoor_weak", (AudioClip clip) =>
		{
			_outdoorWeak = clip;
		});
		yield return LoadClip("amb_rain_outdoor_medium.wav", "amb_rain_outdoor_medium", (AudioClip clip) =>
		{
			_outdoorMedium = clip;
		});
		yield return LoadClip("amb_rain_outdoor_strong.wav", "amb_rain_outdoor_strong", (AudioClip clip) =>
		{
			_outdoorStrong = clip;
		});
		yield return LoadClip("amb_rain_indoor_weak.wav", "amb_rain_indoor_weak", (AudioClip clip) =>
		{
			_indoorWeak = clip;
		});
		yield return LoadClip("amb_rain_indoor_medium.wav", "amb_rain_indoor_medium", (AudioClip clip) =>
		{
			_indoorMedium = clip;
		});
		yield return LoadClip("amb_rain_indoor_strong.wav", "amb_rain_indoor_strong", (AudioClip clip) =>
		{
			_indoorStrong = clip;
		});
		_realCatalog = (bool)_outdoorWeak && (bool)_outdoorMedium && (bool)_outdoorStrong && (bool)_indoorWeak && (bool)_indoorMedium && (bool)_indoorStrong;
		_emergencyCatalog = !_realCatalog && (bool)_outdoorStrong && (bool)_indoorStrong;
		if (_realCatalog)
		{
			Plugin.Log.LogInfo("[RainAudio] recovered retail precipitation catalog ready: six tiers routed through AmbientOut");
		}
		else if (_emergencyCatalog)
		{
			Plugin.Log.LogWarning("[RainAudio] recovered precipitation catalog incomplete; using explicit two-strong-clip emergency");
		}
		else
		{
			Plugin.Log.LogWarning("[RainAudio] precipitation catalog unavailable; rain bed remains silent");
		}
	}

	private IEnumerator LoadClip(string fileName, string clipName, Action<AudioClip> assign)
	{
		string text = Path.Combine(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "audio"), fileName);
		if (!File.Exists(text))
		{
			Plugin.Log.LogWarning("[RainAudio] recovered clip missing: " + text);
			yield break;
		}
		UnityWebRequest request = (_loadingRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(text).AbsoluteUri, AudioType.WAV));
		try
		{
			yield return request.SendWebRequest();
			if (request.result != UnityWebRequest.Result.Success)
			{
				Plugin.Log.LogWarning("[RainAudio] failed loading recovered clip '" + fileName + "': " + request.error);
				yield break;
			}
			AudioClip content = DownloadHandlerAudioClip.GetContent(request);
			if ((bool)content)
			{
				content.name = clipName;
				_ownedClips.Add(content);
				assign(content);
			}
		}
		finally
		{
			TerminalRainBed terminalRainBed = this;
			if (terminalRainBed._loadingRequest == request)
			{
				terminalRainBed._loadingRequest = null;
				request.Dispose();
			}
		}
	}

	private void Update()
	{
		if (!TerminalGate.On || (!_realCatalog && !_emergencyCatalog) || !TryRouteAll())
		{
			return;
		}
		RainController.ERainIntensity intensityType = RainController.IntensityType;
		AmbientAudioSystem ambient = (_ambientSystem = (MonoBehaviourSingleton<AmbientAudioSystem>.Instantiated ? MonoBehaviourSingleton<AmbientAudioSystem>.Instance : _ambientSystem));
		bool flag = TerminalRainAudio.IsWinter(ambient);
		bool flag2 = EnvironmentManager.Instance != null && EnvironmentManager.Instance.Environment == EnvironmentType.Indoor;
		bool isCameraUnderRain = RainController.IsCameraUnderRain;
		bool flag3 = flag2 || !isCameraUnderRain;
		if (!_selectorInitialised)
		{
			_selectorInitialised = true;
			_candidateIndoors = flag3;
			_resolvedIndoors = flag3;
			_candidateSince = Time.unscaledTime;
		}
		else if (_candidateIndoors != flag3)
		{
			_candidateIndoors = flag3;
			_candidateSince = Time.unscaledTime;
		}
		else if (_resolvedIndoors != flag3 && Time.unscaledTime - _candidateSince >= 0.25f)
		{
			_resolvedIndoors = flag3;
			if (intensityType != RainController.ERainIntensity.None)
			{
				Plugin.Log.LogInfo("[RainAudio] acoustic rain -> " + (_resolvedIndoors ? "Indoor" : "Outdoor") + " " + string.Format("(trigger={0}, cameraUnderRain={1})", flag2 ? "Indoor" : "Outdoor", isCameraUnderRain));
			}
		}
		bool resolvedIndoors = _resolvedIndoors;
		ESeasonStatus season = TerminalRainAudio.CurrentSeason(ambient);
		bool flag4 = TryGetRoomPrecipitationVolume(out var volume, out var useRange);
		float num = Mathf.Clamp01(RainController.Intensity);
		RainController.ERainIntensity eRainIntensity = ((!flag) ? intensityType : RainController.ERainIntensity.None);
		float num2 = 0f;
		float num3 = 0f;
		if (eRainIntensity != RainController.ERainIntensity.None)
		{
			num2 = TerminalRainAudio.GetRainMultiplier(season, EnvironmentType.Outdoor, eRainIntensity);
			num3 = TerminalRainAudio.GetRainMultiplier(season, EnvironmentType.Indoor, eRainIntensity);
			if (_realCatalog)
			{
				if (flag4)
				{
					num3 *= volume;
				}
			}
			else
			{
				num2 *= num;
				num3 *= num;
				if (flag4)
				{
					num3 *= volume;
				}
			}
		}
		ApplyBed(_outdoor, EnvironmentType.Outdoor, eRainIntensity, resolvedIndoors ? 0f : num2, useDynamicProfile: false, _outdoorProfile);
		ApplyBed(_indoor, EnvironmentType.Indoor, eRainIntensity, resolvedIndoors ? num3 : 0f, useRange, _indoorProfile);
		if (!_reportedLive && eRainIntensity != RainController.ERainIntensity.None)
		{
			_reportedLive = true;
			Plugin.Log.LogInfo($"[RainAudio] precipitation audible: tier={eRainIntensity}, catalog=" + (_realCatalog ? "retail" : "emergency") + ", environment=" + (resolvedIndoors ? "Indoor" : "Outdoor") + ", " + $"rain={num:0.00}, rainMultOut={num2:0.00}, rainMultIn={num3:0.00}");
		}
	}

	private void ApplyBed(BedState state, EnvironmentType environment, RainController.ERainIntensity intensity, float target, bool useDynamicProfile, TerminalRainAudio.RainBlendProfile profile)
	{
		if (state == null)
		{
			return;
		}
		state.TargetVolume = Mathf.Max(0f, target);
		if (intensity == RainController.ERainIntensity.None)
		{
			FadeStoppedBed(state, profile, useDynamicProfile);
			return;
		}
		AudioClip audioClip = ResolveClip(environment, intensity);
		if (!audioClip)
		{
			return;
		}
		if (state.Transition != null)
		{
			if (state.PendingIntensity != intensity)
			{
				StartTierCrossfade(state, audioClip, intensity, profile);
			}
			return;
		}
		if (state.Intensity != intensity || state.Current.clip != audioClip)
		{
			StartTierCrossfade(state, audioClip, intensity, profile);
			return;
		}
		if (!state.Current.isPlaying)
		{
			state.Current.Play();
		}
		float b = (useDynamicProfile ? profile.DynamicCrossfadeDuration : profile.CrossfadeDuration);
		float num = (useDynamicProfile ? profile.DynamicBlendSpeed : profile.BlendSpeed);
		float num2 = Mathf.Max(0.001f, num / Mathf.Max(0.001f, b));
		state.Current.volume = Mathf.MoveTowards(state.Current.volume, state.TargetVolume, Time.deltaTime * num2);
		if (state.Mix != state.Current)
		{
			state.Mix.volume = Mathf.MoveTowards(state.Mix.volume, 0f, Time.deltaTime * num2);
		}
	}

	private void FadeStoppedBed(BedState state, TerminalRainAudio.RainBlendProfile profile, bool dynamic)
	{
		if (state.Transition != null)
		{
			StopCoroutine(state.Transition);
			state.Transition = null;
			state.PendingIntensity = RainController.ERainIntensity.None;
			state.Mix.Stop();
			state.Mix.volume = 0f;
		}
		float b = (dynamic ? profile.DynamicCrossfadeDuration : profile.CrossfadeDuration);
		float num = (dynamic ? profile.DynamicBlendSpeed : profile.BlendSpeed);
		float num2 = Mathf.Max(0.001f, num / Mathf.Max(0.001f, b));
		state.Current.volume = Mathf.MoveTowards(state.Current.volume, 0f, Time.deltaTime * num2);
		if (state.Current.volume <= 0.0001f)
		{
			state.Current.Stop();
			state.Current.clip = null;
			state.Intensity = RainController.ERainIntensity.None;
		}
	}

	private void StartTierCrossfade(BedState state, AudioClip clip, RainController.ERainIntensity intensity, TerminalRainAudio.RainBlendProfile profile)
	{
		if (state.Transition != null)
		{
			StopCoroutine(state.Transition);
		}
		state.Transition = null;
		state.Mix.Stop();
		state.Mix.volume = 0f;
		if (state.Current.clip == null || state.Intensity == RainController.ERainIntensity.None)
		{
			state.Current.clip = clip;
			state.Current.volume = 0f;
			state.Current.Play();
			state.Intensity = intensity;
			state.PendingIntensity = RainController.ERainIntensity.None;
		}
		else
		{
			state.Intensity = intensity;
			state.PendingIntensity = intensity;
			state.Transition = StartCoroutine(Crossfade(state, clip, profile));
		}
	}

	private IEnumerator Crossfade(BedState state, AudioClip clip, TerminalRainAudio.RainBlendProfile profile)
	{
		AudioSource from = state.Current;
		AudioSource to = state.Mix;
		float fromVolume = from.volume;
		to.clip = clip;
		if (clip.length > 0f)
		{
			to.time = UnityEngine.Random.Range(0f, clip.length);
		}
		to.volume = 0f;
		to.Play();
		float duration = Mathf.Max(0.001f, profile.CrossfadeDuration);
		float speed = Mathf.Max(0.001f, profile.BlendSpeed);
		float elapsed = 0f;
		while (elapsed < duration)
		{
			float t = Mathf.Clamp01(elapsed / duration);
			from.volume = Mathf.Lerp(fromVolume, 0f, t);
			to.volume = Mathf.Lerp(0f, state.TargetVolume, t);
			elapsed += Time.deltaTime * speed;
			yield return null;
		}
		from.volume = 0f;
		from.Stop();
		to.volume = state.TargetVolume;
		state.Current = to;
		state.Mix = from;
		state.PendingIntensity = RainController.ERainIntensity.None;
		state.Transition = null;
	}

	private AudioClip ResolveClip(EnvironmentType environment, RainController.ERainIntensity intensity)
	{
		if (_emergencyCatalog)
		{
			if (environment != EnvironmentType.Indoor)
			{
				return _outdoorStrong;
			}
			return _indoorStrong;
		}
		if (environment == EnvironmentType.Indoor)
		{
			switch (intensity)
			{
			case RainController.ERainIntensity.Low:
				return _indoorWeak;
			case RainController.ERainIntensity.Med:
				return _indoorMedium;
			case RainController.ERainIntensity.High:
				return _indoorStrong;
			}
		}
		else
		{
			switch (intensity)
			{
			case RainController.ERainIntensity.Low:
				return _outdoorWeak;
			case RainController.ERainIntensity.Med:
				return _outdoorMedium;
			case RainController.ERainIntensity.High:
				return _outdoorStrong;
			}
		}
		return null;
	}

	private bool TryGetRoomPrecipitationVolume(out float volume, out bool useRange)
	{
		volume = 1f;
		useRange = false;
		try
		{
			if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated || !SpatialAudioSystem.Initialized)
			{
				return false;
			}
			ISpatialAudioRoom spatialAudioRoom = MonoBehaviourSingleton<SpatialAudioSystem>.Instance?.ListenerCurrentRoom;
			if (spatialAudioRoom == null || spatialAudioRoom.IsOutdoor || spatialAudioRoom.RoomAmbientData == null)
			{
				return false;
			}
			RoomAmbientData roomAmbientData = spatialAudioRoom.RoomAmbientData;
			if (roomAmbientData.UseVolumeRange)
			{
				Transform listenerTransform = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ListenerTransform;
				Bounds bounds = spatialAudioRoom.Bounds;
				float t = 1f;
				if ((bool)listenerTransform && Mathf.Abs(bounds.size.y) > Mathf.Epsilon)
				{
					t = AudioUtils.CalculateHeightPercentageRelativeToBounds(listenerTransform.position, in bounds) / 100f;
				}
				volume = Mathf.Lerp(roomAmbientData.PrecipitationVolumeRange.x, roomAmbientData.PrecipitationVolumeRange.y, t);
				useRange = true;
			}
			else
			{
				volume = roomAmbientData.PrecipitationVolume;
			}
			if (float.IsNaN(volume) || float.IsInfinity(volume))
			{
				volume = 1f;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private bool TryRouteAll()
	{
		bool num = TryRoute(_outdoorA) & TryRoute(_outdoorB) & TryRoute(_indoorA) & TryRoute(_indoorB);
		if (!num && !_routingWarningLogged)
		{
			_routingWarningLogged = true;
			Plugin.Log.LogWarning("[RainAudio] AmbientOut mixer is unavailable; recovered rain sources are muted");
		}
		return num;
	}

	private static bool TryRoute(AudioSource source)
	{
		if (!source)
		{
			return false;
		}
		if ((bool)source.outputAudioMixerGroup)
		{
			source.mute = false;
			return true;
		}
		if (TerminalAudioRouting.Route(source))
		{
			source.mute = false;
			return true;
		}
		source.mute = true;
		return false;
	}

	private void CleanupAndDestroy()
	{
		CancelClipLoad();
		StopAllCoroutines();
		_loadRoutine = null;
		StopBed(_outdoor);
		StopBed(_indoor);
		foreach (AudioClip ownedClip in _ownedClips)
		{
			if ((bool)ownedClip)
			{
				UnityEngine.Object.Destroy(ownedClip);
			}
		}
		_ownedClips.Clear();
		if ((bool)gameObject)
		{
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	private void OnDestroy()
	{
		CancelClipLoad();
		if (_instance == this)
		{
			_instance = null;
		}
		_loadRoutine = null;
		StopBed(_outdoor);
		StopBed(_indoor);
		foreach (AudioClip ownedClip in _ownedClips)
		{
			if ((bool)ownedClip)
			{
				UnityEngine.Object.Destroy(ownedClip);
			}
		}
		_ownedClips.Clear();
	}

	private void CancelClipLoad()
	{
		UnityWebRequest loadingRequest = _loadingRequest;
		_loadingRequest = null;
		if (loadingRequest == null)
		{
			return;
		}
		try
		{
			if (!loadingRequest.isDone)
			{
				loadingRequest.Abort();
			}
		}
		finally
		{
			loadingRequest.Dispose();
		}
	}

	private static void StopBed(BedState state)
	{
		if (state != null)
		{
			state.Transition = null;
			if ((bool)state.Current)
			{
				state.Current.Stop();
				state.Current.clip = null;
				state.Current.volume = 0f;
			}
			if ((bool)state.Mix)
			{
				state.Mix.Stop();
				state.Mix.clip = null;
				state.Mix.volume = 0f;
			}
		}
	}
}
