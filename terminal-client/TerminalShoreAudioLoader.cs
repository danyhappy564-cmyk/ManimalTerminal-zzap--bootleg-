using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Audio.AmbientSubsystem;
using BezierSplineTools;
using Comfort.Common;
using EFT;
using EFT.Settings;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace Manimal.Terminal;

[DefaultExecutionOrder(10000)]
internal sealed class TerminalShoreAudioLoader : MonoBehaviour
{
	private sealed class ShoreSource
	{
		internal AudioSource Source;

		internal AudioClip Clip;

		internal float AuthoredVolume;

		internal AudioSource NativeSource;

		internal float NextStartAttempt;

		internal int PreviousSample = -1;
	}

	private sealed class ShoreBranch
	{
		internal BezierSpline Spline;

		internal Vector3[] SamplePoints;

		internal readonly List<ShoreSource> Sources = new List<ShoreSource>();
	}

	private const int SplineSamples = 160;

	private readonly List<AudioClip> _ownedClips = new List<AudioClip>();

	private readonly List<ShoreBranch> _branches = new List<ShoreBranch>();

	private float _nextRoutingCheck;

	private AudioMixerGroup _mixer;

	private float _nextPlaybackDiagnostic;

	private bool _guardLogged;

	private bool _guardStateKnown;

	private bool _lastGuardAudible;

	private int _audioGeneration;

	private bool _reloadClips;

	private bool _loadingClips;

	private void Awake()
	{
		AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
	}

	private void OnAudioConfigurationChanged(bool deviceWasChanged)
	{
		_audioGeneration++;
		_reloadClips = true;
		TerminalShoreAudio.MarkReloading();
		Plugin.Log.LogInfo($"[ShoreAudio] audio engine reset; reloading shoreline WAVs (deviceChanged={deviceWasChanged})");
	}

	private IEnumerator Start()
	{
		yield return ReloadClips();
	}

	private IEnumerator ReloadClips()
	{
		_loadingClips = true;
		do
		{
			_reloadClips = false;
			int generation = _audioGeneration;
			yield return Load("amb_terminal_spline_sea_close_quite.wav", "amb_terminal_spline_sea_close_quite", generation);
			yield return Load("amb_terminal_spline_sea_distant_quite.wav", "amb_terminal_spline_sea_distant_quite", generation);
			yield return Load("amb_terminal_spline_sea_far_quite.wav", "amb_terminal_spline_sea_far_quite", generation);
		}
		while (_reloadClips);
		_loadingClips = false;
		TerminalShoreAudio.MarkReady();
		int count = _ownedClips.FindAll((AudioClip c) => (bool)c && c.samples > 0).Count;
		Plugin.Log.LogInfo($"[ShoreAudio] native shoreline assets ready: {count}/3 clip(s); " + "dedicated playback will follow the authored sea group membership");
	}

	private IEnumerator Load(string fileName, string clipName, int generation)
	{
		if (generation != _audioGeneration)
		{
			yield break;
		}
		string text = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "audio", fileName);
		if (!File.Exists(text))
		{
			Plugin.Log.LogWarning("[ShoreAudio] recovered shoreline clip missing: " + text);
			yield break;
		}
		using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(text).AbsoluteUri, AudioType.WAV);
		yield return request.SendWebRequest();
		if (request.result != UnityWebRequest.Result.Success)
		{
			Plugin.Log.LogWarning("[ShoreAudio] failed loading '" + fileName + "': " + request.error);
			yield break;
		}
		AudioClip content = DownloadHandlerAudioClip.GetContent(request);
		if (!content)
		{
			yield break;
		}
		if (generation != _audioGeneration || content.samples <= 0)
		{
			UnityEngine.Object.Destroy(content);
			yield break;
		}
		content.name = clipName;
		AudioClip audioClip = _ownedClips.Find((AudioClip c) => (bool)c && c.name == clipName);
		if ((bool)audioClip)
		{
			_ownedClips.Remove(audioClip);
		}
		_ownedClips.Add(content);
		TerminalAcoustics.RegisterRecoveredLoopClip(clipName, content, _branches.Count == 0);
		foreach (ShoreBranch branch in _branches)
		{
			foreach (ShoreSource source in branch.Sources)
			{
				if ((bool)source.Clip && source.Clip.name == clipName)
				{
					source.Clip = content;
					source.NextStartAttempt = 0f;
					source.PreviousSample = -1;
					if ((bool)source.Source)
					{
						source.Source.Stop();
						source.Source.clip = content;
					}
				}
			}
		}
		if ((bool)audioClip)
		{
			UnityEngine.Object.Destroy(audioClip);
		}
		Plugin.Log.LogInfo($"[ShoreAudio] loaded '{clipName}': samples={content.samples}, channels={content.channels}, frequency={content.frequency}");
	}

	private void Update()
	{
		if (_reloadClips && !_loadingClips)
		{
			StartCoroutine(ReloadClips());
		}
		if (TerminalShoreAudio.AssetsReady && !(Time.realtimeSinceStartup < _nextRoutingCheck))
		{
			_nextRoutingCheck = Time.realtimeSinceStartup + 1f;
			_mixer = TerminalAudioRouting.AmbientBed();
			if (_branches.Count == 0 && TerminalAcoustics.AmbientStaged)
			{
				DiscoverBranches();
			}
		}
	}

	private void LateUpdate()
	{
		if (_branches.Count == 0 || !TerminalShoreAudio.AssetsReady)
		{
			return;
		}
		Player player = ((!Singleton<GameWorld>.Instantiated) ? null : Singleton<GameWorld>.Instance?.MainPlayer);
		bool flag = true;
		try
		{
			if (Singleton<SettingsManager>.Instantiated)
			{
				SettingsManager instance = Singleton<SettingsManager>.Instance;
				flag = instance != null && instance.Sound?.Settings?.OverallVolume?.Value > 0;
			}
		}
		catch
		{
		}
		Vector3 vector = ((player != null) ? player.Position : Vector3.zero);
		float num = float.MaxValue;
		bool flag2 = false;
		int num2 = 0;
		bool flag3 = Time.realtimeSinceStartup >= _nextPlaybackDiagnostic;
		if (flag3)
		{
			_nextPlaybackDiagnostic = Time.realtimeSinceStartup + 30f;
		}
		foreach (ShoreBranch branch in _branches)
		{
			if (branch?.Spline == null)
			{
				continue;
			}
			Vector3 vector2 = Vector3.zero;
			float num3 = float.MaxValue;
			Vector3[] samplePoints = branch.SamplePoints;
			if (samplePoints == null || samplePoints.Length == 0)
			{
				continue;
			}
			for (int i = 0; i < samplePoints.Length; i++)
			{
				Vector3 vector3 = samplePoints[i];
				float num4 = vector3.x - vector.x;
				float num5 = vector3.z - vector.z;
				float num6 = num4 * num4 + num5 * num5;
				if (!(num6 >= num3))
				{
					num3 = num6;
					vector2 = vector3;
				}
			}
			if (num3 < num)
			{
				num = num3;
			}
			float sqrMagnitude = (vector2 - vector).sqrMagnitude;
			foreach (ShoreSource source2 in branch.Sources)
			{
				AudioSource source = source2.Source;
				if (!source)
				{
					continue;
				}
				bool flag4 = player != null && sqrMagnitude <= source.maxDistance * source.maxDistance;
				flag2 |= flag4;
				if ((bool)source2.NativeSource && source2.NativeSource.gameObject.activeSelf)
				{
					source2.NativeSource.gameObject.SetActive(value: false);
				}
				source.transform.position = vector2;
				source.volume = source2.AuthoredVolume * Mathf.Clamp01(Plugin.SoundRigVolume.Value);
				source.outputAudioMixerGroup = _mixer;
				source.mute = !flag || !flag4 || !_mixer;
				bool flag5 = !source.mute && source.volume > 0f && source.gameObject.activeInHierarchy;
				if (flag5 && !source.isPlaying && Time.realtimeSinceStartup >= source2.NextStartAttempt)
				{
					source2.NextStartAttempt = Time.realtimeSinceStartup + 1f;
					source.enabled = true;
					if (source2.Clip.loadState == AudioDataLoadState.Unloaded)
					{
						source2.Clip.LoadAudioData();
					}
					if (source2.Clip.loadState == AudioDataLoadState.Loaded && source2.Clip.samples > 0)
					{
						source.Play();
					}
				}
				else if (!flag5 && source.isPlaying)
				{
					source.Stop();
				}
				if (source.isPlaying && !source.mute)
				{
					num2++;
				}
				if (flag3)
				{
					int timeSamples = source.timeSamples;
					Plugin.Log.LogInfo($"[ShoreAudio][playback] '{source2.Clip.name}' playing={source.isPlaying}" + $" enabled={source.enabled} active={source.gameObject.activeInHierarchy} mute={source.mute}" + $" volume={source.volume:F2} pitch={source.pitch:F2} load={source2.Clip.loadState}" + $" clipSamples={source2.Clip.samples} listenerPaused={AudioListener.pause}" + $" distance={Mathf.Sqrt(sqrMagnitude):F1}m maxDistance={source.maxDistance:F1}m" + $" sample={timeSamples} previousSample={source2.PreviousSample}" + " mixer='" + (_mixer ? _mixer.name : "missing") + "'");
					source2.PreviousSample = timeSamples;
				}
			}
		}
		bool flag6 = num2 > 0;
		if (!_guardStateKnown || flag6 != _lastGuardAudible)
		{
			_guardStateKnown = true;
			_lastGuardAudible = flag6;
			Plugin.Log.LogInfo("[ShoreAudio] shoreline " + (flag6 ? "PLAYING" : "STOPPED") + ": nearestSpline=" + ((num < float.MaxValue) ? Mathf.Sqrt(num).ToString("F0") : "n/a") + "m, " + $"masterAudible={flag}, inRange={flag2}, playingSources={num2}");
		}
	}

	private void DiscoverBranches()
	{
		Transform ambientRootTransform = TerminalAcoustics.AmbientRootTransform;
		if (!ambientRootTransform)
		{
			return;
		}
		Transform[] componentsInChildren = ambientRootTransform.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (!transform || !string.Equals(transform.name, "AmbientSplineEmitterSeaGroup", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			BezierSpline[] componentsInChildren2 = transform.GetComponentsInChildren<BezierSpline>(includeInactive: true);
			foreach (BezierSpline bezierSpline in componentsInChildren2)
			{
				if (!bezierSpline)
				{
					continue;
				}
				Vector3[] array = new Vector3[161];
				for (int k = 0; k <= 160; k++)
				{
					array[k] = bezierSpline.GetPoint((float)k / 160f);
				}
				ShoreBranch shoreBranch = new ShoreBranch
				{
					Spline = bezierSpline,
					SamplePoints = array
				};
				Transform parent = bezierSpline.transform.parent;
				if (!parent)
				{
					continue;
				}
				BaseAmbientSoundPlayer[] componentsInChildren3 = parent.GetComponentsInChildren<BaseAmbientSoundPlayer>(includeInactive: true);
				foreach (BaseAmbientSoundPlayer baseAmbientSoundPlayer in componentsInChildren3)
				{
					if (!baseAmbientSoundPlayer || !TerminalAcoustics.IsConfiguredShorePlayer(baseAmbientSoundPlayer))
					{
						continue;
					}
					AudioClip clip = baseAmbientSoundPlayer.GetClip();
					if (!clip || !clip.name.StartsWith("amb_terminal_spline_sea_", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					AudioSource source = baseAmbientSoundPlayer.Source;
					if (!source)
					{
						continue;
					}
					Transform parent2 = baseAmbientSoundPlayer.transform;
					while (parent2 != null && parent2 != ambientRootTransform)
					{
						if (!parent2.gameObject.activeSelf)
						{
							parent2.gameObject.SetActive(value: true);
						}
						parent2 = parent2.parent;
					}
					float baseVolume = baseAmbientSoundPlayer.GetBaseVolume();
					baseAmbientSoundPlayer.ScaleMaxDistance(0f);
					GameObject gameObject = new GameObject("Terminal_Shore_" + clip.name);
					gameObject.SetActive(value: false);
					gameObject.transform.SetParent(transform, worldPositionStays: false);
					AudioSource audioSource = gameObject.AddComponent<AudioSource>();
					audioSource.playOnAwake = false;
					audioSource.loop = true;
					audioSource.clip = clip;
					audioSource.pitch = baseAmbientSoundPlayer.GetPitch();
					audioSource.spatialBlend = 1f;
					audioSource.dopplerLevel = 0f;
					audioSource.minDistance = source.minDistance;
					audioSource.maxDistance = source.maxDistance;
					audioSource.rolloffMode = AudioRolloffMode.Custom;
					audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
					audioSource.spread = source.spread;
					audioSource.SetCustomCurve(AudioSourceCurveType.Spread, source.GetCustomCurve(AudioSourceCurveType.Spread));
					audioSource.volume = 0f;
					audioSource.mute = true;
					audioSource.outputAudioMixerGroup = _mixer;
					Plugin.Log.LogInfo($"[ShoreAudio] taking over '{clip.name}': nativeEnabled={source.enabled}," + $" nativePlaying={source.isPlaying}, nativePitch={source.pitch:F2}; " + $"dedicated source, authored range={audioSource.minDistance:F1}-{audioSource.maxDistance:F1}m, same spline/rolloff");
					baseAmbientSoundPlayer.Stop(force: true);
					source.gameObject.SetActive(value: false);
					gameObject.SetActive(value: true);
					shoreBranch.Sources.Add(new ShoreSource
					{
						Source = audioSource,
						NativeSource = source,
						Clip = clip,
						AuthoredVolume = baseVolume
					});
				}
				if (shoreBranch.Sources.Count > 0)
				{
					_branches.Add(shoreBranch);
				}
			}
		}
		if (_branches.Count <= 0 || _guardLogged)
		{
			return;
		}
		_guardLogged = true;
		int num = 0;
		foreach (ShoreBranch branch in _branches)
		{
			num += branch.Sources.Count;
		}
		_nextPlaybackDiagnostic = Time.realtimeSinceStartup + 10f;
		Plugin.Log.LogInfo($"[ShoreAudio] dedicated spline playback ready: {_branches.Count} authored branch(es), " + $"{num} source(s), per-player authored ranges, AmbientSplines={Plugin.AmbientSplines.Value}, AmbientOut/master-volume gate");
	}

	private void OnDestroy()
	{
		AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
		foreach (ShoreBranch branch in _branches)
		{
			foreach (ShoreSource source in branch.Sources)
			{
				if ((bool)source.Source)
				{
					UnityEngine.Object.Destroy(source.Source.gameObject);
				}
			}
		}
		foreach (AudioClip ownedClip in _ownedClips)
		{
			if ((bool)ownedClip)
			{
				UnityEngine.Object.Destroy(ownedClip);
			}
		}
		_ownedClips.Clear();
		_branches.Clear();
	}
}
