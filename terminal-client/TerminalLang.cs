using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: Korean text for the mod's own on-screen strings (cutscene subtitles, the
// loudspeaker line, notifications, interaction prompts, epilogue labels).
//
// Lookups are keyed by the ORIGINAL English text, read from plugin-data/lang/kr.json
// ({"strings": {"English": "한국어"}}). A line the file does not know (or a line the
// original author rewords later) simply stays English, so nothing breaks.
// Language: F12 Terminal > Language = Auto (follow the game's language setting),
// Korean or English. Subtitles and the epilogue draw with legacy Unity fonts (Bender /
// Arial) that have no Hangul, so Korean text switches them to an OS font.
internal static class TerminalLang
{
	private static Dictionary<string, string> _kr;

	private static bool _loaded;

	private static float _nextLanguageCheck = -1f;

	private static bool _gameIsKorean;

	private static Font _koreanFont;

	private static bool _fontTried;

	private static string _fontSetting;

	internal static bool IsKorean
	{
		get
		{
			string mode = Plugin.Language?.Value ?? "Auto";
			if (string.Equals(mode, "Korean", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (string.Equals(mode, "English", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			float now = Time.realtimeSinceStartup;
			if (now >= _nextLanguageCheck)
			{
				_nextLanguageCheck = now + 5f;
				_gameIsKorean = ReadGameLanguageIsKorean();
			}
			return _gameIsKorean;
		}
	}

	// English in, Korean out when the player plays in Korean and the line is known
	internal static string T(string english)
	{
		if (string.IsNullOrEmpty(english) || !IsKorean)
		{
			return english;
		}
		EnsureLoaded();
		if (_kr != null && _kr.TryGetValue(english, out var korean) && !string.IsNullOrEmpty(korean))
		{
			return korean;
		}
		return english;
	}

	// a Hangul-capable font for legacy UI/IMGUI text, or null to keep the caller's font.
	// F12 Terminal > KoreanFont lists installed font names to try first (comma separated);
	// Malgun Gothic (every Korean Windows) is always the fallback.
	internal static Font KoreanFont
	{
		get
		{
			string setting = Plugin.KoreanFont?.Value ?? "";
			if (!_fontTried || setting != _fontSetting)
			{
				_fontTried = true;
				_fontSetting = setting;
				try
				{
					var names = new List<string>();
					foreach (string name in setting.Split(','))
					{
						if (!string.IsNullOrWhiteSpace(name))
						{
							names.Add(name.Trim());
						}
					}
					names.AddRange(new string[4] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum" });
					_koreanFont = Font.CreateDynamicFontFromOSFont(names.ToArray(), 32);
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[Lang] Korean OS font unavailable — Korean text keeps the default font: " + ex.Message);
				}
			}
			return _koreanFont;
		}
	}

	// the font to draw `text` with: the Korean OS font when the text contains Hangul
	internal static Font FontFor(string text, Font fallback)
	{
		if (!string.IsNullOrEmpty(text) && ContainsHangul(text))
		{
			Font font = KoreanFont;
			if (font != null)
			{
				return font;
			}
		}
		return fallback;
	}

	internal static bool ContainsHangul(string text)
	{
		foreach (char c in text)
		{
			if ((c >= '가' && c <= '힣') || (c >= 'ᄀ' && c <= 'ᇿ') || (c >= '㄰' && c <= '㆏'))
			{
				return true;
			}
		}
		return false;
	}

	// plugin-data/<relative> or its Korean twin "<name>.kr<ext>" when playing in Korean
	internal static string LocalizedDataPath(string path)
	{
		if (!IsKorean || string.IsNullOrEmpty(path))
		{
			return path;
		}
		string kr = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + ".kr" + Path.GetExtension(path));
		return File.Exists(kr) ? kr : path;
	}

	private static void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "lang", "kr.json");
			if (!File.Exists(path))
			{
				return;
			}
			_kr = new Dictionary<string, string>(StringComparer.Ordinal);
			if (JObject.Parse(File.ReadAllText(path))["strings"] is JObject strings)
			{
				foreach (JProperty item in strings.Properties())
				{
					_kr[item.Name] = item.Value?.ToString();
				}
			}
			Plugin.Log.LogInfo($"[Lang] Korean strings loaded: {_kr.Count}");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Lang] lang/kr.json failed to load: " + ex.Message);
			_kr = null;
		}
	}

	// Singleton<EFT.Settings.SettingsManager>.Instance.Game.Settings.Language.Value
	private static bool ReadGameLanguageIsKorean()
	{
		try
		{
			Type manager = TerminalTypes.ByName("EFT.Settings.SettingsManager");
			if (manager == null)
			{
				return false;
			}
			object instance = typeof(Comfort.Common.Singleton<>).MakeGenericType(manager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
			object language = Member(Member(Member(Member(instance, "Game"), "Settings"), "Language"), "Value");
			string code = language as string;
			return string.Equals(code, "kr", StringComparison.OrdinalIgnoreCase) || string.Equals(code, "kr-en", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static object Member(object target, string name)
	{
		if (target == null)
		{
			return null;
		}
		Type type = target.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		PropertyInfo property = type.GetProperty(name, flags);
		if (property != null && property.GetIndexParameters().Length == 0)
		{
			return property.GetValue(target, null);
		}
		return type.GetField(name, flags)?.GetValue(target);
	}
}
