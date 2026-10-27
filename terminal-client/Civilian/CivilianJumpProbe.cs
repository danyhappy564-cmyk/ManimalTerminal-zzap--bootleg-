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
	}

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
			track.Pos = pos;
			track.RootPos = root;
			track.Frame = frame;
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
			Plugin.Log.LogInfo($"[CivJumpNear] last30s cam<=50m one-frame steps>=0.2m: events {_nearSteps}, bots {_nearBots}, rootSteps {_nearRootSteps}, bodyOnly {_nearSteps - _nearRootSteps}");
			_nearSteps = _nearBots = _nearRootSteps = 0;
			_nearWindow++;
		}
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
