using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace Manimal.Terminal;

public class TerminalAttackCutscene : MonoBehaviour
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_ArmAttackTimer
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!TerminalCoop.Active && TerminalGate.On && !_playedThisRaid && Plugin.AttackCutscene.Value)
			{
				new GameObject("Terminal_AttackCutsceneTimer").AddComponent<TimerHost>();
			}
		}
	}

	internal class TimerHost : MonoBehaviour
	{
		private float _armedAt;

		private void Start()
		{
			_armedAt = Time.realtimeSinceStartup;
		}

		private void Update()
		{
			if (_playedThisRaid)
			{
				UnityEngine.Object.Destroy(gameObject);
				return;
			}
			float num = TerminalIntroCutscene.FinishedAt;
			if (num < 0f)
			{
				if (Time.realtimeSinceStartup - _armedAt < 180f)
				{
					return;
				}
				num = _armedAt;
			}
			if (!(Time.realtimeSinceStartup - num < Plugin.AttackCutsceneDelay.Value))
			{
				if (!Available)
				{
					Plugin.Log.LogWarning("[AttackCutscene] 'Terminal_area_PortCutscene_01_Attack' not loadable (bundle missing the scene?) — skipped");
					_playedThisRaid = true;
					UnityEngine.Object.Destroy(gameObject);
				}
				else
				{
					_playedThisRaid = true;
					new GameObject("Terminal_AttackCutscene").AddComponent<TerminalAttackCutscene>();
					UnityEngine.Object.Destroy(gameObject);
				}
			}
		}
	}

	private const string SceneName = "Terminal_area_PortCutscene_01_Attack";

	private const float FadeDur = 0.7f;

	private static bool _playedThisRaid;

	internal static float FinishedAt = -1f;

	internal static bool PlayingNow;

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

	private readonly List<Behaviour> _pausedFx = new List<Behaviour>();

	private float _fade;

	private Texture2D _black;

	private bool _restored;

	private float _savedFov = -1f;

	private readonly List<double> _shakeTimes = new List<double>();

	private int _nextShake;

	private float _shakeLeft;

	private const float ShakeDur = 0.55f;

	private const float ShakeDeg = 0.8f;

	private float _lastFov = -1f;

	public static bool Available
	{
		get
		{
			try
			{
				return Application.CanStreamedLevelBeLoaded("Terminal_area_PortCutscene_01_Attack");
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
		if (skip || !TerminalCoop.LocalHuman || !Plugin.AttackCutscene.Value || !Available)
		{
			FinishedAt = Time.realtimeSinceStartup;
			if (TerminalCoop.LocalHuman)
			{
				TerminalCoop.Request(TerminalEvent.AttackDone);
			}
		}
		else
		{
			new GameObject("Terminal_AttackCutscene").AddComponent<TerminalAttackCutscene>();
		}
	}

	internal static void ResetForNewRaid()
	{
		_playedThisRaid = false;
		FinishedAt = -1f;
		PlayingNow = false;
	}

	private void Start()
	{
		StartCoroutine(Run());
	}

	private IEnumerator Run()
	{
		PlayingNow = true;
		TerminalAcoustics.SetAmbientSilenced(silent: true);
		GamePlayerOwner.SetIgnoreInputInNPCDialog(ignore: true);
		_inputLocked = true;
		yield return Fade(0f, 1f);
		AsyncOperation load = SceneManager.LoadSceneAsync("Terminal_area_PortCutscene_01_Attack", LoadSceneMode.Additive);
		if (load == null)
		{
			Bail("LoadSceneAsync returned null");
			yield break;
		}
		while (!load.isDone)
		{
			yield return null;
		}
		_scene = SceneManager.GetSceneByName("Terminal_area_PortCutscene_01_Attack");
		_sceneLoaded = _scene.IsValid() && _scene.isLoaded;
		if (!_sceneLoaded)
		{
			Bail("cutscene scene failed to load");
			yield break;
		}
		RestoreMachineGunWorldLayer();
		try
		{
			TerminalShaderRebind.RebindNow();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[AttackCutscene] shader rebind failed: " + ex.Message);
		}
		try
		{
			TerminalVolumetricLights.Restore();
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[AttackCutscene] volumetric pass failed: " + ex2.Message);
		}
		try
		{
			TerminalLights.CutsceneBeginLightTracking();
		}
		catch (Exception ex3)
		{
			Plugin.Log.LogWarning("[AttackCutscene] culler hold failed: " + ex3.Message);
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
			Bail("TimeLineDirector missing or playableAsset empty (Author 21A not run?)");
			yield break;
		}
		try
		{
			TimelineAsset timelineAsset = _director.playableAsset as TimelineAsset;
			if (timelineAsset != null)
			{
				foreach (TrackAsset outputTrack in timelineAsset.GetOutputTracks())
				{
					foreach (IMarker marker in outputTrack.GetMarkers())
					{
						if (marker is SignalEmitter signalEmitter && signalEmitter.asset != null && signalEmitter.asset.name.StartsWith("CameraShake"))
						{
							_shakeTimes.Add(signalEmitter.time);
						}
					}
				}
			}
			_shakeTimes.Sort();
			if (_shakeTimes.Count > 0)
			{
				Plugin.Log.LogInfo($"[AttackCutscene] {_shakeTimes.Count} camera-shake signal(s) armed");
			}
		}
		catch (Exception ex4)
		{
			Plugin.Log.LogWarning("[AttackCutscene] shake marker scan failed: " + ex4.Message);
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
		GameObject fadeCanvas = FindInScene(_scene, "UI_FadeToBlack");
		_fadeCanvas = fadeCanvas;
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
			catch (Exception ex5)
			{
				Plugin.Log.LogWarning("[AttackCutscene] fade rig fix failed: " + ex5.Message);
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
		_director.extrapolationMode = DirectorWrapMode.Hold;
		_director.timeUpdateMode = DirectorUpdateMode.DSPClock;
		TerminalAudioRouting.RouteTimeline(_scene, _director);
		_director.Play();
		if (TerminalCoop.Active && TerminalCoop.AttackStarted >= 0.0)
		{
			_director.time = Math.Min(_director.duration - 0.05, Math.Max(0.0, TerminalCoop.Now - TerminalCoop.AttackStarted));
		}
		TerminalSubtitles.Show("attack", _director);
		Plugin.Log.LogInfo("[AttackCutscene] playing '" + _director.playableAsset.name + "' " + string.Format("({0:0.0}s{1})", _director.duration, Plugin.AttackCutsceneSkippable.Value ? ", SPACE skips" : ""));
		yield return Fade(1f, 0f);
		double dur = _director.duration;
		float hardStop = Time.realtimeSinceStartup + (float)dur + 10f;
		while (Time.realtimeSinceStartup < hardStop && !(_director == null) && _director.state == PlayState.Playing && !(_director.time >= dur - 0.05))
		{
			if (_fadeImage != null)
			{
				Color color = _fadeImage.color;
				if (color.r > 0f || color.g > 0f || color.b > 0f)
				{
					_fadeImage.color = new Color(0f, 0f, 0f, color.a);
				}
			}
			while (_nextShake < _shakeTimes.Count && _director.time >= _shakeTimes[_nextShake])
			{
				_shakeLeft = 0.55f;
				_nextShake++;
			}
			if (Plugin.AttackCutsceneSkippable.Value && Input.GetKeyDown(KeyCode.Space))
			{
				Plugin.Log.LogInfo("[AttackCutscene] skipped");
				break;
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
	}

	private void RestoreMachineGunWorldLayer()
	{
		GameObject gameObject = FindInScene(_scene, "Cutscene_weapon_pkm");
		if (gameObject == null)
		{
			Plugin.Log.LogWarning("[AttackCutscene] Cutscene_weapon_pkm missing; machine-gun layer repair skipped");
			return;
		}
		int num = LayerMask.NameToLayer("Weapon Preview");
		int num2 = LayerMask.NameToLayer("Default");
		if (num < 0 || num2 < 0)
		{
			Plugin.Log.LogWarning("[AttackCutscene] weapon-preview/world layer unavailable; machine-gun layer repair skipped");
			return;
		}
		int num3 = 0;
		Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform in componentsInChildren)
		{
			if (transform.gameObject.layer == num)
			{
				transform.gameObject.layer = num2;
				num3++;
			}
		}
		if (num3 > 0)
		{
			Plugin.Log.LogInfo($"[AttackCutscene] restored machine gun to world layer: {num3} preview-layer object(s)");
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
		Quaternion rotation = transform.rotation;
		if (_shakeLeft > 0f)
		{
			_shakeLeft -= Time.unscaledDeltaTime;
			float num = Mathf.Clamp01(_shakeLeft / 0.55f);
			float num2 = 0.8f * num * num;
			float num3 = Time.unscaledTime * 23f;
			rotation *= Quaternion.Euler((Mathf.PerlinNoise(num3, 0.3f) - 0.5f) * 2f * num2, (Mathf.PerlinNoise(0.7f, num3) - 0.5f) * 2f * num2, (Mathf.PerlinNoise(num3, num3) - 0.5f) * num2);
		}
		cam.transform.SetPositionAndRotation(transform.position, rotation);
		TerminalLights.TrackCutsceneCamera(transform.position);
		float fieldOfView = _rigCam.fieldOfView;
		if (fieldOfView <= 1f || fieldOfView >= 179f)
		{
			if (_lastFov > 0f)
			{
				cam.fieldOfView = _lastFov;
			}
			return;
		}
		if (Plugin.DevMode.Value && _lastFov > 0f && Mathf.Abs(fieldOfView - _lastFov) > 5f)
		{
			Plugin.Log.LogWarning($"[AttackCutscene] fov jump {_lastFov:0.0} -> {fieldOfView:0.0} at t={_director?.time:0.00}s " + $"(frame {Time.unscaledDeltaTime * 1000f:0}ms) — passed through");
		}
		_lastFov = fieldOfView;
		cam.fieldOfView = fieldOfView;
	}

	private void Bail(string why)
	{
		Plugin.Log.LogWarning("[AttackCutscene] " + why + " — cutscene skipped");
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
		if (_restored)
		{
			return;
		}
		_restored = true;
		PlayingNow = false;
		if (FinishedAt < 0f)
		{
			FinishedAt = Time.realtimeSinceStartup;
		}
		if (TerminalCoop.Active)
		{
			TerminalCoop.Request(TerminalEvent.AttackDone);
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
			TerminalInteractables.ReassertDoors("after attack cutscene");
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
