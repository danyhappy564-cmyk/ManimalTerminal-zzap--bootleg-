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

		internal int Frame;

		internal float NextLog;

		internal int Jumps;
	}

	private const float MaxPlausibleSpeed = 9f;

	private const float MinJump = 1.2f;

	private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

	private int _logged;

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
		if (!Plugin.CivilianJumpProbe.Value || _logged >= 200)
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
		for (int i = 0; i < players.Count; i++)
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
			if (!_tracks.TryGetValue(id, out var track))
			{
				_tracks[id] = new Track { Pos = pos, Frame = frame };
				continue;
			}
			float moved = Vector3.Distance(pos, track.Pos);
			int frames = Mathf.Max(1, frame - track.Frame);
			track.Pos = pos;
			track.Frame = frame;
			if (frames != 1 || moved < MinJump || moved < MaxPlausibleSpeed * dt * 2f)
			{
				continue;
			}
			track.Jumps++;
			if (Time.realtimeSinceStartup < track.NextLog)
			{
				continue;
			}
			track.NextLog = Time.realtimeSinceStartup + 2f;
			_logged++;
			Plugin.Log.LogWarning("[CivJump] " + Describe(player, moved, dt, track.Jumps));
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
