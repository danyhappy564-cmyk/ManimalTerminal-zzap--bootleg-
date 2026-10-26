using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal class TerminalSoundRig : MonoBehaviour
{
	internal sealed class Patch_AttachAtRaidStart : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(GameWorld), "OnGameStarted");
		}

		[PatchPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On && Plugin.SoundRig.Value && !(_instance != null))
			{
				_instance = new GameObject("Terminal_SoundRig").AddComponent<TerminalSoundRig>();
			}
		}
	}

	private enum Phase
	{
		Waiting,
		Between,
		Attack,
		After
	}

	private const string HolderName = "Terminal_SoundRig_ClipHolder";

	private const string SoundRootName = "SOUND";

	private const string BetweenGroup = "AmbBetween1And2Cutscenes";

	private const string AfterGroup = "AmbAfter2Cutscene";

	private static TerminalSoundRig _instance;

	private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, UnityEngine.Object> _banks = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();

	private Dictionary<string, List<Transform>> _byPath;

	private readonly List<Component> _restored = new List<Component>();

	private readonly List<AudioSource> _alarmSrcs = new List<AudioSource>();

	private GameObject _betweenGo;

	private GameObject _afterGo;

	private TerminalFirefightDirector _firefight;

	private string _firefightStatus = "not configured";

	private bool _initialized;

	private float _startedAt;

	private Phase _phase;

	private bool _betweenVoiced;

	private bool _afterVoiced;

	private JObject _doorOpen;

	private bool _phaseRoutingBlocked;

	private readonly HashSet<string> _missingClips = new HashSet<string>();

	internal static void ResetForNewRaid()
	{
		if (_instance != null)
		{
			UnityEngine.Object.Destroy(_instance.gameObject);
		}
		_instance = null;
	}

	private void Start()
	{
		_startedAt = Time.realtimeSinceStartup;
		try
		{
			Init();
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[SoundRig] init failed: {arg} — rig disabled");
			enabled = false;
		}
	}

	private void Init()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		string text = Path.Combine(directoryName, "plugin-data", "sound_rig_components.json");
		string path = Path.Combine(directoryName, "plugin-data", "sound_rig_banks.json");
		if (!File.Exists(text))
		{
			Plugin.Log.LogWarning("[SoundRig] sidecar missing: " + text);
			enabled = false;
			return;
		}
		GameObject gameObject = FindSoundRoot();
		if (gameObject == null)
		{
			Plugin.Log.LogWarning("[SoundRig] no SOUND object in any Terminal scene — rig not restored");
			enabled = false;
			return;
		}
		IndexClips();
		if (_clips.Count == 0)
		{
			Plugin.Log.LogWarning("[SoundRig] clip holder not found — bank players will be silent (Author 26 + bundle rebuild needed)");
		}
		if (File.Exists(path))
		{
			BuildBanks(JObject.Parse(File.ReadAllText(path)));
		}
		_betweenGo = ChildByName(gameObject.transform, "AmbBetween1And2Cutscenes");
		_afterGo = ChildByName(gameObject.transform, "AmbAfter2Cutscene");
		if (_betweenGo != null)
		{
			_betweenGo.SetActive(value: false);
		}
		if (_afterGo != null)
		{
			_afterGo.SetActive(value: false);
		}
		JArray jArray = JArray.Parse(File.ReadAllText(text));
		bool activeSelf = gameObject.activeSelf;
		gameObject.SetActive(value: false);
		try
		{
			IndexHierarchy(gameObject.transform);
			List<JToken> list = new List<JToken>(jArray.Count);
			foreach (JToken item in jArray)
			{
				if (IsPlayerRecord(item))
				{
					list.Add(item);
				}
			}
			foreach (JToken item2 in jArray)
			{
				if (!IsPlayerRecord(item2))
				{
					list.Add(item2);
				}
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			List<KeyValuePair<JObject, Component>> list2 = new List<KeyValuePair<JObject, Component>>(jArray.Count);
			HashSet<string> hashSet = new HashSet<string>();
			foreach (JObject item3 in list)
			{
				Type type = ResolveType(item3.Value<string>("cls"));
				if (type == null || !typeof(Component).IsAssignableFrom(type))
				{
					num3++;
					hashSet.Add(item3.Value<string>("cls") ?? "?");
					continue;
				}
				Transform transform = TakeTransform(item3.Value<string>("path"), type);
				if (transform == null)
				{
					num2++;
					continue;
				}
				Component component = transform.gameObject.GetComponent(type) ?? transform.gameObject.AddComponent(type);
				if (component == null)
				{
					hashSet.Add(type.Name);
					continue;
				}
				list2.Add(new KeyValuePair<JObject, Component>(item3, component));
				num++;
			}
			int num4 = 0;
			int num5 = 0;
			foreach (KeyValuePair<JObject, Component> item4 in list2)
			{
				if (!(item4.Key["fields"] is JObject jObject2) || item4.Value == null)
				{
					continue;
				}
				foreach (KeyValuePair<string, JToken> item5 in jObject2)
				{
					try
					{
						if (SetField(item4.Value, item5.Key, item5.Value))
						{
							num4++;
						}
					}
					catch
					{
						num5++;
					}
				}
				_restored.Add(item4.Value);
			}
			Plugin.Log.LogInfo($"[SoundRig] rebuilt {num}/{jArray.Count} components " + $"({num4} fields, {num5} field errors, {num2} missing objects, {num3} dead types)" + ((hashSet.Count > 0) ? ("; refused: " + string.Join(", ", new List<string>(hashSet).ToArray())) : ""));
			if (_missingClips.Count > 0)
			{
				Plugin.Log.LogWarning("[SoundRig] clips not found: " + string.Join(", ", new List<string>(_missingClips).ToArray()));
			}
			if (!TerminalAudioRouting.PrepareNativePlayers(gameObject, TerminalAudioRouting.AmbientBed()))
			{
				Plugin.Log.LogWarning("[SoundRig] native ambient mixer fields could not be initialized");
			}
		}
		finally
		{
			gameObject.SetActive(activeSelf);
		}
		int num6 = TerminalAudioRouting.RouteTree(gameObject);
		SetupAlarmAndFirefight(directoryName);
		_initialized = true;
		Plugin.Log.LogInfo($"[SoundRig] up: {_restored.Count} live components, {_banks.Count} banks, " + $"{_clips.Count} clips, {_alarmSrcs.Count} sirens, firefight {((_firefight != null) ? _firefight.Status : _firefightStatus)}, " + $"{num6} source(s) -> Tarkov AmbientOut mixer");
	}

	private static GameObject FindSoundRoot()
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (!sceneAt.isLoaded || sceneAt.name == null || !sceneAt.name.StartsWith("Terminal"))
			{
				continue;
			}
			GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				if (gameObject.name == "SOUND")
				{
					return gameObject;
				}
			}
		}
		return null;
	}

	private static GameObject ChildByName(Transform root, string name)
	{
		for (int i = 0; i < root.childCount; i++)
		{
			if (root.GetChild(i).name == name)
			{
				return root.GetChild(i).gameObject;
			}
		}
		return null;
	}

	private void IndexClips()
	{
		AudioSource[] array = Resources.FindObjectsOfTypeAll<AudioSource>();
		foreach (AudioSource audioSource in array)
		{
			if (!(audioSource == null) && !(audioSource.clip == null))
			{
				Scene scene = audioSource.gameObject.scene;
				if (scene.IsValid() && scene.name != null && scene.name.StartsWith("Terminal") && !_clips.ContainsKey(audioSource.clip.name))
				{
					_clips[audioSource.clip.name] = audioSource.clip;
				}
			}
		}
	}

	private void BuildBanks(JObject wanted)
	{
		Type type = ResolveType("EFT.SoundBank");
		Type type2 = ResolveType("EFT.EnvironmentVariety");
		Type type3 = ResolveType("EFT.DistanceVarity");
		if (type == null || type2 == null || type3 == null)
		{
			Plugin.Log.LogWarning("[SoundRig] SoundBank/EnvironmentVariety/DistanceVarity missing — bank players stay silent");
			return;
		}
		int num = 0;
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, JToken> item in wanted)
		{
			if (!(item.Value is JArray jArray))
			{
				continue;
			}
			List<AudioClip> list2 = new List<AudioClip>();
			foreach (JToken item2 in jArray)
			{
				string text = item2.Value<string>();
				if (text != null && _clips.TryGetValue(text, out var value) && value != null)
				{
					list2.Add(value);
				}
				else if (text != null)
				{
					_missingClips.Add(text);
				}
			}
			if (list2.Count == 0)
			{
				list.Add(item.Key);
				continue;
			}
			ScriptableObject scriptableObject = ScriptableObject.CreateInstance(type);
			scriptableObject.name = item.Key;
			object obj = Activator.CreateInstance(type3);
			AccessTools.Field(type3, "Clips").SetValue(obj, list2.ToArray());
			object obj2 = Activator.CreateInstance(type2);
			Array array = Array.CreateInstance(type3, 1);
			array.SetValue(obj, 0);
			AccessTools.Field(type2, "Clips").SetValue(obj2, array);
			Array array2 = Array.CreateInstance(type2, 1);
			array2.SetValue(obj2, 0);
			AccessTools.Field(type, "Environments").SetValue(scriptableObject, array2);
			AccessTools.Field(type, "HasEnvironment")?.SetValue(scriptableObject, false);
			_banks[item.Key] = scriptableObject;
			num++;
		}
		Plugin.Log.LogDebug($"[SoundRig] rebuilt {num} sound bank(s)" + ((list.Count > 0) ? ("; no clips for: " + string.Join(", ", list.ToArray())) : ""));
	}

	private void SetupAlarmAndFirefight(string dir)
	{
		try
		{
			string path = Path.Combine(dir, "plugin-data", "sound_rig.json");
			if (!File.Exists(path))
			{
				return;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(path));
			foreach (JToken item in (jObject["alarm"] as JArray) ?? new JArray())
			{
				string text = item.Value<string>("path");
				if (text != null && _byPath.TryGetValue(text, out var value) && value.Count != 0)
				{
					AudioSource component = value[0].GetComponent<AudioSource>();
					if (!(component == null) && !(component.clip == null))
					{
						component.loop = item.Value<int?>("loop") != 0;
						component.playOnAwake = false;
						_alarmSrcs.Add(component);
					}
				}
			}
			if (!Plugin.SoundRigFirefight.Value)
			{
				_firefightStatus = "off (config)";
				Plugin.Log.LogInfo("[SoundRig] firefight OFF (config A/B)");
			}
			else
			{
				TerminalFirefightDirector terminalFirefightDirector = new TerminalFirefightDirector(dir, _byPath);
				terminalFirefightDirector.LogContentReadiness();
				if (terminalFirefightDirector.Ready)
				{
					_firefight = terminalFirefightDirector;
					_firefightStatus = terminalFirefightDirector.Status;
				}
				else
				{
					_firefightStatus = terminalFirefightDirector.ReadinessSummary;
					terminalFirefightDirector.Dispose();
					Plugin.Log.LogWarning("[SoundRig] firefight not armed: " + _firefightStatus);
				}
			}
			_doorOpen = jObject["doorOpen"] as JObject;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[SoundRig] alarm/firefight setup failed: " + ex.Message);
		}
	}

	internal static string CheckpointDoorId()
	{
		return JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "sound_rig.json")))["doorOpen"]?.Value<string>("doorId");
	}

	private IEnumerator DoorOpenBeat()
	{
		while (TerminalCoop.Active && !TerminalCoop.AttackReleased)
		{
			yield return null;
		}
		if (_doorOpen == null)
		{
			yield break;
		}
		string text = _doorOpen.Value<string>("doorId");
		Door door = null;
		if (!string.IsNullOrEmpty(text))
		{
			Door[] array = UnityEngine.Object.FindObjectsOfType<Door>(includeInactive: true);
			foreach (Door door2 in array)
			{
				if (door2 == null || door2.Id != text)
				{
					continue;
				}
				door = door2;
				try
				{
					door2.Snap = EDoorState.Locked | EDoorState.Shut | EDoorState.Open;
					if (TerminalCoop.Authority && door2.DoorState == EDoorState.Locked)
					{
						door2.DoorState = EDoorState.Shut;
					}
					if (TerminalCoop.Authority)
					{
						door2.KeyId = string.Empty;
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[SoundRig] door unlock failed: " + ex.Message);
				}
				break;
			}
			Plugin.Log.LogInfo("[SoundRig] cutscene door '" + text + "': " + ((door != null) ? "unlocked" : "NOT FOUND"));
		}
		float t0 = Time.realtimeSinceStartup;
		foreach (JToken item in (_doorOpen["sounds"] as JArray) ?? new JArray())
		{
			JObject s = (JObject)item;
			float delay = s.Value<float?>("delay").GetValueOrDefault();
			while (Time.realtimeSinceStartup - t0 < delay)
			{
				yield return null;
			}
			string text2 = s.Value<string>("path");
			if (text2 == null || !_byPath.TryGetValue(text2, out var value) || value.Count == 0)
			{
				continue;
			}
			AudioSource component = value[0].GetComponent<AudioSource>();
			if (!(component == null))
			{
				string text3 = s.Value<string>("clip");
				if (component.clip == null && text3 != null && _clips.TryGetValue(text3, out var value2))
				{
					component.clip = value2;
				}
				if (component.clip == null && text3 != null)
				{
					component.clip = TerminalFxBundle.FindClip(text3);
				}
				if (component.clip == null)
				{
					Plugin.Log.LogWarning("[SoundRig] doorOpen sound '" + text3 + "' has no clip anywhere — silent");
				}
				if (component.clip != null)
				{
					component.loop = false;
					component.volume = (s.Value<float?>("volume") ?? 1f) * Plugin.SoundRigVolume.Value;
					component.Play();
				}
			}
		}
	}

	private void Update()
	{
		using (TerminalTickProfiler.Measure("SoundRig.Update"))
		{
			if (!_initialized)
			{
				return;
			}
			TerminalAudioRouting.ObserveVolumeChanges();
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			if (TerminalAttackCutscene.PlayingNow)
			{
				if (_phase != Phase.Attack)
				{
					EnterAttack();
				}
			}
			else if (TerminalAttackCutscene.FinishedAt >= 0f)
			{
				if (_phase != Phase.After)
				{
					EnterAfter();
				}
			}
			else if (_phase == Phase.Waiting && (TerminalIntroCutscene.FinishedAt >= 0f || realtimeSinceStartup - _startedAt > 180f))
			{
				EnterBetween();
			}
			_firefight?.Tick(realtimeSinceStartup, _phase == Phase.Attack || _phase == Phase.After);
		}
	}

	private void EnterBetween()
	{
		if ((bool)_betweenGo && !PreparePhase(_betweenGo))
		{
			return;
		}
		_phase = Phase.Between;
		if (_betweenGo != null)
		{
			_betweenGo.SetActive(value: true);
			if (!_betweenVoiced)
			{
				_betweenVoiced = true;
				VoiceGroup(_betweenGo);
			}
		}
		Plugin.Log.LogInfo("[SoundRig] phase BETWEEN — pre-attack ambience up");
	}

	private void EnterAttack()
	{
		_phase = Phase.Attack;
		if (_betweenGo != null)
		{
			_betweenGo.SetActive(value: false);
		}
		if (_afterGo != null)
		{
			_afterGo.SetActive(value: false);
		}
		foreach (AudioSource alarmSrc in _alarmSrcs)
		{
			if (alarmSrc != null && alarmSrc.isPlaying)
			{
				alarmSrc.Stop();
			}
		}
		Plugin.Log.LogInfo("[SoundRig] attack cutscene — environmental groups silenced; retail firefight starts");
	}

	private void EnterAfter()
	{
		if ((bool)_afterGo && !PreparePhase(_afterGo))
		{
			return;
		}
		_phase = Phase.After;
		if (_betweenGo != null)
		{
			_betweenGo.SetActive(value: false);
		}
		if (_afterGo != null)
		{
			_afterGo.SetActive(value: true);
			if (!_afterVoiced)
			{
				_afterVoiced = true;
				VoiceGroup(_afterGo);
			}
		}
		if (Plugin.SoundRigAlarm.Value)
		{
			foreach (AudioSource alarmSrc in _alarmSrcs)
			{
				if (alarmSrc != null)
				{
					alarmSrc.Play();
				}
			}
		}
		StartCoroutine(DoorOpenBeat());
		Plugin.Log.LogInfo("[SoundRig] phase AFTER — post-attack ambience up" + (Plugin.SoundRigAlarm.Value ? $", {_alarmSrcs.Count} sirens on" : ""));
	}

	private void VoiceGroup(GameObject group)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (Component item in _restored)
		{
			if (item == null || !item.transform.IsChildOf(group.transform) || !IsPlayer(item))
			{
				continue;
			}
			if (!ApplyToSource(item))
			{
				num3++;
				continue;
			}
			num++;
			if (StartPlayer(item))
			{
				num2++;
			}
			else
			{
				num3++;
			}
		}
		Plugin.Log.LogDebug($"[SoundRig] group '{group.name}': {num} spatialised, {num2} started, {num3} not started");
	}

	private bool PreparePhase(GameObject group)
	{
		if (TerminalAudioRouting.PrepareNativePlayers(group, TerminalAudioRouting.AmbientBed()))
		{
			_phaseRoutingBlocked = false;
			return true;
		}
		if (!_phaseRoutingBlocked)
		{
			_phaseRoutingBlocked = true;
			Plugin.Log.LogWarning("[SoundRig] phase '" + group.name + "' waiting for the settings-controlled mixer");
		}
		return false;
	}

	private bool ApplyToSource(Component player)
	{
		AudioSource component = player.GetComponent<AudioSource>();
		if (component == null)
		{
			return false;
		}
		if (!TerminalAudioRouting.Route(component))
		{
			component.mute = true;
			return false;
		}
		Type type = player.GetType();
		component.playOnAwake = false;
		component.mute = false;
		component.spatialBlend = Get(player, type, "_spatialBlend", 1f);
		component.minDistance = Get(player, type, "_minDistance", 1f);
		component.maxDistance = Get(player, type, "_maxDistance", 20f);
		float t = Mathf.Clamp01(Get(player, type, "_spread", 0f));
		component.spread = Mathf.Lerp(180f, 0f, t);
		AnimationCurve animationCurve = Get<AnimationCurve>(player, type, "_rolloffCurve", null);
		if (animationCurve != null && animationCurve.length > 0)
		{
			component.rolloffMode = AudioRolloffMode.Custom;
			component.SetCustomCurve(AudioSourceCurveType.CustomRolloff, animationCurve);
		}
		component.volume = Mathf.Clamp01(Get(player, type, "_volume", 1f) * Plugin.SoundRigVolume.Value);
		return true;
	}

	private bool StartPlayer(Component player)
	{
		Type type = player.GetType();
		try
		{
			UnityEngine.Object obj = Get<UnityEngine.Object>(player, type, "_soundBank", null) ?? Get<UnityEngine.Object>(player, type, "_ambientBank", null);
			if (obj == null)
			{
				return false;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "Play");
			if (methodInfo != null && methodInfo.GetParameters().Length == 0)
			{
				methodInfo.Invoke(player, null);
				return true;
			}
			AudioSource component = player.GetComponent<AudioSource>();
			if (component == null)
			{
				return false;
			}
			List<AudioClip> list = BankClips(obj);
			if (list == null || list.Count == 0)
			{
				return false;
			}
			component.clip = list[UnityEngine.Random.Range(0, list.Count)];
			component.Play();
			Plugin.Log.LogWarning("[SoundRig] " + type.Name + ".Play() not found — played one bank clip directly");
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[SoundRig] Play failed on " + type.Name + ": " + ex.Message);
			return false;
		}
	}

	private static List<AudioClip> BankClips(UnityEngine.Object bank)
	{
		try
		{
			if (!(AccessTools.Field(bank.GetType(), "Environments").GetValue(bank) is Array { Length: not 0 } array))
			{
				return null;
			}
			object value = array.GetValue(0);
			if (!(AccessTools.Field(value.GetType(), "Clips").GetValue(value) is Array { Length: not 0 } array2))
			{
				return null;
			}
			return (AccessTools.Field(array2.GetValue(0).GetType(), "Clips").GetValue(array2.GetValue(0)) is AudioClip[] collection) ? new List<AudioClip>(collection) : null;
		}
		catch
		{
			return null;
		}
	}

	private void OnDestroy()
	{
		foreach (AudioSource alarmSrc in _alarmSrcs)
		{
			if (alarmSrc != null && alarmSrc.isPlaying)
			{
				alarmSrc.Stop();
			}
		}
		_firefight?.Dispose();
		if (_instance == this)
		{
			_instance = null;
		}
	}

	private static bool IsPlayerRecord(JToken rec)
	{
		return ((rec as JObject)?.Value<string>("cls"))?.EndsWith("AmbientSoundPlayer", StringComparison.Ordinal) ?? false;
	}

	private static bool IsPlayer(Component c)
	{
		if (c == null)
		{
			return false;
		}
		Type type = c.GetType();
		while (type != null)
		{
			if (type.Name == "BaseAmbientSoundPlayer")
			{
				return true;
			}
			type = type.BaseType;
		}
		return false;
	}

	private void IndexHierarchy(Transform root)
	{
		_byPath = new Dictionary<string, List<Transform>>(512);
		Scene scene = root.gameObject.scene;
		if (scene.IsValid() && scene.isLoaded)
		{
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				WalkTf(gameObject.transform, gameObject.name);
			}
		}
		else
		{
			WalkTf(root, root.name);
		}
	}

	private void WalkTf(Transform t, string path)
	{
		if (!_byPath.TryGetValue(path, out var value))
		{
			value = (_byPath[path] = new List<Transform>(1));
		}
		value.Add(t);
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			WalkTf(child, path + "/" + child.name);
		}
	}

	private Transform TakeTransform(string path, Type type)
	{
		if (string.IsNullOrEmpty(path) || !_byPath.TryGetValue(path, out var value))
		{
			return null;
		}
		foreach (Transform item in value)
		{
			if (item != null && item.gameObject.GetComponent(type) == null)
			{
				return item;
			}
		}
		if (value.Count <= 0)
		{
			return null;
		}
		return value[0];
	}

	private static Type ResolveType(string name)
	{
		if (name == null)
		{
			return null;
		}
		if (_types.TryGetValue(name, out var value))
		{
			return value;
		}
		value = AccessTools.TypeByName(name);
		_types[name] = value;
		return value;
	}

	private static FieldInfo FindField(Type type, string name)
	{
		Type type2 = type;
		while (type2 != null)
		{
			FieldInfo field = type2.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return field;
			}
			type2 = type2.BaseType;
		}
		return null;
	}

	private static T Get<T>(object target, Type type, string field, T fallback)
	{
		FieldInfo fieldInfo = FindField(type, field);
		if (fieldInfo == null)
		{
			return fallback;
		}
		object value = fieldInfo.GetValue(target);
		if (value is T)
		{
			return (T)value;
		}
		return fallback;
	}

	private bool SetField(object target, string name, JToken value)
	{
		FieldInfo fieldInfo = FindField(target.GetType(), name);
		if (fieldInfo == null)
		{
			return false;
		}
		object obj = ConvertTok(value, fieldInfo.FieldType);
		if (obj == null && fieldInfo.FieldType.IsValueType)
		{
			return false;
		}
		fieldInfo.SetValue(target, obj);
		return true;
	}

	private object ConvertTok(JToken tok, Type type)
	{
		if (tok == null || tok.Type == JTokenType.Null)
		{
			return null;
		}
		if (type == typeof(float))
		{
			return tok.Value<float>();
		}
		if (type == typeof(double))
		{
			return tok.Value<double>();
		}
		if (type == typeof(int))
		{
			return tok.Value<int>();
		}
		if (type == typeof(long))
		{
			return tok.Value<long>();
		}
		if (type == typeof(byte))
		{
			return (byte)tok.Value<int>();
		}
		if (type == typeof(bool))
		{
			return (tok.Type == JTokenType.Boolean) ? tok.Value<bool>() : (tok.Value<int>() != 0);
		}
		if (type == typeof(string))
		{
			return tok.Value<string>();
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, tok.Value<int>());
		}
		JObject jObject = tok as JObject;
		if (type == typeof(Vector2) && jObject != null)
		{
			return new Vector2(F(jObject, "x"), F(jObject, "y"));
		}
		if (type == typeof(Vector3) && jObject != null)
		{
			return new Vector3(F(jObject, "x"), F(jObject, "y"), F(jObject, "z"));
		}
		if (type == typeof(Vector4) && jObject != null)
		{
			return new Vector4(F(jObject, "x"), F(jObject, "y"), F(jObject, "z"), F(jObject, "w"));
		}
		if (type == typeof(Quaternion) && jObject != null)
		{
			return new Quaternion(F(jObject, "x"), F(jObject, "y"), F(jObject, "z"), F(jObject, "w"));
		}
		if (type == typeof(AnimationCurve) && jObject != null)
		{
			return BuildCurve(jObject);
		}
		if (typeof(UnityEngine.Object).IsAssignableFrom(type) && jObject != null)
		{
			return ResolveReference(jObject, type);
		}
		if (tok is JArray jArray)
		{
			if (type.IsArray)
			{
				Type elementType = type.GetElementType();
				Array array = Array.CreateInstance(elementType, jArray.Count);
				for (int i = 0; i < jArray.Count; i++)
				{
					array.SetValue(ConvertTok(jArray[i], elementType), i);
				}
				return array;
			}
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
			{
				Type type2 = type.GetGenericArguments()[0];
				IList list = (IList)Activator.CreateInstance(type);
				{
					foreach (JToken item in jArray)
					{
						list.Add(ConvertTok(item, type2));
					}
					return list;
				}
			}
			return null;
		}
		if (jObject != null && !type.IsPrimitive)
		{
			object obj;
			try
			{
				obj = Activator.CreateInstance(type);
			}
			catch
			{
				return null;
			}
			{
				foreach (KeyValuePair<string, JToken> item2 in jObject)
				{
					try
					{
						SetField(obj, item2.Key, item2.Value);
					}
					catch
					{
					}
				}
				return obj;
			}
		}
		return null;
	}

	private static float F(JObject o, string k)
	{
		JToken jToken = o[k];
		if (jToken != null && jToken.Type != JTokenType.Null)
		{
			return jToken.Value<float>();
		}
		return 0f;
	}

	private static AnimationCurve BuildCurve(JObject obj)
	{
		return TerminalSerializedCurves.FromUnity(obj) ?? new AnimationCurve();
	}

	private UnityEngine.Object ResolveReference(JObject obj, Type wanted)
	{
		string text = obj["$AudioClip"]?.Value<string>();
		if (text != null)
		{
			if (_clips.TryGetValue(text, out var value))
			{
				return value;
			}
			_missingClips.Add(text);
			return null;
		}
		string text2 = obj["$asset:SoundBank"]?.Value<string>();
		if (text2 != null)
		{
			if (!_banks.TryGetValue(text2, out var value2) || !wanted.IsInstanceOfType(value2))
			{
				return null;
			}
			return value2;
		}
		string text3 = obj["$ref"]?.Value<string>();
		if (text3 != null)
		{
			if (!_byPath.TryGetValue(text3, out var value3))
			{
				return null;
			}
			foreach (Transform item in value3)
			{
				Component component = item.GetComponent(wanted);
				if (component != null)
				{
					return component;
				}
			}
			return null;
		}
		return null;
	}

	internal static string PathOf(Transform t)
	{
		StringBuilder stringBuilder = new StringBuilder(t.name);
		Transform parent = t.parent;
		while (parent != null)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}
}
