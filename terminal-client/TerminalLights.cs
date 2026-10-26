using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.Weather;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Manimal.Terminal;

internal static class TerminalLights
{
	[HarmonyPatch]
	internal static class Patch_LampsDead
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			string[] array = new string[2] { "Awake", "OnEnable" };
			string[] array2 = array;
			foreach (string name in array2)
			{
				MethodInfo methodInfo = AccessTools.Method(typeof(LampController), name);
				if (methodInfo != null)
				{
					yield return methodInfo;
				}
			}
		}

		[HarmonyPrefix]
		private static bool Prefix(LampController __instance)
		{
			return !IsTerminalLamp(__instance, fresh: true);
		}
	}

	// zzap perf: ManualUpdate runs for every lamp every frame on EVERY map (Streets has
	// thousands), and the old prefix read gameObject.scene.name — a native call and a
	// new string each time. The scene a lamp lives in does not change, so it is read
	// once (Awake/OnEnable or first tick) and remembered by instance id.
	[HarmonyPatch]
	internal static class Patch_LampsDeadTick
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			string[] array = new string[2] { "ManualUpdate", "TurnLights" };
			foreach (string name in array)
			{
				MethodInfo methodInfo = AccessTools.Method(typeof(LampController), name);
				if (methodInfo != null)
				{
					yield return methodInfo;
				}
			}
		}

		[HarmonyPrefix]
		private static bool Prefix(LampController __instance)
		{
			return !IsTerminalLamp(__instance, fresh: false);
		}
	}

	private static readonly Dictionary<int, bool> _lampIsTerminal = new Dictionary<int, bool>();

	internal static void ResetLampSceneCache()
	{
		_lampIsTerminal.Clear();
	}

	private static bool IsTerminalLamp(LampController lamp, bool fresh)
	{
		try
		{
			if (lamp == null)
			{
				return false;
			}
			int id = lamp.GetInstanceID();
			if (!fresh && _lampIsTerminal.TryGetValue(id, out var known))
			{
				return known;
			}
			string text = lamp.gameObject.scene.name;
			bool terminal = !string.IsNullOrEmpty(text) && text.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase);
			_lampIsTerminal[id] = terminal;
			return terminal;
		}
		catch
		{
			return TerminalGate.On;
		}
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_LightsAtRaidStart
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On)
			{
				new GameObject("Terminal_Lights").AddComponent<Host>();
				// zzap: same raid-start moment — the civilian teleport diagnostic
				Civilian.CivilianJumpProbe.EnsureForRaid();
				TerminalGhostDirector.EnsureForRaid();
			}
		}
	}

	internal class Host : MonoBehaviour
	{
		private void Start()
		{
			StartCoroutine(Init());
		}

		private IEnumerator Init()
		{
			DiscoverLamps();
			yield return null;
			ApplyLamps();
			yield return null;
			ApplyAmbient();
			yield return null;
			TerminalVolumetricLights.Restore();
			yield return null;
			try
			{
				TerminalFlares.TryBuild();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Flares] build failed: " + ex.Message);
			}
		}

		private void Update()
		{
			float unscaledDeltaTime = Time.unscaledDeltaTime;
			_frameAvg = Mathf.Lerp((_frameAvg <= 0f) ? unscaledDeltaTime : _frameAvg, unscaledDeltaTime, 0.05f);
			bool flag = _frameAvg > 1.5f / Mathf.Max(30f, (Application.targetFrameRate > 0) ? ((float)Application.targetFrameRate) : 60f);
			if (flag != _inChop && Time.time - _lastChopEdge > 2f)
			{
				_inChop = flag;
				_lastChopEdge = Time.time;
				int num = GC.CollectionCount(0);
				Plugin.Log.LogWarning(string.Format("[Perf] {0} t={1:0.0}s ", flag ? "CHOPPY window start" : "smooth again", Time.time) + $"avgFrame={_frameAvg * 1000f:0.0}ms gc0={num} (delta {num - _lastGc})");
				_lastGc = num;
			}
			if (Time.frameCount % 10 == 7)
			{
				long timestamp = Stopwatch.GetTimestamp();
				TickSkyTime();
				TerminalTickProfiler.Add("SkyTime", Stopwatch.GetTimestamp() - timestamp);
			}
			if (Time.frameCount % 120 == 23)
			{
				long timestamp2 = Stopwatch.GetTimestamp();
				TerminalWeather.TryStage();
				TerminalWeather.TickProbe();
				TerminalTickProfiler.Add("WeatherTk", Stopwatch.GetTimestamp() - timestamp2);
			}
			long timestamp3 = Stopwatch.GetTimestamp();
			TickNightSky();
			TerminalTickProfiler.Add("NightSky", Stopwatch.GetTimestamp() - timestamp3);
			TrackManagerlessGameplayLights();
			if (Plugin.SpatialAudio.Value)
			{
				if (Time.frameCount % 120 == 7)
				{
					TerminalAcoustics.TickSpatialInit();
					TerminalAcoustics.TickSpatialVerdict();
					TerminalAcoustics.TryBuildEnvironmentTriggers();
					if (Plugin.AmbientRetail.Value)
					{
						TerminalAcoustics.TryStageAmbient(this);
					}
					TerminalAcoustics.TryApplyAmbientAuthoring();
				}
				if ((Time.frameCount & 1) == 0)
				{
					long timestamp4 = Stopwatch.GetTimestamp();
					TerminalAcoustics.DriveEnvironment();
					TerminalTickProfiler.Add("EnvDrive", Stopwatch.GetTimestamp() - timestamp4);
				}
			}
			if (Plugin.LightProbeKey.Value.IsDown() || (Plugin.DevMode.Value && Time.frameCount % 300 == 33))
			{
				DumpNearPlayerLights();
			}
			if (Plugin.SoundProbeKey.Value.IsDown())
			{
				DumpNearPlayerSounds();
			}
			// zzap perf: Camera.allCameras allocates a fresh array and each .name a string;
			// this polled every frame for the whole raid while no scope camera exists
			// (always, with PiP-Disabler) — look twice a second instead
			if (Time.frameCount % 30 == 11)
			{
				if (_opticCam == null)
				{
					Camera[] allCameras = Camera.allCameras;
					for (int i = 0; i < allCameras.Length; i++)
					{
						if (allCameras[i] != null && allCameras[i].name == "BaseOpticCamera(Clone)")
						{
							_opticCam = allCameras[i];
							break;
						}
					}
				}
				if (_opticCam != null)
				{
					TOD_Scattering component = _opticCam.GetComponent<TOD_Scattering>();
					if (component != null && component.enabled)
					{
						component.enabled = false;
						Plugin.Log.LogInfo("[Lights] optic camera TOD_Scattering disabled (dark magnified scopes)");
					}
				}
			}
			if (Time.frameCount % 30 == 0)
			{
				if (Plugin.LampIntensity.Value != _lastLamp || Plugin.LampShadows.Value != _lastShadows || Plugin.LightCullDistance.Value != _lastLightCull)
				{
					ApplyLamps();
				}
				if (Plugin.AmbientIntensity.Value != _lastAmbient || Plugin.AmbientColorOverride.Value != _lastColorOverride || Plugin.AmbientColorR.Value != _lastColorR || Plugin.AmbientColorG.Value != _lastColorG || Plugin.AmbientColorB.Value != _lastColorB)
				{
					ApplyAmbient();
				}
			}
		}
	}

	private static readonly List<Light> _lamps = new List<Light>();

	private static float _lastLamp = -1f;

	private static float _lastAmbient = -1f;

	private static bool _lastColorOverride;

	private static float _lastColorR = -1f;

	private static float _lastColorG = -1f;

	private static float _lastColorB = -1f;

	private static float _lastLightCull = -1f;

	private static Camera _opticCam;

	private static bool _lastShadows;

	internal static bool CutsceneHold;

	private static TOD_Sky _sky;

	private static bool _skyTimeLogged;

	private static int _skyProbes;

	private static int _skyTicks;

	private static bool _ambientAuthorityTaken;

	private static float _frameAvg;

	private static bool _inChop;

	private static float _lastChopEdge;

	private static int _lastGc;

	private static float _authoredBrightness = -1f;

	private static float _authoredScatter = -1f;

	private static Dictionary<(int, int, int), List<float[]>> _authored;

	private static bool _authoredTried;

	private static FieldInfo _cloMaxIntensity;

	private const float CutsceneLightRadius = 80f;

	private const float CutsceneLightRefreshSeconds = 0.2f;

	private const float GameplayLightRefreshSeconds = 0.5f;

	private static readonly List<CullingLightObject> _nativeCutsceneLights = new List<CullingLightObject>();

	private static readonly HashSet<CullingLightObject> _cutsceneVisibleLights = new HashSet<CullingLightObject>();

	private static bool _cutsceneLightStateKnown;

	private static float _nextCutsceneLightRefresh;

	private static float _nextManagerlessLightRefresh;

	internal static void ResetForNewRaid()
	{
		ResetLampSceneCache();
		_lamps.Clear();
		_nativeCutsceneLights.Clear();
		_cutsceneVisibleLights.Clear();
		_cutsceneLightStateKnown = false;
		_lastLamp = (_lastAmbient = (_lastLightCull = -1f));
		CutsceneHold = false;
		_nextCutsceneLightRefresh = 0f;
		_nextManagerlessLightRefresh = 0f;
		_sky = null;
		_skyTimeLogged = false;
		_ambientAuthorityTaken = false;
	}

	internal static void TickSkyTime()
	{
		try
		{
			if (!_sky)
			{
				_sky = UnityEngine.Object.FindObjectOfType<TOD_Sky>();
			}
			if (!_sky || !_sky.Initialized)
			{
				return;
			}
			GameDateTime gameDateTime = TODSkyProvider.Instance?.CurrentTime?.GameDateTime ?? Singleton<GameWorld>.Instance?.GameDateTime;
			if (gameDateTime == null)
			{
				return;
			}
			DateTime dateTime = TerminalMissionWeather.ResolveSkyTime(gameDateTime.Calculate());
			_sky.Cycle.DateTime = dateTime.AddHours(Plugin.SkyHourOffset.Value);
			if (!_sky.enabled)
			{
				_sky.enabled = true;
				Plugin.Log.LogWarning("[Sky] TOD_Sky component was DISABLED - re-enabled (this is why the solver froze)");
			}
			if (!_ambientAuthorityTaken)
			{
				_ambientAuthorityTaken = true;
				try
				{
					_sky.Ambient.UpdateInterval = float.MaxValue;
					_lastAmbient = -1f;
					Plugin.Log.LogWarning("[Sky] TOD ambient module muzzled (UpdateInterval=max) - flat fill + NvgAmbient own RenderSettings ambient again");
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[Sky] ambient muzzle failed: " + ex.Message);
				}
			}
			try
			{
				_sky.UpdateCelestials();
			}
			catch (Exception ex2)
			{
				if (_skyProbes < 3)
				{
					Plugin.Log.LogWarning("[Sky] self-driven solve threw: " + ex2.Message);
				}
			}
			if (!_skyTimeLogged)
			{
				_skyTimeLogged = true;
				Plugin.Log.LogWarning($"[Sky] TOD hour synced to raid time: {dateTime:HH:mm} (re-synced ~1/s)");
			}
			_skyTicks++;
			if (_skyProbes >= 3 || _skyTicks % 90 != 5)
			{
				return;
			}
			_skyProbes++;
			try
			{
				Vector3 sunDirection = _sky.SunDirection;
				Plugin.Log.LogWarning($"[Sky] TOD state: cycleHour={_sky.Cycle.Hour:0.00} " + string.Format("sunDir.y={0:0.000} ({1}) ", sunDirection.y, (sunDirection.y > 0f) ? "SUN IS UP - solver disagrees with the clock" : "sun is down - solver agrees") + $"IsDay={_sky.IsDay} IsNight={_sky.IsNight} LerpValue={_sky.LerpValue:0.000} " + $"atmoBrightness={_sky.Atmosphere.Brightness:0.000} " + $"lat={_sky.World.Latitude:0.0} long={_sky.World.Longitude:0.0} UTC={_sky.World.UTC:0.00} " + $"date={_sky.Cycle.Year}-{_sky.Cycle.Month:00}-{_sky.Cycle.Day:00} zenith={_sky.SunZenith:0.0}");
				TOD_Components component = _sky.GetComponent<TOD_Components>();
				if ((bool)component && (bool)component.SunTransform && (bool)component.DomeTransform)
				{
					Transform sunTransform = component.SunTransform;
					Transform domeTransform = component.DomeTransform;
					ManualLogSource log = Plugin.Log;
					string text = string.Format("[Sky] sun rig: sunLocalPos={0} sunParent='{1}' ", sunTransform.localPosition, sunTransform.parent ? sunTransform.parent.name : "NULL");
					string text2 = $"domeScale={domeTransform.localScale} domePos={domeTransform.position} ";
					object arg = sunTransform.position;
					log.LogWarning(text + text2 + $"sunWorld={arg} sunToDome={(domeTransform.position - sunTransform.position).magnitude:0.###}");
				}
			}
			catch (Exception ex3)
			{
				Plugin.Log.LogWarning("[Sky] state probe failed: " + ex3.Message);
			}
			DumpSkyRig();
		}
		catch
		{
		}
	}

	internal static void DumpSkyRig()
	{
		try
		{
			if (!_sky)
			{
				Plugin.Log.LogWarning("[SkyRig] no TOD_Sky");
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append($"init={_sky.Initialized} atmoBrightness={_sky.Atmosphere.Brightness:0.####} ").Append($"scattering={_sky.Atmosphere.ScatteringBrightness:0.####} fog={_sky.Atmosphere.Fogginess:0.###} ").Append($"sunSky={_sky.SunSkyColor} moonSky={_sky.MoonSkyColor} sunMesh={_sky.SunMeshColor} ")
				.Append($"stars={_sky.Stars.Brightness:0.##}x tile={_sky.Stars.Tiling:0.##} " + $"effectiveStars={_sky.Stars.Brightness * (1f - _sky.Atmosphere.Fogginess) * (1f - _sky.LerpValue):0.###} ")
				.Append($"authoredCapture={_authoredBrightness:0.####} nightStrength={Plugin.SkyNightStrength.Value:0.##} ")
				.Append(string.Format("weatherCtrl={0} skybox={1} ", (bool)WeatherController.Instance, RenderSettings.skybox ? RenderSettings.skybox.name : "NULL"))
				.Append($"ambientMode={RenderSettings.ambientMode}");
			Renderer[] componentsInChildren = _sky.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren)
			{
				if (!(renderer == null))
				{
					Material sharedMaterial = renderer.sharedMaterial;
					stringBuilder.Append($"\n    '{renderer.name}' enabled={renderer.enabled} " + $"forceOff={renderer.forceRenderingOff} activeGO={renderer.gameObject.activeInHierarchy} " + "layer=" + LayerMask.LayerToName(renderer.gameObject.layer) + " mat='" + (sharedMaterial ? sharedMaterial.name : "NULL") + "' shader='" + (((bool)sharedMaterial && (bool)sharedMaterial.shader) ? sharedMaterial.shader.name : "NULL") + "' " + $"queue={(sharedMaterial ? sharedMaterial.renderQueue : (-1))}");
				}
			}
			Plugin.Log.LogWarning("[SkyRig] " + stringBuilder.ToString());
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[SkyRig] dump failed: " + ex.Message);
		}
	}

	internal static void TickNightSky()
	{
		try
		{
			if (!_sky || !_sky.Initialized || (bool)WeatherController.Instance)
			{
				return;
			}
			float value = Plugin.SkyNightStrength.Value;
			if (value <= 0f)
			{
				return;
			}
			if (_authoredBrightness < 0f)
			{
				float brightness = _sky.Atmosphere.Brightness;
				float scatteringBrightness = _sky.Atmosphere.ScatteringBrightness;
				if (!(brightness > 0.02f))
				{
					return;
				}
				_authoredBrightness = brightness;
				_authoredScatter = scatteringBrightness;
				Plugin.Log.LogWarning($"[Sky] authored atmosphere captured: brightness={brightness:0.###} scattering={scatteringBrightness:0.###}");
			}
			float y = _sky.SunDirection.y;
			float hour = _sky.Cycle.Hour;
			bool num = hour >= 21f || hour < 6f;
			float num2 = Mathf.Clamp01(y * 4f + 0.15f);
			if (num)
			{
				num2 = Mathf.Min(num2, 1f - value);
			}
			_sky.Atmosphere.Brightness = Mathf.Lerp(_authoredBrightness * 0.06f, _authoredBrightness, num2);
			_sky.Atmosphere.ScatteringBrightness = Mathf.Lerp(_authoredScatter * 0.08f, _authoredScatter, num2);
		}
		catch
		{
		}
	}

	internal static void DumpNearPlayerLights()
	{
		try
		{
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			if (player == null)
			{
				Plugin.Log.LogWarning("[LightProbe] no main player");
				return;
			}
			int num = 0;
			Light[] array = UnityEngine.Object.FindObjectsOfType<Light>();
			foreach (Light light in array)
			{
				if (!(light == null) && light.enabled && !(light.intensity <= 0.01f) && light.type != LightType.Directional && !((light.transform.position - player.Position).sqrMagnitude > 16f))
				{
					string text = light.transform.name;
					Transform parent = light.transform.parent;
					while (parent != null)
					{
						text = parent.name + "/" + text;
						parent = parent.parent;
					}
					ManualLogSource log = Plugin.Log;
					string[] array2 = new string[6] { "[LightProbe] near-player light: '", text, "' scene='", null, null, null };
					array2[3] = light.gameObject.scene.name;
					array2[4] = "' ";
					array2[5] = $"type={light.type} intensity={light.intensity:0.00} range={light.range:0.0} culled={light.GetComponent<CullingLightObject>() != null}";
					log.LogWarning(string.Concat(array2));
					num++;
				}
			}
			if (num == 0)
			{
				Plugin.Log.LogWarning("[LightProbe] no lit lights within 4m right now");
			}
		}
		catch
		{
		}
	}

	internal static void DumpNearPlayerSounds()
	{
		try
		{
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			if (player == null)
			{
				Plugin.Log.LogWarning("[SoundProbe] no main player");
				return;
			}
			List<(float, string)> list = new List<(float, string)>();
			AudioSource[] array = UnityEngine.Object.FindObjectsOfType<AudioSource>();
			foreach (AudioSource audioSource in array)
			{
				if (audioSource == null || !audioSource.isPlaying || audioSource.volume <= 0.001f)
				{
					continue;
				}
				float magnitude = (audioSource.transform.position - player.Position).magnitude;
				bool flag = audioSource.spatialBlend < 0.5f;
				if (flag || !(magnitude > 60f))
				{
					string text = audioSource.transform.name;
					Transform parent = audioSource.transform.parent;
					while (parent != null)
					{
						text = parent.name + "/" + text;
						parent = parent.parent;
					}
					float item = (flag ? float.MaxValue : (audioSource.volume / Mathf.Max(magnitude, 1f)));
					string[] array2 = new string[9]
					{
						"[SoundProbe] ",
						flag ? "GLOBAL/2D " : "",
						"'",
						null,
						null,
						null,
						null,
						null,
						null
					};
					AudioClip clip = audioSource.clip;
					array2[3] = ((clip != null) ? clip.name : null) ?? "<procedural>";
					array2[4] = "' on '";
					array2[5] = text;
					array2[6] = "' ";
					array2[7] = $"vol={audioSource.volume:0.00} dist={magnitude:0.0}m blend={audioSource.spatialBlend:0.0} loop={audioSource.loop} ";
					object arg = audioSource.minDistance;
					object arg2 = audioSource.maxDistance;
					array2[8] = $"min/max={arg:0.0}/{arg2:0.0} scene='{audioSource.gameObject.scene.name}'";
					list.Add((item, string.Concat(array2)));
				}
			}
			list.Sort(((float score, string line) a, (float score, string line) b) => b.score.CompareTo(a.score));
			for (int num = 0; num < list.Count && num < 20; num++)
			{
				Plugin.Log.LogWarning(list[num].Item2);
			}
			Plugin.Log.LogWarning($"[SoundProbe] {list.Count} playing source(s) within 60m ({Mathf.Max(list.Count - 20, 0)} not shown)");
		}
		catch
		{
		}
	}

	private static bool IsWeaponLight(Light l)
	{
		Transform transform = l.transform;
		int num = 0;
		while ((bool)transform && num++ < 24)
		{
			string name = transform.name;
			if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("mod_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("patron_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("shell_", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	internal static void DiscoverLamps()
	{
		HashSet<Light> hashSet = new HashSet<Light>();
		CullingLightObject[] array = UnityEngine.Object.FindObjectsOfType<CullingLightObject>();
		_nativeCutsceneLights.Clear();
		CullingLightObject[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			Light light = array2[i].GetLight();
			if (light != null)
			{
				hashSet.Add(light);
			}
		}
		int num = 0;
		int num2 = 0;
		Light[] array3 = UnityEngine.Object.FindObjectsOfType<Light>();
		foreach (Light light2 in array3)
		{
			if (light2 == null || light2.type == LightType.Directional || light2.intensity > 0.1f || _lamps.Contains(light2))
			{
				continue;
			}
			string name = light2.gameObject.scene.name;
			if (name != null && name.StartsWith("Terminal"))
			{
				if (hashSet.Contains(light2))
				{
					num++;
				}
				else if (light2.GetComponentInParent<LootItem>() != null || IsWeaponLight(light2))
				{
					num2++;
				}
				else
				{
					_lamps.Add(light2);
				}
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[Lights] {num} lamps native-owned (CullingLightObject) - LampIntensity drives only the {_lamps.Count} unowned");
		}
		if (num2 > 0)
		{
			Plugin.Log.LogInfo($"[Lights] {num2} light(s) on loot/weapon hierarchies skipped - guns are not map lamps");
		}
		int num3 = 0;
		for (int num4 = _lamps.Count - 1; num4 >= 0; num4--)
		{
			Light light3 = _lamps[num4];
			if (!light3)
			{
				_lamps.RemoveAt(num4);
			}
			else if (light3.GetComponentInParent<LootItem>() != null || IsWeaponLight(light3))
			{
				light3.intensity = 0f;
				light3.enabled = false;
				_lamps.RemoveAt(num4);
				num3++;
			}
		}
		if (num3 > 0)
		{
			Plugin.Log.LogInfo($"[Lights] {num3} previously-revived weapon light(s) purged and darkened");
		}
	}

	private static void LoadAuthoredLights()
	{
		if (_authoredTried)
		{
			return;
		}
		_authoredTried = true;
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_lights.json");
			if (!File.Exists(path))
			{
				return;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(path));
			_authored = new Dictionary<(int, int, int), List<float[]>>();
			foreach (JToken item in (JArray)jObject["lights"])
			{
				JArray jArray = (JArray)item["p"];
				JArray jArray2 = (JArray)item["c"];
				float[] array = new float[8]
				{
					(float)jArray[0],
					(float)jArray[1],
					(float)jArray[2],
					(float)item["i"],
					(float)jArray2[0],
					(float)jArray2[1],
					(float)jArray2[2],
					(float)(item["rng"] ?? ((JToken)0f))
				};
				(int, int, int) key = ((int)Mathf.Floor(array[0]), (int)Mathf.Floor(array[1]), (int)Mathf.Floor(array[2]));
				if (!_authored.TryGetValue(key, out var value))
				{
					List<float[]> list = (_authored[key] = new List<float[]>());
					value = list;
				}
				value.Add(array);
			}
			Plugin.Log.LogInfo(string.Format("[Lights] authored light table loaded: {0} rows", ((JArray)jObject["lights"]).Count));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Lights] authored light table failed to load: " + ex.Message);
			_authored = null;
		}
	}

	private static float[] AuthoredFor(Vector3 pos)
	{
		if (_authored == null)
		{
			return null;
		}
		float[] result = null;
		float num = 0.5625f;
		int num2 = (int)Mathf.Floor(pos.x);
		int num3 = (int)Mathf.Floor(pos.y);
		int num4 = (int)Mathf.Floor(pos.z);
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				for (int k = -1; k <= 1; k++)
				{
					if (!_authored.TryGetValue((num2 + i, num3 + j, num4 + k), out var value))
					{
						continue;
					}
					foreach (float[] item in value)
					{
						float num5 = (pos.x - item[0]) * (pos.x - item[0]) + (pos.y - item[1]) * (pos.y - item[1]) + (pos.z - item[2]) * (pos.z - item[2]);
						if (num5 < num)
						{
							num = num5;
							result = item;
						}
					}
				}
			}
		}
		return result;
	}

	private static void DriveLamp(Light l, float flatV, float scale, LightShadows shadows, ref int matched)
	{
		float[] array = (Plugin.LampAuthored.Value ? AuthoredFor(l.transform.position) : null);
		if (array != null)
		{
			matched++;
			float num = (l.intensity = array[3] * scale);
			float num3 = num;
			l.color = new Color(array[4], array[5], array[6], 1f);
			if (array[7] > 0.1f)
			{
				l.range = array[7];
			}
			l.shadows = shadows;
			l.enabled = num3 > 0.01f;
		}
		else
		{
			l.intensity = flatV;
			l.shadows = shadows;
			if (flatV <= 0.01f)
			{
				l.enabled = false;
			}
			else if (!l.enabled)
			{
				l.enabled = true;
			}
		}
	}

	internal static void ApplyLamps()
	{
		float value = Plugin.LampIntensity.Value;
		float value2 = Plugin.LampAuthoredScale.Value;
		LightShadows value3 = (Plugin.LampShadows.Value ? LightShadows.Hard : LightShadows.None);
		LoadAuthoredLights();
		int num = 0;
		int matched = 0;
		foreach (Light lamp in _lamps)
		{
			if ((bool)lamp)
			{
				DriveLamp(lamp, value, value2, value3, ref matched);
				num++;
			}
		}
		int num2 = 0;
		int num3 = 0;
		FieldInfo fieldInfo = AccessTools.Field(typeof(CullingLightObject), "_maxLightIntensity");
		FieldInfo fieldInfo2 = AccessTools.Field(typeof(CullingLightObject), "float_1");
		FieldInfo fieldInfo3 = AccessTools.Field(typeof(CullingLightObject), "_fadeStartDistance");
		FieldInfo fieldInfo4 = AccessTools.Field(typeof(CullingLightObject), "_fadeEndDistance");
		float value4 = Plugin.LightCullDistance.Value;
		bool flag = false;
		try
		{
			flag = CullingManager.Instance != null;
		}
		catch
		{
		}
		CullingLightObject[] array = UnityEngine.Object.FindObjectsOfType<CullingLightObject>();
		_nativeCutsceneLights.Clear();
		CullingLightObject[] array2 = array;
		foreach (CullingLightObject cullingLightObject in array2)
		{
			Light light = cullingLightObject.GetLight();
			if (!light)
			{
				continue;
			}
			string name = light.gameObject.scene.name;
			if (name == null || !name.StartsWith("Terminal"))
			{
				continue;
			}
			_nativeCutsceneLights.Add(cullingLightObject);
			if (!flag)
			{
				DriveLamp(light, value, value2, value3, ref matched);
			}
			fieldInfo?.SetValue(cullingLightObject, light.intensity);
			fieldInfo2?.SetValue(cullingLightObject, light.intensity);
			num2++;
			try
			{
				if (fieldInfo3 != null && fieldInfo4 != null && value4 < (float)fieldInfo4.GetValue(cullingLightObject))
				{
					fieldInfo3.SetValue(cullingLightObject, Mathf.Min((float)fieldInfo3.GetValue(cullingLightObject), value4 * 0.6f));
					fieldInfo4.SetValue(cullingLightObject, value4);
					cullingLightObject.CacheLightSqrDistances();
					num3++;
				}
			}
			catch
			{
			}
		}
		if (num3 > 0)
		{
			Plugin.Log.LogDebug($"[Lights] native fade window tightened to {value4:0}m on {num3} lights");
		}
		if (!flag)
		{
			Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
			Player player = ((!Singleton<GameWorld>.Instantiated) ? null : Singleton<GameWorld>.Instance?.MainPlayer);
			Vector3 cameraPosition;
			if (player != null)
			{
				cameraPosition = player.Position;
			}
			else
			{
				cameraPosition = ((camera != null) ? camera.transform.position : Vector3.zero);
			}
			_cutsceneVisibleLights.Clear();
			_cutsceneLightStateKnown = false;
			int num4 = ApplyNativeLightBubble(cameraPosition, value4);
			_nextManagerlessLightRefresh = Time.realtimeSinceStartup + 0.5f;
			Plugin.Log.LogInfo($"[Lights] managerless safety cull retained {num4}/{_nativeCutsceneLights.Count} native lights within {value4:0}m");
		}
		_lastLamp = value;
		_lastShadows = Plugin.LampShadows.Value;
		_lastLightCull = value4;
		Plugin.Log.LogInfo($"[Lights] drove {num} plain lamps + {num2} native culling lights - " + $"{matched} matched AUTHORED retail values (scale {value2:0.00}), rest flat {value:F2}" + (flag ? "" : " (MANAGER-LESS: Light components driven directly)"));
		try
		{
			Camera camera2 = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
			Vector3 b = ((camera2 != null) ? camera2.transform.position : Vector3.zero);
			int num5 = 0;
			int num6 = 0;
			StringBuilder stringBuilder = new StringBuilder();
			array2 = UnityEngine.Object.FindObjectsOfType<CullingLightObject>();
			for (int i = 0; i < array2.Length; i++)
			{
				Light light2 = array2[i].GetLight();
				if (!(light2 == null))
				{
					float num7 = Vector3.Distance(light2.transform.position, b);
					if (num7 < 30f)
					{
						num5++;
					}
					if (num6 < 5 && num7 < 60f)
					{
						num6++;
						stringBuilder.Append($"\n  '{light2.name}' d={num7:0}m enabled={light2.enabled} type={light2.type} intensity={light2.intensity:0.##} range={light2.range:0.#} color={light2.color} mask=0x{light2.cullingMask:X} shadows={light2.shadows}");
					}
				}
			}
			Plugin.Log.LogWarning($"[Lights] AUTOPSY: {num5} native lights within 30m of camera, samples:{stringBuilder}");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[Lights] autopsy failed: " + ex.Message);
		}
	}

	internal static void ApplyAmbient()
	{
		float num = (_lastAmbient = Plugin.AmbientIntensity.Value);
		_lastColorOverride = Plugin.AmbientColorOverride.Value;
		_lastColorR = Plugin.AmbientColorR.Value;
		_lastColorG = Plugin.AmbientColorG.Value;
		_lastColorB = Plugin.AmbientColorB.Value;
		float num2 = 0.16f * num;
		Color color = default;
		if (_lastColorOverride)
		{
			color = new Color(_lastColorR * num, _lastColorG * num, _lastColorB * num, 1f);
		}
		else if ((bool)_sky && _sky.Initialized)
		{
			try
			{
				Color color2 = _sky.SampleEquatorColor();
				float num3 = Mathf.Max(color2.r, Mathf.Max(color2.g, color2.b));
				color = ((num3 > 0.005f) ? (color2 / num3) : new Color(0.85f, 0.85f, 1f)) * num2;
				color.a = 1f;
			}
			catch
			{
				color = new Color(0.15f * num, 0.15f * num, 0.18f * num, 1f);
			}
		}
		else
		{
			color = new Color(0.15f * num, 0.15f * num, 0.18f * num, 1f);
		}
		LevelSettings instance = Singleton<LevelSettings>.Instance;
		if (instance != null)
		{
			instance.AmbientMode = AmbientMode.Flat;
			instance.SkyColor = color;
			instance.EquatorColor = color;
			instance.GroundColor = color;
			instance.AmbientIntensity = num;
			float value = Plugin.NvgAmbient.Value;
			Color color3 = default;
			color3 = (instance.NightVisionGroundColor = (instance.NightVisionEquatorColor = (instance.NightVisionSkyColor = new Color(color.r * value, color.g * value * 1.1f, color.b * value, 1f))));
			instance.NightVisionAmbientIntensity = num * value;
			Plugin.Log.LogDebug($"[Ambient] flat ambient -> {color}, nvg -> {color3} (via LevelSettings, native per-frame apply)");
		}
		else
		{
			RenderSettings.ambientMode = AmbientMode.Flat;
			RenderSettings.ambientLight = color;
			RenderSettings.ambientIntensity = num;
			Plugin.Log.LogDebug($"[Ambient] flat ambient -> {color} (RenderSettings fallback, no LevelSettings singleton)");
		}
	}

	private static void RefreshNativeCutsceneLightCache()
	{
		_nativeCutsceneLights.Clear();
		CullingLightObject[] array = UnityEngine.Object.FindObjectsOfType<CullingLightObject>();
		foreach (CullingLightObject cullingLightObject in array)
		{
			if (cullingLightObject == null)
			{
				continue;
			}
			Light light = cullingLightObject.GetLight();
			if (!(light == null))
			{
				string name = light.gameObject.scene.name;
				if (name != null && name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
				{
					_nativeCutsceneLights.Add(cullingLightObject);
				}
			}
		}
	}

	internal static void TrackCutsceneCamera(Vector3 cameraPosition, bool force = false)
	{
		if ((CutsceneHold || force) && (force || !(Time.realtimeSinceStartup < _nextCutsceneLightRefresh)))
		{
			_nextCutsceneLightRefresh = Time.realtimeSinceStartup + 0.2f;
			if (_nativeCutsceneLights.Count == 0)
			{
				RefreshNativeCutsceneLightCache();
			}
			ApplyNativeLightBubble(cameraPosition, 80f);
		}
	}

	private static int ApplyNativeLightBubble(Vector3 cameraPosition, float radius)
	{
		if (_cloMaxIntensity == null)
		{
			_cloMaxIntensity = AccessTools.Field(typeof(CullingLightObject), "_maxLightIntensity");
		}
		float num = radius * radius;
		int num2 = 0;
		for (int num3 = _nativeCutsceneLights.Count - 1; num3 >= 0; num3--)
		{
			CullingLightObject cullingLightObject = _nativeCutsceneLights[num3];
			if (cullingLightObject == null)
			{
				_nativeCutsceneLights.RemoveAt(num3);
			}
			else
			{
				Light light = cullingLightObject.GetLight();
				if (!(light == null))
				{
					bool flag = (light.transform.position - cameraPosition).sqrMagnitude <= num;
					bool flag2 = _cutsceneVisibleLights.Contains(cullingLightObject);
					if (!_cutsceneLightStateKnown || flag != flag2)
					{
						try
						{
							cullingLightObject.SetVisibility(flag);
						}
						catch
						{
						}
						if (flag)
						{
							_cutsceneVisibleLights.Add(cullingLightObject);
						}
						else
						{
							_cutsceneVisibleLights.Remove(cullingLightObject);
						}
					}
					if (flag)
					{
						num2++;
						if (!light.enabled)
						{
							light.enabled = true;
						}
						try
						{
							float num4 = (float)_cloMaxIntensity.GetValue(cullingLightObject);
							if (num4 > 0f && light.intensity < num4)
							{
								light.intensity = num4;
							}
						}
						catch
						{
						}
					}
				}
			}
		}
		_cutsceneLightStateKnown = true;
		return num2;
	}

	private static void TrackManagerlessGameplayLights()
	{
		if (CutsceneHold || Time.realtimeSinceStartup < _nextManagerlessLightRefresh)
		{
			return;
		}
		try
		{
			if (CullingManager.Instance != null)
			{
				return;
			}
		}
		catch
		{
		}
		_nextManagerlessLightRefresh = Time.realtimeSinceStartup + 0.5f;
		if (_nativeCutsceneLights.Count == 0)
		{
			RefreshNativeCutsceneLightCache();
		}
		Player player = ((!Singleton<GameWorld>.Instantiated) ? null : Singleton<GameWorld>.Instance?.MainPlayer);
		Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
		if (!(player == null) || !(camera == null))
		{
			Vector3 cameraPosition = ((player != null) ? player.Position : camera.transform.position);
			long timestamp = Stopwatch.GetTimestamp();
			ApplyNativeLightBubble(cameraPosition, Plugin.LightCullDistance.Value);
			TerminalTickProfiler.Add("LightBub", Stopwatch.GetTimestamp() - timestamp);
		}
	}

	internal static void CutsceneBeginLightTracking()
	{
		CutsceneHold = true;
		_nextCutsceneLightRefresh = 0f;
		_cutsceneVisibleLights.Clear();
		_cutsceneLightStateKnown = false;
		try
		{
			CullingManager instance = CullingManager.Instance;
			if (instance != null)
			{
				instance.LockState(lockState: true);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[CutsceneHold] native manager lock failed: " + ex.Message);
		}
		if (_nativeCutsceneLights.Count == 0)
		{
			RefreshNativeCutsceneLightCache();
		}
		Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
		int num = ((camera != null) ? ApplyNativeLightBubble(camera.transform.position, 80f) : 0);
		Plugin.Log.LogDebug($"[CutsceneHold] armed - bounded {num}/{_nativeCutsceneLights.Count} native lights to {80f:0}m around the camera");
	}

	internal static void CutsceneRelease()
	{
		CutsceneHold = false;
		try
		{
			CullingManager instance = CullingManager.Instance;
			if (instance != null)
			{
				instance.LockState(lockState: false);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[CutsceneHold] release failed: " + ex.Message);
		}
		Player player = ((!Singleton<GameWorld>.Instantiated) ? null : Singleton<GameWorld>.Instance?.MainPlayer);
		Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
		Vector3 cameraPosition;
		if (player != null)
		{
			cameraPosition = player.Position;
		}
		else
		{
			cameraPosition = ((camera != null) ? camera.transform.position : Vector3.zero);
		}
		int num = ApplyNativeLightBubble(cameraPosition, Plugin.LightCullDistance.Value);
		Plugin.Log.LogDebug($"[CutsceneHold] released - {num}/{_nativeCutsceneLights.Count} native lights retained near gameplay camera, culling unlocked");
		_cutsceneVisibleLights.Clear();
		_cutsceneLightStateKnown = false;
	}
}
