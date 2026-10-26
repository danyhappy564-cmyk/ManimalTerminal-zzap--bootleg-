using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Visual;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Manimal.Terminal;

public class TerminalIntroCutscene : MonoBehaviour
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_PlayAtRaidStart
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalCoop.Active || !TerminalGate.On || _playedThisRaid)
			{
				return;
			}
			if (!Plugin.IntroCutscene.Value)
			{
				_playedThisRaid = true;
				if (FinishedAt < 0f)
				{
					FinishedAt = Time.realtimeSinceStartup;
				}
				Plugin.Log.LogInfo("[IntroCutscene] disabled by config — skipped");
			}
			else if (!Available)
			{
				Plugin.Log.LogWarning("[IntroCutscene] 'Terminal_area_PortCutscene_00_Intro' not loadable (bundle missing the scene?) — skipped");
			}
			else
			{
				_playedThisRaid = true;
				new GameObject("Terminal_IntroCutscene").AddComponent<TerminalIntroCutscene>();
			}
		}
	}

	private const string SceneName = "Terminal_area_PortCutscene_00_Intro";

	private const float FadeDur = 0.7f;

	private static readonly (string scene, string name)[] HideDuringCutscene = new (string, string)[1] { ("Terminal_Design_Stuff", "Inside_Door_Plastic_07_R_210-100 (1)") };

	private static bool _playedThisRaid;

	internal static bool PlayingNow;

	internal static float FinishedAt = -1f;

	private Scene _scene;

	private bool _sceneLoaded;

	private PlayableDirector _director;

	private Camera _rigCam;

	private Camera _realCam;

	private GameObject _fadeCanvas;

	private Image _fadeImage;

	private bool _driving;

	private bool _inputLocked;

	private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

	private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();

	private readonly List<(GameObject go, bool wasActive)> _hiddenProps = new List<(GameObject, bool)>();

	private readonly List<Behaviour> _pausedFx = new List<Behaviour>();

	private float _fade;

	private Texture2D _black;

	private bool _restored;

	private float _savedFov = -1f;

	private float _lastFov = -1f;

	public static bool Available
	{
		get
		{
			try
			{
				return Application.CanStreamedLevelBeLoaded("Terminal_area_PortCutscene_00_Intro");
			}
			catch
			{
				return false;
			}
		}
	}

	internal static void PlayCoop(bool skip)
	{
		if (_playedThisRaid)
		{
			return;
		}
		_playedThisRaid = true;
		if (skip || !TerminalCoop.LocalHuman || !Plugin.IntroCutscene.Value || !Available)
		{
			FinishedAt = Time.realtimeSinceStartup;
			if (TerminalCoop.LocalHuman)
			{
				TerminalCoop.Request(TerminalEvent.IntroDone);
			}
		}
		else
		{
			new GameObject("Terminal_IntroCutscene").AddComponent<TerminalIntroCutscene>();
		}
	}

	internal static void ResetForNewRaid()
	{
		_playedThisRaid = false;
		PlayingNow = false;
		FinishedAt = -1f;
	}

	private void TrySwapActorTop()
	{
		if (!Plugin.CutscenePlayerTop.Value)
		{
			return;
		}
		try
		{
			PlayerBody playerBody = Singleton<GameWorld>.Instance?.MainPlayer?.PlayerBody;
			LoddedSkin value = null;
			if (!playerBody || !playerBody.BodySkins.TryGetValue(EBodyModelPart.Body, out value) || !value)
			{
				Plugin.Log.LogDebug("[IntroCutscene] no player Body skin — actor keeps the packed top");
				return;
			}
			SkinnedMeshRenderer skinnedMeshRenderer = null;
			foreach (Renderer renderer in value.GetRenderers())
			{
				if (renderer is SkinnedMeshRenderer skinnedMeshRenderer2 && (bool)skinnedMeshRenderer2.sharedMesh && (!skinnedMeshRenderer || skinnedMeshRenderer2.sharedMesh.vertexCount > skinnedMeshRenderer.sharedMesh.vertexCount))
				{
					skinnedMeshRenderer = skinnedMeshRenderer2;
				}
			}
			if (!skinnedMeshRenderer)
			{
				Plugin.Log.LogDebug("[IntroCutscene] Body skin has no skinned mesh — packed top kept");
				return;
			}
			Transform transform = null;
			Transform transform2 = null;
			GameObject[] rootGameObjects = _scene.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				transform = FindDeep(gameObject.transform, "Actor_Top");
				if ((bool)transform)
				{
					transform2 = FindDeep(gameObject.transform, "Actor_Player");
					if (!transform2)
					{
						transform2 = transform.root;
					}
					break;
				}
			}
			SkinnedMeshRenderer skinnedMeshRenderer3 = null;
			if ((bool)transform)
			{
				skinnedMeshRenderer3 = transform.GetComponent<SkinnedMeshRenderer>();
				if (!skinnedMeshRenderer3)
				{
					skinnedMeshRenderer3 = transform.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
				}
			}
			if (!skinnedMeshRenderer3)
			{
				Plugin.Log.LogDebug("[IntroCutscene] Actor_Top has no SkinnedMeshRenderer — packed top kept");
				return;
			}
			Transform[] bones = skinnedMeshRenderer.bones;
			if (!TerminalCutsceneClothing.TryBind(skinnedMeshRenderer, skinnedMeshRenderer3, transform2, "IntroCutscene", out var mapped, out var mappedRoot, out var auxiliaryBones, out var failure))
			{
				Plugin.Log.LogWarning("[IntroCutscene] top swap aborted — " + failure + "; packed top kept");
				return;
			}
			skinnedMeshRenderer3.sharedMesh = skinnedMeshRenderer.sharedMesh;
			skinnedMeshRenderer3.sharedMaterials = skinnedMeshRenderer.sharedMaterials;
			skinnedMeshRenderer3.bones = mapped;
			if ((bool)mappedRoot)
			{
				skinnedMeshRenderer3.rootBone = mappedRoot;
			}
			skinnedMeshRenderer3.localBounds = skinnedMeshRenderer.localBounds;
			skinnedMeshRenderer3.updateWhenOffscreen = true;
			Plugin.Log.LogInfo("[IntroCutscene] actor wears the player's top: '" + skinnedMeshRenderer.sharedMesh.name + "' " + $"({bones.Length} bones remapped, {auxiliaryBones} clothing helper bone(s) carried, {skinnedMeshRenderer.sharedMaterials.Length} material(s))");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[IntroCutscene] top swap failed: " + ex.Message);
		}
	}

	private static Transform FindDeep(Transform t, string name)
	{
		if (t.name == name)
		{
			return t;
		}
		for (int i = 0; i < t.childCount; i++)
		{
			Transform transform = FindDeep(t.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	private void Start()
	{
		PlayingNow = true;
		StartCoroutine(Run());
	}

	private IEnumerator Run()
	{
		GamePlayerOwner.SetIgnoreInputInNPCDialog(ignore: true);
		_inputLocked = true;
		yield return Fade(0f, 1f);
		AsyncOperation load = SceneManager.LoadSceneAsync("Terminal_area_PortCutscene_00_Intro", LoadSceneMode.Additive);
		if (load == null)
		{
			Bail("LoadSceneAsync returned null");
			yield break;
		}
		while (!load.isDone)
		{
			yield return null;
		}
		_scene = SceneManager.GetSceneByName("Terminal_area_PortCutscene_00_Intro");
		_sceneLoaded = _scene.IsValid() && _scene.isLoaded;
		if (!_sceneLoaded)
		{
			Bail("cutscene scene failed to load");
			yield break;
		}
		try
		{
			TerminalShaderRebind.RebindNow();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[IntroCutscene] shader rebind failed: " + ex.Message);
		}
		try
		{
			TerminalVolumetricLights.Restore();
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[IntroCutscene] volumetric pass failed: " + ex2.Message);
		}
		try
		{
			TerminalLights.CutsceneBeginLightTracking();
		}
		catch (Exception ex3)
		{
			Plugin.Log.LogWarning("[IntroCutscene] culler hold failed: " + ex3.Message);
		}
		GameObject[] rootGameObjects = _scene.GetRootGameObjects();
		foreach (GameObject gameObject in rootGameObjects)
		{
			_director = gameObject.GetComponentInChildren<PlayableDirector>(includeInactive: true);
			if (_director != null)
			{
				break;
			}
		}
		if (_director == null || _director.playableAsset == null)
		{
			Bail("TimeLineDirector missing or playableAsset empty (Author 21 not run?)");
			yield break;
		}
		rootGameObjects = _scene.GetRootGameObjects();
		foreach (GameObject gameObject2 in rootGameObjects)
		{
			_rigCam = gameObject2.GetComponentInChildren<Camera>(includeInactive: true);
			if (_rigCam != null)
			{
				break;
			}
		}
		if (_rigCam == null)
		{
			Bail("no camera in cutscene scene");
			yield break;
		}
		_rigCam.enabled = false;
		AudioListener component = _rigCam.GetComponent<AudioListener>();
		if (component != null)
		{
			component.enabled = false;
		}
		_realCam = CameraManager.Instance?.Camera;
		if (_realCam == null)
		{
			_realCam = Camera.main;
		}
		if (_realCam == null)
		{
			Bail("no main camera");
			yield break;
		}
		(string, string)[] hideDuringCutscene = HideDuringCutscene;
		for (int i = 0; i < hideDuringCutscene.Length; i++)
		{
			(string, string) tuple = hideDuringCutscene[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			GameObject gameObject3 = FindInScene(SceneManager.GetSceneByName(item), item2);
			if (gameObject3 == null)
			{
				Plugin.Log.LogWarning("[IntroCutscene] hide target '" + item2 + "' not found in '" + item + "'");
			}
			else
			{
				_hiddenProps.Add((gameObject3, gameObject3.activeSelf));
				gameObject3.SetActive(value: false);
				Plugin.Log.LogDebug("[IntroCutscene] hid '" + item2 + "' for the cutscene");
			}
		}
		GameObject gameObject4 = FindInScene(_scene, "UI_FadeToBlack");
		_fadeCanvas = (gameObject4 ? gameObject4 : null);
		Canvas[] componentsInChildren;
		if (_fadeCanvas != null)
		{
			_fadeCanvas.SetActive(value: true);
			try
			{
				componentsInChildren = _fadeCanvas.GetComponentsInChildren<Canvas>(includeInactive: true);
				foreach (Canvas obj in componentsInChildren)
				{
					obj.renderMode = RenderMode.ScreenSpaceOverlay;
					obj.sortingOrder = 9999;
				}
				CanvasScaler[] componentsInChildren2 = _fadeCanvas.GetComponentsInChildren<CanvasScaler>(includeInactive: true);
				foreach (CanvasScaler obj2 in componentsInChildren2)
				{
					obj2.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
					obj2.referenceResolution = new Vector2(1920f, 1080f);
				}
				_fadeImage = _fadeCanvas.GetComponentInChildren<Image>(includeInactive: true);
				if (_fadeImage != null)
				{
					RectTransform rectTransform = _fadeImage.rectTransform;
					rectTransform.anchorMin = Vector2.zero;
					rectTransform.anchorMax = Vector2.one;
					rectTransform.offsetMin = Vector2.zero;
					rectTransform.offsetMax = Vector2.zero;
					_fadeImage.color = new Color(0f, 0f, 0f, _fadeImage.color.a);
				}
			}
			catch (Exception ex4)
			{
				Plugin.Log.LogWarning("[IntroCutscene] fade rig fix failed: " + ex4.Message);
			}
		}
		Behaviour[] components = _realCam.GetComponents<Behaviour>();
		foreach (Behaviour behaviour in components)
		{
			if (behaviour != null && behaviour.enabled && (behaviour is EffectsController || behaviour is CC_Base))
			{
				behaviour.enabled = false;
				_pausedFx.Add(behaviour);
			}
		}
		TrySwapActorTop();
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		if (player != null)
		{
			Renderer[] componentsInChildren3 = player.gameObject.GetComponentsInChildren<Renderer>(includeInactive: false);
			foreach (Renderer renderer in componentsInChildren3)
			{
				if (renderer != null && renderer.enabled)
				{
					renderer.enabled = false;
					_hiddenRenderers.Add(renderer);
				}
			}
		}
		componentsInChildren = UnityEngine.Object.FindObjectsOfType<Canvas>();
		foreach (Canvas canvas in componentsInChildren)
		{
			if (canvas != null && canvas.enabled && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.gameObject.scene != _scene)
			{
				canvas.enabled = false;
				_hiddenCanvases.Add(canvas);
			}
		}
		_savedFov = _realCam.fieldOfView;
		Camera.onPreCull = (Camera.CameraCallback)Delegate.Combine(Camera.onPreCull, new Camera.CameraCallback(DriveCamera));
		_driving = true;
		float barrier = Time.realtimeSinceStartup + 30f;
		_ = Singleton<GameWorld>.Instance;
		int lastBotCount = -1;
		float botsStableAt = Time.realtimeSinceStartup;
		while (Time.realtimeSinceStartup < barrier && (!AmbientReady() || !WeatherReady() || !SkyReady() || !BotsReady()))
		{
			yield return null;
		}
		try
		{
			TerminalLights.ApplyAmbient();
		}
		catch
		{
		}
		try
		{
			float fieldOfView = _rigCam.fieldOfView;
			if (fieldOfView > 1f && fieldOfView < 179f)
			{
				_realCam.fieldOfView = fieldOfView;
			}
			_realCam.transform.SetPositionAndRotation(_rigCam.transform.position, _rigCam.transform.rotation);
		}
		catch
		{
		}
		try
		{
			_director.time = 0.001;
			_director.Evaluate();
		}
		catch
		{
		}
		yield return null;
		yield return null;
		yield return null;
		Plugin.Log.LogInfo($"[IntroCutscene] staging barrier released — ambient={TerminalAcoustics.AmbientStaged} " + $"weather={TerminalWeather.Staged} sky={SkyReady()} bots={lastBotCount} " + $"(waited {30f - (barrier - Time.realtimeSinceStartup):0.0}s)");
		_director.extrapolationMode = DirectorWrapMode.Hold;
		_director.timeUpdateMode = DirectorUpdateMode.DSPClock;
		TerminalAcoustics.SetAmbientSilenced(silent: true);
		TerminalAudioRouting.RouteTimeline(_scene, _director);
		_director.Play();
		if (TerminalCoop.Active && TerminalCoop.IntroStarted >= 0.0)
		{
			_director.time = Math.Min(_director.duration - 0.05, Math.Max(0.0, TerminalCoop.Now - TerminalCoop.IntroStarted));
		}
		TerminalSubtitles.Show("intro_nocase", _director);
		Plugin.Log.LogInfo("[IntroCutscene] playing '" + _director.playableAsset.name + "' " + $"({_director.duration:0.0}s, unskippable)");
		yield return Fade(1f, 0f);
		double dur = _director.duration;
		float hardStop = Time.realtimeSinceStartup + (float)dur + 10f;
		while (Time.realtimeSinceStartup < hardStop && !(_director == null) && _director.state == PlayState.Playing && !(_director.time >= dur - 0.05))
		{
			if (Plugin.IntroCutsceneSkippable.Value && Input.GetKeyDown(KeyCode.Space))
			{
				Plugin.Log.LogInfo("[IntroCutscene] skipped by player");
				break;
			}
			if (_fadeImage != null)
			{
				Color color = _fadeImage.color;
				if (color.r > 0f || color.g > 0f || color.b > 0f)
				{
					_fadeImage.color = new Color(0f, 0f, 0f, color.a);
				}
			}
			yield return null;
		}
		yield return Fade(0f, 1f);
		Restore();
		if (_sceneLoaded)
		{
			AsyncOperation unload = SceneManager.UnloadSceneAsync(_scene);
			while (unload != null && !unload.isDone)
			{
				yield return null;
			}
			_sceneLoaded = false;
		}
		yield return new WaitForSecondsRealtime(0.8f);
		yield return Fade(1f, 0f, 1.6f);
		UnityEngine.Object.Destroy(base.gameObject);
		static bool AmbientReady()
		{
			return TerminalAcoustics.AmbientStaged;
		}
		bool BotsReady()
		{
			int num = 0;
			try
			{
				GameWorld instance = Singleton<GameWorld>.Instance;
				if (instance?.AllAlivePlayersList != null)
				{
					num = instance.AllAlivePlayersList.Count;
				}
			}
			catch
			{
			}
			if (num != lastBotCount)
			{
				lastBotCount = num;
				botsStableAt = Time.realtimeSinceStartup;
				return false;
			}
			return Time.realtimeSinceStartup - botsStableAt >= 3f;
		}
		static bool SkyReady()
		{
			try
			{
				TOD_Sky tOD_Sky = UnityEngine.Object.FindObjectOfType<TOD_Sky>();
				return tOD_Sky != null && tOD_Sky.Initialized;
			}
			catch
			{
				return false;
			}
		}
		static bool WeatherReady()
		{
			return TerminalWeather.TryEnsureRainVisualReady();
		}
	}

	private static GameObject FindInScene(Scene scn, string name)
	{
		if (!scn.IsValid() || !scn.isLoaded)
		{
			return null;
		}
		GameObject[] rootGameObjects = scn.GetRootGameObjects();
		foreach (GameObject gameObject in rootGameObjects)
		{
			if (gameObject.name == name)
			{
				return gameObject;
			}
			Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform in componentsInChildren)
			{
				if (transform != null && transform.name == name)
				{
					return transform.gameObject;
				}
			}
		}
		return null;
	}

	private void DriveCamera(Camera cam)
	{
		if (cam != _realCam || _rigCam == null)
		{
			return;
		}
		Transform transform = _rigCam.transform;
		cam.transform.SetPositionAndRotation(transform.position, transform.rotation);
		TerminalLights.TrackCutsceneCamera(transform.position);
		float fieldOfView = _rigCam.fieldOfView;
		if (fieldOfView <= 1f || fieldOfView >= 179f)
		{
			if (Plugin.DevMode.Value)
			{
				Plugin.Log.LogWarning($"[IntroCutscene] rig fov absurd ({fieldOfView:0.0}) at t={_director?.time:0.00}s — holding {_lastFov:0.0}");
			}
			if (_lastFov > 0f)
			{
				cam.fieldOfView = _lastFov;
			}
		}
		else
		{
			if (Plugin.DevMode.Value && _lastFov > 0f && Mathf.Abs(fieldOfView - _lastFov) > 5f)
			{
				Plugin.Log.LogWarning($"[IntroCutscene] fov jump {_lastFov:0.0} -> {fieldOfView:0.0} at t={_director?.time:0.00}s " + $"(frame {Time.unscaledDeltaTime * 1000f:0}ms) — passed through");
			}
			_lastFov = fieldOfView;
			cam.fieldOfView = fieldOfView;
		}
	}

	private void Bail(string why)
	{
		Plugin.Log.LogWarning("[IntroCutscene] " + why + " — cutscene skipped");
		Restore();
		if (_sceneLoaded)
		{
			SceneManager.UnloadSceneAsync(_scene);
			_sceneLoaded = false;
		}
		UnityEngine.Object.Destroy(gameObject);
	}

	private void Restore()
	{
		PlayingNow = false;
		if (_restored)
		{
			return;
		}
		_restored = true;
		TerminalSubtitles.Hide();
		if (FinishedAt < 0f)
		{
			FinishedAt = Time.realtimeSinceStartup;
		}
		if (TerminalCoop.Active)
		{
			TerminalCoop.Request(TerminalEvent.IntroDone);
		}
		if (_driving)
		{
			Camera.onPreCull = (Camera.CameraCallback)Delegate.Remove(Camera.onPreCull, new Camera.CameraCallback(DriveCamera));
			_driving = false;
		}
		_lastFov = -1f;
		try
		{
			TerminalAcoustics.SetAmbientSilenced(silent: false);
		}
		catch
		{
		}
		try
		{
			TerminalAcoustics.RepairInteractiveLayers();
		}
		catch
		{
		}
		try
		{
			TerminalInteractables.ReassertDoors("after intro cutscene");
		}
		catch
		{
		}
		try
		{
			TerminalLights.CutsceneRelease();
		}
		catch
		{
		}
		if (_realCam != null && _savedFov > 0f)
		{
			_realCam.fieldOfView = _savedFov;
		}
		if (_director != null && _director.state == PlayState.Playing)
		{
			_director.Stop();
		}
		if (_fadeCanvas != null)
		{
			_fadeCanvas.SetActive(value: false);
			_fadeCanvas = null;
		}
		foreach (var (gameObject, active) in _hiddenProps)
		{
			if (gameObject != null)
			{
				gameObject.SetActive(active);
			}
		}
		_hiddenProps.Clear();
		foreach (Renderer hiddenRenderer in _hiddenRenderers)
		{
			if (hiddenRenderer != null)
			{
				hiddenRenderer.enabled = true;
			}
		}
		_hiddenRenderers.Clear();
		foreach (Behaviour item in _pausedFx)
		{
			if (item != null)
			{
				item.enabled = true;
			}
		}
		_pausedFx.Clear();
		foreach (Canvas hiddenCanvase in _hiddenCanvases)
		{
			if (hiddenCanvase != null)
			{
				hiddenCanvase.enabled = true;
			}
		}
		_hiddenCanvases.Clear();
		if (_inputLocked)
		{
			GamePlayerOwner.SetIgnoreInputInNPCDialog(ignore: false);
			_inputLocked = false;
		}
	}

	private IEnumerator Fade(float from, float to, float dur = 0.7f)
	{
		float t = 0f;
		while (t < dur)
		{
			t += Time.unscaledDeltaTime;
			_fade = Mathf.Lerp(from, to, t / dur);
			yield return null;
		}
		_fade = to;
	}

	private void OnGUI()
	{
		if (!(_fade <= 0f))
		{
			if (_black == null)
			{
				_black = new Texture2D(1, 1);
				_black.SetPixel(0, 0, Color.black);
				_black.Apply();
			}
			GUI.depth = -10000;
			GUI.color = new Color(0f, 0f, 0f, _fade);
			GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _black);
			GUI.color = Color.white;
		}
	}

	private void OnDestroy()
	{
		Restore();
		if (_sceneLoaded)
		{
			SceneManager.UnloadSceneAsync(_scene);
			_sceneLoaded = false;
		}
		if (_black != null)
		{
			UnityEngine.Object.Destroy(_black);
		}
	}
}
