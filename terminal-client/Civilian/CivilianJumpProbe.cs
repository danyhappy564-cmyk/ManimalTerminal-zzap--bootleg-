using System;
using System.Collections.Generic;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

// zzap diagnostic (10/26: "civilians stutter and teleport"). Nothing in the logs said
// why, so instead of guessing this records the moment it happens: when a living
// civilian moves farther in one frame than walking/running could explain, log who
// owned the bot at that instant — BigBrain layer + logic, mover state, animator
// controller, distance from the camera — at most once per 2s per civilian.
// Read-only: it never moves or changes anything. F12 Terminal > CivilianJumpProbe.
internal sealed class CivilianJumpProbe : MonoBehaviour
{
	private sealed class Track
	{
		internal Vector3 Pos;
		internal Vector3 RootPos;
		internal int NearWindow = -1;

		internal int Frame;

		internal float NextLog;

		internal int Jumps;

		internal string Layer;

		internal readonly List<float> SwitchTimes = new List<float>();

		internal readonly List<string> SwitchNames = new List<string>();

		internal float NextSwitchLog;

		// [CivMotion] last-frame values; a change counts as one flip.
		internal bool Sprint, Moving, MotionKnown;
		internal float Pose;
		internal EPlayerState State;
		internal int Anim;
		internal System.Type Hands;
		internal Vector3 Step;
		internal int Window = -1;
		internal int Flips, Reversals;
	}

	// zzap 10/28 2nd raid: civilians still stutter, MotionMagic is not installed (so the
	// owner debounce never ran) and position steps are 0. Count, per civilian within 30m
	// of the camera, every frame-to-frame flip of the inputs that drive the body animator
	// plus direction reversals of the root (back-and-forth "shuffling").
	private int _motionCivs, _sprintFlips, _poseFlips, _stateFlips, _handsFlips, _animFlips, _moveFlips, _reversals;
	private int _worstFlips;
	private string _worst;

	private const float MaxPlausibleSpeed = 9f;

	// v2 (10/26 third raid caught only one 9.6m jump, yet the stutter was visible):
	// the stutter is smaller back-and-forth steps, so flag anything faster than a
	// sprint, and separately log brain-layer flip-flops (two systems fighting over
	// the same civilian look exactly like stutter-stepping).
	private const float MinJump = 0.45f;

	private const int SwitchBurst = 4;

	private const float SwitchWindow = 3f;

	private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

	private int _logged;
	private float _nextNearReport;
	private int _nearWindow, _nearSteps, _nearBots, _nearRootSteps;

	internal static void EnsureForRaid()
	{
		if (!TerminalGate.On || !Plugin.CivilianJumpProbe.Value)
		{
			return;
		}
		new GameObject("Terminal_CivilianJumpProbe").AddComponent<CivilianJumpProbe>();
	}

	private void LateUpdate()
	{
		if (!TerminalGate.On)
		{
			Destroy(gameObject);
			return;
		}
		if (!Plugin.CivilianJumpProbe.Value)
		{
			return;
		}
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		List<Player> players = world?.AllAlivePlayersList;
		if (players == null)
		{
			return;
		}
		int frame = Time.frameCount;
		float dt = Mathf.Max(Time.deltaTime, 0.001f);
		Camera camera = Camera.main;
		Vector3 eye = camera ? camera.transform.position : Vector3.zero;
		for (int i = 0; i < players.Count && i < 512; i++)
		{
			Player player = players[i];
			if (player == null || player.IsYourPlayer)
			{
				continue;
			}
			WildSpawnType? role = player.Profile?.Info?.Settings?.Role;
			if (!role.HasValue || !CivilianConstants.IsCivilian(role.Value))
			{
				continue;
			}
			int id = player.GetInstanceID();
			Vector3 pos = player.Position;
			Vector3 root = player.Transform.Original.position;
			if (!_tracks.TryGetValue(id, out var track))
			{
				if (_tracks.Count < 512) _tracks[id] = new Track { Pos = pos, RootPos = root, Frame = frame };
				continue;
			}
			float moved = Vector3.Distance(pos, track.Pos);
			float rootMoved = Vector3.Distance(root, track.RootPos);
			int frames = Mathf.Max(1, frame - track.Frame);
			Vector3 step = pos - track.Pos;
			track.Pos = pos;
			track.RootPos = root;
			track.Frame = frame;
			if (frames == 1 && camera && (pos - eye).sqrMagnitude <= 900f) SampleMotion(player, track, step);
			if (frames == 1 && Mathf.Max(moved, rootMoved) >= 0.2f && camera && (pos - eye).sqrMagnitude <= 2500f)
			{
				_nearSteps++;
				if (rootMoved >= 0.2f) _nearRootSteps++;
				if (track.NearWindow != _nearWindow) { track.NearWindow = _nearWindow; _nearBots++; }
			}
			if (_logged < 200) TrackLayer(player, track);
			if (frames != 1 || moved < MinJump || moved < MaxPlausibleSpeed * dt * 1.5f)
			{
				continue;
			}
			track.Jumps++;
			if (_logged >= 200 || Time.realtimeSinceStartup < track.NextLog)
			{
				continue;
			}
			track.NextLog = Time.realtimeSinceStartup + 2f;
			_logged++;
			Plugin.Log.LogWarning("[CivJump] " + Describe(player, moved, dt, track.Jumps) + $" rootMoved={rootMoved:F2}m bodyOffset={(pos - root).ToString("F2")}");
		}
		if (_nextNearReport == 0f) _nextNearReport = Time.realtimeSinceStartup + 30f;
		if (Time.realtimeSinceStartup >= _nextNearReport)
		{
			_nextNearReport = Time.realtimeSinceStartup + 30f;
			Plugin.Log.LogInfo($"[CivMotion] last30s civs<=30m {_motionCivs}: sprint {_sprintFlips}, pose {_poseFlips}, state {_stateFlips}, hands {_handsFlips}, anim {_animFlips}, moving {_moveFlips}, reversals {_reversals}; worst {_worst ?? "-"}");
			_motionCivs = _sprintFlips = _poseFlips = _stateFlips = _handsFlips = _animFlips = _moveFlips = _reversals = _worstFlips = 0;
			_worst = null;
			Plugin.Log.LogInfo($"[CivJumpNear] last30s cam<=50m one-frame steps>=0.2m: events {_nearSteps}, bots {_nearBots}, rootSteps {_nearRootSteps}, bodyOnly {_nearSteps - _nearRootSteps}; animOwner rawFlips {Patches.CivilianMotionMatchingPatch.RawFlips}, applied {Patches.CivilianMotionMatchingPatch.AppliedFlips}");
			Patches.CivilianMotionMatchingPatch.RawFlips = Patches.CivilianMotionMatchingPatch.AppliedFlips = 0;
			_nearSteps = _nearBots = _nearRootSteps = 0;
			_nearWindow++;
		}
	}

	private void SampleMotion(Player player, Track track, Vector3 step)
	{
		BotOwner bot = player.AIData?.BotOwner;
		bool sprint = bot?.Mover != null && bot.Mover.Sprinting;
		bool moving = bot?.Mover != null && bot.Mover.IsMoving;
		float pose = player.PoseLevel;
		EPlayerState state = player.MovementContext?.CurrentState?.Name ?? EPlayerState.None;
		System.Type hands = player.HandsController?.GetType();
		int anim = 0;
		if (player._animators != null && player._animators.Length != 0 && player._animators[0] != null)
			anim = player._animators[0].GetCurrentAnimatorStateInfo(0).fullPathHash;
		step.y = 0f;
		if (track.Window != _nearWindow) { track.Window = _nearWindow; track.Flips = track.Reversals = 0; _motionCivs++; }
		if (track.MotionKnown)
		{
			int flips = 0;
			if (sprint != track.Sprint) { _sprintFlips++; flips++; }
			if (moving != track.Moving) { _moveFlips++; flips++; }
			if (Mathf.Abs(pose - track.Pose) > 0.05f) { _poseFlips++; flips++; }
			if (state != track.State) { _stateFlips++; flips++; }
			if (hands != track.Hands) { _handsFlips++; flips++; }
			if (anim != track.Anim) { _animFlips++; flips++; }
			if (step.sqrMagnitude > 0.0001f && track.Step.sqrMagnitude > 0.0001f && Vector3.Dot(step, track.Step) < 0f) { _reversals++; track.Reversals++; flips++; }
			track.Flips += flips;
			if (track.Flips > _worstFlips)
			{
				_worstFlips = track.Flips;
				string layer = "?";
				try { layer = bot != null ? BrainManager.GetActiveLayerName(bot) ?? "<none>" : "?"; } catch { }
				_worst = $"{player.Profile?.Nickname} flips {track.Flips} reversals {track.Reversals} layer={layer} state={state} sprint={sprint} pose={pose:F2} hands={hands?.Name}";
			}
		}
		track.MotionKnown = true;
		track.Sprint = sprint;
		track.Moving = moving;
		track.Pose = pose;
		track.State = state;
		track.Hands = hands;
		track.Anim = anim;
		if (step.sqrMagnitude > 0.0001f) track.Step = step;
	}

	private void TrackLayer(Player player, Track track)
	{
		BotOwner bot = player.AIData?.BotOwner;
		if (bot == null)
		{
			return;
		}
		string layer;
		try
		{
			layer = BrainManager.GetActiveLayerName(bot) ?? "<none>";
		}
		catch
		{
			return;
		}
		if (track.Layer == null)
		{
			track.Layer = layer;
			return;
		}
		if (layer == track.Layer)
		{
			return;
		}
		float now = Time.time;
		track.SwitchTimes.Add(now);
		track.SwitchNames.Add(track.Layer + ">" + layer);
		track.Layer = layer;
		for (int removed = 0; removed < 512 && track.SwitchTimes.Count > 0
			&& (now - track.SwitchTimes[0] > SwitchWindow || track.SwitchTimes.Count > 512); removed++)
		{
			track.SwitchTimes.RemoveAt(0);
			track.SwitchNames.RemoveAt(0);
		}
		if (track.SwitchTimes.Count >= SwitchBurst && now >= track.NextSwitchLog)
		{
			track.NextSwitchLog = now + 5f;
			_logged++;
			Plugin.Log.LogWarning($"[CivJump] {player.Profile?.Nickname} brain layer flip-flop: {track.SwitchTimes.Count} switches in {SwitchWindow:0}s — " + string.Join(", ", track.SwitchNames));
		}
	}

	private static string Describe(Player player, float moved, float dt, int jumps)
	{
		string layer = "?";
		string logic = "?";
		string mover = "?";
		string animator = "?";
		string cam = "?";
		BotOwner bot = player.AIData?.BotOwner;
		try
		{
			if (bot != null)
			{
				layer = BrainManager.GetActiveLayerName(bot) ?? "<none>";
				logic = BrainManager.GetActiveLogic(bot)?.GetType().Name ?? "<none>";
				mover = bot.Mover == null ? "no mover" : $"moving={bot.Mover.IsMoving} sprint={bot.Mover.Sprinting}";
			}
		}
		catch (Exception ex)
		{
			layer = "err:" + ex.GetType().Name;
		}
		try
		{
			animator = (player._animators != null && player._animators.Length != 0 && player._animators[0] != null) ? (player._animators[0].runtimeAnimatorController ? player._animators[0].runtimeAnimatorController.name : "<null>") : "<no animator>";
		}
		catch
		{
		}
		try
		{
			Camera main = Camera.main;
			if (main)
			{
				cam = Vector3.Distance(main.transform.position, player.Position).ToString("0");
			}
		}
		catch
		{
		}
		return $"{player.Profile?.Nickname} jumped {moved:0.00}m in one frame (dt={dt * 1000f:0}ms, jump #{jumps}) " + $"layer='{layer}' logic={logic} {mover} state={player.MovementContext?.CurrentState?.Name} " + $"pose={player.PoseLevel:0.00} animator={animator} camDist={cam}m";
	}
}
