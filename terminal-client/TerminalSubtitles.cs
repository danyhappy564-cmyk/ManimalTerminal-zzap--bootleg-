using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Playables;

namespace Manimal.Terminal;

internal static class TerminalSubtitles
{
	internal class StandaloneHost : MonoBehaviour
	{
		public string Text;

		public float EndAt;

		private void Update()
		{
			if (Time.unscaledTime >= EndAt)
			{
				UnityEngine.Object.Destroy(gameObject);
			}
		}

		private void OnGUI()
		{
			DrawPanel(Text);
		}
	}

	internal class SubtitleHost : MonoBehaviour
	{
		internal class Line
		{
			public float Start;

			public float End;

			public string Text;
		}

		public PlayableDirector Director;

		public readonly List<Line> Lines = new List<Line>();

		private string _current;

		private void Update()
		{
			if (Director == null)
			{
				_current = null;
				return;
			}
			float num = (float)Director.time;
			_current = null;
			for (int i = 0; i < Lines.Count; i++)
			{
				if (num >= Lines[i].Start && num <= Lines[i].End)
				{
					_current = Lines[i].Text;
					break;
				}
			}
		}

		private void OnGUI()
		{
			DrawPanel(_current);
		}
	}

	private static JObject _timings;

	private static SubtitleHost _host;

	internal static void ReleaseAfterRaid()
	{
		SubtitleHost host = _host;
		_host = null;
		if (host) TerminalRaidCleanup.DestroyOwned(host.gameObject);
	}

	private static Font _benderBold;

	private static Font _benderRegular;

	private static GUIStyle _sharedText;

	private static Texture2D _sharedBg;

	internal static void Show(string cutsceneKey, PlayableDirector director)
	{
		try
		{
			if (!Plugin.CutsceneSubtitles.Value || director == null)
			{
				return;
			}
			if (_timings == null)
			{
				string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_subtitle_timings.json");
				if (!File.Exists(path))
				{
					return;
				}
				_timings = JObject.Parse(File.ReadAllText(path));
			}
			if (_timings["cutscenes"]?[cutsceneKey] is JArray { Count: not 0 } jArray)
			{
				Hide();
				_host = new GameObject("Terminal_Subtitles").AddComponent<SubtitleHost>();
				_host.Director = director;
				foreach (JToken item in jArray)
				{
					string text = TerminalLang.T(item.Value<string>("text"));
					if (!string.IsNullOrEmpty(text))
					{
						float num = item.Value<float>("start");
						float num2 = item.Value<float>("end");
						float num3 = Mathf.Clamp((float)text.Length * 0.05f + 1.5f, 2f, 6f);
						float num4 = num + num3;
						if (num2 > num4)
						{
							num2 = num4;
						}
						_host.Lines.Add(new SubtitleHost.Line
						{
							Start = num,
							End = num2,
							Text = text
						});
					}
				}
				Plugin.Log.LogInfo($"[Subtitles] '{cutsceneKey}': {_host.Lines.Count} line(s) armed");
			}
			else
			{
				Plugin.Log.LogDebug("[Subtitles] no timing data for '" + cutsceneKey + "'");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Subtitles] show failed: " + ex.Message);
		}
	}

	internal static void Hide()
	{
		if ((bool)_host)
		{
			UnityEngine.Object.Destroy(_host.gameObject);
		}
		_host = null;
	}

	internal static void ShowStandalone(string text, float duration)
	{
		try
		{
			if (Plugin.CutsceneSubtitles.Value && !string.IsNullOrEmpty(text) && !(duration <= 0f))
			{
				StandaloneHost standaloneHost = new GameObject("Terminal_Subtitle_OneShot").AddComponent<StandaloneHost>();
				standaloneHost.Text = TerminalLang.T(text);
				standaloneHost.EndAt = Time.unscaledTime + duration;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Subtitles] standalone show failed: " + ex.Message);
		}
	}

	internal static void EnsureFonts()
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

	private static void DrawPanel(string txt)
	{
		if (!string.IsNullOrEmpty(txt))
		{
			if (_sharedText == null)
			{
				EnsureFonts();
				_sharedText = new GUIStyle(GUI.skin.label)
				{
					alignment = TextAnchor.MiddleLeft,
					fontSize = Mathf.RoundToInt((float)Screen.height * 0.017f),
					richText = true,
					wordWrap = true,
					padding = new RectOffset(10, 10, 4, 4),
					font = (_benderRegular ?? GUI.skin.font)
				};
				_sharedText.normal.textColor = new Color(0.94f, 0.94f, 0.94f, 1f);
				_sharedBg = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
				_sharedBg.SetPixel(0, 0, Color.white);
				_sharedBg.Apply();
				_sharedBg.hideFlags = HideFlags.HideAndDontSave;
			}
			// zzap: Bender has no Hangul — Korean lines draw with the OS Korean font
			_sharedText.font = TerminalLang.FontFor(txt, _benderRegular ?? GUI.skin.font);
			// zzap: size from F12 SubtitleSize (the original 1.7%-of-screen text read tiny)
			_sharedText.fontSize = Mathf.RoundToInt((float)Screen.height * 0.017f * Mathf.Clamp(Plugin.SubtitleSize?.Value ?? 1f, 0.5f, 3f));
			_sharedText.padding = new RectOffset(Mathf.RoundToInt(_sharedText.fontSize * 0.6f), Mathf.RoundToInt(_sharedText.fontSize * 0.6f), Mathf.RoundToInt(_sharedText.fontSize * 0.3f), Mathf.RoundToInt(_sharedText.fontSize * 0.3f));
			// zzap: a dynamic OS font reports glyphs it has not rasterized yet as too
			// narrow, so the box came out smaller than the line — rasterize the line
			// (regular and bold, for the speaker name) before measuring it
			try
			{
				string plain = txt.Replace("<b>", "").Replace("</b>", "");
				_sharedText.font.RequestCharactersInTexture(plain, _sharedText.fontSize, FontStyle.Normal);
				_sharedText.font.RequestCharactersInTexture(plain, _sharedText.fontSize, FontStyle.Bold);
			}
			catch
			{
			}
			float num = (float)Screen.width * 0.6f;
			// zzap: measure the text as drawn (bold speaker name included). Measuring with
			// the <b> tags stripped made the box narrower than the bold Korean line, which
			// then ran past its right edge.
			GUIContent content = new GUIContent(txt);
			float measured = _sharedText.CalcSize(content).x;
			// small slack for synthesized bold on OS fonts, which can draw a hair wider than measured
			float num2 = Mathf.Min(measured * 1.03f + (float)_sharedText.padding.horizontal + 12f, num);
			float height = _sharedText.CalcHeight(content, num2 - (float)_sharedText.padding.horizontal);
			// bottom-anchored so a larger or two-line subtitle grows upward and stays on screen
			Rect position = new Rect(((float)Screen.width - num2) / 2f, (float)Screen.height * 0.93f - height, num2, height);
			Color color = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.55f);
			GUI.DrawTexture(position, _sharedBg);
			GUI.color = color;
			GUI.Label(position, txt, _sharedText);
		}
	}
}
