using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.SessionEnd;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Manimal.Terminal;

internal static class TerminalEpilogueScreen
{
	[HarmonyPatch(typeof(LocalGame), "Stop")]
	internal static class Patch_TestArmOnAnyExit
	{
		[HarmonyPrefix]
		private static void Prefix(ExitStatus exitStatus)
		{
			try
			{
				if (Plugin.EpilogueTestMode.Value && exitStatus == ExitStatus.Survived)
				{
					ArmAfterExtract();
					Plugin.Log.LogInfo("[Epilogue] TEST MODE — armed on Survived extract (any map)");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] test-mode arm failed: " + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(SessionResultExitStatus), "Show", new Type[]
	{
		typeof(Profile),
		typeof(PlayerVisualRepresentation),
		typeof(ESideType),
		typeof(ExitStatus),
		typeof(TimeSpan),
		typeof(IEftSession),
		typeof(bool)
	})]
	internal static class Patch_HijackExitStatus
	{
		[HarmonyPostfix]
		private static void Postfix(SessionResultExitStatus __instance, Profile activeProfile, PlayerVisualRepresentation lastPlayerState, ESideType side, ExitStatus exitStatus, TimeSpan raidTime, IEftSession session, bool isOnline)
		{
			try
			{
				if (Pending && exitStatus == ExitStatus.Survived)
				{
					Pending = false;
					GameObject gameObject = new GameObject("Terminal_Epilogue_Runner");
					gameObject.transform.SetParent(((Component)(object)__instance).transform, worldPositionStays: false);
					RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
					rectTransform.anchorMin = Vector2.zero;
					rectTransform.anchorMax = Vector2.one;
					rectTransform.offsetMin = Vector2.zero;
					rectTransform.offsetMax = Vector2.zero;
					EpilogueRunner epilogueRunner = gameObject.AddComponent<EpilogueRunner>();
					epilogueRunner.Init(__instance, activeProfile, raidTime, session);
					epilogueRunner.HideVanillaImmediately();
					Plugin.Log.LogInfo($"[Epilogue] hijacked ExitStatus screen — {activeProfile.EftStats.Victims?.Count ?? 0} victim(s) available");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] hijack failed, falling back to vanilla results: " + ex.Message);
			}
		}
	}

	internal class EpilogueRunner : MonoBehaviour
	{
		private SessionResultExitStatus _host;

		private Profile _profile;

		private TimeSpan _raidTime;

		private IEftSession _session;

		private JObject _cfg;

		private JArray _slides;

		private JObject _leftPanel;

		private Sprite _bg;

		private Sprite _medal;

		private AudioClip _music;

		private AudioSource _audio;

		private RectTransform _rightRoot;

		private int _slideIx;

		private readonly List<GameObject> _hiddenChildren = new List<GameObject>();

		private readonly HashSet<GameObject> _preserved = new HashSet<GameObject>();

		private GameObject _earlyBlackout;

		private object _nextBtnClone;

		private object _menuBtnClone;

		private GameObject _dim;

		private RectTransform _layoutRoot;

		private RectTransform _buttonRoot;

		private Coroutine _typewriterCo;

		private Text _typewriterTarget;

		private string _typewriterFullText;

		private bool _typewriterDone;

		private static Sprite _usecSprite;

		private static Sprite _bearSprite;

		private static object _prestigeTemplate;

		private static bool _prestigeTemplateChecked;

		private RectTransform _borrowedContainer;

		private Transform _borrowedContainerOrigParent;

		private (Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax) _borrowedContainerOrigAnchors;

		private (Vector2 sizeDelta, Vector2 anchoredPos, Vector2 pivot) _borrowedContainerOrigSize;

		private GameObject _borrowedKillListGo;

		internal void Init(SessionResultExitStatus host, Profile profile, TimeSpan raidTime, IEftSession session)
		{
			_host = host;
			_profile = profile;
			_raidTime = raidTime;
			_session = session;
			StartCoroutine(Run());
		}

		internal void HideVanillaImmediately()
		{
			HideDefaultChildren();
			GameObject gameObject = new GameObject("EarlyBlackout");
			gameObject.transform.SetParent(transform, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 1f);
			image.raycastTarget = true;
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			_earlyBlackout = gameObject;
		}

		private IEnumerator Run()
		{
			if (!LoadConfig())
			{
				UnityEngine.Object.Destroy(gameObject);
				yield break;
			}
			yield return LoadAssets();
			EnsureFonts();
			CaptureAndRerouteButtons();
			Build();
			if (_earlyBlackout != null)
			{
				UnityEngine.Object.Destroy(_earlyBlackout);
				_earlyBlackout = null;
			}
			ShowSlide(0);
			if (_music != null && _audio != null)
			{
				_audio.clip = _music;
				_audio.volume = 0.85f;
				_audio.loop = false;
				_audio.Play();
			}
		}

		private bool LoadConfig()
		{
			try
			{
				string text = Path.Combine(AssetsDir(), "terminal_epilogue.json");
				if (!File.Exists(text))
				{
					Plugin.Log.LogWarning("[Epilogue] no config at " + text);
					return false;
				}
				_cfg = JObject.Parse(File.ReadAllText(text));
				_slides = _cfg["slides"] as JArray;
				_leftPanel = _cfg["left_panel"] as JObject;
				return _slides != null && _slides.Count > 0 && _leftPanel != null;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] config load failed: " + ex.Message);
				return false;
			}
		}

		private static string AssetsDir()
		{
			return Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "epilogue");
		}

		private IEnumerator LoadAssets()
		{
			string path = AssetsDir();
			_bg = LoadSprite(Path.Combine(path, _cfg.Value<string>("background") ?? "background.png"));
			_medal = LoadSprite(Path.Combine(path, _cfg.Value<string>("medal") ?? "medal.png"));
			string text = Path.Combine(path, _cfg.Value<string>("music") ?? "music.ogg");
			if (!File.Exists(text))
			{
				yield break;
			}
			string uri = "file:///" + text.Replace('\\', '/');
			using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS);
			yield return req.SendWebRequest();
			if (req.result == UnityWebRequest.Result.Success)
			{
				_music = DownloadHandlerAudioClip.GetContent(req);
			}
			else
			{
				Plugin.Log.LogWarning("[Epilogue] music load failed: " + req.error);
			}
		}

		private static Sprite LoadSprite(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					Plugin.Log.LogWarning("[Epilogue] image missing: " + path);
					return null;
				}
				byte[] data = File.ReadAllBytes(path);
				Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
				if (!texture2D.LoadImage(data))
				{
					return null;
				}
				texture2D.wrapMode = TextureWrapMode.Clamp;
				return Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f), 100f);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] sprite load (" + path + "): " + ex.Message);
				return null;
			}
		}

		private void CaptureAndRerouteButtons()
		{
		}

		private void BuildFooterButtons()
		{
			try
			{
				Component src = AccessTools.Field(typeof(SessionResultExitStatus), "_nextButton")?.GetValue(_host) as Component;
				Component src2 = AccessTools.Field(typeof(SessionResultExitStatus), "_mainMenuButton")?.GetValue(_host) as Component;
				_nextBtnClone = CloneShellButton(src, "NEXT", new Vector2(0.42f, 0.055f), new Vector2(0.58f, 0.095f), Advance);
				_menuBtnClone = CloneShellButton(src2, "BACK", new Vector2(0.42f, 0.01f), new Vector2(0.58f, 0.05f), Regress);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] footer buttons failed: " + ex.Message);
			}
		}

		private object CloneShellButton(Component src, string label, Vector2 anchorMin, Vector2 anchorMax, UnityAction click)
		{
			if (src == null)
			{
				Plugin.Log.LogWarning("[Epilogue] shell button source null for '" + label + "'");
				return null;
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(src.gameObject, _buttonRoot, worldPositionStays: false);
			gameObject.name = "Epilogue_" + label;
			gameObject.SetActive(value: true);
			RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
			if (rectTransform == null)
			{
				rectTransform = gameObject.AddComponent<RectTransform>();
			}
			rectTransform.anchorMin = anchorMin;
			rectTransform.anchorMax = anchorMax;
			rectTransform.pivot = new Vector2(0.5f, 0.5f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			rectTransform.localScale = Vector3.one;
			Component component = gameObject.GetComponent(src.GetType());
			if (component != null)
			{
				try
				{
					AccessTools.Method(component.GetType(), "SetHeaderText", new Type[1] { typeof(string) })?.Invoke(component, new object[1] { label });
				}
				catch
				{
				}
				try
				{
					object obj2 = AccessTools.Property(component.GetType(), "OnClick")?.GetValue(component) ?? AccessTools.Field(component.GetType(), "OnClick")?.GetValue(component);
					(obj2?.GetType().GetMethod("RemoveAllListeners"))?.Invoke(obj2, null);
					(obj2?.GetType().GetMethod("AddListener", new Type[1] { typeof(UnityAction) }))?.Invoke(obj2, new object[1] { click });
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[Epilogue] onClick rewire ('" + label + "') failed: " + ex.Message);
				}
			}
			return component;
		}

		private void HideDefaultChildren()
		{
			try
			{
				foreach (Transform item in ((Component)(object)_host).transform)
				{
					if (!(item == null) && !(item.gameObject == gameObject) && item.gameObject.activeSelf)
					{
						item.gameObject.SetActive(value: false);
						_hiddenChildren.Add(item.gameObject);
					}
				}
				Plugin.Log.LogInfo($"[Epilogue] hid {_hiddenChildren.Count} default child(ren)");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] hide failed: " + ex.Message);
			}
		}

		private void Build()
		{
			MakeStretch(transform, "Backdrop", new Color(0f, 0f, 0f, 1f));
			if (_bg != null)
			{
				GameObject gameObject = new GameObject("Background");
				gameObject.transform.SetParent(transform, worldPositionStays: false);
				Image image = gameObject.AddComponent<Image>();
				image.sprite = _bg;
				image.preserveAspect = true;
				image.raycastTarget = false;
				RectTransform rectTransform = image.rectTransform;
				rectTransform.anchorMin = Vector2.zero;
				rectTransform.anchorMax = Vector2.one;
				rectTransform.offsetMin = Vector2.zero;
				rectTransform.offsetMax = Vector2.zero;
			}
			_dim = MakeStretch(transform, "Dim", new Color(0f, 0f, 0f, 0.4f));
			_dim.GetComponent<Image>().raycastTarget = false;
			_audio = base.gameObject.AddComponent<AudioSource>();
			_audio.playOnAwake = false;
			_audio.spatialBlend = 0f;
			if (!TerminalAudioRouting.RouteMaster(_audio))
			{
				_audio.mute = true;
				Plugin.Log.LogWarning("[Epilogue] volume-controlled mixer unavailable; audio muted");
			}
			GameObject gameObject2 = new GameObject("Layout");
			gameObject2.transform.SetParent(transform, worldPositionStays: false);
			_layoutRoot = gameObject2.AddComponent<RectTransform>();
			_layoutRoot.anchorMin = Vector2.zero;
			_layoutRoot.anchorMax = Vector2.one;
			_layoutRoot.offsetMin = Vector2.zero;
			_layoutRoot.offsetMax = Vector2.zero;
			GameObject gameObject3 = new GameObject("ButtonRoot");
			gameObject3.transform.SetParent(transform, worldPositionStays: false);
			_buttonRoot = gameObject3.AddComponent<RectTransform>();
			_buttonRoot.anchorMin = Vector2.zero;
			_buttonRoot.anchorMax = Vector2.one;
			_buttonRoot.offsetMin = Vector2.zero;
			_buttonRoot.offsetMax = Vector2.zero;
			BuildFooterButtons();
		}

		private static GameObject MakeStretch(Transform parent, string name, Color color)
		{
			GameObject gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = color;
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			return gameObject;
		}

		private void BuildHeader(JObject slide)
		{
			string content = slide.Value<string>("title") ?? "COMPLETION RESULTS";
			string content2 = slide.Value<string>("subtitle") ?? "REACHED ENDING";
			Text text = MakeText(_layoutRoot, "HeaderTitle", content, 40, TextAnchor.UpperCenter);
			text.fontStyle = FontStyle.Bold;
			text.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			RectTransform rectTransform = text.rectTransform;
			rectTransform.anchorMin = new Vector2(0f, 0.9f);
			rectTransform.anchorMax = new Vector2(1f, 0.98f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			text.raycastTarget = false;
			Text text2 = MakeText(_layoutRoot, "HeaderSub", content2, 18, TextAnchor.UpperCenter);
			text2.color = new Color(0.8f, 0.8f, 0.75f, 1f);
			RectTransform rectTransform2 = text2.rectTransform;
			rectTransform2.anchorMin = new Vector2(0f, 0.86f);
			rectTransform2.anchorMax = new Vector2(1f, 0.9f);
			rectTransform2.offsetMin = Vector2.zero;
			rectTransform2.offsetMax = Vector2.zero;
			text2.raycastTarget = false;
		}

		private void BuildLeftPanel()
		{
			GameObject gameObject = new GameObject("LeftPanel");
			gameObject.transform.SetParent(_layoutRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.72f);
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = new Vector2(0.02f, 0.15f);
			rectTransform.anchorMax = new Vector2(0.3f, 0.87f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			if (_medal != null)
			{
				GameObject gameObject2 = new GameObject("Medal");
				gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
				Image image2 = gameObject2.AddComponent<Image>();
				image2.sprite = _medal;
				image2.preserveAspect = true;
				image2.raycastTarget = false;
				RectTransform rectTransform2 = image2.rectTransform;
				rectTransform2.anchorMin = new Vector2(0.15f, 0.6f);
				rectTransform2.anchorMax = new Vector2(0.85f, 0.95f);
				rectTransform2.offsetMin = Vector2.zero;
				rectTransform2.offsetMax = Vector2.zero;
			}
			Text text = MakeText(gameObject.transform, "Verdict1", _leftPanel.Value<string>("verdict_line1") ?? "", 22, TextAnchor.UpperCenter);
			text.fontStyle = FontStyle.Bold;
			text.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			text.raycastTarget = false;
			RectTransform rectTransform3 = text.rectTransform;
			rectTransform3.anchorMin = new Vector2(0.05f, 0.53f);
			rectTransform3.anchorMax = new Vector2(0.95f, 0.6f);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			Text text2 = MakeText(gameObject.transform, "Verdict2", _leftPanel.Value<string>("verdict_line2") ?? "", 22, TextAnchor.UpperCenter);
			text2.fontStyle = FontStyle.Bold;
			text2.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			text2.raycastTarget = false;
			RectTransform rectTransform4 = text2.rectTransform;
			rectTransform4.anchorMin = new Vector2(0.05f, 0.47f);
			rectTransform4.anchorMax = new Vector2(0.95f, 0.53f);
			rectTransform4.offsetMin = Vector2.zero;
			rectTransform4.offsetMax = Vector2.zero;
			GameObject gameObject3 = new GameObject("Divider");
			gameObject3.transform.SetParent(gameObject.transform, worldPositionStays: false);
			Image image3 = gameObject3.AddComponent<Image>();
			image3.color = new Color(0.5f, 0.5f, 0.45f, 0.6f);
			image3.raycastTarget = false;
			RectTransform rectTransform5 = image3.rectTransform;
			rectTransform5.anchorMin = new Vector2(0.15f, 0.45f);
			rectTransform5.anchorMax = new Vector2(0.85f, 0.455f);
			rectTransform5.offsetMin = Vector2.zero;
			rectTransform5.offsetMax = Vector2.zero;
			Text text3 = MakeText(gameObject.transform, "Body", _leftPanel.Value<string>("body") ?? "", 15, TextAnchor.UpperCenter);
			text3.color = new Color(0.85f, 0.85f, 0.8f, 1f);
			text3.raycastTarget = false;
			RectTransform rectTransform6 = text3.rectTransform;
			rectTransform6.anchorMin = new Vector2(0.06f, 0.1f);
			rectTransform6.anchorMax = new Vector2(0.94f, 0.44f);
			rectTransform6.offsetMin = Vector2.zero;
			rectTransform6.offsetMax = Vector2.zero;
			Text text4 = MakeText(gameObject.transform, "Foot", _leftPanel.Value<string>("footer") ?? "", 14, TextAnchor.LowerCenter);
			text4.color = new Color(0.75f, 0.75f, 0.7f, 1f);
			text4.raycastTarget = false;
			RectTransform rectTransform7 = text4.rectTransform;
			rectTransform7.anchorMin = new Vector2(0.05f, 0.02f);
			rectTransform7.anchorMax = new Vector2(0.95f, 0.08f);
			rectTransform7.offsetMin = Vector2.zero;
			rectTransform7.offsetMax = Vector2.zero;
		}

		private void ShowSlide(int ix)
		{
			_slideIx = Mathf.Clamp(ix, 0, _slides.Count - 1);
			if (_typewriterCo != null)
			{
				StopCoroutine(_typewriterCo);
				_typewriterCo = null;
			}
			_typewriterDone = true;
			_typewriterTarget = null;
			RestoreBorrowedContainer();
			foreach (Transform item in _layoutRoot)
			{
				UnityEngine.Object.Destroy(item.gameObject);
			}
			_rightRoot = null;
			AttachFadeIn(_layoutRoot.gameObject, 0f, 0.35f);
			if (!(_slides[_slideIx] is JObject jObject))
			{
				CloseToMenu();
				return;
			}
			string text = jObject.Value<string>("kind") ?? "narrative_full";
			switch (text)
			{
			case "narrative_full":
				SetDim(0.35f);
				BuildNarrativeFull(jObject);
				break;
			case "medal_center":
				SetDim(0.55f);
				BuildHeader(jObject);
				BuildMedalCenter();
				break;
			case "thanks":
				SetDim(0.65f);
				BuildHeader(jObject);
				BuildThanks(jObject);
				break;
			default:
				SetDim(0.55f);
				BuildHeader(jObject);
				BuildLeftPanel();
				BuildRightRoot();
				switch (text)
				{
				case "stats":
					BuildStatsSlide();
					break;
				case "achievements":
					BuildAchievementsSlide();
					break;
				case "rewards":
					BuildRewardsSlide();
					break;
				case "killfeed":
					BuildKillfeedSlide();
					break;
				}
				break;
			}
			UpdateButtonVisibility(text);
		}

		private void UpdateButtonVisibility(string kind)
		{
			bool typewriterDone = _typewriterDone;
			bool active = _slideIx > 0 && kind != "thanks";
			if (_nextBtnClone is Component component)
			{
				component.gameObject.SetActive(typewriterDone);
			}
			if (_menuBtnClone is Component component2)
			{
				component2.gameObject.SetActive(active);
			}
			try
			{
				if (_nextBtnClone is Component component3)
				{
					AccessTools.Method(component3.GetType(), "SetHeaderText", new Type[1] { typeof(string) })?.Invoke(component3, new object[1] { (kind == "thanks") ? "OK" : "NEXT" });
				}
			}
			catch
			{
			}
		}

		private void SetDim(float a)
		{
			if (!(_dim == null))
			{
				Image component = _dim.GetComponent<Image>();
				if (component != null)
				{
					component.color = new Color(0f, 0f, 0f, a);
				}
			}
		}

		private void BuildRightRoot()
		{
			GameObject gameObject = new GameObject("RightRoot");
			gameObject.transform.SetParent(_layoutRoot, worldPositionStays: false);
			_rightRoot = gameObject.AddComponent<RectTransform>();
			_rightRoot.anchorMin = new Vector2(0.32f, 0.15f);
			_rightRoot.anchorMax = new Vector2(0.98f, 0.87f);
			_rightRoot.offsetMin = Vector2.zero;
			_rightRoot.offsetMax = Vector2.zero;
		}

		private void BuildNarrativeFull(JObject s)
		{
			string text = s.Value<string>("text") ?? "";
			GameObject gameObject = new GameObject("NarrativeDivider");
			gameObject.transform.SetParent(_layoutRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0.85f, 0.85f, 0.8f, 0.6f);
			image.raycastTarget = false;
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = new Vector2(0.32f, 0.235f);
			rectTransform.anchorMax = new Vector2(0.68f, 0.238f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			AttachFadeIn(gameObject, 0f, 0.35f);
			Text text2 = MakeText(_layoutRoot, "NarrativeBody", "", 20, TextAnchor.UpperCenter);
			text2.color = new Color(0.92f, 0.92f, 0.9f, 1f);
			text2.horizontalOverflow = HorizontalWrapMode.Wrap;
			RectTransform rectTransform2 = text2.rectTransform;
			rectTransform2.anchorMin = new Vector2(0.3f, 0.05f);
			rectTransform2.anchorMax = new Vector2(0.7f, 0.23f);
			rectTransform2.offsetMin = Vector2.zero;
			rectTransform2.offsetMax = Vector2.zero;
			_typewriterTarget = text2;
			_typewriterFullText = text;
			_typewriterDone = false;
			_typewriterCo = StartCoroutine(TypewriterCoroutine(text, text2));
		}

		private void BuildMedalCenter()
		{
			if (_medal != null)
			{
				GameObject gameObject = new GameObject("Medal");
				gameObject.transform.SetParent(_layoutRoot, worldPositionStays: false);
				Image image = gameObject.AddComponent<Image>();
				image.sprite = _medal;
				image.preserveAspect = true;
				image.raycastTarget = false;
				RectTransform rectTransform = image.rectTransform;
				rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
				rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
				rectTransform.pivot = new Vector2(0.5f, 0.5f);
				rectTransform.sizeDelta = new Vector2(360f, 360f);
				rectTransform.anchoredPosition = new Vector2(0f, 90f);
				AttachFadeIn(gameObject, 0.05f, 0.5f);
			}
			Text text = MakeText(_layoutRoot, "Verdict1", _leftPanel.Value<string>("verdict_line1") ?? "", 30, TextAnchor.UpperCenter);
			text.fontStyle = FontStyle.Bold;
			text.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			text.raycastTarget = false;
			RectTransform rectTransform2 = text.rectTransform;
			rectTransform2.anchorMin = new Vector2(0.2f, 0.32f);
			rectTransform2.anchorMax = new Vector2(0.8f, 0.38f);
			rectTransform2.offsetMin = Vector2.zero;
			rectTransform2.offsetMax = Vector2.zero;
			AttachFadeIn(text.gameObject, 0.3f, 0.35f);
			Text text2 = MakeText(_layoutRoot, "Verdict2", _leftPanel.Value<string>("verdict_line2") ?? "", 30, TextAnchor.UpperCenter);
			text2.fontStyle = FontStyle.Bold;
			text2.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			text2.raycastTarget = false;
			RectTransform rectTransform3 = text2.rectTransform;
			rectTransform3.anchorMin = new Vector2(0.2f, 0.26f);
			rectTransform3.anchorMax = new Vector2(0.8f, 0.32f);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			AttachFadeIn(text2.gameObject, 0.35f, 0.35f);
			GameObject gameObject2 = new GameObject("BodyBox");
			gameObject2.transform.SetParent(_layoutRoot, worldPositionStays: false);
			Image image2 = gameObject2.AddComponent<Image>();
			image2.color = new Color(0.12f, 0.12f, 0.12f, 0.65f);
			image2.raycastTarget = false;
			RectTransform rectTransform4 = image2.rectTransform;
			rectTransform4.anchorMin = new Vector2(0.28f, 0.14f);
			rectTransform4.anchorMax = new Vector2(0.72f, 0.24f);
			rectTransform4.offsetMin = Vector2.zero;
			rectTransform4.offsetMax = Vector2.zero;
			AttachFadeIn(gameObject2, 0.55f, 0.35f);
			Text text3 = MakeText(gameObject2.transform, "Body", _leftPanel.Value<string>("body") ?? "", 15, TextAnchor.MiddleCenter);
			text3.color = new Color(0.88f, 0.88f, 0.85f, 1f);
			text3.raycastTarget = false;
			RectTransform rectTransform5 = text3.rectTransform;
			rectTransform5.anchorMin = new Vector2(0.03f, 0.05f);
			rectTransform5.anchorMax = new Vector2(0.97f, 0.95f);
			rectTransform5.offsetMin = Vector2.zero;
			rectTransform5.offsetMax = Vector2.zero;
			Text text4 = MakeText(_layoutRoot, "Foot", _leftPanel.Value<string>("footer") ?? "", 15, TextAnchor.UpperCenter);
			text4.color = new Color(0.75f, 0.75f, 0.7f, 1f);
			text4.raycastTarget = false;
			RectTransform rectTransform6 = text4.rectTransform;
			rectTransform6.anchorMin = new Vector2(0.28f, 0.1f);
			rectTransform6.anchorMax = new Vector2(0.72f, 0.13f);
			rectTransform6.offsetMin = Vector2.zero;
			rectTransform6.offsetMax = Vector2.zero;
			AttachFadeIn(text4.gameObject, 0.8f, 0.35f);
		}

		private void BuildThanks(JObject s)
		{
			GameObject gameObject = new GameObject("ThanksBox");
			gameObject.transform.SetParent(_layoutRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
			image.raycastTarget = false;
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = new Vector2(0.25f, 0.2f);
			rectTransform.anchorMax = new Vector2(0.75f, 0.55f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			GameObject gameObject2 = new GameObject("Stroke");
			gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
			Image image2 = gameObject2.AddComponent<Image>();
			image2.color = new Color(0.35f, 0.33f, 0.3f, 0.55f);
			image2.raycastTarget = false;
			image2.type = Image.Type.Sliced;
			RectTransform rectTransform2 = image2.rectTransform;
			rectTransform2.anchorMin = Vector2.zero;
			rectTransform2.anchorMax = Vector2.one;
			rectTransform2.offsetMin = Vector2.zero;
			rectTransform2.offsetMax = Vector2.zero;
			image2.color = new Color(1f, 1f, 1f, 0f);
			Text text = MakeText(gameObject.transform, "Body", "", 20, TextAnchor.MiddleCenter);
			text.color = new Color(0.92f, 0.92f, 0.88f, 1f);
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			RectTransform rectTransform3 = text.rectTransform;
			rectTransform3.anchorMin = new Vector2(0.05f, 0.06f);
			rectTransform3.anchorMax = new Vector2(0.95f, 0.94f);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			string text2 = s.Value<string>("body") ?? "";
			_typewriterTarget = text;
			_typewriterFullText = text2;
			_typewriterDone = false;
			_typewriterCo = StartCoroutine(TypewriterCoroutine(text2, text));
		}

		private IEnumerator TypewriterCoroutine(string full, Text target)
		{
			float num = _cfg.Value<float?>("typewriter_chars_per_second") ?? 45f;
			if (num < 1f)
			{
				num = 45f;
			}
			float delay = 1f / num;
			for (int i = 1; i <= full.Length; i++)
			{
				if (target == null)
				{
					yield break;
				}
				target.text = full.Substring(0, i);
				yield return new WaitForSecondsRealtime(delay);
			}
			_typewriterDone = true;
			UpdateButtonVisibility(_slides[_slideIx].Value<string>("kind") ?? "");
		}

		private static void EnsureFactionSprites()
		{
			if (_usecSprite != null && _bearSprite != null)
			{
				return;
			}
			try
			{
				Type type = AccessTools.TypeByName("InventoryPlayerModelWithStatsWindow");
				if (type == null)
				{
					return;
				}
				UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(type);
				foreach (UnityEngine.Object obj in array)
				{
					if (!(obj == null))
					{
						if ((object)_usecSprite == null)
						{
							_usecSprite = AccessTools.Field(type, "_usecSprite")?.GetValue(obj) as Sprite;
						}
						if ((object)_bearSprite == null)
						{
							_bearSprite = AccessTools.Field(type, "_bearSprite")?.GetValue(obj) as Sprite;
						}
						if (_usecSprite != null && _bearSprite != null)
						{
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] faction sprite lookup failed: " + ex.Message);
			}
		}

		private void BuildProfileHeader(Transform parent, string side, string nickname)
		{
			EnsureFactionSprites();
			GameObject gameObject = new GameObject("ProfileHeader");
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0.12f, 0.12f, 0.12f, 0.6f);
			image.raycastTarget = false;
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = new Vector2(0.02f, 0.87f);
			rectTransform.anchorMax = new Vector2(0.98f, 0.99f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			AttachFadeIn(gameObject, 0.05f, 0.3f);
			string text = (side ?? "").ToLowerInvariant();
			Sprite sprite = null;
			if (text.Contains("usec"))
			{
				sprite = _usecSprite;
			}
			else if (text.Contains("bear"))
			{
				sprite = _bearSprite;
			}
			if (sprite != null)
			{
				GameObject gameObject2 = new GameObject("FactionIcon");
				gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
				Image image2 = gameObject2.AddComponent<Image>();
				image2.sprite = sprite;
				image2.preserveAspect = true;
				image2.raycastTarget = false;
				RectTransform rectTransform2 = image2.rectTransform;
				rectTransform2.anchorMin = new Vector2(0.01f, 0.1f);
				rectTransform2.anchorMax = new Vector2(0.09f, 0.9f);
				rectTransform2.offsetMin = Vector2.zero;
				rectTransform2.offsetMax = Vector2.zero;
				AttachFadeIn(gameObject2, 0.1f, 0.35f);
			}
			Text text2 = MakeText(gameObject.transform, "Nickname", nickname ?? "—", 32, TextAnchor.MiddleLeft);
			text2.fontStyle = FontStyle.Bold;
			text2.color = new Color(0.55f, 0.85f, 1f, 1f);
			text2.raycastTarget = false;
			RectTransform rectTransform3 = text2.rectTransform;
			rectTransform3.anchorMin = new Vector2(0.11f, 0.1f);
			rectTransform3.anchorMax = new Vector2(0.99f, 0.9f);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			AttachFadeIn(text2.gameObject, 0.15f, 0.35f);
		}

		private void AttachFadeIn(GameObject go, float delay, float dur)
		{
			if (!(go == null))
			{
				CanvasGroup canvasGroup = go.GetComponent<CanvasGroup>();
				if (canvasGroup == null)
				{
					canvasGroup = go.AddComponent<CanvasGroup>();
				}
				canvasGroup.alpha = 0f;
				StartCoroutine(FadeInCoroutine(canvasGroup, delay, dur));
			}
		}

		private static IEnumerator FadeInCoroutine(CanvasGroup cg, float delay, float dur)
		{
			if (delay > 0f)
			{
				yield return new WaitForSecondsRealtime(delay);
			}
			float t = 0f;
			while (t < dur)
			{
				if (cg == null)
				{
					yield break;
				}
				t += Time.unscaledDeltaTime;
				cg.alpha = Mathf.Clamp01(t / dur);
				yield return null;
			}
			if (cg != null)
			{
				cg.alpha = 1f;
			}
		}

		private RectTransform MakeScrollableContent(GameObject parent, float viewportTopFrac, float viewportBottomFrac)
		{
			GameObject gameObject = new GameObject("Viewport");
			gameObject.transform.SetParent(parent.transform, worldPositionStays: false);
			RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
			rectTransform.anchorMin = new Vector2(0.02f, viewportBottomFrac);
			rectTransform.anchorMax = new Vector2(0.96500003f, viewportTopFrac);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.001f);
			image.raycastTarget = true;
			gameObject.AddComponent<RectMask2D>();
			GameObject gameObject2 = new GameObject("Content");
			gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
			RectTransform rectTransform2 = gameObject2.AddComponent<RectTransform>();
			rectTransform2.anchorMin = new Vector2(0f, 1f);
			rectTransform2.anchorMax = new Vector2(1f, 1f);
			rectTransform2.pivot = new Vector2(0.5f, 1f);
			rectTransform2.anchoredPosition = Vector2.zero;
			rectTransform2.sizeDelta = new Vector2(0f, 0f);
			GameObject gameObject3 = new GameObject("Scrollbar");
			gameObject3.transform.SetParent(parent.transform, worldPositionStays: false);
			RectTransform rectTransform3 = gameObject3.AddComponent<RectTransform>();
			rectTransform3.anchorMin = new Vector2(0.96500003f, viewportBottomFrac);
			rectTransform3.anchorMax = new Vector2(0.98f, viewportTopFrac);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			gameObject3.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 0.8f);
			Scrollbar scrollbar = gameObject3.AddComponent<Scrollbar>();
			scrollbar.direction = Scrollbar.Direction.BottomToTop;
			GameObject gameObject4 = new GameObject("SlidingArea");
			gameObject4.transform.SetParent(gameObject3.transform, worldPositionStays: false);
			RectTransform rectTransform4 = gameObject4.AddComponent<RectTransform>();
			rectTransform4.anchorMin = Vector2.zero;
			rectTransform4.anchorMax = Vector2.one;
			rectTransform4.offsetMin = new Vector2(2f, 2f);
			rectTransform4.offsetMax = new Vector2(-2f, -2f);
			GameObject gameObject5 = new GameObject("Handle");
			gameObject5.transform.SetParent(gameObject4.transform, worldPositionStays: false);
			RectTransform rectTransform5 = gameObject5.AddComponent<RectTransform>();
			rectTransform5.anchorMin = Vector2.zero;
			rectTransform5.anchorMax = Vector2.one;
			rectTransform5.offsetMin = Vector2.zero;
			rectTransform5.offsetMax = Vector2.zero;
			Image image2 = gameObject5.AddComponent<Image>();
			image2.color = new Color(0.45f, 0.45f, 0.42f, 0.9f);
			scrollbar.targetGraphic = image2;
			scrollbar.handleRect = rectTransform5;
			ScrollRect scrollRect = parent.AddComponent<ScrollRect>();
			scrollRect.viewport = rectTransform;
			scrollRect.content = rectTransform2;
			scrollRect.verticalScrollbar = scrollbar;
			scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
			scrollRect.verticalScrollbarSpacing = 4f;
			scrollRect.horizontal = false;
			scrollRect.vertical = true;
			scrollRect.movementType = ScrollRect.MovementType.Clamped;
			scrollRect.scrollSensitivity = 30f;
			StartCoroutine(ForceScrollTop(scrollRect));
			return rectTransform2;
		}

		private static IEnumerator ForceScrollTop(ScrollRect s)
		{
			yield return null;
			yield return null;
			if (s != null)
			{
				s.verticalNormalizedPosition = 1f;
			}
		}

		private IEnumerator StaggerFadeIn(List<CanvasGroup> groups, float perItem, float fade)
		{
			foreach (CanvasGroup group in groups)
			{
				if (group != null)
				{
					group.alpha = 0f;
				}
			}
			foreach (CanvasGroup g in groups)
			{
				if (g == null)
				{
					continue;
				}
				float t = 0f;
				while (t < fade)
				{
					if (!(g == null))
					{
						t += Time.unscaledDeltaTime;
						g.alpha = Mathf.Clamp01(t / fade);
						yield return null;
						continue;
					}
					yield break;
				}
				if (g != null)
				{
					g.alpha = 1f;
				}
				yield return new WaitForSecondsRealtime(perItem);
			}
		}

		private void BuildStatsSlide()
		{
			ProfileData.Stats stats = ProfileData.ReadStats(_profile);
			(string, string)[] array = new (string, string)[14]
			{
				("Character Level", stats.Level.ToString()),
				("Favorite Weapon", stats.FavoriteWeapon),
				("Prestige Level", stats.PrestigeLevel.ToString()),
				("K/D", stats.Kd),
				("Total Played Time", stats.PlayedTime),
				("Game Style", stats.GameStyle),
				("Game Edition", stats.GameEdition),
				("PMC Eliminated", stats.PmcKills.ToString()),
				("Game Mode", stats.GameMode),
				("Scavs Eliminated", stats.ScavKills.ToString()),
				("Total Raid Count", stats.TotalRaids.ToString()),
				("Bosses Neutralized", stats.BossKills.ToString()),
				("Longest Win Streak", stats.WinStreak.ToString()),
				("Survival Rate", stats.SurvivalRate)
			};
			GameObject gameObject = new GameObject("StatsPanel");
			gameObject.transform.SetParent(_rightRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.72f);
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			BuildProfileHeader(gameObject.transform, stats.Side, stats.Nickname);
			List<CanvasGroup> list = new List<CanvasGroup>();
			List<(Text, string)> list2 = new List<(Text, string)>();
			for (int i = 0; i < array.Length; i++)
			{
				int num = i / 7;
				int num2 = i % 7;
				float num3 = 0.1f;
				float num4 = 0.85f - (float)num2 * (num3 + 0.005f);
				float y = num4 - num3;
				float x = ((num == 0) ? 0.04f : 0.52f);
				float x2 = ((num == 0) ? 0.48f : 0.96f);
				GameObject gameObject2 = new GameObject($"Row{i}");
				gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
				Image image2 = gameObject2.AddComponent<Image>();
				image2.color = new Color(0.12f, 0.12f, 0.12f, 0.6f);
				image2.raycastTarget = false;
				RectTransform rectTransform2 = image2.rectTransform;
				rectTransform2.anchorMin = new Vector2(x, y);
				rectTransform2.anchorMax = new Vector2(x2, num4);
				rectTransform2.offsetMin = Vector2.zero;
				rectTransform2.offsetMax = Vector2.zero;
				list.Add(gameObject2.AddComponent<CanvasGroup>());
				Text text = MakeText(gameObject2.transform, "Lbl", array[i].Item1, 15, TextAnchor.MiddleLeft);
				text.color = new Color(0.85f, 0.85f, 0.8f, 1f);
				text.raycastTarget = false;
				RectTransform rectTransform3 = text.rectTransform;
				rectTransform3.anchorMin = new Vector2(0.05f, 0f);
				rectTransform3.anchorMax = new Vector2(0.65f, 1f);
				rectTransform3.offsetMin = Vector2.zero;
				rectTransform3.offsetMax = Vector2.zero;
				Text text2 = MakeText(gameObject2.transform, "Val", "", 15, TextAnchor.MiddleRight);
				text2.color = new Color(0.95f, 0.95f, 0.9f, 1f);
				text2.raycastTarget = false;
				RectTransform rectTransform4 = text2.rectTransform;
				rectTransform4.anchorMin = new Vector2(0.35f, 0f);
				rectTransform4.anchorMax = new Vector2(0.95f, 1f);
				rectTransform4.offsetMin = Vector2.zero;
				rectTransform4.offsetMax = Vector2.zero;
				list2.Add((text2, array[i].Item2));
			}
			StartCoroutine(StaggerFadeIn(list, 0.06f, 0.18f));
			StartCoroutine(TypewriteStatValues(list2));
		}

		private IEnumerator TypewriteStatValues(List<(Text txt, string full)> targets)
		{
			yield return new WaitForSecondsRealtime(0.05f);
			foreach (var (txt, text) in targets)
			{
				if (txt == null)
				{
					continue;
				}
				string s = text ?? "";
				if (s.Length == 0)
				{
					continue;
				}
				for (int i = 1; i <= s.Length; i++)
				{
					if (!(txt == null))
					{
						txt.text = s.Substring(0, i);
						yield return new WaitForSecondsRealtime(0.005f);
						continue;
					}
					yield break;
				}
				yield return new WaitForSecondsRealtime(0.05f);
			}
		}

		private void BuildAchievementsSlide()
		{
			GameObject gameObject = new GameObject("AchvPanel");
			gameObject.transform.SetParent(_rightRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.72f);
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			ProfileData.Stats stats = ProfileData.ReadStats(_profile);
			BuildProfileHeader(gameObject.transform, stats.Side, stats.Nickname);
			List<object> list = ResolveUnlockedTemplates();
			int count = list.Count;
			Plugin.Log.LogInfo($"[Epilogue] achievements slide: {count} unlocked template(s) resolved");
			RectTransform rectTransform2 = MakeScrollableContent(gameObject, 0.82f, 0.02f);
			float y = (float)((count + 6 - 1) / 6) * 162f + 12f;
			rectTransform2.sizeDelta = new Vector2(0f, y);
			List<CanvasGroup> list2 = new List<CanvasGroup>();
			for (int i = 0; i < count; i++)
			{
				int num = i % 6;
				int num2 = i / 6;
				GameObject gameObject2 = new GameObject($"Achv{i}");
				gameObject2.transform.SetParent(rectTransform2, worldPositionStays: false);
				Image image2 = gameObject2.AddComponent<Image>();
				image2.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
				image2.raycastTarget = false;
				RectTransform rectTransform3 = image2.rectTransform;
				rectTransform3.anchorMin = new Vector2(0f, 1f);
				rectTransform3.anchorMax = new Vector2(0f, 1f);
				rectTransform3.pivot = new Vector2(0f, 1f);
				rectTransform3.sizeDelta = new Vector2(150f, 150f);
				rectTransform3.anchoredPosition = new Vector2(12f + (float)num * 162f, 0f - (12f + (float)num2 * 162f));
				list2.Add(gameObject2.AddComponent<CanvasGroup>());
				GameObject gameObject3 = new GameObject("Icon");
				gameObject3.transform.SetParent(gameObject2.transform, worldPositionStays: false);
				Image image3 = gameObject3.AddComponent<Image>();
				image3.raycastTarget = false;
				image3.preserveAspect = true;
				RectTransform rectTransform4 = image3.rectTransform;
				rectTransform4.anchorMin = new Vector2(0.1f, 0.1f);
				rectTransform4.anchorMax = new Vector2(0.9f, 0.9f);
				rectTransform4.offsetMin = Vector2.zero;
				rectTransform4.offsetMax = Vector2.zero;
				StartCoroutine(LoadAndAssignAchievementIcon(image3, list[i]));
			}
			StartCoroutine(StaggerFadeIn(list2, 0.03f, 0.15f));
		}

		private List<object> ResolveUnlockedTemplates()
		{
			List<object> list = new List<object>();
			try
			{
				if (!((AccessTools.Property(_profile.GetType(), "AchievementsData")?.GetValue(_profile) ?? AccessTools.Field(_profile.GetType(), "AchievementsData")?.GetValue(_profile)) is IDictionary dictionary))
				{
					Plugin.Log.LogWarning("[Epilogue] AchievementsData not a Dictionary — path likely wrong");
					return list;
				}
				Plugin.Log.LogInfo($"[Epilogue] AchievementsData has {dictionary.Count} unlocked id(s)");
				Type type = AccessTools.TypeByName("EFT.Quests.ConditionalTemplatesStorage");
				if (type == null)
				{
					Plugin.Log.LogWarning("[Epilogue] EFT.Quests.ConditionalTemplatesStorage type not found");
					return list;
				}
				object obj = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (obj == null)
				{
					Plugin.Log.LogWarning("[Epilogue] EFT.Quests.ConditionalTemplatesStorage.Instance null");
					return list;
				}
				if (!(type.GetMethod("GetAllAchievementTemplates", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj, null) is IEnumerable enumerable))
				{
					Plugin.Log.LogWarning("[Epilogue] GetAllAchievementTemplates returned null");
					return list;
				}
				Dictionary<string, object> dictionary2 = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				int num = 0;
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						string text = AccessTools.Property(item.GetType(), "Id")?.GetValue(item)?.ToString() ?? AccessTools.Field(item.GetType(), "Id")?.GetValue(item)?.ToString();
						if (!string.IsNullOrEmpty(text))
						{
							dictionary2[text] = item;
							num++;
						}
					}
				}
				Plugin.Log.LogInfo($"[Epilogue] {num} global achievement template(s) indexed");
				int num2 = 0;
				foreach (DictionaryEntry item2 in dictionary)
				{
					string text2 = item2.Key?.ToString();
					if (!string.IsNullOrEmpty(text2))
					{
						if (dictionary2.TryGetValue(text2, out var value))
						{
							list.Add(value);
							num2++;
						}
						else
						{
							Plugin.Log.LogDebug("[Epilogue] unlocked id '" + text2 + "' has no matching template");
						}
					}
				}
				Plugin.Log.LogInfo($"[Epilogue] matched {num2}/{dictionary.Count} unlocked achievements to templates");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] achievement templates resolve failed: " + ex.Message);
			}
			return list;
		}

		private IEnumerator LoadAndAssignAchievementIcon(Image target, object template)
		{
			if (target == null || template == null)
			{
				yield break;
			}
			PropertyInfo spriteProp = AccessTools.Property(template.GetType(), "Sprite");
			Sprite sprite = spriteProp?.GetValue(template) as Sprite;
			if (sprite != null)
			{
				target.sprite = sprite;
				yield break;
			}
			MethodInfo methodInfo = AccessTools.Method(template.GetType(), "LoadIconSprite");
			if (methodInfo == null || _session == null)
			{
				yield break;
			}
			Task task;
			try
			{
				task = methodInfo.Invoke(template, new object[1] { _session }) as Task;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] LoadIconSprite invoke failed: " + ex.Message);
				yield break;
			}
			if (task != null)
			{
				float deadline = Time.unscaledTime + 5f;
				while (!task.IsCompleted && Time.unscaledTime < deadline)
				{
					yield return null;
				}
			}
			sprite = spriteProp?.GetValue(template) as Sprite;
			if (target != null && sprite != null)
			{
				target.sprite = sprite;
			}
		}

		private void BuildRewardsSlide()
		{
			GameObject gameObject = new GameObject("RwdPanel");
			gameObject.transform.SetParent(_rightRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.72f);
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			ProfileData.Stats stats = ProfileData.ReadStats(_profile);
			BuildProfileHeader(gameObject.transform, stats.Side, stats.Nickname);
			JArray jArray = _cfg["rewards"]?["items"] as JArray;
			Plugin.Log.LogInfo($"[Epilogue] rewards slide: {jArray?.Count ?? 0} item(s) in JSON");
			if (jArray == null || jArray.Count == 0)
			{
				return;
			}
			RectTransform rectTransform2 = MakeScrollableContent(gameObject, 0.82f, 0.02f);
			string path = Path.Combine(AssetsDir(), "rewards");
			int num = 0;
			int num2 = 0;
			List<CanvasGroup> list = new List<CanvasGroup>();
			for (int i = 0; i < jArray.Count; i++)
			{
				if (jArray[i] is JObject jObject)
				{
					int num3 = ((jObject.Value<bool?>("big") != true) ? 1 : 2);
					if (num + num3 > 5)
					{
						num = 0;
						num2++;
					}
					float x = 12f + (float)num * 212f;
					float y = 0f - (12f + (float)num2 * 232f);
					float num4 = (float)num3 * 200f + (float)(num3 - 1) * 12f;
					Sprite iconSprite = null;
					string text = jObject.Value<string>("sprite");
					if (!string.IsNullOrEmpty(text))
					{
						iconSprite = LoadSprite(Path.Combine(path, text));
					}
					string text2 = jObject.Value<string>("kind") ?? "";
					string label = jObject.Value<string>("label") ?? "";
					bool itemKind = text2.Equals("Item", StringComparison.OrdinalIgnoreCase) || text2.Equals("Dogtag", StringComparison.OrdinalIgnoreCase);
					GameObject gameObject2 = TryClonePrestigeRewardCard(rectTransform2, iconSprite, text2, label, itemKind, num4, 220f) ?? BuildFallbackRewardCard(rectTransform2, iconSprite, text2, label);
					gameObject2.name = $"Rwd{i}";
					RectTransform rectTransform3 = gameObject2.GetComponent<RectTransform>();
					if (rectTransform3 == null)
					{
						rectTransform3 = gameObject2.AddComponent<RectTransform>();
					}
					rectTransform3.anchorMin = new Vector2(0f, 1f);
					rectTransform3.anchorMax = new Vector2(0f, 1f);
					rectTransform3.pivot = new Vector2(0f, 1f);
					rectTransform3.sizeDelta = new Vector2(num4, 220f);
					rectTransform3.anchoredPosition = new Vector2(x, y);
					list.Add(gameObject2.GetComponent<CanvasGroup>() ?? gameObject2.AddComponent<CanvasGroup>());
					num += num3;
					if (num >= 5)
					{
						num = 0;
						num2++;
					}
				}
			}
			int num5 = num2 + ((num > 0) ? 1 : 0);
			rectTransform2.sizeDelta = new Vector2(0f, (float)num5 * 232f + 12f);
			StartCoroutine(StaggerFadeIn(list, 0.1f, 0.2f));
		}

		private GameObject TryClonePrestigeRewardCard(Transform parent, Sprite iconSprite, string kind, string label, bool itemKind, float cardW, float rowH)
		{
			try
			{
				if (!_prestigeTemplateChecked)
				{
					_prestigeTemplateChecked = true;
					Type type = AccessTools.TypeByName("PrestigeRewardView");
					if (type != null)
					{
						UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(type);
						if (array != null && array.Length != 0)
						{
							_prestigeTemplate = array[0];
						}
					}
				}
				if (_prestigeTemplate == null)
				{
					return null;
				}
				Component component = _prestigeTemplate as Component;
				if (component == null || component.gameObject == null)
				{
					return null;
				}
				GameObject gameObject = UnityEngine.Object.Instantiate(component.gameObject, parent, worldPositionStays: false);
				gameObject.SetActive(value: true);
				Component component2 = gameObject.GetComponent(component.GetType());
				RectTransform rectTransform = AccessTools.Field(component.GetType(), "_sizeContainer")?.GetValue(component2) as RectTransform;
				if (rectTransform != null)
				{
					rectTransform.sizeDelta = new Vector2(cardW, rowH);
				}
				FieldInfo fieldInfo = AccessTools.Field(component.GetType(), "_icon");
				if (fieldInfo != null && iconSprite != null)
				{
					Image image = fieldInfo.GetValue(component2) as Image;
					if (image != null)
					{
						image.sprite = iconSprite;
						image.preserveAspect = true;
					}
				}
				SetTmpText(component2, "_rewardType", kind);
				SetTmpText(component2, "_rewardName", label);
				GameObject gameObject2 = AccessTools.Field(component.GetType(), "_background_1")?.GetValue(component2) as GameObject;
				GameObject gameObject3 = AccessTools.Field(component.GetType(), "_background_2")?.GetValue(component2) as GameObject;
				if (gameObject2 != null)
				{
					gameObject2.SetActive(!itemKind);
				}
				if (gameObject3 != null)
				{
					gameObject3.SetActive(itemKind);
				}
				GameObject gameObject4 = AccessTools.Field(component.GetType(), "_highlight")?.GetValue(component2) as GameObject;
				if (gameObject4 != null)
				{
					gameObject4.SetActive(value: false);
				}
				return gameObject;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] prestige clone failed, using fallback: " + ex.Message);
				return null;
			}
		}

		private static void SetTmpText(object comp, string fieldName, string text)
		{
			try
			{
				object obj = AccessTools.Field(comp.GetType(), fieldName)?.GetValue(comp);
				if (obj != null)
				{
					AccessTools.Property(obj.GetType(), "text")?.SetValue(obj, text);
				}
			}
			catch
			{
			}
		}

		private GameObject BuildFallbackRewardCard(Transform parent, Sprite iconSprite, string kind, string label)
		{
			GameObject gameObject = new GameObject("Rwd");
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);
			image.raycastTarget = false;
			if (iconSprite != null)
			{
				GameObject gameObject2 = new GameObject("Image");
				gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
				Image image2 = gameObject2.AddComponent<Image>();
				image2.sprite = iconSprite;
				image2.preserveAspect = true;
				image2.raycastTarget = false;
				RectTransform rectTransform = image2.rectTransform;
				rectTransform.anchorMin = new Vector2(0.02f, 0.02f);
				rectTransform.anchorMax = new Vector2(0.98f, 0.98f);
				rectTransform.offsetMin = Vector2.zero;
				rectTransform.offsetMax = Vector2.zero;
			}
			Text text = MakeText(gameObject.transform, "Kind", kind, 12, TextAnchor.UpperRight);
			text.color = new Color(0.65f, 0.65f, 0.6f, 1f);
			text.raycastTarget = false;
			RectTransform rectTransform2 = text.rectTransform;
			rectTransform2.anchorMin = new Vector2(0.02f, 0.88f);
			rectTransform2.anchorMax = new Vector2(0.98f, 0.98f);
			rectTransform2.offsetMin = Vector2.zero;
			rectTransform2.offsetMax = Vector2.zero;
			Text text2 = MakeText(gameObject.transform, "Name", label, 14, TextAnchor.UpperRight);
			text2.color = new Color(0.95f, 0.95f, 0.9f, 1f);
			text2.raycastTarget = false;
			RectTransform rectTransform3 = text2.rectTransform;
			rectTransform3.anchorMin = new Vector2(0.02f, 0.8f);
			rectTransform3.anchorMax = new Vector2(0.98f, 0.9f);
			rectTransform3.offsetMin = Vector2.zero;
			rectTransform3.offsetMax = Vector2.zero;
			return gameObject;
		}

		private void BuildKillfeedSlide()
		{
			GameObject gameObject = new GameObject("KillPanelWrap");
			gameObject.transform.SetParent(_rightRoot, worldPositionStays: false);
			Image image = gameObject.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0.72f);
			RectTransform rectTransform = image.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			try
			{
				SessionEndUI instance = MonoBehaviourSingleton<SessionEndUI>.Instance;
				SessionResultKillList sessionResultKillList = ((instance != null) ? instance.SessionResultKillList : null);
				if ((UnityEngine.Object)(object)sessionResultKillList == null)
				{
					Plugin.Log.LogWarning("[Epilogue] SessionResultKillList not available");
					MakeText(gameObject.transform, "Empty", "kill list unavailable", 18, TextAnchor.MiddleCenter).color = new Color(0.7f, 0.7f, 0.65f, 1f);
					return;
				}
				object obj = AccessTools.Property(_profile.GetType(), "EftStats")?.GetValue(_profile) ?? AccessTools.Field(_profile.GetType(), "EftStats")?.GetValue(_profile);
				object obj2 = AccessTools.Field(obj?.GetType(), "Victims")?.GetValue(obj) ?? AccessTools.Property(obj?.GetType(), "Victims")?.GetValue(obj);
				DogtagComponent[] array = new DogtagComponent[0];
				GameObject gameObject2 = (_borrowedKillListGo = ((Component)(object)sessionResultKillList).gameObject);
				if (!gameObject2.activeSelf)
				{
					gameObject2.SetActive(value: true);
				}
				MethodInfo methodInfo = Array.Find(((object)sessionResultKillList).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public), (MethodInfo m) => m.Name == "Show" && m.GetParameters().Length == 2);
				if (methodInfo != null && obj2 != null)
				{
					methodInfo.Invoke(sessionResultKillList, new object[2] { obj2, array });
				}
				else
				{
					Plugin.Log.LogWarning("[Epilogue] KillList Show(victims,tags) not resolved");
				}
				RectTransform rectTransform2 = AccessTools.Field(typeof(SessionResultKillList), "_container")?.GetValue(sessionResultKillList) as RectTransform;
				if (rectTransform2 != null)
				{
					_borrowedContainer = rectTransform2;
					_borrowedContainerOrigParent = rectTransform2.parent;
					_borrowedContainerOrigAnchors = (aMin: rectTransform2.anchorMin, aMax: rectTransform2.anchorMax, offMin: rectTransform2.offsetMin, offMax: rectTransform2.offsetMax);
					_borrowedContainerOrigSize = (sizeDelta: rectTransform2.sizeDelta, anchoredPos: rectTransform2.anchoredPosition, pivot: rectTransform2.pivot);
					RectTransform rectTransform3 = MakeScrollableContent(gameObject, 0.87f, 0.02f);
					rectTransform2.SetParent(rectTransform3, worldPositionStays: false);
					rectTransform2.anchorMin = new Vector2(0f, 1f);
					rectTransform2.anchorMax = new Vector2(1f, 1f);
					rectTransform2.pivot = new Vector2(0.5f, 1f);
					rectTransform2.anchoredPosition = Vector2.zero;
					StartCoroutine(SizeKillContentAfterLayout(rectTransform3, rectTransform2));
				}
				else
				{
					Plugin.Log.LogWarning("[Epilogue] KillList _container not reachable — killfeed slide will be empty");
				}
				gameObject2.SetActive(value: false);
				Text text = MakeText(gameObject.transform, "Head", "RAID KILL LIST", 20, TextAnchor.MiddleCenter);
				text.color = new Color(0.9f, 0.9f, 0.85f, 1f);
				text.raycastTarget = false;
				text.fontStyle = FontStyle.Bold;
				RectTransform rectTransform4 = text.rectTransform;
				rectTransform4.anchorMin = new Vector2(0.04f, 0.92f);
				rectTransform4.anchorMax = new Vector2(0.96f, 0.99f);
				rectTransform4.offsetMin = Vector2.zero;
				rectTransform4.offsetMax = Vector2.zero;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] killfeed reuse failed: " + ex.Message);
			}
		}

		private IEnumerator SizeKillContentAfterLayout(RectTransform scrollContent, RectTransform container)
		{
			for (int i = 0; i < 8; i++)
			{
				yield return null;
				if (scrollContent == null || container == null)
				{
					yield break;
				}
				LayoutRebuilder.ForceRebuildLayoutImmediate(container);
				float num = Mathf.Max(container.rect.height, container.sizeDelta.y);
				if (num > 1f)
				{
					scrollContent.sizeDelta = new Vector2(0f, num + 20f);
					ScrollRect scrollRect = ((!(scrollContent.parent != null)) ? null : scrollContent.parent.parent?.GetComponent<ScrollRect>());
					if (scrollRect != null)
					{
						scrollRect.verticalNormalizedPosition = 1f;
					}
					yield break;
				}
			}
			if (scrollContent != null)
			{
				scrollContent.sizeDelta = new Vector2(0f, 3000f);
			}
		}

		private static Text MakeText(Transform parent, string name, string content, int size, TextAnchor align)
		{
			GameObject gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, worldPositionStays: false);
			Text text = gameObject.AddComponent<Text>();
			text.text = content;
			text.font = ((_benderRegular != null) ? _benderRegular : Resources.GetBuiltinResource<Font>("Arial.ttf"));
			text.fontSize = size;
			text.alignment = align;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			RectTransform rectTransform = text.rectTransform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			return text;
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
			{
				Advance();
			}
			else if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Escape))
			{
				Regress();
			}
		}

		private void Advance()
		{
			if (!_typewriterDone && _typewriterTarget != null && _typewriterFullText != null)
			{
				if (_typewriterCo != null)
				{
					StopCoroutine(_typewriterCo);
				}
				_typewriterCo = null;
				_typewriterTarget.text = _typewriterFullText;
				_typewriterDone = true;
				UpdateButtonVisibility(_slides[_slideIx].Value<string>("kind") ?? "");
			}
			else if (_slideIx + 1 >= _slides.Count)
			{
				CloseToMenu();
			}
			else
			{
				ShowSlide(_slideIx + 1);
			}
		}

		private void Regress()
		{
			if (_slideIx != 0)
			{
				ShowSlide(_slideIx - 1);
			}
		}

		private void CloseToMenu()
		{
			if (_audio != null)
			{
				try
				{
					_audio.Stop();
				}
				catch
				{
				}
			}
			RestoreBorrowedContainer();
			UnityEngine.Object.Destroy(gameObject);
			try
			{
				object obj2 = AccessTools.Field(((object)_host).GetType(), "ScreenController")?.GetValue(_host);
				if (obj2 != null)
				{
					MethodInfo methodInfo = AccessTools.Method(obj2.GetType(), "GoToMainMenu");
					if (methodInfo != null)
					{
						methodInfo.Invoke(obj2, null);
						Plugin.Log.LogInfo("[Epilogue] ScreenController.GoToMainMenu → main menu");
					}
					else
					{
						Plugin.Log.LogWarning("[Epilogue] GoToMainMenu method not found on controller");
					}
				}
				else
				{
					Plugin.Log.LogWarning("[Epilogue] ScreenController field not reachable — main menu handoff missed");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] menu handoff failed: " + ex.Message);
			}
		}

		private void RestoreBorrowedContainer()
		{
			if (!(_borrowedContainer == null) && !(_borrowedContainerOrigParent == null))
			{
				try
				{
					_borrowedContainer.SetParent(_borrowedContainerOrigParent, worldPositionStays: false);
					_borrowedContainer.anchorMin = _borrowedContainerOrigAnchors.aMin;
					_borrowedContainer.anchorMax = _borrowedContainerOrigAnchors.aMax;
					_borrowedContainer.offsetMin = _borrowedContainerOrigAnchors.offMin;
					_borrowedContainer.offsetMax = _borrowedContainerOrigAnchors.offMax;
					_borrowedContainer.pivot = _borrowedContainerOrigSize.pivot;
					_borrowedContainer.sizeDelta = _borrowedContainerOrigSize.sizeDelta;
					_borrowedContainer.anchoredPosition = _borrowedContainerOrigSize.anchoredPos;
				}
				catch
				{
				}
				_borrowedContainer = null;
				_borrowedContainerOrigParent = null;
				_borrowedKillListGo = null;
			}
		}

		private void OnDestroy()
		{
			RestoreBorrowedContainer();
		}
	}

	internal static class ProfileData
	{
		internal struct Stats
		{
			public string Nickname;

			public string GameEdition;

			public string GameMode;

			public string GameStyle;

			public string PlayedTime;

			public string Kd;

			public string SurvivalRate;

			public string FavoriteWeapon;

			public string Side;

			public int Level;

			public int PrestigeLevel;

			public long TotalRaids;

			public long PmcKills;

			public long ScavKills;

			public long BossKills;

			public long WinStreak;
		}

		internal struct AchvRow
		{
			public string Id;

			public string Title;

			public bool Unlocked;
		}

		internal struct KillRow
		{
			public string Name;

			public string Location;

			public string Time;

			public string Faction;

			public string Status;

			public int Level;
		}

		internal static Stats ReadStats(Profile prof)
		{
			Stats result = new Stats
			{
				Nickname = "—",
				GameEdition = "—",
				GameMode = "—",
				GameStyle = "—",
				PlayedTime = "—",
				Kd = "—",
				SurvivalRate = "—",
				FavoriteWeapon = "—"
			};
			try
			{
				if (prof == null)
				{
					return result;
				}
				object obj = AccessTools.Property(prof.GetType(), "Info")?.GetValue(prof) ?? AccessTools.Field(prof.GetType(), "Info")?.GetValue(prof);
				if (obj != null)
				{
					result.Nickname = GetStr(obj, "Nickname") ?? "—";
					result.Level = GetInt(obj, "Level");
					result.PrestigeLevel = GetInt(obj, "PrestigeLevel");
					result.GameEdition = MapEdition(GetStr(obj, "GameVersion"));
					result.Side = GetStr(obj, "Side") ?? "";
					bool? flag = GetBool(obj, "HasPveGame");
					string gameMode;
					if (flag.HasValue)
					{
						gameMode = (flag.Value ? "PVE" : "PVP");
					}
					else
					{
						gameMode = "—";
					}
					result.GameMode = gameMode;
				}
				object obj2 = AccessTools.Property(prof.GetType(), "EftStats")?.GetValue(prof) ?? AccessTools.Field(prof.GetType(), "EftStats")?.GetValue(prof);
				if (obj2 != null)
				{
					long? longVal = GetLongVal(obj2, "TotalInGameTime");
					if (longVal.HasValue)
					{
						result.PlayedTime = FormatTime(TimeSpan.FromSeconds(longVal.Value));
					}
				}
				object obj3 = AccessTools.Field(obj2?.GetType(), "OverallCounters")?.GetValue(obj2) ?? AccessTools.Property(obj2?.GetType(), "OverallCounters")?.GetValue(obj2);
				if (obj3 != null)
				{
					result.TotalRaids = CounterLong(obj3, "Sessions");
					result.WinStreak = CounterLong(obj3, "LongestWinStreak");
					result.PmcKills = CounterLong(obj3, "KilledPmc");
					result.ScavKills = CounterLong(obj3, "KilledSavage");
					result.BossKills = CounterLong(obj3, "KilledBoss");
					long num = CounterLong(obj3, "Kills");
					long num2 = CounterLong(obj3, "Deaths");
					result.Kd = ((num2 > 0) ? ((float)num / (float)num2).ToString("0.0") : num.ToString("0.0"));
					if (result.TotalRaids > 0)
					{
						long num3 = Math.Max(0L, result.TotalRaids - num2);
						result.SurvivalRate = num3 * 100 / result.TotalRaids + " %";
					}
				}
				object obj4 = AccessTools.Property(prof.GetType(), "SurvivorClass")?.GetValue(prof) ?? AccessTools.Field(prof.GetType(), "SurvivorClass")?.GetValue(prof);
				if (obj4 != null)
				{
					result.GameStyle = obj4.ToString();
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] stats read failed: " + ex.Message);
			}
			return result;
		}

		internal static List<AchvRow> ReadAchievements(Profile prof)
		{
			List<AchvRow> list = new List<AchvRow>();
			try
			{
				if (prof == null)
				{
					return list;
				}
				if ((AccessTools.Property(prof.GetType(), "AchievementsData")?.GetValue(prof) ?? AccessTools.Field(prof.GetType(), "AchievementsData")?.GetValue(prof) ?? AccessTools.Property(prof.GetType(), "Achievements")?.GetValue(prof)) is IDictionary dictionary)
				{
					foreach (DictionaryEntry item in dictionary)
					{
						string text = item.Key?.ToString() ?? "";
						int num = 0;
						try
						{
							num = Convert.ToInt32(item.Value);
						}
						catch
						{
						}
						list.Add(new AchvRow
						{
							Id = text,
							Title = ((text.Length > 6) ? text.Substring(0, 6) : text),
							Unlocked = (num > 0)
						});
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] achievements read failed: " + ex.Message);
			}
			return list;
		}

		internal static List<KillRow> ReadVictims(Profile prof)
		{
			List<KillRow> list = new List<KillRow>();
			try
			{
				if (prof == null)
				{
					return list;
				}
				object obj = AccessTools.Property(prof.GetType(), "EftStats")?.GetValue(prof) ?? AccessTools.Field(prof.GetType(), "EftStats")?.GetValue(prof);
				if (obj == null)
				{
					return list;
				}
				if ((AccessTools.Field(obj.GetType(), "Victims")?.GetValue(obj) ?? AccessTools.Property(obj.GetType(), "Victims")?.GetValue(obj)) is IEnumerable enumerable)
				{
					foreach (object item in enumerable)
					{
						list.Add(VictimToRow(item));
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Epilogue] victims read failed: " + ex.Message);
			}
			return list;
		}

		private static KillRow VictimToRow(object v)
		{
			KillRow result = new KillRow
			{
				Name = "—",
				Location = "Terminal",
				Time = "—",
				Faction = "—",
				Status = "—"
			};
			if (v == null)
			{
				return result;
			}
			result.Name = GetStr(v, "Name") ?? "—";
			result.Location = GetStr(v, "Location") ?? "Terminal";
			result.Level = GetInt(v, "Level");
			if ((AccessTools.Property(v.GetType(), "Time")?.GetValue(v) ?? AccessTools.Field(v.GetType(), "Time")?.GetValue(v)) is TimeSpan timeSpan)
			{
				result.Time = $"{(int)timeSpan.TotalHours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
			}
			object obj = AccessTools.Property(v.GetType(), "Side")?.GetValue(v) ?? AccessTools.Field(v.GetType(), "Side")?.GetValue(v);
			result.Faction = MapFaction(role: (AccessTools.Property(v.GetType(), "Role")?.GetValue(v) ?? AccessTools.Field(v.GetType(), "Role")?.GetValue(v))?.ToString(), side: obj?.ToString());
			string text = GetStr(v, "Weapon") ?? "";
			string text2 = (AccessTools.Property(v.GetType(), "BodyPart")?.GetValue(v) ?? AccessTools.Field(v.GetType(), "BodyPart")?.GetValue(v))?.ToString() ?? "";
			float num = 0f;
			try
			{
				num = Convert.ToSingle(AccessTools.Property(v.GetType(), "Distance")?.GetValue(v) ?? AccessTools.Field(v.GetType(), "Distance")?.GetValue(v) ?? ((object)0f));
			}
			catch
			{
			}
			string text3 = ((text2 == "Head") ? "Headshot" : text2);
			result.Status = ((text.Length > 0) ? $"{text3} ({ShortWeapon(text)}, {num:0.0}m)" : text3);
			return result;
		}

		private static long CounterLong(object overall, string tagName)
		{
			try
			{
				Type type = AccessTools.TypeByName("EFT.Counters.PredefinedCounters");
				if (type == null)
				{
					return 0L;
				}
				object obj = type.GetField(tagName, BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (obj == null)
				{
					return 0L;
				}
				MethodInfo method = overall.GetType().GetMethod("GetLong", new Type[1] { obj.GetType() });
				if (method != null)
				{
					return Convert.ToInt64(method.Invoke(overall, new object[1] { obj }));
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogDebug("[Epilogue] counter '" + tagName + "' read failed: " + ex.Message);
			}
			return 0L;
		}

		private static string GetStr(object obj, string name)
		{
			if (obj == null)
			{
				return null;
			}
			PropertyInfo propertyInfo = AccessTools.Property(obj.GetType(), name);
			if (propertyInfo != null)
			{
				return propertyInfo.GetValue(obj)?.ToString();
			}
			return AccessTools.Field(obj.GetType(), name)?.GetValue(obj)?.ToString();
		}

		private static int GetInt(object obj, string name)
		{
			try
			{
				return Convert.ToInt32(AccessTools.Property(obj.GetType(), name)?.GetValue(obj) ?? AccessTools.Field(obj.GetType(), name)?.GetValue(obj) ?? ((object)0));
			}
			catch
			{
				return 0;
			}
		}

		private static bool? GetBool(object obj, string name)
		{
			try
			{
				return ((AccessTools.Property(obj.GetType(), name)?.GetValue(obj) ?? AccessTools.Field(obj.GetType(), name)?.GetValue(obj)) is bool value) ? new bool?(value) : ((bool?)null);
			}
			catch
			{
				return null;
			}
		}

		private static long? GetLongVal(object obj, string name)
		{
			try
			{
				object obj2 = AccessTools.Property(obj.GetType(), name)?.GetValue(obj) ?? AccessTools.Field(obj.GetType(), name)?.GetValue(obj);
				return (obj2 == null) ? ((long?)null) : new long?(Convert.ToInt64(obj2));
			}
			catch
			{
				return null;
			}
		}

		private static string FormatTime(TimeSpan ts)
		{
			int num = (int)ts.TotalDays;
			return $"{num}.{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
		}

		private static string MapEdition(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return "—";
			}
			return key.ToLowerInvariant() switch
			{
				"standard" => "Standard Edition", 
				"left_behind" => "Left Behind Edition", 
				"prepare_for_escape" => "Prepare for Escape Edition", 
				"edge_of_darkness" => "Edge of Darkness", 
				"unheard_edition" => "The Unheard Edition", 
				_ => key, 
			};
		}

		private static string MapFaction(string side, string role)
		{
			if (!string.IsNullOrEmpty(role))
			{
				string text = role.ToLowerInvariant();
				if (text.StartsWith("boss") || text.Contains("boss"))
				{
					return "BOSS";
				}
				switch (text)
				{
				case "assault":
				case "marksman":
				case "cursedassault":
					return "SCAV";
				case "sptbear":
				case "sptusec":
				case "pmcbear":
				case "pmcusec":
					return "PMC";
				}
			}
			if (!string.IsNullOrEmpty(side))
			{
				switch (side.ToLowerInvariant())
				{
				case "bear":
				case "usec":
					return "PMC";
				case "savage":
					return "SCAV";
				}
			}
			return "—";
		}

		private static string ShortWeapon(string weapon)
		{
			if (string.IsNullOrEmpty(weapon))
			{
				return "";
			}
			int num = weapon.IndexOf(' ');
			if (num > 0 && weapon.Length - num > 24)
			{
				return weapon.Substring(0, num);
			}
			if (weapon.Length <= 20)
			{
				return weapon;
			}
			return weapon.Substring(0, 20);
		}
	}

	internal const string DirName = "epilogue";

	internal const string JsonName = "terminal_epilogue.json";

	internal static bool Pending;

	private static Font _benderRegular;

	private static Font _benderBold;

	internal static void ResetForRaid()
	{
		Pending = false;
	}

	internal static void ArmAfterExtract()
	{
		if (Plugin.Epilogue.Value)
		{
			Pending = true;
			Plugin.Log.LogInfo("[Epilogue] armed — will take over SessionResultExitStatus.Show");
		}
	}

	private static void EnsureFonts()
	{
		if (_benderBold != null && _benderRegular != null)
		{
			return;
		}
		try
		{
			Font[] array = Resources.FindObjectsOfTypeAll<Font>();
			foreach (Font font in array)
			{
				if (!(font == null) && !string.IsNullOrEmpty(font.name))
				{
					if (_benderBold == null && font.name.EndsWith("Bender Bold"))
					{
						_benderBold = font;
					}
					else if (_benderRegular == null && font.name.EndsWith("Bender"))
					{
						_benderRegular = font;
					}
				}
			}
		}
		catch
		{
		}
	}
}
