using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Visual;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace Manimal.Terminal;

internal class EndingRunner : MonoBehaviour
{
	public BaseLocalGame<EftGamePlayerOwner> Game;

	public string ProfileId;

	public string ExitName;

	private Scene _scene;

	private bool _sceneLoaded;

	private PlayableDirector _director;

	private Camera _rigCam;

	private Camera _realCam;

	private GameObject _fadeCanvas;

	private Image _fadeImage;

	private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

	private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();

	private readonly List<Behaviour> _pausedFx = new List<Behaviour>();

	private readonly List<(GameObject go, bool wasActive)> _hiddenProps = new List<(GameObject, bool)>();

	private static readonly string[] HideDuringCutscene = new string[5] { "OO/Ships/Zubr_01/Zubr", "OO/Ships/Zubr_01/Trap", "OO/Ships/Container_ship_01", "OO/Ships/Container_ship_02", "OO/pallet_weapon_box_1_update (6)" };

	private bool _driving;

	private bool _released;

	private bool _inputLocked;

	private Canvas _ownCanvas;

	private Image _ownFade;

	private void Start()
	{
		TerminalEndingCutscene.PlayingNow = true;
		StartCoroutine(Run());
	}

	private IEnumerator Run()
	{
		BuildOwnFade();
		yield return Fade(0f, 1f);
		AsyncOperation load = SceneManager.LoadSceneAsync("Terminal_area_PortCutscene_02_Ending_00", LoadSceneMode.Additive);
		if (load == null)
		{
			Bail("LoadSceneAsync returned null");
			yield break;
		}
		while (!load.isDone)
		{
			yield return null;
		}
		_scene = SceneManager.GetSceneByName("Terminal_area_PortCutscene_02_Ending_00");
		_sceneLoaded = _scene.IsValid() && _scene.isLoaded;
		if (!_sceneLoaded)
		{
			Bail("ending scene failed to load");
			yield break;
		}
		try
		{
			TerminalShaderRebind.RebindNow();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] shader rebind failed: " + ex.Message);
		}
		try
		{
			TerminalLights.CutsceneBeginLightTracking();
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Ending] culler hold failed: " + ex2.Message);
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
			Bail("director missing or playableAsset empty (Author 21B not run?)");
			yield break;
		}
		int num = 0;
		int num2 = 0;
		rootGameObjects = _scene.GetRootGameObjects();
		foreach (GameObject gameObject2 in rootGameObjects)
		{
			Animator[] componentsInChildren = gameObject2.GetComponentsInChildren<Animator>(includeInactive: true);
			for (int j = 0; j < componentsInChildren.Length; j++)
			{
				componentsInChildren[j].cullingMode = AnimatorCullingMode.AlwaysAnimate;
				num++;
			}
			SkinnedMeshRenderer[] componentsInChildren2 = gameObject2.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
			for (int j = 0; j < componentsInChildren2.Length; j++)
			{
				componentsInChildren2[j].updateWhenOffscreen = true;
				num2++;
			}
		}
		Plugin.Log.LogInfo($"[Ending] anti-cull: {num} animator(s) AlwaysAnimate, {num2} skinned mesh(es) updateWhenOffscreen");
		ApplyVariantMutes();
		RepairBindings();
		rootGameObjects = _scene.GetRootGameObjects();
		foreach (GameObject gameObject3 in rootGameObjects)
		{
			_rigCam = gameObject3.GetComponentInChildren<Camera>(includeInactive: true);
			if (_rigCam != null)
			{
				break;
			}
		}
		if (_rigCam == null)
		{
			Bail("no camera in ending scene");
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
		Canvas[] componentsInChildren3;
		if (_fadeCanvas != null)
		{
			_fadeCanvas.SetActive(value: true);
			try
			{
				componentsInChildren3 = _fadeCanvas.GetComponentsInChildren<Canvas>(includeInactive: true);
				foreach (Canvas obj in componentsInChildren3)
				{
					obj.renderMode = RenderMode.ScreenSpaceOverlay;
					obj.sortingOrder = 9998;
				}
				CanvasScaler[] componentsInChildren4 = _fadeCanvas.GetComponentsInChildren<CanvasScaler>(includeInactive: true);
				foreach (CanvasScaler obj2 in componentsInChildren4)
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
			catch (Exception ex3)
			{
				Plugin.Log.LogWarning("[Ending] fade rig fix failed: " + ex3.Message);
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
		HideRetailProps();
		TrySwapActorPart(EBodyModelPart.Body, "Actor_Top");
		TrySwapActorPart(EBodyModelPart.Feet, "Actor_Pants");
		try
		{
			GamePlayerOwner.SetIgnoreInputInNPCDialog(ignore: true);
			_inputLocked = true;
		}
		catch
		{
		}
		GameWorld instance = Singleton<GameWorld>.Instance;
		if (instance != null && instance.AllAlivePlayersList != null)
		{
			foreach (Player allAlivePlayers in instance.AllAlivePlayersList)
			{
				if (allAlivePlayers == null)
				{
					continue;
				}
				Renderer[] componentsInChildren5 = allAlivePlayers.gameObject.GetComponentsInChildren<Renderer>(includeInactive: false);
				foreach (Renderer renderer in componentsInChildren5)
				{
					if (renderer != null && renderer.enabled)
					{
						renderer.enabled = false;
						_hiddenRenderers.Add(renderer);
					}
				}
			}
		}
		if ((bool)_realCam)
		{
			Renderer[] componentsInChildren5 = _realCam.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer2 in componentsInChildren5)
			{
				if (renderer2 != null && renderer2.enabled)
				{
					renderer2.enabled = false;
					_hiddenRenderers.Add(renderer2);
				}
			}
		}
		componentsInChildren3 = UnityEngine.Object.FindObjectsOfType<Canvas>();
		foreach (Canvas canvas in componentsInChildren3)
		{
			if (canvas != null && canvas.enabled && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.gameObject.scene != _scene && canvas != _ownCanvas)
			{
				canvas.enabled = false;
				_hiddenCanvases.Add(canvas);
			}
		}
		Camera.onPreCull = (Camera.CameraCallback)Delegate.Combine(Camera.onPreCull, new Camera.CameraCallback(DriveCamera));
		StartCoroutine(RehideSweep());
		_driving = true;
		_director.extrapolationMode = DirectorWrapMode.Hold;
		_director.timeUpdateMode = DirectorUpdateMode.DSPClock;
		TerminalAcoustics.SetAmbientSilenced(silent: true);
		TerminalAudioRouting.RouteTimeline(_scene, _director);
		try
		{
			if (!TerminalMissionWeather.TryApplyEnding00())
			{
				Plugin.Log.LogWarning("[Ending] Ending_00 weather profile was not applied; keeping the current weather authority");
			}
		}
		catch (Exception ex4)
		{
			Plugin.Log.LogWarning("[Ending] Ending_00 weather apply failed: " + ex4.Message);
		}
		_director.Play();
		TerminalSubtitles.Show("ending_00", _director);
		Plugin.Log.LogInfo($"[Ending] playing '{_director.playableAsset.name}' ({_director.duration:0.0}s, SPACE skips)");
		StartCoroutine(BindingProbe());
		yield return Fade(1f, 0f, 0.8f);
		double dur = _director.duration;
		float hardStop = Time.realtimeSinceStartup + (float)dur + 10f;
		float lastAlpha = ((_fadeImage != null) ? _fadeImage.color.a : 0f);
		float lastAlphaMove = Time.realtimeSinceStartup;
		bool fadeNeutered = false;
		while (Time.realtimeSinceStartup < hardStop && !(_director == null) && _director.state == PlayState.Playing && !(_director.time >= dur - 0.05))
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				Plugin.Log.LogInfo("[Ending] skipped");
				break;
			}
			if (_fadeImage != null)
			{
				Color color = _fadeImage.color;
				if (color.r > 0f || color.g > 0f || color.b > 0f)
				{
					_fadeImage.color = new Color(0f, 0f, 0f, color.a);
					color = _fadeImage.color;
				}
				if (fadeNeutered)
				{
					if (color.a > 0.001f)
					{
						_fadeImage.color = new Color(0f, 0f, 0f, 0f);
					}
				}
				else if (Mathf.Abs(color.a - lastAlpha) > 0.002f)
				{
					lastAlpha = color.a;
					lastAlphaMove = Time.realtimeSinceStartup;
				}
				else if (color.a > 0.5f && Time.realtimeSinceStartup - lastAlphaMove > 2f)
				{
					fadeNeutered = true;
					_fadeImage.color = new Color(0f, 0f, 0f, 0f);
					Plugin.Log.LogWarning($"[Ending] retail UI_FadeToBlack sat opaque (a={color.a:0.00}) for 2s with no " + "timeline movement — its alpha track is dead on this rip. neutralised for the rest of the take; our own fade still handles the in/out.");
				}
			}
			yield return null;
		}
		yield return Fade(0f, 1f);
		Release("finished");
	}

	private void ApplyVariantMutes()
	{
		try
		{
			TimelineAsset timelineAsset = _director.playableAsset as TimelineAsset;
			if (timelineAsset == null)
			{
				return;
			}
			string text = "";
			try
			{
				Profile profile = Singleton<GameWorld>.Instance?.MainPlayer?.Profile;
				if (profile != null)
				{
					text = Singleton<CustomizationSolver>.Instance.GetVoice(profile.Customization[EBodyModelPart.Voice])?.Name ?? "";
				}
			}
			catch
			{
			}
			bool flag = false;
			try
			{
				GameWorld instance = Singleton<GameWorld>.Instance;
				PropertyInfo propertyInfo = ((instance != null) ? AccessTools.Property(instance.GetType(), "Season") : null);
				object obj2 = ((propertyInfo != null) ? propertyInfo.GetValue(instance) : null);
				flag = obj2 != null && obj2.ToString().IndexOf("Winter", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			catch
			{
			}
			List<string> list = new List<string>();
			List<TrackAsset> list2 = new List<TrackAsset>();
			foreach (TrackAsset outputTrack in timelineAsset.GetOutputTracks())
			{
				list2.Add(outputTrack);
				list.Add(outputTrack.name);
			}
			foreach (TrackAsset rootTrack in timelineAsset.GetRootTracks())
			{
				if (!list2.Contains(rootTrack))
				{
					list2.Add(rootTrack);
					list.Add(rootTrack.name);
				}
			}
			string text2 = null;
			foreach (string item in list)
			{
				if (item == text + "_Voice")
				{
					text2 = item;
					break;
				}
			}
			if (text2 == null)
			{
				string value = (text.StartsWith("Usec", StringComparison.OrdinalIgnoreCase) ? "Usec" : "Bear");
				foreach (string item2 in list)
				{
					if (item2.StartsWith(value) && item2.EndsWith("_Voice"))
					{
						text2 = item2;
						break;
					}
				}
			}
			int num = 0;
			int num2 = 0;
			foreach (TrackAsset item3 in list2)
			{
				string text3 = item3.name ?? "";
				bool? flag2 = null;
				if (text3.EndsWith("_Voice"))
				{
					flag2 = text3 == text2;
				}
				if (text3.Contains("Winter_Additional"))
				{
					flag2 = flag;
				}
				if (text3.Contains("Summer_Additional"))
				{
					flag2 = !flag;
				}
				if (text3.Contains("Case") && !text3.Contains("NoCase"))
				{
					flag2 = false;
				}
				if (flag2.HasValue && item3.muted != !flag2.Value)
				{
					item3.muted = !flag2.Value;
					if (flag2.Value)
					{
						num2++;
					}
					else
					{
						num++;
					}
				}
			}
			Plugin.Log.LogInfo("[Ending] variants applied: voice track '" + (text2 ?? "none") + "' (profile '" + text + "'), " + string.Format("{0}, NoCase — {1} unmuted, {2} muted of {3}", flag ? "winter" : "summer", num2, num, list2.Count));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] variant mutes failed: " + ex.Message);
		}
	}

	private void HideRetailProps()
	{
		try
		{
			Transform transform = TerminalRigFill.FindRootNamed("SBG_Terminal_Design_Stuff");
			if (!transform)
			{
				Scene sceneByName = SceneManager.GetSceneByName("Terminal_Design_Stuff");
				if (sceneByName.IsValid() && sceneByName.isLoaded && sceneByName.GetRootGameObjects().Length != 0)
				{
					transform = sceneByName.GetRootGameObjects()[0].transform;
				}
			}
			if (!transform)
			{
				Plugin.Log.LogWarning("[Ending] Design_Stuff root not found — retail hide list skipped");
				return;
			}
			string[] hideDuringCutscene = HideDuringCutscene;
			foreach (string text in hideDuringCutscene)
			{
				Transform transform2 = transform.Find(text);
				if (transform2 == null)
				{
					Plugin.Log.LogDebug("[Ending] hide target '" + text + "' not found");
					continue;
				}
				_hiddenProps.Add((transform2.gameObject, transform2.gameObject.activeSelf));
				transform2.gameObject.SetActive(value: false);
			}
			Plugin.Log.LogInfo($"[Ending] retail hide list applied ({_hiddenProps.Count}/{HideDuringCutscene.Length})");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] retail hides failed: " + ex.Message);
		}
	}

	private void TrySwapActorPart(EBodyModelPart part, string actorGoName)
	{
		if (!Plugin.CutscenePlayerTop.Value)
		{
			return;
		}
		try
		{
			PlayerBody playerBody = Singleton<GameWorld>.Instance?.MainPlayer?.PlayerBody;
			LoddedSkin value = null;
			if (!playerBody || !playerBody.BodySkins.TryGetValue(part, out value) || !value)
			{
				Plugin.Log.LogDebug($"[Ending] no player {part} skin — actor keeps packed {actorGoName}");
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
				Plugin.Log.LogDebug($"[Ending] {part} skin has no skinned mesh — packed {actorGoName} kept");
				return;
			}
			Transform transform = null;
			Transform transform2 = null;
			GameObject[] rootGameObjects = _scene.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				transform = FindDeep(gameObject.transform, actorGoName);
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
				Plugin.Log.LogDebug("[Ending] " + actorGoName + " has no SkinnedMeshRenderer — packed kept");
				return;
			}
			Animator componentInChildren = transform2.GetComponentInChildren<Animator>(includeInactive: true);
			Transform transform3 = (componentInChildren ? componentInChildren.transform : transform2);
			Transform[] bones = skinnedMeshRenderer.bones;
			if (!TerminalCutsceneClothing.TryBind(skinnedMeshRenderer, skinnedMeshRenderer3, transform2, "Ending", out var mapped, out var mappedRoot, out var auxiliaryBones, out var failure))
			{
				Plugin.Log.LogWarning("[Ending] " + actorGoName + " swap aborted — " + failure + "; packed clothing kept");
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
			int num = 0;
			for (int j = 0; j < mapped.Length; j++)
			{
				if ((bool)mapped[j] && mapped[j].IsChildOf(transform3))
				{
					num++;
				}
			}
			Plugin.Log.LogInfo($"[Ending] actor wears the player's {part}: '{skinnedMeshRenderer.sharedMesh.name}' " + $"({bones.Length} bones remapped, {num} on the animated rig '{transform3.name}', " + $"{auxiliaryBones} clothing helper bone(s) carried, {skinnedMeshRenderer.sharedMaterials.Length} material(s))");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] " + actorGoName + " swap failed: " + ex.Message);
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

	private void RepairBindings()
	{
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_ending_bindings.json");
			if (!File.Exists(path) || !(JObject.Parse(File.ReadAllText(path))["bindings"] is JArray jArray))
			{
				return;
			}
			Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>();
			GameObject[] rootGameObjects = _scene.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				TerminalRigFill.IndexTree(gameObject.transform, gameObject.name, dictionary);
			}
			TimelineAsset timelineAsset = _director.playableAsset as TimelineAsset;
			if (timelineAsset == null)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			HashSet<int> hashSet = new HashSet<int>();
			foreach (TrackAsset outputTrack in timelineAsset.GetOutputTracks())
			{
				if (_director.GetGenericBinding(outputTrack) != null || (!(outputTrack is AnimationTrack) && !(outputTrack is AudioTrack)))
				{
					continue;
				}
				num++;
				for (int j = 0; j < jArray.Count; j++)
				{
					if (hashSet.Contains(j))
					{
						continue;
					}
					JObject jObject = (JObject)jArray[j];
					if (jObject.Value<string>("track") != outputTrack.name)
					{
						continue;
					}
					string text = jObject.Value<string>("path");
					string text2 = jObject.Value<string>("kind");
					if (string.IsNullOrEmpty(text) || !dictionary.TryGetValue(text, out var value))
					{
						continue;
					}
					hashSet.Add(j);
					UnityEngine.Object obj = null;
					switch (text2)
					{
					case "GameObject":
						obj = value.gameObject;
						break;
					case "Transform":
						obj = value;
						break;
					case "Animator":
					{
						Animator animator = value.GetComponent<Animator>();
						if (!animator)
						{
							animator = value.gameObject.AddComponent<Animator>();
							num3++;
						}
						obj = animator;
						break;
					}
					case "AudioSource":
					{
						AudioSource audioSource = value.GetComponent<AudioSource>();
						if (!audioSource)
						{
							audioSource = value.gameObject.AddComponent<AudioSource>();
						}
						obj = audioSource;
						break;
					}
					}
					if (obj != null)
					{
						_director.SetGenericBinding(outputTrack, obj);
						num2++;
					}
					break;
				}
			}
			if (num > 0)
			{
				Plugin.Log.LogWarning($"[Ending] binding repair: {num} null binding(s) found, {num2} re-wired" + ((num3 > 0) ? $", {num3} Animator(s) added" : ""));
			}
			if (num2 > 0)
			{
				_director.RebuildGraph();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] binding repair failed: " + ex.Message);
		}
	}

	private IEnumerator BindingProbe()
	{
		yield return new WaitForSecondsRealtime(1f);
		List<string> names = new List<string>();
		List<Transform> probes = new List<Transform>();
		List<Vector3> starts = new List<Vector3>();
		try
		{
			TimelineAsset timelineAsset = ((_director != null) ? (_director.playableAsset as TimelineAsset) : null);
			if (timelineAsset == null)
			{
				yield break;
			}
			foreach (TrackAsset outputTrack in timelineAsset.GetOutputTracks())
			{
				if (outputTrack is AnimationTrack)
				{
					Animator animator = _director.GetGenericBinding(outputTrack) as Animator;
					if (!animator)
					{
						Plugin.Log.LogWarning("[Ending][probe] '" + outputTrack.name + "': binding null or not an Animator");
						continue;
					}
					SkinnedMeshRenderer componentInChildren = animator.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
					Transform transform = (((bool)componentInChildren && (bool)componentInChildren.rootBone) ? componentInChildren.rootBone : animator.transform);
					names.Add(outputTrack.name);
					probes.Add(transform);
					starts.Add(transform.position);
					Plugin.Log.LogInfo("[Ending][probe] '" + outputTrack.name + "' -> " + animator.gameObject.name + $" active={animator.gameObject.activeInHierarchy} enabled={animator.enabled}" + " avatar=" + (animator.avatar ? animator.avatar.name : "none") + " bone='" + transform.name + "'");
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending][probe] setup failed: " + ex.Message);
			yield break;
		}
		try
		{
			string[] array = new string[2] { "Actor_Top", "Actor_Pants" };
			foreach (string text in array)
			{
				GameObject[] rootGameObjects = _scene.GetRootGameObjects();
				for (int j = 0; j < rootGameObjects.Length; j++)
				{
					Transform transform2 = FindDeep(rootGameObjects[j].transform, text);
					SkinnedMeshRenderer skinnedMeshRenderer = (transform2 ? transform2.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) : null);
					if ((bool)skinnedMeshRenderer && skinnedMeshRenderer.bones != null && skinnedMeshRenderer.bones.Length != 0 && (bool)skinnedMeshRenderer.bones[0])
					{
						Transform transform3 = skinnedMeshRenderer.bones[0];
						names.Add("swap:" + text);
						probes.Add(transform3);
						starts.Add(transform3.position);
						Plugin.Log.LogInfo("[Ending][probe] swap '" + text + "' mesh='" + (skinnedMeshRenderer.sharedMesh ? skinnedMeshRenderer.sharedMesh.name : "null") + "'" + $" bone0='{transform3.name}' visible={skinnedMeshRenderer.isVisible} enabled={skinnedMeshRenderer.enabled}");
						break;
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Ending][probe] swap probe failed: " + ex2.Message);
		}
		yield return new WaitForSecondsRealtime(2f);
		try
		{
			for (int k = 0; k < probes.Count; k++)
			{
				if ((bool)probes[k])
				{
					float magnitude = (probes[k].position - starts[k]).magnitude;
					Plugin.Log.LogInfo($"[Ending][probe] '{names[k]}' bone '{probes[k].name}' moved {magnitude:F3}m in 2s" + ((magnitude < 0.001f) ? "  << STATIC" : ""));
				}
			}
			if (_director != null)
			{
				Plugin.Log.LogInfo($"[Ending][probe] director time={_director.time:F2} state={_director.state}");
			}
		}
		catch (Exception ex3)
		{
			Plugin.Log.LogWarning("[Ending][probe] readback failed: " + ex3.Message);
		}
	}

	private IEnumerator RehideSweep()
	{
		for (int i = 0; i < 10; i++)
		{
			yield return new WaitForSecondsRealtime(0.5f);
			if (_released)
			{
				break;
			}
			try
			{
				GameWorld instance = Singleton<GameWorld>.Instance;
				if (instance != null && instance.AllAlivePlayersList != null)
				{
					foreach (Player allAlivePlayers in instance.AllAlivePlayersList)
					{
						if (allAlivePlayers == null)
						{
							continue;
						}
						Renderer[] componentsInChildren = allAlivePlayers.gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
						foreach (Renderer renderer in componentsInChildren)
						{
							if (renderer != null && renderer.enabled)
							{
								renderer.enabled = false;
								_hiddenRenderers.Add(renderer);
							}
						}
						Player.AbstractHandsController handsController = allAlivePlayers.HandsController;
						Transform transform = ((handsController != null) ? (AccessTools.Property(handsController.GetType(), "WeaponRoot")?.GetValue(handsController) as Transform) : null);
						if (!transform)
						{
							continue;
						}
						componentsInChildren = transform.GetComponentsInChildren<Renderer>(includeInactive: true);
						foreach (Renderer renderer2 in componentsInChildren)
						{
							if (renderer2 != null && renderer2.enabled)
							{
								renderer2.enabled = false;
								_hiddenRenderers.Add(renderer2);
							}
						}
					}
				}
				if ((bool)_realCam)
				{
					Renderer[] componentsInChildren = _realCam.GetComponentsInChildren<Renderer>(includeInactive: true);
					foreach (Renderer renderer3 in componentsInChildren)
					{
						if (renderer3 != null && renderer3.enabled)
						{
							renderer3.enabled = false;
							_hiddenRenderers.Add(renderer3);
						}
					}
				}
				LaserBeam[] array = UnityEngine.Object.FindObjectsOfType<LaserBeam>();
				foreach (LaserBeam laserBeam in array)
				{
					if (laserBeam != null && laserBeam.enabled)
					{
						laserBeam.enabled = false;
						_pausedFx.Add(laserBeam);
					}
				}
				GameWorld instance2 = Singleton<GameWorld>.Instance;
				if (!(instance2 != null) || instance2.AllAlivePlayersList == null)
				{
					continue;
				}
				foreach (Player allAlivePlayers2 in instance2.AllAlivePlayersList)
				{
					if (allAlivePlayers2 == null)
					{
						continue;
					}
					Light[] componentsInChildren2 = allAlivePlayers2.gameObject.GetComponentsInChildren<Light>(includeInactive: true);
					foreach (Light light in componentsInChildren2)
					{
						if (light != null && light.enabled)
						{
							light.enabled = false;
							_pausedFx.Add(light);
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	private void DriveCamera(Camera cam)
	{
		if (!(cam != _realCam) && !(_rigCam == null))
		{
			Transform transform = _rigCam.transform;
			cam.transform.SetPositionAndRotation(transform.position, transform.rotation);
			TerminalLights.TrackCutsceneCamera(transform.position);
			float fieldOfView = _rigCam.fieldOfView;
			if (fieldOfView > 1f && fieldOfView < 179f)
			{
				cam.fieldOfView = fieldOfView;
			}
		}
	}

	private void BuildOwnFade()
	{
		GameObject gameObject = new GameObject("Terminal_EndingFade");
		gameObject.transform.SetParent(transform, worldPositionStays: false);
		_ownCanvas = gameObject.AddComponent<Canvas>();
		_ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_ownCanvas.sortingOrder = 9999;
		GameObject gameObject2 = new GameObject("Black");
		gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
		_ownFade = gameObject2.AddComponent<Image>();
		_ownFade.color = new Color(0f, 0f, 0f, 0f);
		RectTransform rectTransform = _ownFade.rectTransform;
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
	}

	private IEnumerator Fade(float from, float to, float dur = 0.6f)
	{
		for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
		{
			if (_ownFade != null)
			{
				_ownFade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / dur));
			}
			yield return null;
		}
		if (_ownFade != null)
		{
			_ownFade.color = new Color(0f, 0f, 0f, to);
		}
	}

	private void Bail(string why)
	{
		Plugin.Log.LogWarning("[Ending] " + why + " — extracting without the cutscene");
		Release("bail");
	}

	private void Release(string how)
	{
		if (_released)
		{
			return;
		}
		_released = true;
		try
		{
			Restore();
		}
		catch
		{
		}
		try
		{
			TerminalEndingCutscene.PassThrough = true;
			if (Game != null)
			{
				Game.Stop(ProfileId, ExitStatus.Survived, ExitName);
			}
			Plugin.Log.LogInfo("[Ending] raid end released (" + how + ")");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[Ending] releasing Stop FAILED ({how}): {arg}");
		}
		try
		{
			TerminalEpilogueScreen.ArmAfterExtract();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] epilogue arm failed: " + ex.Message);
		}
		UnityEngine.Object.Destroy(gameObject, 1f);
	}

	private void Restore()
	{
		TerminalEndingCutscene.PlayingNow = false;
		try
		{
			TerminalMissionWeather.ResetEndingWeather();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ending] Ending_00 weather restore failed: " + ex.Message);
		}
		if (_inputLocked)
		{
			try
			{
				GamePlayerOwner.SetIgnoreInputInNPCDialog(ignore: false);
			}
			catch
			{
			}
			_inputLocked = false;
		}
		if (_driving)
		{
			Camera.onPreCull = (Camera.CameraCallback)Delegate.Remove(Camera.onPreCull, new Camera.CameraCallback(DriveCamera));
			_driving = false;
		}
		foreach (Canvas hiddenCanvase in _hiddenCanvases)
		{
			if ((bool)hiddenCanvase)
			{
				hiddenCanvase.enabled = true;
			}
		}
		_hiddenCanvases.Clear();
		foreach (Renderer hiddenRenderer in _hiddenRenderers)
		{
			if ((bool)hiddenRenderer)
			{
				hiddenRenderer.enabled = true;
			}
		}
		_hiddenRenderers.Clear();
		foreach (Behaviour item in _pausedFx)
		{
			if ((bool)item)
			{
				item.enabled = true;
			}
		}
		_pausedFx.Clear();
		foreach (var (gameObject, active) in _hiddenProps)
		{
			if ((bool)gameObject)
			{
				gameObject.SetActive(active);
			}
		}
		_hiddenProps.Clear();
		try
		{
			TerminalAcoustics.SetAmbientSilenced(silent: false);
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
		if (_director != null && _director.state == PlayState.Playing)
		{
			_director.Stop();
		}
	}

	private void OnDestroy()
	{
		if (!_released)
		{
			Release("destroyed");
			return;
		}
		try
		{
			Restore();
		}
		catch
		{
		}
	}
}
