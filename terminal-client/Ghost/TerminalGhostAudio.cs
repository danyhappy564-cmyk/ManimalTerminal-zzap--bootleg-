using System;
using System.Collections.Generic;
using Audio.AuxiliaryAudioUtils;
using Audio.ReverbSubsystem;
using Audio.SpatialSystem;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using EFT.Weapons;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Manimal.Terminal;

// zzap: sound-only accompaniment to an existing Fight. All reservations stay in
// fixed raid-local storage; never schedule a future Play/Enqueue/coroutine.
internal sealed class TerminalGhostAudio
{
	private const int StartsPerFrame = 4; // bodies AND tails share this budget
	private const int MaxVoices = 32;

	private struct Voice
	{
		internal BetterSource Source;
		internal TerminalGhostCombat.Fight Fight;
		internal float Started;
		internal double End;
	}

	internal struct Shooter
	{
		internal Weapon Weapon;
		internal WeaponSoundPlayer Sound;
		internal float NextAt;
		internal int Remaining;
		internal bool Tail, Stopped;
	}

	internal sealed class State
	{
		internal readonly Shooter[] Shooters = new Shooter[TerminalGhostCombat.MaxMembers * 2];
		internal bool Disabled, Selected, Armed;
		internal int Cursor;
	}

	private readonly TerminalGhostCombat.Fight[] _nearest = new TerminalGhostCombat.Fight[TerminalGhostCombat.MaxFights];
	private readonly float[] _distances = new float[TerminalGhostCombat.MaxFights];
	private readonly AudioSourcePriorityCalculator _priority = new AudioSourcePriorityCalculator();
	private readonly Voice[] _voices = new Voice[MaxVoices];
	private int _fightCursor;
	internal int Shots { get; private set; }

	internal void ResetShots() { Shots = 0; }

	internal void ReleaseSources(bool all)
	{
		double now = AudioSettings.dspTime;
		for (int i = 0; i < _voices.Length; i++)
		{
			Voice voice = _voices[i];
			if (ReferenceEquals(voice.Source, null) || (!all && now < voice.End)) continue;
			_voices[i] = default;
			try
			{
				if (!voice.Source) continue;
				try { voice.Source.Stop(); }
				finally { voice.Source.Release(); }
			}
			catch (Exception ex)
			{
				// Pool slots can be reused while old sounds finish naturally.
				if (voice.Fight.Active && voice.Fight.Started == voice.Started) Fail(voice.Fight, ex);
			}
		}
	}

	internal static void Reset(TerminalGhostCombat.Fight fight)
	{
		State state = fight.Audio;
		Array.Clear(state.Shooters, 0, state.Shooters.Length);
		state.Disabled = state.Selected = state.Armed = false;
		state.Cursor = 0;
	}

	internal static void StopShooter(TerminalGhostCombat.Fight fight, TerminalGhostDirector.BotRecord member)
	{
		for (int i = 0; i < fight.CountA + fight.CountB; i++)
		{
			if (fight.Members[i] != member) continue;
			fight.Audio.Shooters[i] = default;
			fight.Audio.Shooters[i].Stopped = true;
			return;
		}
	}

	private static void Suspend(State state)
	{
		if (!state.Armed) return;
		for (int i = 0; i < state.Shooters.Length; i++)
		{
			bool stopped = state.Shooters[i].Stopped;
			state.Shooters[i] = default;
			state.Shooters[i].Stopped = stopped;
		}
		state.Armed = false;
	}

	private static bool Available(TerminalGhostCombat.Fight fight, float now)
	{
		return fight.Active && !fight.Audio.Disabled && now < fight.Ends
			&& fight.A.Dormant && fight.B.Dormant && !fight.A.Queued && !fight.B.Queued;
	}

	private static bool Alive(TerminalGhostDirector.BotRecord member)
	{
		BotOwner bot = member?.Bot;
		return member != null && member.Dormant && bot && !bot.IsDead && bot.GetPlayer
			&& bot.BotState == EBotState.NonActive && bot.HealthController != null && bot.HealthController.IsAlive;
	}

	private static float HumanDistanceSquared(Vector3 position, List<Player> humans)
	{
		float nearest = float.PositiveInfinity;
		for (int i = 0; i < humans.Count; i++)
		{
			Player human = humans[i];
			if (!human || human.IsAI || human.HealthController == null || !human.HealthController.IsAlive) continue;
			nearest = Mathf.Min(nearest, (position - human.Position).sqrMagnitude);
		}
		return nearest;
	}

	internal void Tick(float now, TerminalGhostCombat.Fight[] fights, List<Player> humans)
	{
		ReleaseSources(false);
		bool enabled = TerminalGhostDirector.Running && Plugin.GhostCombat.Value && Plugin.GhostAudio.Value
			&& Plugin.GhostBrainThrottle.Value && !TerminalGhostPolicy.Cutscene && humans.Count != 0
			&& Singleton<BetterAudio>.Instantiated && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated;
		float range = Mathf.Clamp(Plugin.GhostAudioRange.Value, 1f, 1500f);
		float rangeSq = range * range;
		int count = 0;
		for (int i = 0; i < fights.Length; i++)
		{
			TerminalGhostCombat.Fight fight = fights[i];
			fight.Audio.Selected = false;
			if (!enabled || !Available(fight, now)) { Suspend(fight.Audio); continue; }
			try
			{
				float nearest = float.PositiveInfinity;
				for (int j = 0; j < fight.CountA + fight.CountB; j++)
					if (!fight.Audio.Shooters[j].Stopped && Alive(fight.Members[j]))
						nearest = Mathf.Min(nearest, HumanDistanceSquared(fight.Members[j].Bot.Position, humans));
				if (nearest > rangeSq) { Suspend(fight.Audio); continue; }
				int at = count++;
				while (at > 0 && nearest < _distances[at - 1])
				{
					_nearest[at] = _nearest[at - 1];
					_distances[at] = _distances[at - 1];
					at--;
				}
				_nearest[at] = fight;
				_distances[at] = nearest;
			}
			catch (Exception ex) { Fail(fight, ex); }
		}
		int limit = Mathf.Min(count, Mathf.Clamp(Plugin.GhostAudioMaxFights.Value, 1, fights.Length));
		for (int i = 0; i < limit; i++) _nearest[i].Audio.Selected = true;
		for (int i = 0; i < fights.Length; i++)
			if (!fights[i].Audio.Selected) Suspend(fights[i].Audio);
		// Retail ambience has its own 12 sequence slots: firefight.json director
		// _maxConcurrentSequencesTotal, consumed by TerminalFirefightDirector.TickCore.
		// Those authored off-map sequences are independent of bot fights: configured
		// sequence budgets sum to 12 + GhostAudioMaxFights (default 14); there is no
		// shared voice cap. Ghost additionally limits ALL starts to four per frame and
		// leases at most 32 voices from already idle EFT sources. The EFT factory's
		// SourcesCountLimit only WARNS then allocates: Play checks stack_0 first so
		// Ghost cannot cause that expansion. Our own release slots also avoid growth
		// of EFT TimeQueue._actions in AddToAudioSourceQueue.
		int budget = StartsPerFrame;
		if (limit > 0)
		{
			int first = _fightCursor++ % limit;
			if (_fightCursor == int.MaxValue) _fightCursor = 0;
			for (int i = 0; i < limit && budget > 0; i++)
			{
				TerminalGhostCombat.Fight fight = _nearest[(first + i) % limit];
				try { Pump(fight, now, humans, rangeSq, ref budget); }
				catch (Exception ex) { Fail(fight, ex); }
			}
		}
		Array.Clear(_nearest, 0, count);
	}

	private void Pump(TerminalGhostCombat.Fight fight, float now, List<Player> humans, float rangeSq, ref int budget)
	{
		State state = fight.Audio;
		int count = fight.CountA + fight.CountB;
		if (!state.Armed)
		{
			for (int i = 0; i < count; i++) state.Shooters[i].NextAt = now + Random.Range(0.05f, 0.7f);
			state.Armed = true;
		}
		int first = state.Cursor;
		state.Cursor = (state.Cursor + 1) % count;
		for (int i = 0; i < count && budget > 0; i++)
		{
			// Recheck at each start: no sounds after a wake/cancellation/deadline.
			if (!Available(fight, now) || !Plugin.GhostAudio.Value || !TerminalGhostDirector.Running) return;
			int index = (first + i) % count;
			ref Shooter shooter = ref state.Shooters[index];
			if (shooter.Stopped || now < shooter.NextAt) continue;
			var member = fight.Members[index];
			if (!Alive(member)) { StopShooter(fight, member); continue; }
			Player player = member.Bot.GetPlayer;
			Vector3 position = player.Position + Vector3.up * 1.4f;
			if (HumanDistanceSquared(player.Position, humans) > rangeSq)
			{
				shooter = default;
				shooter.NextAt = now + 0.5f;
				continue;
			}
			var controller = player.HandsController as Player.FirearmController;
			Weapon weapon = controller?.Item;
			WeaponSoundPlayer sound = controller?.WeaponSoundPlayer;
			if (weapon == null || !sound) { shooter.Stopped = true; continue; }
			bool doublet = weapon.SelectedFireMode == Weapon.EFireMode.doublet;
			bool loopBody = sound.IsAutoWeapon && !doublet;
			int rounds = doublet ? 2 : 1;
			if (budget < rounds) continue;
			// Installed EFT Player.FirearmController.InitSoundPlayer/OnWeaponModified
			// writes IsSilenced from the equipped muzzle; read it, never infer by name.
			SoundBank body = doublet ? (sound.IsSilenced ? sound.DoubletSilenced : sound.Doublet)
				: (sound.IsSilenced ? sound.BodySilenced : sound.Body);
			SoundBank tail = sound.IsSilenced ? sound.TailSilenced : sound.Tail;
			if (!body || (loopBody && !tail)) { shooter.Stopped = true; continue; }
			if (shooter.Weapon != weapon || shooter.Sound != sound)
			{
				shooter.Remaining = 0;
				shooter.Tail = false;
				shooter.Weapon = weapon;
				shooter.Sound = sound;
			}
			bool automatic = weapon.SelectedFireMode == Weapon.EFireMode.fullauto || weapon.SelectedFireMode == Weapon.EFireMode.burst;
			// Weapon.FireRate -> WeaponTemplate.bFirerate. For trigger taps read the
			// template directly (Weapon.SingleFireRate's getter can mutate invalid data).
			int rpm = automatic ? weapon.FireRate : weapon.Template.SingleFireRate;
			if (rpm <= 0) { shooter.Stopped = true; continue; }
			float interval = 60f / rpm;
			float beat = loopBody ? body.ClipLength / 16f : 0f;
			if (loopBody && (beat <= 0f || weapon.FireRate <= 0)) { shooter.Stopped = true; continue; }
			// Single-mode trigger gaps must not slow the recorded report itself.
			float pitch = loopBody ? beat * weapon.FireRate / 60f : 1f;
			float distance = CameraManager.Instance.Distance(position);
			if (distance > body.Rolloff) { Pause(ref shooter, now); continue; }
			if (shooter.Tail)
			{
				budget--;
				Play(fight, player, tail, position, distance, 0f, pitch);
				Pause(ref shooter, now);
				continue;
			}
			if (shooter.Remaining == 0)
			{
				shooter.Remaining = Random.Range(2, 7);
				if (doublet) shooter.Remaining = Random.Range(1, 4) * 2;
				if (weapon.SelectedFireMode == Weapon.EFireMode.burst)
					shooter.Remaining = Mathf.Clamp(weapon.FireMode.BurstShotsCount, 2, 6);
			}
			budget -= rounds;
			// WeaponSoundPlayer.FireBullet/StopFiringLoop use Body as a 16-beat
			// automatic loop and Tail after its last beat. Playing Body in full for
			// every reservation would emit 16 unbudgeted rounds. Bound this source to
			// ONE beat, pitch it to the actual RPM, then start Tail only when due.
			if (Play(fight, player, body, position, distance, beat, pitch)) Shots += rounds;
			shooter.Remaining -= rounds;
			shooter.NextAt = now + interval; // no catch-up avalanche after a stall
			if (shooter.Remaining > 0) continue;
			if (loopBody) { shooter.Tail = true; shooter.NextAt = now + beat / pitch; }
			else Pause(ref shooter, now);
		}
	}

	private static void Pause(ref Shooter shooter, float now)
	{
		shooter.Remaining = 0;
		shooter.Tail = false;
		shooter.NextAt = now + (Random.value < 0.15f ? Random.Range(4f, 8f) : Random.Range(0.5f, 3f));
	}

	private bool Play(TerminalGhostCombat.Fight fight, Player player, SoundBank bank, Vector3 position, float distance, float beat, float pitch)
	{
		if (!bank || distance > bank.Rolloff) return false;
		int slot = 0;
		while (slot < _voices.Length && !ReferenceEquals(_voices[slot].Source, null)) slot++;
		if (slot == _voices.Length) return false;
		BetterAudio audio = Singleton<BetterAudio>.Instance;
		var factory = audio.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Gunshots] as BetterAudio.NonspatialAudioMixer<ReverbSuperSource>;
		if (factory == null || factory.stack_0.Count == 0) return false;
		AudioClip near = null, far = null;
		float balance = 1f;
		float length = bank.PickClips(distance, ref near, ref far, ref balance, player.Environment);
		if (length <= 0f || (!near && !far)) return false;
		// Installed Assembly-CSharp: BetterAudio.Init allocates Gunshots as
		// Audio.ReverbSubsystem.ReverbSuperSource (two distance layers + reverb).
		// WeaponSoundPlayer.RegisterSourceForOcclusionProcess/UpdateMixerGroup use
		// this same player occlusion path and AudioWeaponUtils mixer selection.
		SuperSource source = audio.GetSource(BetterAudio.AudioSourceGroupType.Gunshots) as SuperSource;
		if (!source) return false;
		try
		{
			source.ResetOcclusion();
			source.Position = position;
			source.SetRolloff(bank.Rolloff);
			source.SetPriority(_priority.CalculatePriority(distance, bank.Rolloff));
			source.SetMixerGroup(audio.GunshotMixerGroup);
			source.SetSpatialBlend();
			source.Loop = false;
			source.SetPitch(pitch);
			source.IncludeInOcclusionProcess(true);
			var spatial = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
			spatial.ProcessSourceOcclusion(player, source);
			if (spatial.TryGetPlayerAudioContainer(player.Id, out var container))
				source.SetMixerGroup(AudioWeaponUtils.GetGunshotOcclusionMixerGroup(container.OcclusionEffect));
			// SuperAudioSample.PlayOn sets these fields and calls PlayScheduled.
			// Use NOW, with no future starts: ReverbSuperSource.PlayScheduled also
			// enables its auxiliary reverb sources (plain Play does not).
			source.source1.clip = near;
			source.source1.volume = balance * bank.BaseVolume;
			source.source2.enabled = true;
			source.source2.clip = far;
			source.source2.volume = (1f - balance) * bank.BaseVolume;
			double start = AudioSettings.dspTime;
			source.PlayScheduled(start);
			bank.Balance(source, distance);
			double end = start + (beat > 0f ? beat : length) / pitch;
			if (beat > 0f) source.SetScheduledEndTime(end);
			_voices[slot].Source = source;
			_voices[slot].Fight = fight;
			_voices[slot].Started = fight.Started;
			_voices[slot].End = end + 0.1;
			return true;
		}
		catch
		{
			// A borrowed source that failed before queuing must go back to EFT.
			try { source.Stop(); }
			finally { source.Release(); }
			throw;
		}
		// Deliberately NO FireBullet, FirearmController.Shot/RegisterShot,
		// AIData.TryPlayShootSound, BotsGroup sound or GlobalEventDispatcher calls.
		// Installed SAIN TryPlayShootSoundPatch/RegisterShotPatch/OnMakingShotPatch
		// listen to gameplay events, not BetterSource output. Ghost stage 1 wakes
		// on Damage/AllowTarget/proximity; none is invoked by audio playback here.
		// This also bypasses Terminal's Civilian/Patches/GunshotHearingPatch (Shot).
	}

	private static void Fail(TerminalGhostCombat.Fight fight, Exception ex)
	{
		if (fight.Audio.Disabled) return;
		fight.Audio.Disabled = true;
		Suspend(fight.Audio);
		// Once per Fight, independent of combat/damage: an audio failure never wakes
		// either squad or cancels its simulated health reservations.
		Plugin.Log.LogWarning("[GhostAudio] disabled sound for one fight: " + ex.Message);
	}
}
