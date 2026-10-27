using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Audio.AmbientSubsystem;
using EFT.CameraControl;
using EFT.Weather;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalWeather
{
	private static JObject _sidecar;

	private static bool _sidecarTried;

	private static GameObject _marker;

	private static bool _rainVisualReady;

	private static float _nextRainVisualAttempt;

	private static RainController _rainController;

	private static Camera _rainCamera;

	private static readonly Dictionary<long, Component> _comps = new Dictionary<long, Component>();

	private static readonly Dictionary<string, Transform> _pathIndex = new Dictionary<string, Transform>();

	private static readonly Dictionary<string, UnityEngine.Object> _assetCache = new Dictionary<string, UnityEngine.Object>();

	private static readonly Dictionary<string, UnityEngine.Object> _settingsCache = new Dictionary<string, UnityEngine.Object>();

	private static readonly List<string> _missingAssets = new List<string>();

	private static Texture2D _terminalCloudMap;

	private static Texture2D _terminalRainRipple;

	private static readonly HashSet<string> SkipClasses = new HashSet<string> { "TOD_Sky", "TOD_Components", "TOD_Resources", "TOD_Time", "SnowFlakes", "SnowRippleController", "SnowWetRenderer", "WinterEventVisual", "SceneLights" };

	private static readonly HashSet<string> ForceDisabled = new HashSet<string> { "Tonemapping", "AmbientLight", "AmbientHighlight" };

	private static float _probeAt = -1f;

	private static int _probePasses;

	internal static bool Staged
	{
		get
		{
			if (!_marker)
			{
				return TerminalWeatherLifecycle.HasNativeWeather(WeatherController.Instance);
			}
			return true;
		}
	}

	internal static void ResetForRaid()
	{
		_comps.Clear();
		_pathIndex.Clear();
		_assetCache.Clear();
		_settingsCache.Clear();
		_terminalCloudMap = null;
		_terminalRainRipple = null;
		_marker = null;
		_rainVisualReady = false;
		_nextRainVisualAttempt = 0f;
		_rainController = null;
		_rainCamera = null;
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
			string text = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_weather.json");
			if (!File.Exists(text))
			{
				Plugin.Log.LogDebug("[Weather] no sidecar at " + text + " — weather stays off");
				return null;
			}
			_sidecar = JObject.Parse(File.ReadAllText(text));
			Plugin.Log.LogInfo("[Weather] sidecar loaded");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Weather] sidecar parse failed: " + ex.Message);
		}
		return _sidecar;
	}

	public static bool TryStage()
	{
		if ((bool)_marker)
		{
			return true;
		}
		if (!Plugin.WeatherStack.Value)
		{
			return false;
		}
		WeatherController instance = WeatherController.Instance;
		if (TerminalWeatherLifecycle.HasNativeWeather(instance) && !TerminalWeatherLifecycle.IsReconstructed(instance))
		{
			_rainController = instance.RainController;
			return true;
		}
		if (!(Sidecar()?["components"] is JObject jObject) || !TerminalLoaded.Check())
		{
			return false;
		}
		try
		{
			_comps.Clear();
			_pathIndex.Clear();
			_assetCache.Clear();
			_settingsCache.Clear();
			_missingAssets.Clear();
			List<Transform> list = new List<Transform>();
			string[] array = new string[2] { "Weather", "Sky Dome" };
			foreach (string text in array)
			{
				Transform transform = FindSceneGo(text);
				if (!transform)
				{
					Plugin.Log.LogDebug("[Weather] root '" + text + "' not in scene — aborting");
					return false;
				}
				list.Add(transform);
				IndexSubtree(transform, text);
			}
			List<GameObject> list2 = new List<GameObject>();
			foreach (Transform item3 in list)
			{
				if (item3.gameObject.activeSelf)
				{
					item3.gameObject.SetActive(value: false);
					list2.Add(item3.gameObject);
				}
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			List<(JToken, Component)> list3 = new List<(JToken, Component)>();
			List<JToken> list4 = new List<JToken>();
			foreach (JProperty item4 in jObject.Properties())
			{
				if (!(item4.Value is JArray jArray))
				{
					continue;
				}
				foreach (JToken item5 in jArray)
				{
					if ((item5.Value<string>("class") ?? "") != "EFT.Weather.WeatherController")
					{
						list4.Add(item5);
					}
				}
			}
			foreach (JProperty item6 in jObject.Properties())
			{
				if (!(item6.Value is JArray jArray2))
				{
					continue;
				}
				foreach (JToken item7 in jArray2)
				{
					if ((item7.Value<string>("class") ?? "") == "EFT.Weather.WeatherController")
					{
						list4.Add(item7);
					}
				}
			}
			foreach (JToken item8 in list4)
			{
				string text2 = item8.Value<string>("class") ?? "";
				string item = (text2.Contains('.') ? text2.Substring(text2.LastIndexOf('.') + 1) : text2);
				if (SkipClasses.Contains(item))
				{
					num3++;
					continue;
				}
				Type type = TerminalTypes.ByName(text2);
				if (type == null)
				{
					Plugin.Log.LogDebug("[Weather] type not in 4.0: " + text2);
					continue;
				}
				GameObject gameObject = (_pathIndex.TryGetValue(item8.Value<string>("go") ?? "", out var value) ? value.gameObject : null);
				if (!gameObject)
				{
					num2++;
					continue;
				}
				Component component = gameObject.GetComponent(type);
				if (!component)
				{
					component = gameObject.AddComponent(type);
				}
				if ((bool)component)
				{
					_comps[item8.Value<long>("path_id")] = component;
					if (component is RainController rainController)
					{
						_rainController = rainController;
					}
					list3.Add((item8, component));
					num++;
				}
			}
			if (num2 > 0)
			{
				Plugin.Log.LogWarning($"[Weather] {num2} component GOs not found in bundled tree");
			}
			foreach (var (jToken, target) in list3)
			{
				if (jToken["fields"] is JObject fields)
				{
					FillFields(target, fields);
				}
			}
			foreach (var item9 in list3)
			{
				if (item9.Item2 is WeatherController { WeatherDebug: null } weatherController)
				{
					weatherController.WeatherDebug = new WeatherDebug();
					Plugin.Log.LogWarning("[Weather] WeatherDebug didn't fill from the sidecar — fresh instance substituted");
				}
			}
			int num4 = 0;
			foreach (var item10 in list3)
			{
				Component item2 = item10.Item2;
				if (!(item2 is Behaviour behaviour) || item2 is WeatherController)
				{
					continue;
				}
				string name = item2.GetType().Name;
				if (ForceDisabled.Contains(name))
				{
					behaviour.enabled = false;
					num4++;
					continue;
				}
				string text3 = null;
				FieldInfo[] fields2 = item2.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields2)
				{
					if (fieldInfo.FieldType == typeof(Shader) && !(fieldInfo.GetValue(item2) as Shader))
					{
						text3 = "Shader " + fieldInfo.Name;
						break;
					}
					if (fieldInfo.Name == "_close" && fieldInfo.FieldType == typeof(Material) && !(fieldInfo.GetValue(item2) as Material))
					{
						text3 = "Material _close";
						break;
					}
				}
				if (text3 == null)
				{
					continue;
				}
				behaviour.enabled = false;
				num4++;
				_missingAssets.Add(item2.GetType().Name + " (" + text3 + ")");
				Shader shader = Shader.Find("Hidden/BlitCopy");
				if (!shader)
				{
					shader = Shader.Find("Sprites/Default");
				}
				if (!shader)
				{
					continue;
				}
				fields2 = item2.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo2 in fields2)
				{
					if (fieldInfo2.FieldType == typeof(Shader) && !(fieldInfo2.GetValue(item2) as Shader))
					{
						fieldInfo2.SetValue(item2, shader);
					}
				}
			}
			foreach (GameObject item11 in list2)
			{
				item11.SetActive(value: true);
			}
			_probeAt = Time.time + 25f;
			_probePasses = 0;
			_marker = new GameObject("Terminal_WeatherStaged");
			Scene sceneByName = SceneManager.GetSceneByName("Terminal_Scripts");
			if (sceneByName.IsValid() && sceneByName.isLoaded)
			{
				SceneManager.MoveGameObjectToScene(_marker, sceneByName);
			}
			if (_missingAssets.Count > 0)
			{
				HashSet<string> hashSet = new HashSet<string>(_missingAssets);
				Plugin.Log.LogWarning(string.Format("[Weather] MISSING ASSETS ({0}) — add via editor carrier pass: {1}", hashSet.Count, string.Join(", ", hashSet)));
			}
			Plugin.Log.LogWarning($"[Weather] WEATHER STACK REBUILT: {num} component(s) " + string.Format("({0} sky/snow row(s) skipped by design{1}) — ", num3, (num4 > 0) ? $", {num4} disabled for unresolved render assets" : "") + "WeatherController.Instance " + (WeatherController.Instance ? "OK" : "STILL NULL"));
			return true;
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Weather] rebuild failed: {arg}");
			return false;
		}
	}

	internal static bool TryEnsureRainVisualReady()
	{
		if (!Staged)
		{
			return false;
		}
		try
		{
			Camera camera = CameraManager.Instance?.Camera;
			if (!camera)
			{
				_rainVisualReady = false;
				return false;
			}
			if (_rainVisualReady && _rainCamera == camera && (bool)_rainController && _rainController.isActiveAndEnabled)
			{
				return true;
			}
			_rainVisualReady = false;
			if (Time.realtimeSinceStartup < _nextRainVisualAttempt)
			{
				return false;
			}
			_nextRainVisualAttempt = Time.realtimeSinceStartup + 1f;
			RainController rainController = _rainController;
			if (!rainController)
			{
				rainController = (_rainController = UnityEngine.Object.FindObjectOfType<RainController>());
			}
			if (!rainController || !rainController.enabled || !rainController.gameObject.activeInHierarchy)
			{
				return false;
			}
			FieldInfo fieldInfo = AccessTools.Field(typeof(RainController), "_camera");
			Transform transform = fieldInfo?.GetValue(rainController) as Transform;
			if (!transform || transform != camera.transform)
			{
				try
				{
					AccessTools.Method(typeof(RainController), "CameraChanged")?.Invoke(rainController, null);
				}
				catch
				{
				}
				transform = fieldInfo?.GetValue(rainController) as Transform;
				if ((!transform || transform != camera.transform) && fieldInfo != null)
				{
					fieldInfo.SetValue(rainController, camera.transform);
					transform = camera.transform;
				}
			}
			bool flag = AccessTools.Field(typeof(RainController), "_state")?.GetValue(rainController) != null;
			bool flag2 = AccessTools.Field(typeof(RainController), "_rainFallDrops")?.GetValue(rainController) != null;
			bool flag3 = AccessTools.Field(typeof(RainController), "_depthPhotograper")?.GetValue(rainController) != null;
			_rainVisualReady = (transform == camera.transform) & flag & flag2 & flag3;
			if (_rainVisualReady)
			{
				_rainCamera = camera;
				Plugin.Log.LogInfo("[Weather] outdoor rain dependencies ready on camera '" + camera.name + "'; screen effects tracked separately");
			}
			return _rainVisualReady;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[Weather] early rain visual hookup waiting: " + ex.Message);
			return false;
		}
	}

	internal static void TickProbe()
	{
		if (_probeAt < 0f || Time.time < _probeAt)
		{
			return;
		}
		_probeAt = ((++_probePasses < 3) ? (Time.time + 60f) : (-1f));
		bool flag = Plugin.SceneCensus != null && Plugin.SceneCensus.Value;
		try
		{
			RainController rc = _rainController;
			if (!rc)
			{
				rc = UnityEngine.Object.FindObjectOfType<RainController>();
				_rainController = rc;
			}
			if (!rc)
			{
				if (flag)
				{
					Plugin.Log.LogWarning("[Weather] RAIN PROBE: no RainController instance in scene");
				}
			}
			else
			{
				if (flag)
				{
					WeatherController instance = WeatherController.Instance;
					float num = -1f;
					try
					{
						num = (instance ? instance.WeatherCurve.Rain : (-1f));
					}
					catch
					{
					}
					Plugin.Log.LogWarning("[Weather] RAIN PROBE: " + $"enabled={rc.enabled} active={rc.gameObject.activeInHierarchy} " + F("_rainFallDrops") + " " + F("_rippleController") + " " + F("_rainSplashController") + " " + F("_wetRenderer") + " " + F("_depthPhotograper") + " " + F("_camera") + " stateMachine=" + ((AccessTools.Field(typeof(RainController), "_state")?.GetValue(rc) != null) ? "BUILT" : "NOT BUILT") + " " + $"staticIntensity={RainController.Intensity:0.00} curveRain={num:0.00} " + "debugEnabled=" + (((bool)instance && instance.WeatherDebug != null) ? instance.WeatherDebug.isEnabled.ToString() : "n/a"));
				}
				FieldInfo fieldInfo = AccessTools.Field(typeof(RainController), "_camera");
				if (fieldInfo != null && !(fieldInfo.GetValue(rc) as Transform))
				{
					Camera camera = CameraManager.Instance?.Camera;
					if ((bool)camera)
					{
						try
						{
							AccessTools.Method(typeof(RainController), "CameraChanged")?.Invoke(rc, null);
						}
						catch
						{
						}
						if (!(fieldInfo.GetValue(rc) as Transform))
						{
							fieldInfo.SetValue(rc, camera.transform);
						}
						Plugin.Log.LogWarning("[Weather] rain camera anchor healed (transform_0 was null — the load-time hookup ran before the camera existed): now '" + (fieldInfo.GetValue(rc) as Transform)?.name + "'");
						_probeAt = Time.time + 20f;
					}
					else
					{
						Plugin.Log.LogWarning("[Weather] rain anchor still unhealable — no camera yet");
					}
				}
			}
			if (!flag)
			{
				return;
			}
			try
			{
				PrecipitationAmbientBlender[] array = Resources.FindObjectsOfTypeAll<PrecipitationAmbientBlender>();
				if (array.Length == 0)
				{
					Plugin.Log.LogWarning("[Weather] RAIN AUDIO: no PrecipitationAmbientBlender exists AT ALL — staging never built them");
				}
				PrecipitationAmbientBlender[] array2 = array;
				foreach (PrecipitationAmbientBlender bl in array2)
				{
					if ((bool)bl && bl.gameObject.scene.IsValid())
					{
						Plugin.Log.LogWarning($"[Weather] RAIN AUDIO: blender '{bl.name}' enabled={bl.enabled} " + $"activeInHierarchy={bl.gameObject.activeInHierarchy} " + FF("_precipitationSource") + " " + FF("_precipitationMixSource") + " " + FF("_outputMixerGroup") + " " + $"controllerSays={RainController.IntensityType}");
					}
					string FF(string n)
					{
						object obj3 = AccessTools.Field(bl.GetType(), n)?.GetValue(bl);
						bool flag2 = ((obj3 is UnityEngine.Object obj4) ? ((bool)obj4) : (obj3 != null));
						return n + "=" + (flag2 ? "ok" : "NULL");
					}
				}
				EnvironmentSoundBlendSystem environmentSoundBlendSystem = null;
				EnvironmentSoundBlendSystem[] array3 = Resources.FindObjectsOfTypeAll<EnvironmentSoundBlendSystem>();
				foreach (EnvironmentSoundBlendSystem environmentSoundBlendSystem2 in array3)
				{
					if ((bool)environmentSoundBlendSystem2 && environmentSoundBlendSystem2.gameObject.scene.IsValid())
					{
						environmentSoundBlendSystem = environmentSoundBlendSystem2;
						break;
					}
				}
				Plugin.Log.LogWarning("[Weather] RAIN AUDIO: EnvironmentSoundBlendSystem " + ((!environmentSoundBlendSystem) ? "MISSING — nothing drives the blenders" : $"present, enabled={environmentSoundBlendSystem.enabled}, active={environmentSoundBlendSystem.gameObject.activeInHierarchy}"));
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Weather] rain audio probe failed: " + ex.Message);
			}
			string F(string name)
			{
				FieldInfo fieldInfo2 = AccessTools.Field(typeof(RainController), name);
				if (fieldInfo2 == null)
				{
					return name + "=?";
				}
				object value = fieldInfo2.GetValue(rc);
				bool flag2 = ((value is UnityEngine.Object obj3) ? ((bool)obj3) : (value != null));
				return name + "=" + (flag2 ? "ok" : "NULL");
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Weather] rain probe failed: " + ex2.Message);
		}
		finally
		{
			TryHealRainAudio();
		}
	}

	private static void TryHealRainAudio()
	{
		try
		{
			TerminalRainAudio.TryStage();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Weather] rain audio recovery failed: " + ex.Message);
		}
	}

	private static Transform FindSceneGo(string name)
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (!sceneAt.isLoaded || !sceneAt.name.StartsWith("Terminal"))
			{
				continue;
			}
			GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				if (gameObject.name == name)
				{
					return gameObject.transform;
				}
				Transform transform = FindChildRecursive(gameObject.transform, name);
				if (transform != null)
				{
					return transform;
				}
			}
		}
		return null;
	}

	private static Transform FindChildRecursive(Transform t, string name)
	{
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			if (child.name == name)
			{
				return child;
			}
			Transform transform = FindChildRecursive(child, name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	private static void IndexSubtree(Transform t, string path)
	{
		_pathIndex[path] = t;
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			IndexSubtree(child, path + "/" + child.name);
		}
	}

	private static UnityEngine.Object ResolveRef(JObject tok, Type want)
	{
		long valueOrDefault = tok.Value<long?>("ref").GetValueOrDefault();
		int valueOrDefault2 = tok.Value<int?>("file").GetValueOrDefault();
		if (valueOrDefault == 0L)
		{
			return null;
		}
		if (valueOrDefault2 == 0)
		{
			if (_comps.TryGetValue(valueOrDefault, out var value))
			{
				if (!want.IsInstanceOfType(value))
				{
					if (!(want == typeof(GameObject)))
					{
						return value.GetComponent(want);
					}
					return value.gameObject;
				}
				return value;
			}
			JToken jToken = _sidecar?["scene_objects"]?[valueOrDefault.ToString()];
			if (jToken != null && _pathIndex.TryGetValue(jToken.Value<string>("path") ?? "", out var value2))
			{
				if (want == typeof(GameObject))
				{
					return value2.gameObject;
				}
				if (want == typeof(Transform) || want.IsAssignableFrom(typeof(Transform)))
				{
					return value2;
				}
				if (typeof(Component).IsAssignableFrom(want))
				{
					return value2.GetComponent(want);
				}
			}
			return null;
		}
		JToken jToken2 = _sidecar?["external_assets"]?[$"{valueOrDefault2}:{valueOrDefault}"];
		if (jToken2 == null)
		{
			return null;
		}
		string text = jToken2.Value<string>("type");
		string text2 = jToken2.Value<string>("name");
		if (string.IsNullOrEmpty(text2))
		{
			return null;
		}
		if (text == "Shader")
		{
			return Shader.Find(text2);
		}
		if (text == "MonoBehaviour")
		{
			return GetOrBuildSettingsAsset(text2);
		}
		return FindAssetByName(text, text2);
	}

	private static UnityEngine.Object FindAssetByName(string unityType, string name)
	{
		string key = unityType + ":" + name;
		if (_assetCache.TryGetValue(key, out var value))
		{
			return value;
		}
		Type type = unityType switch
		{
			"ComputeShader" => typeof(ComputeShader), 
			"Texture2D" => typeof(Texture2D), 
			"Mesh" => typeof(Mesh), 
			"Material" => typeof(Material), 
			_ => null, 
		};
		UnityEngine.Object obj = null;
		if (type != null)
		{
			UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(type);
			foreach (UnityEngine.Object obj2 in array)
			{
				if (obj2.name == name)
				{
					obj = obj2;
					break;
				}
			}
		}
		if (obj == null && unityType == "Texture2D" && name == "circles")
		{
			obj = LoadTerminalRainRipple();
		}
		if (obj == null)
		{
			_missingAssets.Add(unityType + " '" + name + "'");
		}
		_assetCache[key] = obj;
		return obj;
	}

	private static UnityEngine.Object GetOrBuildSettingsAsset(string name)
	{
		if (_settingsCache.TryGetValue(name, out var value))
		{
			return value;
		}
		UnityEngine.Object obj = null;
		JToken jToken = _sidecar?["settings_assets"]?[name];
		if (jToken != null)
		{
			Type type = TerminalTypes.ByName(jToken.Value<string>("class"));
			if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
			{
				ScriptableObject scriptableObject = ScriptableObject.CreateInstance(type);
				scriptableObject.name = name;
				if (jToken["fields"] is JObject fields)
				{
					FillFields(scriptableObject, fields);
				}
				if (name == "CloudLayerSettings_main")
				{
					ApplyTerminalCloudSettings(type, scriptableObject);
				}
				obj = scriptableObject;
			}
		}
		if (obj == null)
		{
			_missingAssets.Add("SettingsAsset '" + name + "'");
		}
		_settingsCache[name] = obj;
		return obj;
	}

	private static void ApplyTerminalCloudSettings(Type type, ScriptableObject so)
	{
		Set(so, "Opacity", 1f);
		Set(so, "ShadowMultiplier", 1.09f);
		Set(so, "ShadowTint", new Color(0.2264151f, 0.2264151f, 0.2264151f, 0f));
		Set(so, "ShadowSize", 500f);
		Set(so, "PlanetCenterRadius", new Vector4(0f, -6704000f, 0f, 6704000f));
		object obj = AccessTools.Field(type, "LayerA")?.GetValue(so);
		if (obj != null)
		{
			Texture2D texture2D = LoadTerminalCloudMap();
			Set(obj, "CloudTexture", texture2D ? texture2D : Texture2D.grayTexture);
			Set(obj, "Altitude", 6000f);
			Set(obj, "Rotation", 191f);
			Set(obj, "Tint", Color.white);
			Set(obj, "Exposure", 2.1f);
			Set(obj, "ScrollOrientation", 180f);
			Set(obj, "MinScrollSpeed", 20f);
			Set(obj, "MaxScrollSpeed", 100f);
			Set(obj, "Lighting", true);
			Set(obj, "Thickness", 1f);
			Set(obj, "AmbientProbeDimmer", 0.675f);
			Plugin.Log.LogWarning(texture2D ? $"[Weather] restored retail cloud layer with '{texture2D.name}' ({texture2D.width}x{texture2D.height})" : "[Weather] retail cloud PNG missing/unreadable — emergency gray fallback remains active");
		}
		static void Set(object target, string field, object value)
		{
			FieldInfo fieldInfo = AccessTools.Field(target.GetType(), field);
			if (fieldInfo != null)
			{
				fieldInfo.SetValue(target, value);
			}
		}
	}

	private static Texture2D LoadTerminalCloudMap()
	{
		if ((bool)_terminalCloudMap)
		{
			return _terminalCloudMap;
		}
		Texture2D[] array = Resources.FindObjectsOfTypeAll<Texture2D>();
		foreach (Texture2D texture2D in array)
		{
			if ((bool)texture2D && texture2D.name == "CloudMapWoHorizonBorder2")
			{
				return _terminalCloudMap = texture2D;
			}
		}
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "weather", "CloudMapWoHorizonBorder2.png");
			if (!File.Exists(path))
			{
				return null;
			}
			Texture2D texture2D2 = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: false)
			{
				name = "CloudMapWoHorizonBorder2",
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
				anisoLevel = 1
			};
			if (!texture2D2.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
			{
				UnityEngine.Object.Destroy(texture2D2);
				return null;
			}
			return _terminalCloudMap = texture2D2;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Weather] cloud PNG load failed: " + ex.Message);
			return null;
		}
	}

	private static Texture2D LoadTerminalRainRipple()
	{
		if ((bool)_terminalRainRipple)
		{
			return _terminalRainRipple;
		}
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "water", "circles.png");
			if (!File.Exists(path))
			{
				return null;
			}
			Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: true)
			{
				name = "circles",
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Bilinear,
				anisoLevel = 1
			};
			if (!texture2D.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
			{
				UnityEngine.Object.Destroy(texture2D);
				return null;
			}
			texture2D.Apply(updateMipmaps: true, makeNoLongerReadable: false);
			_terminalRainRipple = texture2D;
			Plugin.Log.LogInfo("[Weather] restored retail rain ripple '" + texture2D.name + "' " + $"({texture2D.width}x{texture2D.height}, linear=true, wrap={texture2D.wrapMode}, " + $"filter={texture2D.filterMode}, aniso={texture2D.anisoLevel})");
			return texture2D;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Weather] rain ripple PNG load failed: " + ex.Message);
			return null;
		}
	}

	private static void FillFields(object target, JObject fields)
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
			return Enum.ToObject(type, tok.Value<long>());
		}
		if (type.IsPrimitive)
		{
			return Convert.ChangeType(((JValue)tok).Value, type, CultureInfo.InvariantCulture);
		}
		if (tok is JObject jObject)
		{
			if (jObject["extname"] != null)
			{
				return FindAssetByName(jObject.Value<string>("exttype"), jObject.Value<string>("extname"));
			}
			if (jObject["ref"] != null && jObject.Count <= 2)
			{
				if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
				{
					return null;
				}
				return ResolveRef(jObject, type);
			}
			if (type == typeof(Vector2))
			{
				return new Vector2(jObject.Value<float>("x"), jObject.Value<float>("y"));
			}
			if (type == typeof(Vector3))
			{
				return new Vector3(jObject.Value<float>("x"), jObject.Value<float>("y"), jObject.Value<float>("z"));
			}
			if (type == typeof(Vector4))
			{
				return new Vector4(jObject.Value<float>("x"), jObject.Value<float>("y"), jObject.Value<float>("z"), jObject.Value<float>("w"));
			}
			if (type == typeof(Quaternion))
			{
				return new Quaternion(jObject.Value<float>("x"), jObject.Value<float>("y"), jObject.Value<float>("z"), jObject.Value<float>("w"));
			}
			if (type == typeof(Color))
			{
				return new Color(jObject.Value<float>("r"), jObject.Value<float>("g"), jObject.Value<float>("b"), jObject.Value<float>("a"));
			}
			if (type == typeof(Bounds))
			{
				return new Bounds((Vector3)ConvertToken(jObject["m_Center"], typeof(Vector3), null), (Vector3)ConvertToken(jObject["m_Extent"], typeof(Vector3), null) * 2f);
			}
			if (type == typeof(LayerMask))
			{
				return (LayerMask)jObject.Value<int?>("m_Bits").GetValueOrDefault();
			}
			if (type == typeof(AnimationCurve))
			{
				return BuildCurve(jObject) ?? existing;
			}
			if (type == typeof(Gradient))
			{
				return BuildGradient(jObject) ?? existing;
			}
			if (typeof(UnityEngine.Object).IsAssignableFrom(type))
			{
				return null;
			}
			object obj = existing ?? Activator.CreateInstance(type);
			FillFields(obj, jObject);
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
				list.Add(ConvertToken(item, type2, null));
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

	private static AnimationCurve BuildCurve(JObject o)
	{
		return TerminalSerializedCurves.FromUnity(o);
	}

	private static Gradient BuildGradient(JObject o)
	{
		if (o["ctime0"] == null && o["key0"] == null)
		{
			return null;
		}
		int value = o.Value<int?>("m_NumColorKeys") ?? 2;
		int value2 = o.Value<int?>("m_NumAlphaKeys") ?? 2;
		GradientColorKey[] array = new GradientColorKey[Mathf.Clamp(value, 1, 8)];
		GradientAlphaKey[] array2 = new GradientAlphaKey[Mathf.Clamp(value2, 1, 8)];
		for (int i = 0; i < array.Length; i++)
		{
			Color col = ((o[$"key{i}"] is JObject jObject) ? new Color(jObject.Value<float>("r"), jObject.Value<float>("g"), jObject.Value<float>("b"), jObject.Value<float>("a")) : Color.white);
			array[i] = new GradientColorKey(col, (float)o.Value<int?>($"ctime{i}").GetValueOrDefault() / 65535f);
		}
		for (int j = 0; j < array2.Length; j++)
		{
			float alpha = ((o[$"key{j}"] is JObject jObject2) ? jObject2.Value<float>("a") : 1f);
			array2[j] = new GradientAlphaKey(alpha, (float)o.Value<int?>($"atime{j}").GetValueOrDefault() / 65535f);
		}
		Gradient gradient = new Gradient();
		gradient.SetKeys(array, array2);
		gradient.mode = (GradientMode)o.Value<int?>("m_Mode").GetValueOrDefault();
		return gradient;
	}
}
