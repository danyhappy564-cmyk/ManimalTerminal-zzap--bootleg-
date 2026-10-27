using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalFxBundle
{
	private static AssetBundle _bundle;

	private static bool _tried;

	private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

	internal static void ReleaseAfterRaid() { _clips.Clear(); }

	internal static AssetBundle Get()
	{
		if (_tried)
		{
			return _bundle;
		}
		_tried = true;
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "terminal_fx.bundle");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[FxBundle] terminal_fx.bundle missing — run Terminal/Build FX Bundle in the SDK");
				return null;
			}
			_bundle = AssetBundle.LoadFromFile(path);
			if (_bundle == null)
			{
				Plugin.Log.LogWarning("[FxBundle] terminal_fx.bundle failed to open");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[FxBundle] load failed: " + ex.Message);
		}
		return _bundle;
	}

	internal static AudioClip FindClip(string name)
	{
		if (_clips.TryGetValue(name, out var value) && (bool)value)
		{
			return value;
		}
		AssetBundle assetBundle = Get();
		AudioClip audioClip = ((assetBundle != null) ? assetBundle.LoadAsset<AudioClip>(name) : null);
		if (!audioClip)
		{
			AudioClip[] array = Resources.FindObjectsOfTypeAll<AudioClip>();
			foreach (AudioClip audioClip2 in array)
			{
				if (audioClip2 != null && audioClip2.name == name)
				{
					audioClip = audioClip2;
					break;
				}
			}
		}
		if ((bool)audioClip)
		{
			_clips[name] = audioClip;
		}
		else
		{
			Plugin.Log.LogWarning("[FxBundle] clip '" + name + "' not found — rebuild Terminal/Build FX Bundle in the SDK");
		}
		return audioClip;
	}
}
