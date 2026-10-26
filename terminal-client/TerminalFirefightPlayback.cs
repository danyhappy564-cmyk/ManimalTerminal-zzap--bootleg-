using System;
using System.Collections.Generic;
using Audio.AuxiliaryAudioUtils;
using EFT;
using UnityEngine;
using UnityEngine.Audio;

namespace Manimal.Terminal;

internal sealed class TerminalFirefightPlayback : IDisposable
{
	internal sealed class Selection
	{
		internal SoundBank Bank;

		internal AudioClip Near;

		internal AudioClip Far;

		internal float Balance;

		internal float Length;
	}

	private readonly HashSet<BetterSource> _leased = new HashSet<BetterSource>();

	private BetterAudio _audio;

	private readonly Dictionary<SoundBank, int> _fadeTypes;

	private readonly AudioSourcePriorityCalculator _priority = new AudioSourcePriorityCalculator();

	internal bool Ready
	{
		get
		{
			if (MonoBehaviourSingleton<BetterAudio>.Instantiated)
			{
				return TerminalAudioRouting.Firefight();
			}
			return false;
		}
	}

	internal TerminalFirefightPlayback(Dictionary<SoundBank, int> fadeTypes)
	{
		_fadeTypes = fadeTypes;
	}

	internal Selection Pick(SoundBank bank, float distance)
	{
		if (!bank)
		{
			return null;
		}
		Selection selection = new Selection
		{
			Bank = bank
		};
		selection.Length = bank.PickClips(distance, ref selection.Near, ref selection.Far, ref selection.Balance);
		if (_fadeTypes.TryGetValue(bank, out var value))
		{
			selection.Balance = TerminalFirefightTiming.Fade(selection.Balance, value);
		}
		if (!selection.Near && !selection.Far)
		{
			return null;
		}
		return selection;
	}

	internal bool Play(Selection selected, Vector3 position, double start, double end, float volume, float pitch, bool loop, float distance, bool scheduled = true, bool stereo = false)
	{
		AudioMixerGroup audioMixerGroup = TerminalAudioRouting.Firefight();
		if (!audioMixerGroup || selected == null || !MonoBehaviourSingleton<BetterAudio>.Instantiated)
		{
			return false;
		}
		_audio = MonoBehaviourSingleton<BetterAudio>.Instance;
		BetterSource source = _audio.GetSource(selected.Bank);
		if (!source)
		{
			return false;
		}
		_leased.Add(source);
		source.OnReleased += Released;
		try
		{
			source.EnabledHighPassFilter(enabledFilter: false);
			source.EnabledLowPassFilter(enabledFilter: false);
			source.ResetOcclusion();
			source.Position = position;
			source.SetPriority(_priority.CalculatePriority(distance, selected.Bank.Rolloff));
			source.SetRolloff(selected.Bank.Rolloff);
			source.SetMixerGroup(audioMixerGroup);
			source.SetSpatialBlend();
			source.Loop = loop;
			source.SetPitch(pitch);
			source.Play(selected.Near, selected.Far, selected.Balance, volume * Plugin.SoundRigVolume.Value, stereo, !scheduled);
			if (scheduled)
			{
				source.PlayScheduled(start);
			}
			if (loop)
			{
				source.SetScheduledEndTime(end);
			}
			double val = (loop ? end : (start + (double)(selected.Length / Math.Max(0.01f, Math.Abs(pitch)))));
			_audio.AddToAudioSourceQueue(source, Math.Max(val, start + (scheduled ? 1.0 : 0.01)));
			return true;
		}
		catch
		{
			source.OnReleased -= Released;
			_leased.Remove(source);
			_audio.RemoveFromAudioSourceQueue(source);
			source.Stop();
			source.Release();
			throw;
		}
	}

	private void Released(BetterSource source)
	{
		source.OnReleased -= Released;
		_leased.Remove(source);
	}

	internal void StopAll()
	{
		foreach (BetterSource item in _leased)
		{
			if ((bool)item)
			{
				item.OnReleased -= Released;
				if ((bool)_audio)
				{
					_audio.RemoveFromAudioSourceQueue(item);
				}
				item.Stop();
				item.Release();
			}
		}
		_leased.Clear();
	}

	public void Dispose()
	{
		StopAll();
	}
}
