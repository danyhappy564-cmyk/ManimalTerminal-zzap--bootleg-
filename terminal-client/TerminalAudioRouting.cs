using System;
using System.Collections.Generic;
using Audio.AmbientSubsystem;
using Comfort.Common;
using EFT.Settings;
using EFT.Settings.Sound;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalAudioRouting
{
	private static AudioMixer _settingsMixer;

	private static int _lastOverallIndex = -1;

	private static readonly Dictionary<string, AudioMixerGroup> Groups = new Dictionary<string, AudioMixerGroup>(StringComparer.OrdinalIgnoreCase);

	internal static void ReleaseAfterRaid()
	{
		Groups.Clear();
		_settingsMixer = null;
		_lastOverallIndex = -1;
	}

	internal static AudioMixerGroup AmbientBed()
	{
		return FindMasterGroup("AmbientOut");
	}

	internal static AudioMixerGroup AmbientEffects()
	{
		return FindMasterGroup("CommonAmbOutEffects", "AmbientOut");
	}

	internal static AudioMixerGroup Firefight()
	{
		return FindMasterGroup("AmbFirefight", "CommonAmbOutEffects", "AmbientOut");
	}

	internal static void ObserveVolumeChanges()
	{
		if (!Singleton<SettingsManager>.Instantiated)
		{
			return;
		}
		SoundSettingsGroup soundSettingsGroup = Singleton<SettingsManager>.Instance?.Sound?.Settings;
		if (soundSettingsGroup == null || soundSettingsGroup.OverallVolume.Value == _lastOverallIndex)
		{
			return;
		}
		_lastOverallIndex = soundSettingsGroup.OverallVolume.Value;
		foreach (AudioMixerGroup value in Groups.Values)
		{
			if ((bool)value)
			{
				LogVolume(value);
			}
		}
	}

	private static AudioMixerGroup FindMasterGroup(params string[] names)
	{
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			AudioMixer mixer = SoundSettingsController.MasterMixer;
			if (!mixer)
			{
				return null;
			}
			if (_settingsMixer != mixer)
			{
				_settingsMixer = mixer;
				Groups.Clear();
			}
			foreach (string wanted in names)
			{
				if (Groups.TryGetValue(wanted, out var value) && (bool)value)
				{
					return value;
				}
				AudioMixerGroup[] array = mixer.FindMatchingGroups(wanted);
				if (array != null)
				{
					AudioMixerGroup audioMixerGroup = ValueEnumerableExtensions.FirstOrDefault<FromArray<AudioMixerGroup>, AudioMixerGroup>(ValueEnumerable.AsValueEnumerable<AudioMixerGroup>(array), (Func<AudioMixerGroup, bool>)((AudioMixerGroup group) => (bool)group && group.audioMixer == mixer && string.Equals(group.name, wanted, StringComparison.OrdinalIgnoreCase)));
					if ((bool)audioMixerGroup)
					{
						Groups[wanted] = audioMixerGroup;
						LogVolume(audioMixerGroup);
						return audioMixerGroup;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[AudioRouting] mixer lookup failed: " + ex.Message);
		}
		return null;
	}

	private static void LogVolume(AudioMixerGroup group)
	{
		bool flag = group.audioMixer.GetFloat("MasterVolume", out var value);
		string text = "unavailable";
		try
		{
			SoundSettingsGroup soundSettingsGroup = ((!Singleton<SettingsManager>.Instantiated) ? null : Singleton<SettingsManager>.Instance?.Sound?.Settings);
			if (soundSettingsGroup != null)
			{
				text = $"{soundSettingsGroup.OverallVolume.Value} (expected {soundSettingsGroup.GetRealSoundValue(soundSettingsGroup.OverallVolume.Value)}dB)";
			}
		}
		catch
		{
		}
		Plugin.Log.LogInfo(string.Format("[AudioRouting] group='{0}' settingsMixer={1} MasterVolume={2} overallIndex={3}", group.name, group.audioMixer.GetInstanceID(), flag ? (value.ToString("0.##") + "dB") : "unreadable", text));
	}

	internal static bool RouteMaster(AudioSource source)
	{
		if (!source)
		{
			return false;
		}
		AudioMixerGroup audioMixerGroup = FindMasterGroup("Master");
		if (!audioMixerGroup)
		{
			return false;
		}
		source.outputAudioMixerGroup = audioMixerGroup;
		return true;
	}

	internal static void RouteTimeline(Scene scene, PlayableDirector director)
	{
		HashSet<AudioSource> hashSet = new HashSet<AudioSource>();
		if (scene.IsValid() && scene.isLoaded)
		{
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for (int i = 0; i < rootGameObjects.Length; i++)
			{
				AudioSource[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<AudioSource>(includeInactive: true);
				foreach (AudioSource audioSource in componentsInChildren)
				{
					if ((bool)audioSource)
					{
						hashSet.Add(audioSource);
					}
				}
			}
		}
		if ((bool)director && (bool)director.playableAsset)
		{
			foreach (PlayableBinding output in director.playableAsset.outputs)
			{
				if (!(output.outputTargetType != typeof(AudioSource)))
				{
					AudioSource audioSource2 = director.GetGenericBinding(output.sourceObject) as AudioSource;
					if (!audioSource2)
					{
						GameObject gameObject = new GameObject("Terminal_TimelineAudio_" + output.streamName);
						gameObject.transform.SetParent(director.transform, worldPositionStays: false);
						audioSource2 = gameObject.AddComponent<AudioSource>();
						audioSource2.playOnAwake = false;
						audioSource2.spatialBlend = 0f;
						director.SetGenericBinding(output.sourceObject, audioSource2);
					}
					if ((bool)audioSource2)
					{
						hashSet.Add(audioSource2);
					}
				}
			}
		}
		int num = 0;
		foreach (AudioSource item in hashSet)
		{
			if (RouteMaster(item))
			{
				num++;
			}
			else
			{
				item.mute = true;
			}
		}
		if ((bool)director && director.playableGraph.IsValid())
		{
			director.RebindPlayableGraphOutputs();
		}
		Plugin.Log.LogInfo($"[AudioRouting] timeline '{scene.name}': {num}/{hashSet.Count} sources routed to overall-volume output");
	}

	internal static bool IsMusicSource(AudioSource source)
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		if (!source || !source.outputAudioMixerGroup)
		{
			return false;
		}
		AudioMixerGroup assigned = source.outputAudioMixerGroup;
		if (assigned.name.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		try
		{
			AudioMixerGroup[] array = SoundSettingsController.MasterMixer?.FindMatchingGroups("Music");
			if (array == null)
			{
				return false;
			}
			return ValueEnumerableExtensions.Any<AudioMixerGroup>(ValueEnumerable.AsValueEnumerable<AudioMixerGroup>(array), (Func<AudioMixerGroup, bool>)((AudioMixerGroup group) => group == assigned));
		}
		catch
		{
		}
		return false;
	}

	internal static bool Route(AudioSource source, bool effects = false)
	{
		if (!source)
		{
			return false;
		}
		AudioMixerGroup audioMixerGroup = (effects ? AmbientEffects() : AmbientBed());
		if (!audioMixerGroup)
		{
			return false;
		}
		source.outputAudioMixerGroup = audioMixerGroup;
		return true;
	}

	internal static bool PrepareNativePlayers(GameObject root, AudioMixerGroup group)
	{
		if (!root || !group || group.audioMixer != SoundSettingsController.MasterMixer)
		{
			return false;
		}
		AmbientSoundPlayerGroup[] componentsInChildren = root.GetComponentsInChildren<AmbientSoundPlayerGroup>(includeInactive: true);
		foreach (AmbientSoundPlayerGroup ambientSoundPlayerGroup in componentsInChildren)
		{
			if ((bool)ambientSoundPlayerGroup)
			{
				ambientSoundPlayerGroup._outputMixerGroup = group;
			}
		}
		BaseAmbientSoundPlayer[] componentsInChildren2 = root.GetComponentsInChildren<BaseAmbientSoundPlayer>(includeInactive: true);
		foreach (BaseAmbientSoundPlayer baseAmbientSoundPlayer in componentsInChildren2)
		{
			if ((bool)baseAmbientSoundPlayer)
			{
				baseAmbientSoundPlayer._mixerGroup = group;
			}
		}
		RouteTree(root, group);
		return true;
	}

	internal static int RouteTree(GameObject root, bool effects = false)
	{
		if (!root)
		{
			return 0;
		}
		return RouteTree(root, effects ? AmbientEffects() : AmbientBed());
	}

	internal static int RouteTree(GameObject root, AudioMixerGroup mixerGroup)
	{
		if (!root || !mixerGroup)
		{
			return 0;
		}
		int num = 0;
		AudioSource[] componentsInChildren = root.GetComponentsInChildren<AudioSource>(includeInactive: true);
		foreach (AudioSource audioSource in componentsInChildren)
		{
			if ((bool)audioSource)
			{
				audioSource.outputAudioMixerGroup = mixerGroup;
				num++;
			}
		}
		return num;
	}
}
