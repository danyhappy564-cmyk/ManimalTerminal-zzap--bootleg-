using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Audio.AmbientSubsystem;
using Audio.SpatialSystem;
using Audio.SpatialSystem.Data;
using Audio.SpatialSystem.Utils;
using Comfort.Common;
using EFT;
using EFT.EnvironmentEffect;
using EFT.Interactive;
using EFT.Settings.Sound;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalAcoustics
{
	private sealed class BorrowedAmbientFieldState
	{
		internal Component Target;

		internal FieldInfo Field;

		internal object Value;
	}

	private sealed class AmbientFallbackSourceState
	{
		internal AudioClip Clip;

		internal AudioMixerGroup Mixer;

		internal float Volume;

		internal float SpatialBlend;

		internal float MinDistance;

		internal float MaxDistance;

		internal float Pitch;

		internal float Spread;

		internal bool Loop;

		internal bool PlayOnAwake;

		internal bool Mute;

		internal bool WasPlaying;

		internal AudioRolloffMode RolloffMode;

		internal AnimationCurve RolloffCurve;

		internal AnimationCurve SpreadCurve;
	}

	internal class AmbientGovernor : MonoBehaviour
	{
		internal AudioSource Src;

		internal float MinWait = 20f;

		internal float MaxWait = 90f;

		private float _next;

		private void Start()
		{
			_next = Time.time + UnityEngine.Random.Range(MinWait, MaxWait);
		}

		private void Update()
		{
			if (Time.time < _next)
			{
				return;
			}
			_next = Time.time + UnityEngine.Random.Range(MinWait, MaxWait);
			try
			{
				if (Src != null && Src.clip != null && !Src.isPlaying)
				{
					Src.Play();
				}
			}
			catch
			{
			}
		}
	}

	private sealed class Suspended
	{
		internal MethodBase Target;

		internal Patch Patch;

		internal HarmonyPatchType Kind;
	}

	private static JObject _sidecar;

	private static bool _sidecarTried;

	private static GameObject _envRoot;

	private static GameObject _spatialMarker;

	private static GameObject _ambientRoot;

	private static GameObject _ambientConstructionRoot;

	private static Dictionary<long, Component> _ambientComponents;

	private static readonly TerminalAmbientLifecycle _ambientLifecycle = new TerminalAmbientLifecycle();

	private static readonly List<Component> _ownedAmbientComponents = new List<Component>();

	private static readonly List<UnityEngine.Object> _ownedAmbientAssets = new List<UnityEngine.Object>();

	private static readonly List<BorrowedAmbientFieldState> _borrowedAmbientFields = new List<BorrowedAmbientFieldState>();

	private static bool _ambientRootWasActive;

	private static bool _ambientFailureReported;

	private static readonly HashSet<Component> _configuredShorePlayers = new HashSet<Component>();

	private static readonly Dictionary<string, UnityEngine.Object> _banks = new Dictionary<string, UnityEngine.Object>();

	private static readonly HashSet<string> _bankClipMisses = new HashSet<string>();

	private static readonly List<AudioSource> _forcedLoopSources = new List<AudioSource>();

	private static readonly Dictionary<string, AudioClip> _recoveredLoopClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

	private static readonly string[] AmbientClasses = new string[22]
	{
		"BezierSpline", "SoundPoint", "SoundPointsManager", "AmbientSoundPlayer", "LoopAmbientSoundPlayer", "OneShotAmbientSoundPlayer", "WeatherRandomAmbientSoundPlayer", "AmbientSoundPlayerGroup", "SoundPlayerRandomPointComponent", "SoundPlayerSplineTrigger",
		"AmbientPlayerSplineMappedEmitter", "AmbientPlayerGroupSplineEmitter", "SplineEmitterPathMover", "SoundAmbientZoneCalculator", "SoundPlayerRoomObserverComponent", "AmbientSoundPlayerGroupController", "AmbientSplineEmitterController", "SplineTriggerChecker", "AmbientPlayerAutoPanner", "PrecipitationAmbientBlender",
		"AmbientSoundBlender", "EnvironmentSoundBlendSystem"
	};

	private static bool _shoreAssetWaitLogged;

	private static bool _seasonInvalidationSubscribed;

	private static float _seaDiagAt = -1f;

	private static bool _ambientSilenced;

	private static GameObject _ambientMarker;

	private static readonly HashSet<AudioSource> _fallbackSources = new HashSet<AudioSource>();

	private static readonly Dictionary<AudioSource, AmbientFallbackSourceState> _fallbackSourceStates = new Dictionary<AudioSource, AmbientFallbackSourceState>();

	private static readonly List<AmbientGovernor> _fallbackGovernors = new List<AmbientGovernor>();

	private static readonly string[] LoopPlayerClasses = new string[2] { "LoopAmbientSoundPlayer", "SeasonLoopAmbientSoundPlayer" };

	private static readonly string[] ShotPlayerClasses = new string[4] { "AmbientSoundPlayer", "OneShotAmbientSoundPlayer", "SeasonAmbientSoundPlayer", "WeatherRandomAmbientSoundPlayer" };

	private static bool _spatialKick;

	private static Task _spatialTask;

	private static bool _spatialVerdict;

	private static EnvironmentType _lastEnv = (EnvironmentType)(-1);

	private static int _driveTick;

	private static int _volumesFixed;

	private static Dictionary<string, AudioClip> _clipCache;

	private static int _roomTonesBound;

	private static readonly HashSet<string> _roomToneMisses = new HashSet<string>();

	private static string DataDir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "acoustics");

	internal static bool AmbientStaged
	{
		get
		{
			if (_ambientRoot != null)
			{
				return _ambientLifecycle.IsConstructed;
			}
			return false;
		}
	}

	internal static bool AmbientNativeReady
	{
		get
		{
			if (_ambientRoot != null)
			{
				return _ambientLifecycle.IsReady;
			}
			return false;
		}
	}

	internal static bool AmbientFallbackActive => _ambientLifecycle.IsFallback;

	internal static Transform AmbientRootTransform
	{
		get
		{
			if (!_ambientRoot)
			{
				return null;
			}
			return _ambientRoot.transform;
		}
	}

	private static JObject Sidecar()
	{
		if (_sidecarTried)
		{
			return _sidecar;
		}
		_sidecarTried = true;
		try
		{
			string text = Path.Combine(DataDir, "terminal_spatial_audio.json");
			if (!File.Exists(text))
			{
				Plugin.Log.LogDebug("[Acoustics] no sidecar at " + text + " — spatial audio + env triggers stay off");
				return null;
			}
			_sidecar = JObject.Parse(File.ReadAllText(text));
			Plugin.Log.LogInfo("[Acoustics] sidecar loaded");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Acoustics] sidecar parse failed: " + ex.Message);
			_sidecar = null;
		}
		return _sidecar;
	}

	public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "Terminal_Sound" || !Plugin.SpatialAudio.Value)
		{
			return;
		}
		try
		{
			if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated || Sidecar() == null)
			{
				return;
			}
			GameObject gameObject = null;
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			foreach (GameObject gameObject2 in rootGameObjects)
			{
				if (gameObject2.name == "SpatialAudioSystem")
				{
					gameObject = gameObject2;
					break;
				}
			}
			if (gameObject == null)
			{
				gameObject = new GameObject("SpatialAudioSystem");
				SceneManager.MoveGameObjectToScene(gameObject, scene);
			}
			if (!gameObject.activeSelf)
			{
				gameObject.SetActive(value: true);
				Plugin.Log.LogDebug("[Acoustics] SpatialAudioSystem host was inactive — activated");
			}
			if (gameObject.GetComponent<SpatialAudioSystem>() == null)
			{
				gameObject.AddComponent<SpatialAudioSystem>();
				Plugin.Log.LogInfo("[Acoustics] SpatialAudioSystem component created on the Sound scene — staging will run at Initialize");
			}
			_spatialKick = false;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Acoustics] system resurrection failed: " + ex.Message);
		}
	}

	public static bool TryBuildEnvironmentTriggers()
	{
		if (_envRoot != null)
		{
			return true;
		}
		JObject jObject = Sidecar();
		if (!(jObject?["scenes"]?["Terminal_Scripts"]?["IndoorTrigger"] is JArray { Count: not 0 } jArray))
		{
			return false;
		}
		try
		{
			GameObject gameObject = new GameObject("Terminal_EnvSwitch");
			GameObject gameObject2 = new GameObject("TriggerGroup");
			gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
			int num = 0;
			foreach (JToken item in jArray)
			{
				JToken jToken = item["world_trs"] ?? item["trs"];
				if (jToken != null)
				{
					GameObject gameObject3 = new GameObject($"IndoorTrigger_{num}");
					gameObject3.transform.SetParent(gameObject2.transform, worldPositionStays: false);
					gameObject3.transform.position = V3(jToken["pos"]);
					gameObject3.transform.rotation = Q4(jToken["rot"]);
					gameObject3.transform.localScale = V3(jToken["scale"]);
					IndoorTrigger indoorTrigger = gameObject3.AddComponent<IndoorTrigger>();
					JToken jToken2 = item["fields"];
					if (jToken2 != null)
					{
						indoorTrigger.IsBunker = jToken2.Value<bool?>("IsBunker") == true;
						indoorTrigger.FadeTime = jToken2.Value<float?>("FadeTime") ?? 1f;
						indoorTrigger.BunkerDepth = jToken2.Value<float?>("BunkerDepth") ?? (-15f);
						indoorTrigger.BunkerLowPass = jToken2.Value<float?>("BunkerLowPass") ?? 300f;
						indoorTrigger.ExposureSpeed = jToken2.Value<float?>("ExposureSpeed") ?? 4f;
						indoorTrigger.ExposureOffset = (jToken2.Value<float?>("ExposureOffset") ?? 0.14f) * Plugin.EnvironmentExposure.Value;
						indoorTrigger.RainVolume = jToken2.Value<float?>("RainVolume") ?? 0.7f;
						indoorTrigger.Reinit();
					}
					num++;
				}
			}
			gameObject2.AddComponent<TriggerGroup>();
			gameObject.SetActive(value: false);
			EnvironmentManager environmentManager = gameObject.AddComponent<EnvironmentManager>();
			if (((jObject["scenes"]?["Terminal_Scripts"]?["EnvironmentManager"] as JArray)?.First)?["fields"] is JObject fields)
			{
				FillFields(environmentManager, fields, (string name) => name != "Bounds");
			}
			float value = Plugin.EnvironmentExposure.Value;
			FieldInfo fieldInfo = AccessTools.Field(typeof(EnvironmentManager), "OutdoorExposureOffset");
			if (fieldInfo != null)
			{
				float num2 = (float)fieldInfo.GetValue(environmentManager);
				fieldInfo.SetValue(environmentManager, num2 * value);
				if (value <= 0.001f)
				{
					Plugin.Log.LogInfo($"[Acoustics] environment exposure neutralised (retail outdoor offset {num2:0.00} -> 0) " + "— set EnvironmentExposure=1 for retail's authored brightness");
				}
			}
			gameObject.SetActive(value: true);
			_envRoot = gameObject;
			Plugin.Log.LogInfo($"[Acoustics] environment switcher rebuilt: {num} indoor triggers (indoor/outdoor audio + exposure live)");
			return true;
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Acoustics] env trigger build failed: {arg}");
			return false;
		}
	}

	internal static bool IsConfiguredShorePlayer(Component player)
	{
		return _configuredShorePlayers.Contains(player);
	}

	internal static void ResetRecoveredAudioForRaid()
	{
		TeardownAmbientForRaid();
		_recoveredLoopClips.Clear();
		_ambientComponents = null;
		_configuredShorePlayers.Clear();
		_clipCache = null;
		_shoreAssetWaitLogged = false;
		_ambientLifecycle.Reset();
		_ambientRoot = null;
		_ambientConstructionRoot = null;
		_ambientFailureReported = false;
		_ambientSilenced = false;
	}

	internal static void RegisterRecoveredLoopClip(string clipName, AudioClip clip, bool wakePlayers = true)
	{
		if (string.IsNullOrEmpty(clipName) || !clip)
		{
			return;
		}
		_recoveredLoopClips[clipName] = clip;
		if (_clipCache != null)
		{
			_clipCache[clipName] = clip;
		}
		if (!_ambientRoot || _ambientComponents == null || !(Sidecar()?["scenes"]?["Terminal_Sound"] is JObject scene))
		{
			return;
		}
		int num = 0;
		foreach (JToken item in Rows(scene, "LoopAmbientSoundPlayer"))
		{
			if (!string.Equals(item.Value<string>("_loopClipName"), clipName, StringComparison.OrdinalIgnoreCase) || !_ambientComponents.TryGetValue(item.Value<long>("path_id"), out var value) || !value)
			{
				continue;
			}
			FieldInfo fieldInfo = AccessTools.Field(value.GetType(), "_loopClip");
			if (fieldInfo != null && fieldInfo.FieldType == typeof(AudioClip))
			{
				fieldInfo.SetValue(value, clip);
			}
			if (!_ambientLifecycle.IsReady)
			{
				num++;
				continue;
			}
			if (!wakePlayers || (IsUnderSeaGroup(value.transform) && !IsConfiguredShorePlayer(value)))
			{
				num++;
				continue;
			}
			Transform transform = value.transform;
			while (transform != null && transform.gameObject != _ambientRoot)
			{
				if (!transform.gameObject.activeSelf)
				{
					transform.gameObject.SetActive(value: true);
				}
				transform = transform.parent;
			}
			BaseAmbientSoundPlayer baseAmbientSoundPlayer = value as BaseAmbientSoundPlayer;
			AudioSource audioSource = ((baseAmbientSoundPlayer != null) ? baseAmbientSoundPlayer.Source : (value.GetComponent<AudioSource>() ?? value.gameObject.AddComponent<AudioSource>()));
			TerminalAudioRouting.Route(audioSource);
			audioSource.mute = true;
			if (!_forcedLoopSources.Contains(audioSource))
			{
				_forcedLoopSources.Add(audioSource);
			}
			if (baseAmbientSoundPlayer != null && audioSource.gameObject.activeInHierarchy && !audioSource.isPlaying)
			{
				baseAmbientSoundPlayer.Play();
			}
			num++;
		}
		Plugin.Log.LogInfo($"[ShoreAudio] '{clipName}' bound to {num} authored shoreline player(s)");
	}

	// zzap perf: was TypeByName + a second full GetTypes() scan of every assembly on each
	// call, repeated on every ambient attempt — now cached (TerminalTypes)
	private static Type ResolveType(string name)
	{
		return TerminalTypes.ByNameOrShort(name);
	}

	internal static void TickDiagnostics()
	{
		if (_seaDiagAt < 0f || Time.realtimeSinceStartup < _seaDiagAt)
		{
			return;
		}
		_seaDiagAt = -1f;
		try
		{
			if (!_ambientRoot)
			{
				return;
			}
			Player player = ((!Singleton<GameWorld>.Instantiated) ? null : Singleton<GameWorld>.Instance?.MainPlayer);
			int num = 0;
			Transform[] componentsInChildren = _ambientRoot.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform in componentsInChildren)
			{
				if (!(transform.name != "AmbientSplineEmitterSeaGroup"))
				{
					AudioSource[] componentsInChildren2 = transform.GetComponentsInChildren<AudioSource>(includeInactive: true);
					foreach (AudioSource audioSource in componentsInChildren2)
					{
						num++;
						float num2 = ((player != null) ? (audioSource.transform.position - player.Position).magnitude : (-1f));
						Plugin.Log.LogInfo("[Ambient][sea] '" + audioSource.gameObject.name + "' clip='" + (audioSource.clip ? audioSource.clip.name : "NULL") + "'" + $" playing={audioSource.isPlaying} enabled={audioSource.enabled} mute={audioSource.mute} pitch={audioSource.pitch:F2} vol={audioSource.volume:F2} blend={audioSource.spatialBlend:F1}" + $" maxDist={audioSource.maxDistance:F0} activeGO={audioSource.gameObject.activeInHierarchy} dist={num2:F0}m" + " mixer='" + (audioSource.outputAudioMixerGroup ? audioSource.outputAudioMixerGroup.name : "BYPASS/NULL") + "'");
					}
				}
			}
			if (num == 0)
			{
				Plugin.Log.LogWarning("[Ambient][sea] sea group has NO AudioSources at diag time");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ambient][sea] diag failed: " + ex.Message);
		}
	}

	internal static int EnsureNativeShoreRouting()
	{
		if (!_ambientRoot)
		{
			return 0;
		}
		int num = 0;
		int num2 = 0;
		Transform[] componentsInChildren = _ambientRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (!string.Equals(transform.name, "AmbientSplineEmitterSeaGroup", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			AudioSource[] componentsInChildren2 = transform.GetComponentsInChildren<AudioSource>(includeInactive: true);
			foreach (AudioSource audioSource in componentsInChildren2)
			{
				if ((bool)audioSource)
				{
					num++;
					bool flag = TerminalAudioRouting.Route(audioSource);
					if (!flag)
					{
						audioSource.mute = true;
					}
					if (flag)
					{
						num2++;
					}
				}
			}
		}
		if (num <= 0 || num2 != num)
		{
			return 0;
		}
		return num2;
	}

	internal static void SetAmbientSilenced(bool silent)
	{
		_ambientSilenced = silent;
		try
		{
			if (_ambientRoot != null && _ambientRoot.activeSelf != !silent)
			{
				_ambientRoot.SetActive(!silent);
			}
			foreach (AudioSource fallbackSource in _fallbackSources)
			{
				if (!fallbackSource)
				{
					continue;
				}
				fallbackSource.mute = silent;
				if (silent)
				{
					if (fallbackSource.isPlaying)
					{
						fallbackSource.Stop();
					}
				}
				else if (fallbackSource.loop && fallbackSource.gameObject.activeInHierarchy && (bool)fallbackSource.clip && !fallbackSource.isPlaying)
				{
					fallbackSource.Play();
				}
			}
			if (!silent)
			{
				foreach (AudioSource forcedLoopSource in _forcedLoopSources)
				{
					if ((bool)forcedLoopSource && forcedLoopSource.gameObject.activeInHierarchy && (bool)forcedLoopSource.clip && !forcedLoopSource.isPlaying)
					{
						BaseAmbientSoundPlayer component = forcedLoopSource.GetComponent<BaseAmbientSoundPlayer>();
						if (component != null)
						{
							component.Play();
						}
						else
						{
							forcedLoopSource.Play();
						}
					}
				}
			}
			Plugin.Log.LogDebug("[Ambient] retail layer " + (silent ? "silenced for the cutscene" : "back up"));
		}
		catch
		{
		}
	}

	private static void BuildBanks(JObject sc)
	{
		if (_banks.Count > 0)
		{
			return;
		}
		if (!(sc["banks"] is JObject jObject))
		{
			Plugin.Log.LogWarning("[Ambient] sidecar carries no banks section");
			return;
		}
		Type type = ResolveType("EFT.SoundBank");
		Type type2 = ResolveType("EFT.EnvironmentVariety");
		Type type3 = ResolveType("EFT.DistanceVarity");
		if (type == null || type2 == null || type3 == null)
		{
			Plugin.Log.LogWarning("[Ambient] SoundBank types missing — ambient players would stay silent");
			return;
		}
		int num = 0;
		int num2 = 0;
		foreach (KeyValuePair<string, JToken> item in jObject)
		{
			if (!(item.Value is JObject jObject2))
			{
				continue;
			}
			List<AudioClip> list = new List<AudioClip>();
			foreach (JToken item2 in (jObject2["clipNames"] as JArray) ?? new JArray())
			{
				string text = item2.Value<string>();
				if (!string.IsNullOrEmpty(text))
				{
					AudioClip audioClip = FindClip(text);
					if (audioClip != null)
					{
						list.Add(audioClip);
					}
					else
					{
						_bankClipMisses.Add(text);
					}
				}
			}
			if (list.Count == 0)
			{
				num2++;
				continue;
			}
			ScriptableObject scriptableObject = ScriptableObject.CreateInstance(type);
			scriptableObject.name = jObject2.Value<string>("name") ?? item.Key;
			object obj = Activator.CreateInstance(type3);
			AccessTools.Field(type3, "Clips").SetValue(obj, list.ToArray());
			object obj2 = Activator.CreateInstance(type2);
			Array array = Array.CreateInstance(type3, 1);
			array.SetValue(obj, 0);
			AccessTools.Field(type2, "Clips").SetValue(obj2, array);
			Array array2 = Array.CreateInstance(type2, 1);
			array2.SetValue(obj2, 0);
			AccessTools.Field(type, "Environments").SetValue(scriptableObject, array2);
			if (jObject2["fields"] is JObject jObject3)
			{
				FillFields(scriptableObject, jObject3, (string f) => f != "SourceType");
				int? num3 = jObject3.Value<int?>("SourceType");
				FieldInfo fieldInfo = AccessTools.Field(type, "SourceType");
				if (num3.HasValue && fieldInfo != null)
				{
					try
					{
						fieldInfo.SetValue(scriptableObject, Enum.ToObject(fieldInfo.FieldType, num3.Value));
					}
					catch
					{
					}
				}
			}
			AccessTools.Field(type, "HasEnvironment")?.SetValue(scriptableObject, false);
			_banks[item.Key] = scriptableObject;
			_ownedAmbientAssets.Add(scriptableObject);
			num++;
		}
		Plugin.Log.LogInfo($"[Ambient] {num} retail sound bank(s) rebuilt with authored volumes" + ((num2 > 0) ? $", {num2} skipped (no clips in bundle)" : ""));
	}

	private static bool PrepareNativeAmbientRouting(GameObject root, AudioMixerGroup ambientBus)
	{
		if (!root || !ambientBus)
		{
			return false;
		}
		try
		{
			AmbientSoundPlayerGroup[] componentsInChildren = root.GetComponentsInChildren<AmbientSoundPlayerGroup>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				CaptureBorrowedAmbientFields(componentsInChildren[i], null);
			}
			BaseAmbientSoundPlayer[] componentsInChildren2 = root.GetComponentsInChildren<BaseAmbientSoundPlayer>(includeInactive: true);
			foreach (BaseAmbientSoundPlayer baseAmbientSoundPlayer in componentsInChildren2)
			{
				if ((bool)baseAmbientSoundPlayer)
				{
					CaptureBorrowedAmbientFields(baseAmbientSoundPlayer, null);
				}
			}
			return TerminalAudioRouting.PrepareNativePlayers(root, ambientBus);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[Ambient] native mixer pre-route failed: " + ex.Message);
			return false;
		}
	}

	public static bool TryStageAmbient(MonoBehaviour runner)
	{
		if (_ambientRoot != null && _ambientLifecycle.IsReady)
		{
			return true;
		}
		if (_ambientRoot != null && _ambientLifecycle.Phase == TerminalAmbientPhase.Staged)
		{
			return TryAdvanceStagedAmbient();
		}
		if (_ambientLifecycle.IsFallback)
		{
			return false;
		}
		AbstractGame instance = Singleton<AbstractGame>.Instance;
		if (instance == null || instance.Status != GameStatus.Started)
		{
			return false;
		}
		if (!_seasonInvalidationSubscribed)
		{
			TerminalAmbientSeasonAssets.Invalidating += OnSeasonAssetsInvalidating;
			_seasonInvalidationSubscribed = true;
		}
		if (!TerminalAmbientSeasonAssets.EnsureLoading(runner) && TerminalAmbientSeasonAssets.State != TerminalAmbientSeasonAssets.SeasonAmbientAssetState.Failed)
		{
			return false;
		}
		if (!TerminalShoreAudio.AssetsReady)
		{
			if (!_shoreAssetWaitLogged)
			{
				_shoreAssetWaitLogged = true;
				Plugin.Log.LogDebug("[Ambient] waiting for native shoreline clips before staging the retail ambient tree");
			}
			return false;
		}
		JObject jObject = Sidecar();
		if (!(jObject?["scenes"]?["Terminal_Sound"] is JObject jObject2))
		{
			return false;
		}
		JToken first = Rows(jObject2, "AmbientAudioSystem").First;
		string text = first?.Value<string>("go") ?? "AmbientAudioSystem";
		Dictionary<string, List<Transform>> dictionary = BuildGoIndex();
		if (!dictionary.TryGetValue(text, out var value) || value.Count == 0)
		{
			Plugin.Log.LogDebug("[Ambient] root '" + text + "' not in the scene yet — retrying");
			return false;
		}
		GameObject gameObject = value[0].gameObject;
		AudioMixerGroup audioMixerGroup = TerminalAudioRouting.AmbientBed();
		if (!audioMixerGroup)
		{
			Plugin.Log.LogDebug("[Ambient] live AmbientOut mixer unavailable — staging deferred");
			return false;
		}
		if (!_ambientLifecycle.BeginAttempt())
		{
			return false;
		}
		ReleaseAmbientFallback();
		bool flag = (_ambientRootWasActive = gameObject.activeSelf);
		_ambientConstructionRoot = gameObject;
		Dictionary<long, Component> dictionary2 = null;
		try
		{
			_clipCache = null;
			_banks.Clear();
			_bankClipMisses.Clear();
			_forcedLoopSources.Clear();
			if (!PrepareNativeAmbientRouting(gameObject, audioMixerGroup))
			{
				throw new InvalidOperationException("existing native sources could not be routed to the settings mixer");
			}
			BuildBanks(jObject);
			if (_banks.Count == 0)
			{
				throw new InvalidOperationException("no authored SoundBank contained a loaded clip");
			}
			gameObject.SetActive(value: false);
			dictionary2 = new Dictionary<long, Component>();
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			string[] ambientClasses = AmbientClasses;
			foreach (string text2 in ambientClasses)
			{
				Type type = ResolveType(text2);
				if (type == null)
				{
					continue;
				}
				foreach (JToken item in Rows(jObject2, text2))
				{
					if (item["parse_error"] != null)
					{
						continue;
					}
					Transform transform = FindGo(dictionary, item);
					if (transform == null)
					{
						num3++;
						continue;
					}
					if (transform.gameObject.GetComponent<WorldInteractiveObject>() != null)
					{
						num2++;
						continue;
					}
					Component component = transform.gameObject.GetComponent(type);
					if (component != null)
					{
						dictionary2[item.Value<long>("path_id")] = component;
						num2++;
						continue;
					}
					try
					{
						Component component2 = transform.gameObject.AddComponent(type);
						dictionary2[item.Value<long>("path_id")] = component2;
						if (component2 != null)
						{
							_ownedAmbientComponents.Add(component2);
						}
						num++;
					}
					catch (Exception ex)
					{
						Plugin.Log.LogWarning("[Ambient] " + text2 + " on '" + item.Value<string>("go") + "': " + ex.Message);
					}
				}
			}
			int num4 = 0;
			ambientClasses = AmbientClasses;
			foreach (string text3 in ambientClasses)
			{
				foreach (JToken item2 in Rows(jObject2, text3))
				{
					if (item2["parse_error"] != null || !dictionary2.TryGetValue(item2.Value<long>("path_id"), out var value2) || value2 == null || !(item2["fields"] is JObject fields))
					{
						continue;
					}
					try
					{
						if (!_ownedAmbientComponents.Contains(value2))
						{
							CaptureBorrowedAmbientFields(value2, fields);
						}
						FillFields(value2, fields, null);
						WireRefs(value2, fields, dictionary2);
						BindContent(value2, item2, fields);
						num4++;
					}
					catch (Exception ex2)
					{
						Plugin.Log.LogWarning("[Ambient] fill " + text3 + " '" + item2.Value<string>("go") + "': " + ex2.Message);
					}
				}
			}
			int num5 = WireGroupEmitterConfigs(jObject2, dictionary2);
			int num6 = PruneDeadSplineEmitters(dictionary2);
			if (!Plugin.AmbientSplines.Value)
			{
				int num7 = 0;
				ambientClasses = new string[5] { "SplineEmitterPathMover", "SplineTriggerChecker", "AmbientPlayerSplineMappedEmitter", "AmbientPlayerGroupSplineEmitter", "SoundAmbientZoneCalculator" };
				for (int i = 0; i < ambientClasses.Length; i++)
				{
					Type type2 = ResolveType(ambientClasses[i]);
					if (type2 == null)
					{
						continue;
					}
					foreach (long item3 in new List<long>(dictionary2.Keys))
					{
						Component component3 = dictionary2[item3];
						if (!(component3 == null) && type2.IsInstanceOfType(component3) && !IsUnderSeaGroup(component3.transform))
						{
							UnityEngine.Object.DestroyImmediate(component3);
							dictionary2[item3] = null;
							num7++;
						}
					}
				}
				Plugin.Log.LogWarning($"[Ambient] OPTIONAL SPLINE STACK OFF — {num7} component(s) dropped; " + "2 authored shoreline proximity emitters retained");
			}
			ConfigureShoreMembership(jObject2, dictionary2);
			Type type3 = ResolveType("AmbientAudioSystem");
			if (type3 == null)
			{
				throw new InvalidOperationException("AmbientAudioSystem type is unavailable in the target client");
			}
			Component component4 = gameObject.GetComponent(type3);
			if (component4 != null)
			{
				UnityEngine.Object.DestroyImmediate(component4);
			}
			Component component5 = gameObject.AddComponent(type3);
			if (component5 == null)
			{
				throw new InvalidOperationException("AmbientAudioSystem component could not be created");
			}
			_ownedAmbientComponents.Add(component5);
			if (first?["fields"] is JObject fields2)
			{
				FillFields(component5, fields2, null);
			}
			if (!PrepareNativeAmbientRouting(gameObject, audioMixerGroup))
			{
				throw new InvalidOperationException("rebuilt native sources could not be routed to the settings mixer");
			}
			if (!VerifyNativeAmbientRouting(gameObject, audioMixerGroup, out var error))
			{
				throw new InvalidOperationException(error);
			}
			if (!TryBindAmbientDependencies(component5, out var error2))
			{
				throw new InvalidOperationException(error2);
			}
			gameObject.SetActive(value: true);
			if (!flag)
			{
				Plugin.Log.LogDebug("[Ambient] root GO was inactive in the bundle — activated");
			}
			int num8 = gameObject.GetComponentsInChildren<AudioSource>(includeInactive: true).Length;
			int num9 = TerminalAudioRouting.RouteTree(gameObject, audioMixerGroup);
			if (num9 != num8)
			{
				throw new InvalidOperationException($"settings mixer disappeared during activation ({num9}/{num8} sources routed)");
			}
			if (!VerifyNativeAmbientRouting(gameObject, audioMixerGroup, out error))
			{
				throw new InvalidOperationException(error);
			}
			_ambientRoot = gameObject;
			_ambientComponents = dictionary2;
			_ambientLifecycle.MarkStaged();
			Component component6 = gameObject.GetComponent(type3);
			if (!TryInitializeAmbient(component6, type3, out var error3, out var deferred))
			{
				if (deferred && _ambientLifecycle.RecordDeferredReadiness())
				{
					Plugin.Log.LogDebug($"[Ambient] native readiness deferred ({_ambientLifecycle.Attempts}/" + $"{3}); waiting for Seasons.OnInitialized");
					return false;
				}
				return FailAmbientAttempt(gameObject, dictionary2, error3);
			}
			if (!TryVerifyAmbientReadiness(component6, type3, gameObject, out var deferred2, out var error4))
			{
				if (deferred2 && _ambientLifecycle.RecordDeferredReadiness())
				{
					Plugin.Log.LogDebug($"[Ambient] native listener/spline readiness deferred ({_ambientLifecycle.Attempts}/" + $"{3}); waiting for target listener setup");
					return false;
				}
				return FailAmbientAttempt(gameObject, dictionary2, error4);
			}
			int num10 = ((!_ambientSilenced) ? WakeGroupPlayers(dictionary2, gameObject) : 0);
			_ambientLifecycle.MarkReady();
			Plugin.Log.LogInfo("[Ambient] AmbientAudioSystem initialized (Initialized=true, EffectsData and season data verified)");
			if (num5 > 0 || num6 > 0 || num10 > 0)
			{
				Plugin.Log.LogInfo($"[Ambient] spline emitters: {num5} group emitter(s) wired (sea/waves), {num6} dead emitter/mover(s) pruned, {num10} loop player(s) woken");
			}
			RepairInteractiveLayers();
			if (_ambientSilenced && gameObject.activeSelf)
			{
				gameObject.SetActive(value: false);
				Plugin.Log.LogDebug("[Ambient] staged silenced — a cutscene is running");
			}
			Plugin.Log.LogInfo($"[Ambient] AMBIENT LAYER READY: {num} component(s) rebuilt, {num4} filled, " + $"{_banks.Count} banks, {num9} source(s) -> Tarkov AmbientOut mixer" + ((num2 > 0) ? $", {num2} left to the sound rig" : "") + ((num3 > 0) ? $", {num3} GO(s) not in the bundle" : ""));
			_seaDiagAt = Time.realtimeSinceStartup + 30f;
			if (_bankClipMisses.Count > 0)
			{
				Plugin.Log.LogWarning($"[Ambient] {_bankClipMisses.Count} bank clip(s) missing from the bundle " + "(rerun Author 26 + rebuild): " + string.Join(", ", new List<string>(_bankClipMisses).GetRange(0, Math.Min(6, _bankClipMisses.Count))) + ((_bankClipMisses.Count > 6) ? "..." : ""));
			}
			return true;
		}
		catch (Exception ex3)
		{
			return FailAmbientAttempt(gameObject, dictionary2, ex3.Message);
		}
	}

	private static bool TryAdvanceStagedAmbient()
	{
		GameObject ambientRoot = _ambientRoot;
		if (!ambientRoot)
		{
			return false;
		}
		Type type = ResolveType("AmbientAudioSystem");
		Component system = ((type != null) ? ambientRoot.GetComponent(type) : null);
		if (!TryInitializeAmbient(system, type, out var error, out var deferred))
		{
			if (deferred && _ambientLifecycle.RecordDeferredReadiness())
			{
				Plugin.Log.LogDebug($"[Ambient] native readiness remains deferred ({_ambientLifecycle.Attempts}/" + $"{3}); waiting for Seasons.OnInitialized");
				return false;
			}
			return FailAmbientAttempt(ambientRoot, _ambientComponents, error);
		}
		if (!TryVerifyAmbientReadiness(system, type, ambientRoot, out var deferred2, out var error2))
		{
			if (deferred2 && _ambientLifecycle.RecordDeferredReadiness())
			{
				Plugin.Log.LogDebug($"[Ambient] native listener/spline readiness remains deferred ({_ambientLifecycle.Attempts}/" + $"{3}); waiting for target listener setup");
				return false;
			}
			return FailAmbientAttempt(ambientRoot, _ambientComponents, error2);
		}
		AudioMixerGroup audioMixerGroup = TerminalAudioRouting.AmbientBed();
		if (!audioMixerGroup)
		{
			return FailAmbientAttempt(ambientRoot, _ambientComponents, "active AmbientOut mixer disappeared while native readiness was deferred");
		}
		if (!VerifyNativeAmbientRouting(ambientRoot, audioMixerGroup, out var error3))
		{
			return FailAmbientAttempt(ambientRoot, _ambientComponents, error3);
		}
		try
		{
			if (!_ambientSilenced)
			{
				WakeGroupPlayers(_ambientComponents, ambientRoot);
			}
			ReleaseAmbientFallback();
			_ambientLifecycle.MarkReady();
			Plugin.Log.LogInfo("[Ambient] AmbientAudioSystem became ready after deferred Seasons initialization");
			RepairInteractiveLayers();
			if (_ambientSilenced && ambientRoot.activeSelf)
			{
				ambientRoot.SetActive(value: false);
			}
			_seaDiagAt = Time.realtimeSinceStartup + 30f;
			return true;
		}
		catch (Exception ex)
		{
			return FailAmbientAttempt(ambientRoot, _ambientComponents, "deferred native ambient handoff failed: " + ex.GetBaseException().Message);
		}
	}

	private static bool TryInitializeAmbient(Component system, Type systemType, out string error, out bool deferred)
	{
		error = null;
		deferred = false;
		if (system == null)
		{
			error = "AmbientAudioSystem component is missing after construction";
			return false;
		}
		MethodInfo methodInfo = AccessTools.PropertyGetter(systemType, "Initialized");
		MethodInfo methodInfo2 = AccessTools.Method(systemType, "Initialize");
		if (methodInfo == null || methodInfo2 == null)
		{
			error = "AmbientAudioSystem readiness API (Initialized/Initialize) is unavailable";
			return false;
		}
		try
		{
			if (!(bool)methodInfo.Invoke(system, null))
			{
				methodInfo2.Invoke(system, null);
			}
			if ((bool)methodInfo.Invoke(system, null))
			{
				return true;
			}
			deferred = IsSeasonControllerDeferred();
			error = "AmbientAudioSystem.Initialize returned without Initialized=true; " + (deferred ? "waiting for Seasons.OnInitialized" : "native init caught an exception or a required dependency is still invalid");
			return false;
		}
		catch (Exception ex)
		{
			error = "AmbientAudioSystem.Initialize failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static bool IsSeasonControllerDeferred()
	{
		Type type = ResolveType("Seasons");
		MethodInfo methodInfo = ((type != null) ? AccessTools.PropertyGetter(type, "Controller") : null);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			return methodInfo.Invoke(null, null) == null;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryVerifyAmbientReadiness(Component system, Type systemType, GameObject root, out bool deferred, out string error)
	{
		if (VerifyNativeAmbientReadiness(system, systemType, root, out deferred, out error))
		{
			return true;
		}
		string error2 = null;
		if (!deferred || !TryRefreshAmbientListener(system, systemType, out error2))
		{
			if (string.IsNullOrEmpty(error))
			{
				error = error2;
			}
			return false;
		}
		return VerifyNativeAmbientReadiness(system, systemType, root, out deferred, out error);
	}

	private static bool TryRefreshAmbientListener(Component system, Type systemType, out string error)
	{
		error = null;
		if (system == null || systemType == null)
		{
			error = "AmbientAudioSystem is unavailable for listener recovery";
			return false;
		}
		MethodInfo methodInfo = AccessTools.Method(systemType, "method_2");
		MethodInfo methodInfo2 = AccessTools.Method(systemType, "method_6");
		if (methodInfo == null || methodInfo2 == null)
		{
			error = "target ambient listener/spline recovery methods are unavailable";
			return false;
		}
		try
		{
			methodInfo.Invoke(system, null);
			methodInfo2.Invoke(system, new object[0]);
			return true;
		}
		catch (Exception ex)
		{
			error = "target ambient listener/spline recovery failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static bool VerifyNativeAmbientReadiness(Component system, Type systemType, GameObject root, out bool deferred, out string error)
	{
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		deferred = false;
		error = null;
		if (!root || !system || systemType == null)
		{
			error = "native AmbientAudioSystem is unavailable while verifying readiness";
			return false;
		}
		try
		{
			AudioListenerConsistencyManager audioListenerConsistencyManager = (Singleton<AudioListenerConsistencyManager>.Instantiated ? Singleton<AudioListenerConsistencyManager>.Instance : null);
			if (!audioListenerConsistencyManager)
			{
				deferred = true;
				error = "target AudioListenerConsistencyManager is not ready";
				return false;
			}
			Component component = ((systemType != null) ? AccessTools.Field(systemType, "_audioSourceCulling") : null)?.GetValue(system) as Component;
			if (!component)
			{
				error = "native AmbientAudioSystem AudioSourceCulling is unavailable";
				return false;
			}
			FieldInfo fieldInfo = AccessTools.Field(component.GetType(), "_transform");
			if (fieldInfo == null)
			{
				error = "native AudioSourceCulling listener field is unavailable";
				return false;
			}
			Transform transform = fieldInfo.GetValue(component) as Transform;
			if (!transform || transform != audioListenerConsistencyManager.transform)
			{
				deferred = true;
				error = "native AudioSourceCulling is not bound to the registered listener";
				return false;
			}
			Type type = ResolveType("AmbientSplineEmitterController");
			if (type == null)
			{
				error = "AmbientSplineEmitterController type is unavailable";
				return false;
			}
			FieldInfo initializedField = AccessTools.Field(type, "_initialized");
			if (initializedField == null)
			{
				error = "AmbientSplineEmitterController readiness field is unavailable";
				return false;
			}
			if (!(((systemType != null) ? AccessTools.Field(systemType, "_splineEmitterControllers") : null)?.GetValue(system) is IEnumerable<Component> enumerable))
			{
				error = "native AmbientAudioSystem spline controller cache is unavailable";
				return false;
			}
			Component component2 = ValueEnumerableExtensions.FirstOrDefault<FromEnumerable<Component>, Component>(ValueEnumerable.AsValueEnumerable<Component>(enumerable), (Func<Component, bool>)((Component component3) => (bool)component3 && !(bool)initializedField.GetValue(component3)));
			if ((bool)component2)
			{
				deferred = true;
				error = "ambient spline controller '" + component2.name + "' did not complete listener initialization";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			error = "ambient listener/spline readiness check failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static bool TryBindAmbientDependencies(Component system, out string error)
	{
		error = null;
		Type type = system?.GetType();
		if (type == null)
		{
			error = "AmbientAudioSystem type is missing while binding dependencies";
			return false;
		}
		FieldInfo fieldInfo = AccessTools.Field(type, "_ambientData");
		if (fieldInfo == null || !TryBindLoadedAmbientAsset(system, fieldInfo, "TerminalSeasonAmbientSoundData", new string[3] { "_windSoundStorage", "_dayTimeAmbientSeasonClips", "_precipitationSoundStorageSo" }, out error))
		{
			return false;
		}
		FieldInfo fieldInfo2 = AccessTools.Field(type, "EffectsData");
		if (fieldInfo2 == null)
		{
			error = "AmbientAudioSystem EffectsData field is unavailable";
			return false;
		}
		if (!TryBindLoadedAmbientAsset(system, fieldInfo2, "TerminalMixerParamsData", new string[4] { "reverbMixerData", "delayMixerData", "effectsSendsMixerData", "effectsVolumesMixerData" }, out error))
		{
			string missingError = error;
			if (!TryBindReconstructedAmbientEffects(system, fieldInfo2, missingError, out error))
			{
				return false;
			}
		}
		try
		{
			AccessTools.Method(type, "method_0")?.Invoke(system, null);
		}
		catch (Exception ex)
		{
			error = "native ambient data cache refresh failed: " + ex.GetBaseException().Message;
			return false;
		}
		return true;
	}

	private static bool TryBindReconstructedAmbientEffects(Component system, FieldInfo effects, string missingError, out string error)
	{
		error = missingError;
		if (!TerminalAmbientEffectsAssets.TryBuildFromLiveMixer(out var effects2, out var error2) || !effects2)
		{
			error = error + "; reconstructed EffectsData graph unavailable: " + error2;
			return false;
		}
		try
		{
			effects.SetValue(system, effects2);
			if (!IsCompleteAmbientAsset(effects.GetValue(system), new string[4] { "reverbMixerData", "delayMixerData", "effectsSendsMixerData", "effectsVolumesMixerData" }))
			{
				error += "; reconstructed EffectsData graph was incomplete after assignment";
				return false;
			}
			Plugin.Log.LogInfo("[Ambient] bound reconstructed TerminalMixerParamsData graph validated against the active MasterMixer");
			return true;
		}
		catch (Exception ex)
		{
			error = error + "; reconstructed EffectsData assignment failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static bool TryBindLoadedAmbientAsset(Component system, FieldInfo field, string expectedName, string[] requiredFields, out string error)
	{
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		error = null;
		object obj = null;
		try
		{
			obj = field.GetValue(system);
		}
		catch
		{
		}
		if (IsCompleteAmbientAsset(obj, requiredFields) && obj is UnityEngine.Object obj3 && string.Equals(obj3.name, expectedName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		Type type = ResolveType(field.FieldType.Name);
		if (type == null)
		{
			error = "required ambient asset type '" + field.FieldType.Name + "' is unavailable; no empty '" + expectedName + "' asset was created";
			return false;
		}
		UnityEngine.Object obj4 = null;
		try
		{
			obj4 = ValueEnumerableExtensions.FirstOrDefault<FromArray<UnityEngine.Object>, UnityEngine.Object>(ValueEnumerable.AsValueEnumerable<UnityEngine.Object>(Resources.FindObjectsOfTypeAll(type)), (Func<UnityEngine.Object, bool>)((UnityEngine.Object loaded) => (bool)loaded && string.Equals(loaded.name, expectedName, StringComparison.OrdinalIgnoreCase) && IsCompleteAmbientAsset(loaded, requiredFields)));
		}
		catch (Exception ex)
		{
			error = "could not inspect loaded '" + expectedName + "': " + ex.Message;
			return false;
		}
		if (!obj4)
		{
			error = "required loaded ambient asset '" + expectedName + "' is absent or incomplete; native startup remains disabled (no empty dependency was created)";
			return false;
		}
		try
		{
			field.SetValue(system, obj4);
			if (!IsCompleteAmbientAsset(field.GetValue(system), requiredFields))
			{
				error = "loaded ambient asset '" + expectedName + "' could not be assigned to " + field.Name;
				return false;
			}
			Plugin.Log.LogInfo("[Ambient] bound loaded native asset '" + expectedName + "' to " + field.Name);
			return true;
		}
		catch (Exception ex2)
		{
			error = "loaded ambient asset '" + expectedName + "' assignment failed: " + ex2.Message;
			return false;
		}
	}

	private static bool IsCompleteAmbientAsset(object value, string[] requiredFields)
	{
		if (!(value is UnityEngine.Object obj) || !obj)
		{
			return false;
		}
		if (requiredFields == null)
		{
			return true;
		}
		foreach (string name in requiredFields)
		{
			FieldInfo fieldInfo = AccessTools.Field(value.GetType(), name);
			if (fieldInfo == null)
			{
				return false;
			}
			object obj2 = null;
			try
			{
				obj2 = fieldInfo.GetValue(value);
			}
			catch
			{
			}
			if (obj2 is UnityEngine.Object obj4)
			{
				if (!obj4)
				{
					return false;
				}
			}
			else if (obj2 == null)
			{
				return false;
			}
		}
		return true;
	}

	private static bool VerifyNativeAmbientRouting(GameObject root, AudioMixerGroup ambientBus, out string error)
	{
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		error = null;
		if (!root || !ambientBus)
		{
			error = "native ambient routing has no root or settings mixer group";
			return false;
		}
		try
		{
			AudioMixer masterMixer = SoundSettingsController.MasterMixer;
			if (!masterMixer || ambientBus.audioMixer != masterMixer)
			{
				error = "native ambient mixer is not owned by the active settings master";
				return false;
			}
			if (!MonoBehaviourSingleton<BetterAudio>.Instantiated || MonoBehaviourSingleton<BetterAudio>.Instance == null || !MonoBehaviourSingleton<BetterAudio>.Instance.Master)
			{
				error = "native BetterAudio master is unavailable while verifying ambient routing";
				return false;
			}
			if (MonoBehaviourSingleton<BetterAudio>.Instance.Master != masterMixer)
			{
				error = "native BetterAudio.Master differs from SoundSettingsController.MasterMixer";
				return false;
			}
			AmbientSoundPlayerGroup ambientSoundPlayerGroup = ValueEnumerableExtensions.FirstOrDefault<FromArray<AmbientSoundPlayerGroup>, AmbientSoundPlayerGroup>(ValueEnumerable.AsValueEnumerable<AmbientSoundPlayerGroup>(root.GetComponentsInChildren<AmbientSoundPlayerGroup>(includeInactive: true)), (Func<AmbientSoundPlayerGroup, bool>)((AmbientSoundPlayerGroup group) => (bool)group && group._outputMixerGroup != ambientBus));
			if ((bool)ambientSoundPlayerGroup)
			{
				error = "ambient group '" + ambientSoundPlayerGroup.name + "' is not routed to the active AmbientOut group";
				return false;
			}
			BaseAmbientSoundPlayer baseAmbientSoundPlayer = ValueEnumerableExtensions.FirstOrDefault<FromArray<BaseAmbientSoundPlayer>, BaseAmbientSoundPlayer>(ValueEnumerable.AsValueEnumerable<BaseAmbientSoundPlayer>(root.GetComponentsInChildren<BaseAmbientSoundPlayer>(includeInactive: true)), (Func<BaseAmbientSoundPlayer, bool>)((BaseAmbientSoundPlayer player) => (bool)player && player._mixerGroup != ambientBus));
			if ((bool)baseAmbientSoundPlayer)
			{
				error = "ambient player '" + baseAmbientSoundPlayer.name + "' is not routed to the active AmbientOut group";
				return false;
			}
			AudioSource audioSource = ValueEnumerableExtensions.FirstOrDefault<FromArray<AudioSource>, AudioSource>(ValueEnumerable.AsValueEnumerable<AudioSource>(root.GetComponentsInChildren<AudioSource>(includeInactive: true)), (Func<AudioSource, bool>)((AudioSource source) => (bool)source && source.outputAudioMixerGroup != ambientBus));
			if ((bool)audioSource)
			{
				error = "ambient source '" + audioSource.name + "' bypasses the active settings mixer";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			error = "native ambient mixer ownership check failed: " + ex.Message;
			return false;
		}
	}

	private static bool FailAmbientAttempt(GameObject root, Dictionary<long, Component> comps, string reason)
	{
		if (_ambientLifecycle.FailAttempt().Retry)
		{
			Plugin.Log.LogDebug($"[Ambient] native attempt {_ambientLifecycle.Attempts}/{3} failed; " + "cleaning up before a bounded retry: " + reason);
			CleanupAmbientAttempt(root, restoreRoot: true, releaseAssets: false);
			_ambientRoot = null;
			_ambientConstructionRoot = null;
			_ambientComponents = null;
			_configuredShorePlayers.Clear();
			_forcedLoopSources.Clear();
			return false;
		}
		_ambientRoot = root;
		_ambientConstructionRoot = root;
		_ambientComponents = comps;
		RemoveOwnedAmbientSystem();
		MuteNativeAmbientSources(root);
		try
		{
			if ((bool)root)
			{
				root.SetActive(!_ambientSilenced);
			}
		}
		catch
		{
		}
		_forcedLoopSources.Clear();
		if (!_ambientFailureReported)
		{
			_ambientFailureReported = true;
			Plugin.Log.LogWarning("[Ambient] native initialization failed after " + $"{3} bounded attempt(s); compatibility ambient ownership is active. " + reason);
		}
		return false;
	}

	private static void CleanupAmbientAttempt(GameObject root, bool restoreRoot, bool releaseAssets)
	{
		try
		{
			if ((bool)root & restoreRoot)
			{
				root.SetActive(value: false);
			}
		}
		catch
		{
		}
		RestoreBorrowedAmbientFields();
		for (int num = _ownedAmbientComponents.Count - 1; num >= 0; num--)
		{
			Component component = _ownedAmbientComponents[num];
			if ((bool)component)
			{
				try
				{
					UnityEngine.Object.DestroyImmediate(component);
				}
				catch
				{
				}
			}
		}
		_ownedAmbientComponents.Clear();
		if (releaseAssets)
		{
			for (int num2 = _ownedAmbientAssets.Count - 1; num2 >= 0; num2--)
			{
				UnityEngine.Object obj3 = _ownedAmbientAssets[num2];
				if ((bool)obj3)
				{
					try
					{
						UnityEngine.Object.DestroyImmediate(obj3);
					}
					catch
					{
					}
				}
			}
			_ownedAmbientAssets.Clear();
		}
		_banks.Clear();
		try
		{
			if ((bool)root & restoreRoot)
			{
				root.SetActive(_ambientRootWasActive);
			}
		}
		catch
		{
		}
	}

	private static void RemoveOwnedAmbientSystem()
	{
		Type type = ResolveType("AmbientAudioSystem");
		for (int num = _ownedAmbientComponents.Count - 1; num >= 0; num--)
		{
			Component component = _ownedAmbientComponents[num];
			if ((bool)component && !(type == null) && type.IsInstanceOfType(component))
			{
				_ownedAmbientComponents.RemoveAt(num);
				try
				{
					UnityEngine.Object.DestroyImmediate(component);
				}
				catch
				{
				}
			}
		}
	}

	private static void MuteNativeAmbientSources(GameObject root)
	{
		if (!root)
		{
			return;
		}
		try
		{
			AudioSource[] componentsInChildren = root.GetComponentsInChildren<AudioSource>(includeInactive: true);
			foreach (AudioSource audioSource in componentsInChildren)
			{
				if ((bool)audioSource)
				{
					audioSource.Stop();
					audioSource.mute = true;
				}
			}
		}
		catch
		{
		}
	}

	private static void TeardownAmbientForRaid()
	{
		ReleaseAmbientFallback();
		GameObject gameObject = (_ambientRoot ? _ambientRoot : _ambientConstructionRoot);
		if ((bool)gameObject)
		{
			MuteNativeAmbientSources(gameObject);
		}
		CleanupAmbientAttempt(gameObject, restoreRoot: true, releaseAssets: true);
		_ambientRoot = null;
		_ambientConstructionRoot = null;
		_ambientComponents = null;
		_configuredShorePlayers.Clear();
		_forcedLoopSources.Clear();
		_seaDiagAt = -1f;
		TerminalAmbientEffectsAssets.Reset();
		if (_seasonInvalidationSubscribed)
		{
			TerminalAmbientSeasonAssets.Invalidating -= OnSeasonAssetsInvalidating;
		}
		_seasonInvalidationSubscribed = false;
		TerminalAmbientSeasonAssets.ResetForRaid();
	}

	private static void OnSeasonAssetsInvalidating()
	{
		if (_ambientLifecycle.InvalidateReadyGraph())
		{
			RemoveOwnedAmbientSystem();
			MuteNativeAmbientSources(_ambientRoot);
			_forcedLoopSources.Clear();
			Plugin.Log.LogWarning("[Ambient] audio device reset invalidated seasonal assets; compatibility playback owns the remainder of this raid");
		}
	}

	private static void CaptureBorrowedAmbientFields(Component component, JObject fields)
	{
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		if (!component || _ownedAmbientComponents.Contains(component))
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal) { "_ambientBank", "_soundBank", "_loopClip", "_mixerGroup", "_outputMixerGroup", "_configs" };
		if (fields != null)
		{
			foreach (JProperty item in fields.Properties())
			{
				hashSet.Add(item.Name);
			}
		}
		foreach (string item2 in hashSet)
		{
			FieldInfo field = AccessTools.Field(component.GetType(), item2);
			if (!(field == null) && !ValueEnumerableExtensions.Any<FromList<BorrowedAmbientFieldState>, BorrowedAmbientFieldState>(ValueEnumerable.AsValueEnumerable<BorrowedAmbientFieldState>(_borrowedAmbientFields), (Func<BorrowedAmbientFieldState, bool>)((BorrowedAmbientFieldState snapshot) => snapshot.Target == component && snapshot.Field == field)))
			{
				try
				{
					_borrowedAmbientFields.Add(new BorrowedAmbientFieldState
					{
						Target = component,
						Field = field,
						Value = field.GetValue(component)
					});
				}
				catch
				{
				}
			}
		}
	}

	private static void RestoreBorrowedAmbientFields()
	{
		for (int num = _borrowedAmbientFields.Count - 1; num >= 0; num--)
		{
			BorrowedAmbientFieldState borrowedAmbientFieldState = _borrowedAmbientFields[num];
			if ((bool)borrowedAmbientFieldState.Target && borrowedAmbientFieldState.Field != null)
			{
				try
				{
					borrowedAmbientFieldState.Field.SetValue(borrowedAmbientFieldState.Target, borrowedAmbientFieldState.Value);
				}
				catch
				{
				}
			}
		}
		_borrowedAmbientFields.Clear();
	}

	private static void WireRefs(Component target, JObject fields, Dictionary<long, Component> comps)
	{
		Type type = target.GetType();
		foreach (JProperty item in fields.Properties())
		{
			FieldInfo fieldInfo = AccessTools.Field(type, item.Name);
			if (fieldInfo == null)
			{
				continue;
			}
			try
			{
				if (item.Value is JObject jObject && jObject["ref"] != null)
				{
					Component component = Ref(comps, jObject);
					if (component != null && fieldInfo.FieldType.IsInstanceOfType(component))
					{
						fieldInfo.SetValue(target, component);
					}
				}
				else
				{
					if (!(item.Value is JArray { Count: >0 } jArray) || !(jArray[0] is JObject jObject2) || jObject2["ref"] == null)
					{
						continue;
					}
					Type type2;
					if (fieldInfo.FieldType.IsArray)
					{
						type2 = fieldInfo.FieldType.GetElementType();
					}
					else
					{
						type2 = (fieldInfo.FieldType.IsGenericType ? fieldInfo.FieldType.GetGenericArguments()[0] : null);
					}
					if (type2 == null)
					{
						continue;
					}
					List<Component> list = new List<Component>();
					foreach (JToken item2 in jArray)
					{
						Component component2 = Ref(comps, item2);
						if (component2 != null && type2.IsInstanceOfType(component2))
						{
							list.Add(component2);
						}
					}
					if (list.Count == 0)
					{
						continue;
					}
					if (fieldInfo.FieldType.IsArray)
					{
						Array array = Array.CreateInstance(type2, list.Count);
						for (int i = 0; i < list.Count; i++)
						{
							array.SetValue(list[i], i);
						}
						fieldInfo.SetValue(target, array);
						continue;
					}
					IList list2 = (IList)Activator.CreateInstance(fieldInfo.FieldType);
					foreach (Component item3 in list)
					{
						list2.Add(item3);
					}
					fieldInfo.SetValue(target, list2);
					continue;
				}
			}
			catch
			{
			}
		}
	}

	private static int WireGroupEmitterConfigs(JObject sound, Dictionary<long, Component> comps)
	{
		Type type = ResolveType("AmbientPlayerGroupSplineEmitter");
		Type type2 = type?.GetNestedType("SoundPlayerConfig", BindingFlags.Public | BindingFlags.NonPublic);
		FieldInfo fieldInfo = ((type != null) ? AccessTools.Field(type, "_configs") : null);
		if (type2 == null || fieldInfo == null)
		{
			return 0;
		}
		FieldInfo fieldInfo2 = AccessTools.Field(type2, "soundPlayer");
		FieldInfo fieldInfo3 = AccessTools.Field(type2, "spreadRange");
		int num = 0;
		foreach (JToken item in Rows(sound, "AmbientPlayerGroupSplineEmitter"))
		{
			if (item["parse_error"] != null || !comps.TryGetValue(item.Value<long>("path_id"), out var value) || value == null)
			{
				continue;
			}
			IList list = (IList)Activator.CreateInstance(fieldInfo.FieldType);
			foreach (JToken item2 in (item["fields"]?["_configs"] as JArray) ?? new JArray())
			{
				Component component = Ref(comps, item2["soundPlayer"]);
				if (!(component == null))
				{
					object obj = Activator.CreateInstance(type2);
					fieldInfo2.SetValue(obj, component);
					JToken jToken = item2["spreadRange"];
					if (jToken != null && fieldInfo3 != null)
					{
						fieldInfo3.SetValue(obj, new Vector2(jToken.Value<float?>("x").GetValueOrDefault(), jToken.Value<float?>("y") ?? 1f));
					}
					list.Add(obj);
				}
			}
			if (list.Count > 0)
			{
				fieldInfo.SetValue(value, list);
				num++;
			}
		}
		return num;
	}

	private static void ConfigureShoreMembership(JObject sound, Dictionary<long, Component> comps)
	{
		_configuredShorePlayers.Clear();
		if (sound == null || comps == null)
		{
			return;
		}
		foreach (long item in TerminalShoreAuthoring.ConfiguredPlayerIds(sound))
		{
			if (comps.TryGetValue(item, out var value) && (bool)value)
			{
				_configuredShorePlayers.Add(value);
			}
		}
		Plugin.Log.LogInfo($"[Ambient] shoreline playback membership: {_configuredShorePlayers.Count} player(s) from the authored sea group");
	}

	private static int WakeGroupPlayers(Dictionary<long, Component> comps, GameObject ambientRoot)
	{
		int num = 0;
		Type type = ResolveType("AmbientPlayerGroupSplineEmitter");
		Type type2 = ResolveType("AmbientPlayerSplineMappedEmitter");
		Type type3 = ResolveType("LoopAmbientSoundPlayer");
		if (type3 == null || !ambientRoot)
		{
			return 0;
		}
		FieldInfo fieldInfo = ((type != null) ? AccessTools.Field(type, "_configs") : null);
		FieldInfo fieldInfo2 = ((type2 != null) ? AccessTools.Field(type2, "_soundPlayer") : null);
		Type type4 = type?.GetNestedType("SoundPlayerConfig", BindingFlags.Public | BindingFlags.NonPublic);
		FieldInfo fieldInfo3 = ((type4 != null) ? AccessTools.Field(type4, "soundPlayer") : null);
		FieldInfo fieldInfo4 = AccessTools.Field(type3, "_loopClip");
		HashSet<Component> hashSet = new HashSet<Component>();
		foreach (KeyValuePair<long, Component> comp in comps)
		{
			Component value = comp.Value;
			if (value == null)
			{
				continue;
			}
			if (type != null && type.IsInstanceOfType(value) && fieldInfo != null)
			{
				if (!(fieldInfo.GetValue(value) is IList list))
				{
					continue;
				}
				foreach (object item in list)
				{
					Component component = fieldInfo3?.GetValue(item) as Component;
					if ((bool)component)
					{
						hashSet.Add(component);
					}
				}
			}
			else if (type2 != null && type2.IsInstanceOfType(value) && fieldInfo2 != null)
			{
				Component component2 = fieldInfo2.GetValue(value) as Component;
				if ((bool)component2)
				{
					hashSet.Add(component2);
				}
			}
		}
		foreach (Component item2 in hashSet)
		{
			if (!type3.IsInstanceOfType(item2) || (IsUnderSeaGroup(item2.transform) && !IsConfiguredShorePlayer(item2)) || fieldInfo4?.GetValue(item2) as AudioClip == null)
			{
				continue;
			}
			Transform transform = item2.transform;
			while (transform != null && transform.gameObject != ambientRoot)
			{
				if (!transform.gameObject.activeSelf)
				{
					transform.gameObject.SetActive(value: true);
				}
				transform = transform.parent;
			}
			BaseAmbientSoundPlayer baseAmbientSoundPlayer = item2 as BaseAmbientSoundPlayer;
			if (!(baseAmbientSoundPlayer == null))
			{
				AudioSource source = baseAmbientSoundPlayer.Source;
				TerminalAudioRouting.Route(source);
				source.mute = true;
				if (!source.isPlaying)
				{
					baseAmbientSoundPlayer.Play();
				}
				if (!_forcedLoopSources.Contains(source))
				{
					_forcedLoopSources.Add(source);
				}
				num++;
			}
		}
		return num;
	}

	private static int PruneDeadSplineEmitters(Dictionary<long, Component> comps)
	{
		int num = 0;
		Type type = ResolveType("AmbientPlayerSplineMappedEmitter");
		Type type2 = ResolveType("AmbientPlayerGroupSplineEmitter");
		FieldInfo fieldInfo = ((type != null) ? AccessTools.Field(type, "_soundPlayer") : null);
		FieldInfo fieldInfo2 = ((type2 != null) ? AccessTools.Field(type2, "_configs") : null);
		List<long> list = new List<long>(comps.Keys);
		foreach (long item in list)
		{
			Component component = comps[item];
			if (!(component == null))
			{
				bool flag = false;
				if (type != null && fieldInfo != null && type.IsInstanceOfType(component))
				{
					flag = !(fieldInfo.GetValue(component) as Component);
				}
				else if (type2 != null && fieldInfo2 != null && type2.IsInstanceOfType(component))
				{
					flag = !(fieldInfo2.GetValue(component) is IList list2) || list2.Count == 0;
				}
				if (flag)
				{
					UnityEngine.Object.DestroyImmediate(component);
					comps[item] = null;
					num++;
				}
			}
		}
		string[] array = new string[2] { "SplineEmitterPathMover", "SplineTriggerChecker" };
		for (int i = 0; i < array.Length; i++)
		{
			Type type3 = ResolveType(array[i]);
			FieldInfo fieldInfo3 = ((type3 != null) ? AccessTools.Field(type3, "_splineEmitter") : null);
			if (fieldInfo3 == null)
			{
				continue;
			}
			foreach (long item2 in list)
			{
				Component component2 = comps[item2];
				if (!(component2 == null) && type3.IsInstanceOfType(component2) && !(fieldInfo3.GetValue(component2) as Component))
				{
					UnityEngine.Object.DestroyImmediate(component2);
					comps[item2] = null;
					num++;
				}
			}
		}
		return num;
	}

	private static void BindContent(Component comp, JToken row, JObject fields)
	{
		Type type = comp.GetType();
		string text = row.Value<string>("_bankKey");
		if (!string.IsNullOrEmpty(text) && _banks.TryGetValue(text, out var value) && value != null)
		{
			string[] array = new string[2] { "_ambientBank", "_soundBank" };
			foreach (string name in array)
			{
				FieldInfo fieldInfo = AccessTools.Field(type, name);
				if (fieldInfo != null && fieldInfo.FieldType.IsInstanceOfType(value))
				{
					fieldInfo.SetValue(comp, value);
					break;
				}
			}
		}
		string text2 = row.Value<string>("_loopClipName");
		if (string.IsNullOrEmpty(text2))
		{
			return;
		}
		AudioClip audioClip = FindClip(text2);
		if (audioClip != null)
		{
			FieldInfo fieldInfo2 = AccessTools.Field(type, "_loopClip");
			if (fieldInfo2 != null && fieldInfo2.FieldType == typeof(AudioClip))
			{
				fieldInfo2.SetValue(comp, audioClip);
			}
		}
		else
		{
			_bankClipMisses.Add(text2);
		}
	}

	public static void TryApplyAmbientAuthoring()
	{
		if (_ambientMarker != null || AmbientNativeReady || (AmbientStaged && !AmbientFallbackActive))
		{
			return;
		}
		JObject sound = Sidecar()?["scenes"]?["Terminal_Sound"] as JObject;
		if (sound == null)
		{
			return;
		}
		Scene sceneByName = SceneManager.GetSceneByName("Terminal_Sound");
		if (!sceneByName.IsValid() || !sceneByName.isLoaded || !TerminalAudioRouting.AmbientBed())
		{
			return;
		}
		try
		{
			Dictionary<string, List<Transform>> goIndex = BuildGoIndex();
			int tuned = 0;
			int stopped = 0;
			int governed = 0;
			bool routingPending = false;
			string[] loopPlayerClasses = LoopPlayerClasses;
			for (int i = 0; i < loopPlayerClasses.Length; i++)
			{
				Apply(loopPlayerClasses[i], isLoop: true);
			}
			loopPlayerClasses = ShotPlayerClasses;
			for (int i = 0; i < loopPlayerClasses.Length; i++)
			{
				Apply(loopPlayerClasses[i], isLoop: false);
			}
			if (routingPending)
			{
				ReleaseAmbientFallback();
				return;
			}
			_ambientMarker = new GameObject("Terminal_AmbientAuthored");
			SceneManager.MoveGameObjectToScene(_ambientMarker, sceneByName);
			Plugin.Log.LogInfo($"[Acoustics] ambient authoring applied: {tuned} source(s) set to retail volumes/rolloffs, " + $"{stopped} ungated loop(s) stopped, {governed} random re-trigger governor(s) attached");
			void Apply(string cls, bool isLoop)
			{
				foreach (JToken item in Rows(sound, cls))
				{
					if (item["parse_error"] == null)
					{
						JObject jObject = item["fields"] as JObject;
						Transform transform = FindGo(goIndex, item);
						if (jObject != null && !(transform == null) && (_configuredShorePlayers.Count <= 0 || !IsUnderSeaGroup(transform)))
						{
							float volume = jObject.Value<float?>("_volume") ?? 1f;
							float spatialBlend = jObject.Value<float?>("_spatialBlend") ?? 1f;
							float minDistance = jObject.Value<float?>("_minDistance") ?? 1f;
							float maxDistance = jObject.Value<float?>("_maxDistance") ?? 40f;
							bool flag = jObject.Value<int?>("_playOnAwake").GetValueOrDefault() != 0;
							JObject jObject2 = jObject["_randomTimeRange"] as JObject;
							AnimationCurve animationCurve = Curve(jObject["_rolloffCurve"]);
							AudioSource[] array = transform.GetComponents<AudioSource>();
							if (array.Length == 0)
							{
								array = transform.GetComponentsInChildren<AudioSource>(includeInactive: true);
							}
							AudioSource[] array2 = array;
							foreach (AudioSource audioSource in array2)
							{
								if (!(audioSource == null))
								{
									CaptureFallbackSource(audioSource);
									if (!TerminalAudioRouting.Route(audioSource))
									{
										routingPending = true;
									}
									else
									{
										_fallbackSources.Add(audioSource);
										audioSource.volume = volume;
										audioSource.spatialBlend = spatialBlend;
										audioSource.minDistance = minDistance;
										audioSource.maxDistance = maxDistance;
										if (animationCurve != null && animationCurve.length > 0)
										{
											audioSource.rolloffMode = AudioRolloffMode.Custom;
											audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, animationCurve);
										}
										audioSource.mute = _ambientSilenced;
										if (_ambientSilenced)
										{
											audioSource.Stop();
										}
										if (isLoop)
										{
											audioSource.loop = true;
											if (!_ambientSilenced && !audioSource.isPlaying && audioSource.clip != null)
											{
												audioSource.Play();
											}
										}
										else
										{
											audioSource.loop = false;
											if (!flag && audioSource.isPlaying)
											{
												audioSource.Stop();
												stopped++;
											}
											float valueOrDefault = (jObject2?.Value<float?>("x")).GetValueOrDefault();
											float valueOrDefault2 = (jObject2?.Value<float?>("y")).GetValueOrDefault();
											if (valueOrDefault2 > 0.5f && audioSource.clip != null && transform.GetComponent<AmbientGovernor>() == null)
											{
												AmbientGovernor ambientGovernor = transform.gameObject.AddComponent<AmbientGovernor>();
												ambientGovernor.Src = audioSource;
												ambientGovernor.MinWait = Mathf.Max(valueOrDefault, 4f);
												ambientGovernor.MaxWait = Mathf.Max(valueOrDefault2, ambientGovernor.MinWait + 1f);
												_fallbackGovernors.Add(ambientGovernor);
												governed++;
											}
										}
										tuned++;
									}
								}
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			ReleaseAmbientFallback();
			Plugin.Log.LogWarning("[Acoustics] ambient authoring failed: " + ex.Message);
		}
	}

	private static void CaptureFallbackSource(AudioSource source)
	{
		if ((bool)source && !_fallbackSourceStates.ContainsKey(source))
		{
			_fallbackSourceStates[source] = new AmbientFallbackSourceState
			{
				Clip = source.clip,
				Mixer = source.outputAudioMixerGroup,
				Volume = source.volume,
				SpatialBlend = source.spatialBlend,
				MinDistance = source.minDistance,
				MaxDistance = source.maxDistance,
				Pitch = source.pitch,
				Spread = source.spread,
				Loop = source.loop,
				PlayOnAwake = source.playOnAwake,
				Mute = source.mute,
				WasPlaying = source.isPlaying,
				RolloffMode = source.rolloffMode,
				RolloffCurve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff),
				SpreadCurve = source.GetCustomCurve(AudioSourceCurveType.Spread)
			};
		}
	}

	private static void ReleaseAmbientFallback()
	{
		for (int num = _fallbackGovernors.Count - 1; num >= 0; num--)
		{
			AmbientGovernor ambientGovernor = _fallbackGovernors[num];
			if ((bool)ambientGovernor)
			{
				UnityEngine.Object.DestroyImmediate(ambientGovernor);
			}
		}
		_fallbackGovernors.Clear();
		foreach (KeyValuePair<AudioSource, AmbientFallbackSourceState> fallbackSourceState in _fallbackSourceStates)
		{
			AudioSource key = fallbackSourceState.Key;
			if (!key)
			{
				continue;
			}
			AmbientFallbackSourceState value = fallbackSourceState.Value;
			try
			{
				key.Stop();
				key.clip = value.Clip;
				key.outputAudioMixerGroup = value.Mixer;
				key.volume = value.Volume;
				key.spatialBlend = value.SpatialBlend;
				key.minDistance = value.MinDistance;
				key.maxDistance = value.MaxDistance;
				key.pitch = value.Pitch;
				key.spread = value.Spread;
				key.loop = value.Loop;
				key.playOnAwake = value.PlayOnAwake;
				key.mute = value.Mute;
				key.rolloffMode = value.RolloffMode;
				if (value.RolloffCurve != null)
				{
					key.SetCustomCurve(AudioSourceCurveType.CustomRolloff, value.RolloffCurve);
				}
				if (value.SpreadCurve != null)
				{
					key.SetCustomCurve(AudioSourceCurveType.Spread, value.SpreadCurve);
				}
				if (value.WasPlaying && key.gameObject.activeInHierarchy && (bool)key.clip && !key.mute)
				{
					key.Play();
				}
			}
			catch
			{
			}
		}
		_fallbackSources.Clear();
		_fallbackSourceStates.Clear();
		if ((bool)_ambientMarker)
		{
			try
			{
				UnityEngine.Object.DestroyImmediate(_ambientMarker);
			}
			catch
			{
			}
			_ambientMarker = null;
		}
	}

	public static void TickSpatialVerdict()
	{
		if (!_spatialVerdict && _spatialTask != null && _spatialTask.IsCompleted)
		{
			_spatialVerdict = true;
			if (_spatialTask.IsFaulted)
			{
				Plugin.Log.LogError($"[Acoustics] SPATIAL INIT FAILED: {_spatialTask.Exception?.GetBaseException()}");
			}
			else
			{
				Plugin.Log.LogWarning("[Acoustics] spatial init finished — SpatialAudioSystem.Initialized=" + $"{SpatialAudioSystem.Initialized} (rooms tracked = ambient gating live)");
			}
		}
	}

	public static void TickSpatialInit()
	{
		if (_spatialKick || !Plugin.SpatialAudio.Value)
		{
			return;
		}
		try
		{
			if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated)
			{
				return;
			}
			SpatialAudioSystem instance = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
			if (instance == null)
			{
				return;
			}
			if (SpatialAudioSystem.Initialized)
			{
				_spatialKick = true;
				return;
			}
			_spatialKick = true;
			Plugin.Log.LogInfo("[Acoustics] driving SpatialAudioSystem.Initialize ourselves (the loader never did)");
			List<Suspended> suspended = SuspendForeign(AccessTools.Method(typeof(SpatialAudioSystem), "Initialize"));
			try
			{
				_spatialTask = instance.Initialize(default);
			}
			finally
			{
				RestoreForeign(suspended);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Acoustics] self-driven spatial init failed: " + ex.Message);
		}
	}

	private static List<Suspended> SuspendForeign(MethodBase target)
	{
		List<Suspended> outp = new List<Suspended>();
		if (target == null)
		{
			return outp;
		}
		try
		{
			Patches patchInfo = Harmony.GetPatchInfo(target);
			if (patchInfo == null)
			{
				return outp;
			}
			Collect(patchInfo.Prefixes, HarmonyPatchType.Prefix);
			Collect(patchInfo.Finalizers, HarmonyPatchType.Finalizer);
			Harmony harmony = new Harmony("com.manimal.terminal.spatialisolation");
			foreach (Suspended item in outp)
			{
				harmony.Unpatch(item.Target, item.Patch.PatchMethod);
			}
			if (outp.Count > 0)
			{
				HashSet<string> hashSet = new HashSet<string>();
				foreach (Suspended item2 in outp)
				{
					hashSet.Add(item2.Patch.owner);
				}
				Plugin.Log.LogInfo($"[Acoustics] suspended {outp.Count} third-party patch(es) on Initialize " + "(" + string.Join(", ", hashSet) + ") — restored right after");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Acoustics] patch suspend failed: " + ex.Message);
		}
		return outp;
		void Collect(IEnumerable<Patch> patches, HarmonyPatchType kind)
		{
			foreach (Patch item3 in patches ?? Array.Empty<Patch>())
			{
				if (item3.owner != null && !item3.owner.StartsWith("com.manimal.terminal", StringComparison.OrdinalIgnoreCase))
				{
					outp.Add(new Suspended
					{
						Target = target,
						Patch = item3,
						Kind = kind
					});
				}
			}
		}
	}

	private static void RestoreForeign(List<Suspended> suspended)
	{
		foreach (Suspended item in suspended)
		{
			try
			{
				Harmony harmony = new Harmony(item.Patch.owner);
				HarmonyMethod harmonyMethod = new HarmonyMethod(item.Patch.PatchMethod)
				{
					priority = item.Patch.priority
				};
				string[] before = item.Patch.before;
				harmonyMethod.before = ((before != null && before.Length != 0) ? item.Patch.before : null);
				string[] after = item.Patch.after;
				harmonyMethod.after = ((after != null && after.Length != 0) ? item.Patch.after : null);
				HarmonyMethod harmonyMethod2 = harmonyMethod;
				harmony.Patch(item.Target, (item.Kind == HarmonyPatchType.Prefix) ? harmonyMethod2 : null, (HarmonyMethod)null, (HarmonyMethod)null, (item.Kind == HarmonyPatchType.Finalizer) ? harmonyMethod2 : null, (HarmonyMethod)null);
			}
			catch
			{
			}
		}
	}

	public static void DriveEnvironment()
	{
		EnvironmentManager instance = EnvironmentManager.Instance;
		if (instance == null)
		{
			return;
		}
		GameWorld instance2 = Singleton<GameWorld>.Instance;
		if (instance2 == null)
		{
			return;
		}
		_driveTick++;
		Player mainPlayer = instance2.MainPlayer;
		if (mainPlayer != null)
		{
			try
			{
				instance.UpdateEnvironmentForPlayer(mainPlayer);
			}
			catch
			{
			}
			if (instance.Environment != _lastEnv)
			{
				_lastEnv = instance.Environment;
				Plugin.Log.LogDebug($"[Acoustics] environment -> {_lastEnv}");
			}
		}
		if ((_driveTick & 3) != 0)
		{
			return;
		}
		List<IPlayer> registeredPlayers = instance2.RegisteredPlayers;
		for (int i = 0; i < registeredPlayers.Count; i++)
		{
			IPlayer player = registeredPlayers[i];
			if (player != null && !player.IsYourPlayer)
			{
				try
				{
					instance.UpdateEnvironmentForPlayer(player);
				}
				catch
				{
				}
			}
		}
	}

	public static bool TryPrepareSpatialAudio(SpatialAudioSystem system)
	{
		if (_spatialMarker != null)
		{
			return true;
		}
		JObject jObject = Sidecar();
		if (!(jObject?["scenes"]?["Terminal_Sound"] is JObject jObject2))
		{
			return false;
		}
		Scene sceneByName = SceneManager.GetSceneByName("Terminal_Sound");
		if (!sceneByName.IsValid() || !sceneByName.isLoaded)
		{
			Plugin.Log.LogDebug("[Acoustics] Terminal_Sound not loaded yet — staging deferred");
			return false;
		}
		try
		{
			if (!EnsureBakeFile(jObject))
			{
				return false;
			}
			JObject jObject3 = jObject["assets"]?["location_info"]?["fields"] as JObject;
			if (!ValidateSpatialGraph(jObject2, jObject3, out var error))
			{
				Plugin.Log.LogError("[Acoustics] RETAIL GRAPH REJECTED: " + error + " — spatial audio remains off");
				return false;
			}
			_clipCache = null;
			_roomTonesBound = 0;
			_volumesFixed = 0;
			_roomToneMisses.Clear();
			Dictionary<string, List<Transform>> dictionary = BuildGoIndex();
			if (!ValidateSpatialSceneBindings(jObject2, dictionary, out var error2))
			{
				Plugin.Log.LogError("[Acoustics] RETAIL GRAPH COULD NOT BIND: " + error2 + " — spatial audio remains off");
				return false;
			}
			Dictionary<long, Component> dictionary2 = new Dictionary<long, Component>();
			List<GameObject> list = new List<GameObject>();
			CreateAll<AudioTriggerArea>(jObject2, "AudioTriggerArea", dictionary, dictionary2, list);
			CreateAll<SpatialAudioPortal>(jObject2, "SpatialAudioPortal", dictionary, dictionary2, list);
			CreateAll<UniversalTriggerSpatialAudioPortal>(jObject2, "UniversalTriggerSpatialAudioPortal", dictionary, dictionary2, list);
			CreateAll<SpatialAudioRoom>(jObject2, "SpatialAudioRoom", dictionary, dictionary2, list);
			foreach (JToken item in Rows(jObject2, "AudioTriggerArea"))
			{
				AudioTriggerArea audioTriggerArea = Comp<AudioTriggerArea>(dictionary2, item);
				if ((object)audioTriggerArea != null)
				{
					FillArea(audioTriggerArea, item);
				}
			}
			foreach (JToken item2 in Rows(jObject2, "SpatialAudioPortal"))
			{
				SpatialAudioPortal spatialAudioPortal = Comp<SpatialAudioPortal>(dictionary2, item2);
				if ((object)spatialAudioPortal != null)
				{
					FillPortal(spatialAudioPortal, item2, dictionary2);
				}
			}
			foreach (JToken item3 in Rows(jObject2, "UniversalTriggerSpatialAudioPortal"))
			{
				UniversalTriggerSpatialAudioPortal universalTriggerSpatialAudioPortal = Comp<UniversalTriggerSpatialAudioPortal>(dictionary2, item3);
				if ((object)universalTriggerSpatialAudioPortal != null)
				{
					FillPortal(universalTriggerSpatialAudioPortal, item3, dictionary2);
				}
			}
			foreach (JToken item4 in Rows(jObject2, "SpatialAudioRoom"))
			{
				SpatialAudioRoom spatialAudioRoom = Comp<SpatialAudioRoom>(dictionary2, item4);
				if ((object)spatialAudioRoom != null)
				{
					FillRoom(spatialAudioRoom, item4, dictionary2);
				}
			}
			if (dictionary2.Count == 0)
			{
				foreach (GameObject item5 in list)
				{
					item5.SetActive(value: true);
				}
				Plugin.Log.LogWarning("[Acoustics] no acoustic components resolved in the scene — staging abandoned");
				return false;
			}
			foreach (GameObject item6 in list)
			{
				item6.SetActive(value: true);
			}
			if (_roomTonesBound > 0 || _roomToneMisses.Count > 0)
			{
				Plugin.Log.LogInfo($"[Acoustics] bound {_roomTonesBound} authored room tones" + ((_roomToneMisses.Count > 0) ? ("; missing from the bundle: " + string.Join(", ", new List<string>(_roomToneMisses).GetRange(0, Math.Min(8, _roomToneMisses.Count))) + ((_roomToneMisses.Count > 8) ? "..." : "")) : ""));
			}
			string text = Rows(jObject2, "SpatialAudioCrossSceneGroup").First?.Value<string>("go") ?? "SpatialAudioSystem";
			if (dictionary.TryGetValue(text, out var value) && value.Count > 0)
			{
				GameObject gameObject = value[0].gameObject;
				if (gameObject.GetComponent<SpatialAudioCrossSceneGroup>() == null)
				{
					gameObject.AddComponent<SpatialAudioCrossSceneGroup>();
				}
			}
			else
			{
				Plugin.Log.LogWarning("[Acoustics] room-tree root '" + text + "' not found — tracker will see no rooms");
			}
			StampDoorIds(jObject2, dictionary2);
			SpatialAudioLocationInfo spatialAudioLocationInfo = ScriptableObject.CreateInstance<SpatialAudioLocationInfo>();
			if (jObject3 != null)
			{
				FillFields(spatialAudioLocationInfo, jObject3, null);
			}
			AccessTools.Field(typeof(SpatialAudioSystem), "_locationInfo").SetValue(system, spatialAudioLocationInfo);
			if (system.OcclusionSettings == null)
			{
				AudioOcclusionSettings audioOcclusionSettings = ScriptableObject.CreateInstance<AudioOcclusionSettings>();
				if (jObject["assets"]?["occlusion_settings"]?["fields"] is JObject fields)
				{
					FillFields(audioOcclusionSettings, fields, null);
				}
				system.OcclusionSettings = audioOcclusionSettings;
			}
			if (system.poolsConfig == null)
			{
				system.poolsConfig = new SpatialAudioPoolsConfig();
				if (jObject2["SpatialAudioSystem"] is JArray { First: var first } && first?["fields"]?["poolsConfig"] is JObject fields2)
				{
					FillFields(system.poolsConfig, fields2, null);
				}
			}
			if (_volumesFixed > 0)
			{
				Plugin.Log.LogWarning($"[Acoustics] {_volumesFixed} acoustic collider(s) were SOLID — made triggers " + "(a solid portal in a doorway blocks the interaction ray and the door goes promptless)");
			}
			RepairInteractiveLayers();
			_spatialMarker = new GameObject("Terminal_AcousticsStaged");
			SceneManager.MoveGameObjectToScene(_spatialMarker, sceneByName);
			Plugin.Log.LogInfo($"[Acoustics] SPATIAL AUDIO STAGED: {dictionary2.Count} components rehydrated — letting BSG's Initialize run for real");
			return true;
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Acoustics] spatial audio staging failed (falling back to no-spatial): {arg}");
			return false;
		}
	}

	private static bool EnsureBakeFile(JObject sc)
	{
		string text = sc["assets"]?["location_info"]?["fields"]?.Value<string>("relativeBakeDataPath") ?? "AudioBakeData\\terminal_sound.audiobakedata";
		string text2 = Path.Combine(Application.streamingAssetsPath, text.Replace('\\', Path.DirectorySeparatorChar));
		if (File.Exists(text2))
		{
			return true;
		}
		string text3 = Path.Combine(DataDir, Path.GetFileName(text2));
		if (!File.Exists(text3))
		{
			Plugin.Log.LogWarning("[Acoustics] bake file missing: copy the 1.0 client's StreamingAssets/AudioBakeData/" + Path.GetFileName(text2) + " into " + DataDir + " — spatial audio stays off until then");
			return false;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(text2) ?? ".");
		File.Copy(text3, text2);
		Plugin.Log.LogInfo("[Acoustics] installed audio bake -> " + text2);
		return true;
	}

	private static Dictionary<string, List<Transform>> BuildGoIndex()
	{
		Dictionary<string, List<Transform>> dictionary = new Dictionary<string, List<Transform>>();
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (sceneAt.isLoaded && sceneAt.name.StartsWith("Terminal"))
			{
				GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
				foreach (GameObject gameObject in rootGameObjects)
				{
					Walk(gameObject.transform, gameObject.name, dictionary);
				}
			}
		}
		return dictionary;
	}

	private static void Walk(Transform t, string path, Dictionary<string, List<Transform>> index)
	{
		if (!index.TryGetValue(path, out var value))
		{
			value = (index[path] = new List<Transform>(1));
		}
		value.Add(t);
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			Walk(child, path + "/" + child.name, index);
		}
	}

	private static Transform FindGo(Dictionary<string, List<Transform>> index, JToken row)
	{
		string text = row.Value<string>("go");
		if (text == null || !index.TryGetValue(text, out var value))
		{
			return null;
		}
		if (value.Count == 1)
		{
			return value[0];
		}
		JToken jToken = row["trs"]?["pos"];
		if (jToken == null)
		{
			return value[0];
		}
		Vector3 vector = V3(jToken);
		Transform result = null;
		float num = float.MaxValue;
		foreach (Transform item in value)
		{
			float sqrMagnitude = (item.localPosition - vector).sqrMagnitude;
			if (sqrMagnitude < num)
			{
				num = sqrMagnitude;
				result = item;
			}
		}
		return result;
	}

	internal static void RepairInteractiveLayers()
	{
		try
		{
			int num = LayerMask.NameToLayer("Interactive");
			int num2 = LayerMask.NameToLayer("Triggers");
			if (num < 0 || num2 < 0)
			{
				return;
			}
			int num3 = 0;
			WorldInteractiveObject[] array = UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>();
			foreach (WorldInteractiveObject worldInteractiveObject in array)
			{
				if (!(worldInteractiveObject == null) && worldInteractiveObject.gameObject.layer == num2)
				{
					worldInteractiveObject.gameObject.layer = num;
					num3++;
				}
			}
			if (num3 > 0)
			{
				Plugin.Log.LogWarning($"[Acoustics] {num3} interactive object(s) had been moved to the Triggers " + "layer — put back on Interactive so their prompts work");
			}
		}
		catch
		{
		}
	}

	private static JArray Rows(JObject scene, string cls)
	{
		return (scene[cls] as JArray) ?? new JArray();
	}

	private static long? RefId(JToken token)
	{
		return (token as JObject)?.Value<long?>("ref");
	}

	private static bool ValidateSpatialGraph(JObject sound, JObject info, out string error)
	{
		error = null;
		JToken[] array = new List<JToken>(Rows(sound, "SpatialAudioRoom")).ToArray();
		List<JToken> list = new List<JToken>(Rows(sound, "SpatialAudioPortal"));
		list.AddRange(Rows(sound, "UniversalTriggerSpatialAudioPortal"));
		JToken[] array2 = list.ToArray();
		JToken[] array3 = new List<JToken>(Rows(sound, "AudioTriggerArea")).ToArray();
		int num = info?.Value<int?>("roomsCount") ?? 76;
		int num2 = info?.Value<int?>("portalsCount") ?? 220;
		if (array.Length != num || array2.Length != num2)
		{
			error = $"component counts are {array.Length} rooms / {array2.Length} portals; bake requires {num} / {num2}";
			return false;
		}
		HashSet<long> hashSet = new HashSet<long>();
		HashSet<long> hashSet2 = new HashSet<long>();
		HashSet<long> hashSet3 = new HashSet<long>();
		HashSet<int> hashSet4 = new HashSet<int>();
		HashSet<int> hashSet5 = new HashSet<int>();
		JToken[] array4 = array;
		foreach (JToken jToken in array4)
		{
			hashSet.Add(jToken.Value<long>("path_id"));
			hashSet4.Add(jToken["fields"]?.Value<int?>("_iD") ?? (-1));
		}
		array4 = array2;
		foreach (JToken jToken2 in array4)
		{
			hashSet2.Add(jToken2.Value<long>("path_id"));
			hashSet5.Add(jToken2["fields"]?.Value<int?>("_iD") ?? (-1));
		}
		array4 = array3;
		foreach (JToken jToken3 in array4)
		{
			hashSet3.Add(jToken3.Value<long>("path_id"));
		}
		bool flag = hashSet4.Count == num;
		bool flag2 = hashSet5.Count == num2;
		for (int j = 1; j <= num; j++)
		{
			flag &= hashSet4.Contains(j);
		}
		for (int k = 1; k <= num2; k++)
		{
			flag2 &= hashSet5.Contains(k);
		}
		if (!flag)
		{
			error = "room IDs are not the unique contiguous bake range";
			return false;
		}
		if (!flag2)
		{
			error = "portal IDs are not the unique contiguous bake range";
			return false;
		}
		Dictionary<long, HashSet<long>> dictionary = new Dictionary<long, HashSet<long>>();
		array4 = array2;
		foreach (JToken jToken4 in array4)
		{
			long key = jToken4.Value<long>("path_id");
			List<long> list2 = new List<long>();
			foreach (JToken item in (jToken4["fields"]?["_connectedRooms"] as JArray) ?? new JArray())
			{
				long? num3 = RefId(item);
				if (num3.HasValue)
				{
					list2.Add(num3.Value);
				}
			}
			if (list2.Count != 2 || list2[0] == list2[1] || !hashSet.Contains(list2[0]) || !hashSet.Contains(list2[1]))
			{
				error = string.Format("portal {0} does not connect two valid distinct rooms", jToken4["fields"]?.Value<int?>("_iD"));
				return false;
			}
			dictionary[key] = new HashSet<long>(list2);
		}
		Dictionary<long, int> dictionary2 = new Dictionary<long, int>();
		foreach (long item2 in hashSet2)
		{
			dictionary2.Add(item2, 0);
		}
		array4 = array;
		foreach (JToken jToken5 in array4)
		{
			long num4 = jToken5.Value<long>("path_id");
			foreach (JToken item3 in (jToken5["fields"]?["Areas"] as JArray) ?? new JArray())
			{
				long? num5 = RefId(item3);
				if (!num5.HasValue || !hashSet3.Contains(num5.Value))
				{
					error = string.Format("room {0} references a missing trigger area", jToken5["fields"]?.Value<int?>("_iD"));
					return false;
				}
			}
			foreach (JToken item4 in (jToken5["fields"]?["roomConnections"] as JArray) ?? new JArray())
			{
				long? num6 = RefId(item4["connectedRoom"]);
				if (!num6.HasValue || !hashSet.Contains(num6.Value) || num6.Value == num4)
				{
					error = string.Format("room {0} has an invalid room connection", jToken5["fields"]?.Value<int?>("_iD"));
					return false;
				}
				foreach (JToken item5 in (item4["connectingPortals"] as JArray) ?? new JArray())
				{
					long? num7 = RefId(item5);
					if (!num7.HasValue || !dictionary.TryGetValue(num7.Value, out var value) || !value.Contains(num4) || !value.Contains(num6.Value))
					{
						error = string.Format("room {0} has a portal wired to the wrong endpoint pair", jToken5["fields"]?.Value<int?>("_iD"));
						return false;
					}
					dictionary2[num7.Value]++;
				}
			}
		}
		foreach (KeyValuePair<long, int> item6 in dictionary2)
		{
			if (item6.Value != 2)
			{
				error = $"portal path {item6.Key} appears in {item6.Value} room connection lists instead of 2";
				return false;
			}
		}
		Plugin.Log.LogInfo($"[Acoustics] RETAIL GRAPH VERIFIED: {num} unique rooms, {num2} unique portals, {array3.Length} trigger areas; all endpoint references are symmetric");
		return true;
	}

	private static bool ValidateSpatialSceneBindings(JObject sound, Dictionary<string, List<Transform>> goIndex, out string error)
	{
		error = null;
		string[] array = new string[4] { "AudioTriggerArea", "SpatialAudioPortal", "UniversalTriggerSpatialAudioPortal", "SpatialAudioRoom" };
		foreach (string text in array)
		{
			foreach (JToken item in Rows(sound, text))
			{
				Transform transform = FindGo(goIndex, item);
				if (transform == null)
				{
					error = text + " path " + item.Value<string>("go") + " was not found in the loaded Terminal scenes";
					return false;
				}
				if (transform.GetComponent<WorldInteractiveObject>() != null)
				{
					error = text + " path " + item.Value<string>("go") + " resolves to an interactive object";
					return false;
				}
			}
		}
		return true;
	}

	private static void CreateAll<T>(JObject scene, string cls, Dictionary<string, List<Transform>> goIndex, Dictionary<long, Component> comps, List<GameObject> reactivate) where T : Component
	{
		int num = 0;
		int num2 = 0;
		foreach (JToken item in Rows(scene, cls))
		{
			Transform transform = FindGo(goIndex, item);
			if (transform == null)
			{
				num++;
				continue;
			}
			GameObject gameObject = transform.gameObject;
			if (gameObject.GetComponent<WorldInteractiveObject>() != null)
			{
				num2++;
				continue;
			}
			if (gameObject.activeSelf)
			{
				gameObject.SetActive(value: false);
				reactivate.Add(gameObject);
			}
			comps[item.Value<long>("path_id")] = gameObject.AddComponent<T>();
		}
		if (num > 0)
		{
			Plugin.Log.LogWarning($"[Acoustics] {cls}: {num} GOs not found in scene (bundle drift?)");
		}
		if (num2 > 0)
		{
			Plugin.Log.LogWarning($"[Acoustics] {cls}: {num2} skipped — the GO carries an interactive object " + "(attaching would move it to the Triggers layer and kill its prompt)");
		}
	}

	private static T Comp<T>(Dictionary<long, Component> comps, JToken row) where T : Component
	{
		if (!comps.TryGetValue(row.Value<long>("path_id"), out var value))
		{
			return null;
		}
		return value as T;
	}

	private static Component Ref(Dictionary<long, Component> comps, JToken refTok)
	{
		long? num = (refTok as JObject)?.Value<long?>("ref");
		if (!num.HasValue || !comps.TryGetValue(num.Value, out var value))
		{
			return null;
		}
		return value;
	}

	private static void MakeAcousticVolume(Component c)
	{
		try
		{
			if (c == null)
			{
				return;
			}
			int num = LayerMask.NameToLayer("Triggers");
			if (num >= 0 && c.gameObject.layer != num)
			{
				c.gameObject.layer = num;
			}
			Collider[] components = c.GetComponents<Collider>();
			foreach (Collider collider in components)
			{
				if (!(collider == null) && !collider.isTrigger)
				{
					collider.isTrigger = true;
					_volumesFixed++;
				}
			}
		}
		catch
		{
		}
	}

	private static void FillArea(AudioTriggerArea area, JToken row)
	{
		JToken? jToken = row["fields"];
		MakeAcousticVolume(area);
		AccessTools.Field(typeof(ServerRoomColliderArea), "areaCollider")?.SetValue(area, area.GetComponent<BoxCollider>());
		int? num = jToken?.Value<int?>("_validatePlayerCenterInside");
		if (num.HasValue)
		{
			AccessTools.Field(typeof(ServerRoomColliderArea), "_validatePlayerCenterInside")?.SetValue(area, num.Value != 0);
		}
	}

	private static void FillPortal(BaseSpatialAudioPortal p, JToken row, Dictionary<long, Component> comps)
	{
		if (!(row["fields"] is JObject jObject))
		{
			return;
		}
		p.Depth = jObject.Value<float?>("Depth") ?? 2f;
		p.portalName = jObject.Value<string>("portalName") ?? p.gameObject.name;
		p.FitToGeometry = jObject.Value<int?>("FitToGeometry").GetValueOrDefault() != 0;
		p.AutoRotate = jObject.Value<int?>("AutoRotate").GetValueOrDefault() != 0;
		p.traversalMaxCost = jObject.Value<float?>("traversalMaxCost").GetValueOrDefault();
		p.ToOutdoor = jObject.Value<int?>("ToOutdoor").GetValueOrDefault() != 0;
		p.openFadeTime = jObject.Value<float?>("openFadeTime") ?? 0.1f;
		p.closeFadeTime = jObject.Value<float?>("closeFadeTime") ?? 0.1f;
		p.openEnvelope = Curve(jObject["openEnvelope"]) ?? p.openEnvelope;
		p.closeEnvelope = Curve(jObject["closeEnvelope"]) ?? p.closeEnvelope;
		MakeAcousticVolume(p);
		p.portalCollider = p.GetComponent<BoxCollider>();
		AccessTools.Field(typeof(BaseSpatialAudioPortal), "_iD").SetValue(p, (short)jObject.Value<int?>("_iD").GetValueOrDefault());
		List<SpatialAudioRoom> list = new List<SpatialAudioRoom>(2);
		foreach (JToken item2 in (jObject["_connectedRooms"] as JArray) ?? new JArray())
		{
			if (Ref(comps, item2) is SpatialAudioRoom item)
			{
				list.Add(item);
			}
		}
		AccessTools.Field(typeof(BaseSpatialAudioPortal), "_connectedRooms").SetValue(p, list);
		string text = jObject.Value<string>("DoorID");
		if (p is SpatialAudioPortal spatialAudioPortal)
		{
			spatialAudioPortal.DoorID = text ?? "";
			spatialAudioPortal.portalType = BaseSpatialAudioPortal.PortalType.Opening;
			spatialAudioPortal.state = BaseSpatialAudioPortal.PortalState.Open;
		}
		else if (p is UniversalTriggerSpatialAudioPortal universalTriggerSpatialAudioPortal)
		{
			universalTriggerSpatialAudioPortal.portalType = BaseSpatialAudioPortal.PortalType.Opening;
			universalTriggerSpatialAudioPortal.state = BaseSpatialAudioPortal.PortalState.Open;
			AccessTools.Field(typeof(UniversalTriggerSpatialAudioPortal), "_openTriggerID")?.SetValue(universalTriggerSpatialAudioPortal, jObject.Value<string>("_openTriggerID") ?? "");
			AccessTools.Field(typeof(UniversalTriggerSpatialAudioPortal), "_closeTriggerID")?.SetValue(universalTriggerSpatialAudioPortal, jObject.Value<string>("_closeTriggerID") ?? "");
		}
	}

	private static AudioClip FindClip(string name)
	{
		if (_recoveredLoopClips.TryGetValue(name, out var value) && (bool)value)
		{
			return value;
		}
		if (_clipCache == null)
		{
			_clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
			AudioClip[] array = Resources.FindObjectsOfTypeAll<AudioClip>();
			foreach (AudioClip audioClip in array)
			{
				if (audioClip != null && !string.IsNullOrEmpty(audioClip.name))
				{
					_clipCache[audioClip.name] = audioClip;
				}
			}
		}
		if (!_clipCache.TryGetValue(name, out var value2))
		{
			return null;
		}
		return value2;
	}

	private static bool IsUnderSeaGroup(Transform transform)
	{
		Transform transform2 = transform;
		while (transform2 != null)
		{
			if (string.Equals(transform2.name, "AmbientSplineEmitterSeaGroup", StringComparison.Ordinal))
			{
				return true;
			}
			transform2 = transform2.parent;
		}
		return false;
	}

	private static void FillRoom(SpatialAudioRoom r, JToken row, Dictionary<long, Component> comps)
	{
		if (!(row["fields"] is JObject jObject))
		{
			return;
		}
		MakeAcousticVolume(r);
		r.priority = jObject.Value<int?>("priority").GetValueOrDefault();
		r.WallOcclusion = jObject.Value<float?>("WallOcclusion") ?? 0.5f;
		r.FitToGeometry = jObject.Value<int?>("FitToGeometry").GetValueOrDefault() != 0;
		r.OnlyWired = (jObject.Value<int?>("OnlyWired") ?? 1) != 0;
		AccessTools.Field(typeof(SpatialAudioRoom), "Outdoor").SetValue(r, jObject.Value<int?>("Outdoor").GetValueOrDefault() != 0);
		AccessTools.Field(typeof(SpatialAudioRoom), "Isolated").SetValue(r, jObject.Value<int?>("Isolated").GetValueOrDefault() != 0);
		AccessTools.Field(typeof(SpatialAudioRoom), "_type").SetValue(r, (EAudioRoomTypeMask)jObject.Value<int?>("_type").GetValueOrDefault());
		AccessTools.Field(typeof(SpatialAudioRoom), "_iD").SetValue(r, (short)(jObject.Value<int?>("_iD") ?? (-1)));
		AccessTools.Field(typeof(SpatialAudioRoom), "_roomSize").SetValue(r, jObject.Value<float?>("_roomSize").GetValueOrDefault());
		if (jObject["_bounds"] is JObject jObject2)
		{
			AccessTools.Field(typeof(SpatialAudioRoom), "_bounds").SetValue(r, new Bounds(V3(jObject2["m_Center"]), V3(jObject2["m_Extent"]) * 2f));
		}
		r.Areas = new List<AudioTriggerArea>();
		foreach (JToken item3 in (jObject["Areas"] as JArray) ?? new JArray())
		{
			if (Ref(comps, item3) is AudioTriggerArea item)
			{
				r.Areas.Add(item);
			}
		}
		r.roomConnections = new List<SpatialAudioRoom.RoomConnection>();
		foreach (JToken item4 in (jObject["roomConnections"] as JArray) ?? new JArray())
		{
			SpatialAudioRoom.RoomConnection roomConnection = new SpatialAudioRoom.RoomConnection
			{
				connectedRoom = (Ref(comps, item4["connectedRoom"]) as SpatialAudioRoom)
			};
			List<BaseSpatialAudioPortal> list = new List<BaseSpatialAudioPortal>();
			foreach (JToken item5 in (item4["connectingPortals"] as JArray) ?? new JArray())
			{
				if (Ref(comps, item5) is BaseSpatialAudioPortal item2)
				{
					list.Add(item2);
				}
			}
			roomConnection.connectingPortals = list.ToArray();
			if (roomConnection.connectedRoom != null)
			{
				r.roomConnections.Add(roomConnection);
			}
		}
		RoomAmbientData roomAmbientData = new RoomAmbientData();
		if (jObject["AmbientData"] is JObject jObject3)
		{
			FillFields(roomAmbientData, jObject3, (string name) => name != "RoomTone" && name != "SeasonSoundPreset");
			string text = jObject3.Value<string>("RoomToneName");
			if (!string.IsNullOrEmpty(text))
			{
				AudioClip audioClip = FindClip(text);
				if (audioClip != null)
				{
					roomAmbientData.RoomTone = audioClip;
					_roomTonesBound++;
				}
				else
				{
					_roomToneMisses.Add(text);
				}
			}
		}
		r.AmbientData = roomAmbientData;
	}

	private static void StampDoorIds(JObject sound, Dictionary<long, Component> comps)
	{
		Door[] array = UnityEngine.Object.FindObjectsOfType<Door>();
		if (array.Length == 0)
		{
			return;
		}
		List<(float, Door, string, string)> list = new List<(float, Door, string, string)>();
		List<(SpatialAudioPortal, string)> list2 = new List<(SpatialAudioPortal, string)>();
		foreach (JToken item3 in Rows(sound, "SpatialAudioPortal"))
		{
			string text = item3["fields"]?.Value<string>("DoorID");
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			SpatialAudioPortal spatialAudioPortal = Comp<SpatialAudioPortal>(comps, item3);
			if ((object)spatialAudioPortal == null)
			{
				continue;
			}
			list2.Add((spatialAudioPortal, text));
			Door[] array2 = array;
			foreach (Door door in array2)
			{
				float sqrMagnitude = (door.transform.position - spatialAudioPortal.transform.position).sqrMagnitude;
				if (sqrMagnitude < 225f)
				{
					list.Add((sqrMagnitude, door, text, door.Id));
				}
			}
		}
		list.Sort(((float d2, Door door, string doorId, string origId) a, (float d2, Door door, string doorId, string origId) b) =>
		{
			int num3 = a.d2.CompareTo(b.d2);
			if (num3 != 0)
			{
				return num3;
			}
			num3 = string.CompareOrdinal(a.doorId, b.doorId);
			return (num3 == 0) ? string.CompareOrdinal(a.origId, b.origId) : num3;
		});
		HashSet<Door> hashSet = new HashSet<Door>();
		HashSet<string> hashSet2 = new HashSet<string>();
		int num = 0;
		int num2 = 0;
		foreach (var item4 in list)
		{
			Door item = item4.Item2;
			string item2 = item4.Item3;
			if (hashSet.Contains(item) || hashSet2.Contains(item2))
			{
				continue;
			}
			hashSet.Add(item);
			hashSet2.Add(item2);
			if (item.Id != item2)
			{
				if (++num2 <= 12)
				{
					Plugin.Log.LogDebug($"[Acoustics] door id RENAME '{item.Id}' -> '{item2}' at {item.transform.position}");
				}
				item.Id = item2;
			}
			num++;
		}
		if (num2 > 12)
		{
			Plugin.Log.LogDebug($"[Acoustics] ...{num2} renames total");
		}
		Plugin.Log.LogDebug($"[Acoustics] stamped retail DoorIDs: {num} doors bound " + $"({list2.Count - num} portal ids unmatched, {array.Length - num} doors unbound)");
	}

	private static void FillFields(object target, JObject fields, Func<string, bool> fieldFilter)
	{
		Type type = target.GetType();
		foreach (JProperty item in fields.Properties())
		{
			if (fieldFilter != null && !fieldFilter(item.Name))
			{
				continue;
			}
			FieldInfo fieldInfo = AccessTools.Field(type, item.Name);
			if (fieldInfo == null)
			{
				continue;
			}
			try
			{
				object obj = ConvertToken(item.Value, fieldInfo.FieldType, fieldInfo.GetValue(target));
				if (obj != null || !fieldInfo.FieldType.IsValueType)
				{
					fieldInfo.SetValue(target, obj);
				}
			}
			catch
			{
			}
		}
	}

	private static object ConvertToken(JToken tok, Type type, object existing)
	{
		if (tok == null || tok.Type == JTokenType.Null)
		{
			return null;
		}
		if (type == typeof(string))
		{
			return tok.Value<string>();
		}
		if (type == typeof(bool))
		{
			return (tok.Type == JTokenType.Boolean) ? tok.Value<bool>() : (tok.Value<int>() != 0);
		}
		if (type == typeof(short))
		{
			return (short)tok.Value<int>();
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, tok.Value<int>());
		}
		if (type.IsPrimitive)
		{
			return Convert.ChangeType(((JValue)tok).Value, type, CultureInfo.InvariantCulture);
		}
		if (type == typeof(AnimationCurve))
		{
			return TerminalSerializedCurves.FromUnity(tok) ?? existing;
		}
		if (tok is JObject jObject)
		{
			if (type == typeof(Vector3))
			{
				return V3(jObject);
			}
			if (type == typeof(Vector2))
			{
				return new Vector2(jObject.Value<float>("x"), jObject.Value<float>("y"));
			}
			if (type == typeof(Quaternion))
			{
				return Q4(jObject);
			}
			if (type == typeof(Bounds))
			{
				return new Bounds(V3(jObject["m_Center"]), V3(jObject["m_Extent"]) * 2f);
			}
			if (type == typeof(LayerMask))
			{
				return (LayerMask)jObject.Value<int?>("m_Bits").GetValueOrDefault();
			}
			if (jObject["ref"] != null)
			{
				return null;
			}
			object obj = existing ?? Activator.CreateInstance(type);
			FillFields(obj, jObject, null);
			return obj;
		}
		if (tok is JArray jArray)
		{
			Type type2;
			if (type.IsArray)
			{
				type2 = type.GetElementType();
			}
			else
			{
				type2 = ((type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) ? type.GetGenericArguments()[0] : null);
			}
			if (type2 == null)
			{
				return null;
			}
			List<object> list = new List<object>(jArray.Count);
			foreach (JToken item in jArray)
			{
				object obj2 = ConvertToken(item, type2, null);
				if (obj2 == null && type2.IsValueType)
				{
					return null;
				}
				list.Add(obj2);
			}
			if (type.IsArray)
			{
				Array array = Array.CreateInstance(type2, list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					array.SetValue(list[i], i);
				}
				return array;
			}
			IList list2 = (IList)Activator.CreateInstance(type);
			{
				foreach (object item2 in list)
				{
					list2.Add(item2);
				}
				return list2;
			}
		}
		return null;
	}

	private static AnimationCurve Curve(JToken tok)
	{
		return TerminalSerializedCurves.FromUnity(tok);
	}

	private static Vector3 V3(JToken t)
	{
		if (t is JArray jArray)
		{
			return new Vector3(jArray[0].Value<float>(), jArray[1].Value<float>(), jArray[2].Value<float>());
		}
		return new Vector3(t.Value<float>("x"), t.Value<float>("y"), t.Value<float>("z"));
	}

	private static Quaternion Q4(JToken t)
	{
		if (t is JArray jArray)
		{
			return new Quaternion(jArray[0].Value<float>(), jArray[1].Value<float>(), jArray[2].Value<float>(), jArray[3].Value<float>());
		}
		return new Quaternion(t.Value<float>("x"), t.Value<float>("y"), t.Value<float>("z"), t.Value<float>("w"));
	}
}
