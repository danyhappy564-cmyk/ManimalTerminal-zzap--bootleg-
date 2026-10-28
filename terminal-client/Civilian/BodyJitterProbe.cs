using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

// zzap 10/28 5th raid, read-only diagnostics for two user reports:
//  - "civilians still tremble": [CivMotion] showed 0 root reversals, so the shaking is
//    in the pose, not the position. Sample the head relative to the root at end of
//    frame (after animation and IK) and count frame-to-frame direction flips of more
//    than 4mm, for civilians AND for other AI within 50m as a baseline. A civilian
//    rate far above the baseline points at the civilian animator/IK setup.
//  - "frames drop when I look toward the bots": bucket the frame time by how many
//    awake AI bodies EFT's own culler currently renders ([BotView]).
internal sealed class BodyJitterProbe : MonoBehaviour
{
	private sealed class Track
	{
		internal Vector3 Local, Delta;
		internal int Frame = -10;
		internal int Window = -1;
		internal int Flips;
	}

	private const float MinDelta = 0.004f;
	private const float Radius = 50f;

	private static readonly int[] BucketTop = { 2, 5, 10, int.MaxValue };
	private static readonly string[] BucketName = { "0-2", "3-5", "6-10", "11+" };

	private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
	private readonly double[] _bucketMs = new double[4];
	private readonly int[] _bucketFrames = new int[4];
	private int _civFrames, _civFlips, _botFrames, _botFlips, _peakVisible, _window, _worstFlips;
	private string _worst;
	private float _nextReport;

	internal static void Attach(GameObject host)
	{
		if (host && !host.GetComponent<BodyJitterProbe>()) host.AddComponent<BodyJitterProbe>();
	}

	private void Start()
	{
		StartCoroutine(EndOfFrame());
	}

	private IEnumerator EndOfFrame()
	{
		var wait = new WaitForEndOfFrame();
		while (true)
		{
			yield return wait;
			if (!TerminalGate.On || !Plugin.CivilianJumpProbe.Value) continue;
			try { Sample(); }
			catch { }
		}
	}

	private void Sample()
	{
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		List<Player> players = world?.AllAlivePlayersList;
		if (players == null) return;
		Camera camera = Camera.main;
		Vector3 eye = camera ? camera.transform.position : Vector3.zero;
		int frame = Time.frameCount;
		int visible = 0;
		for (int i = 0; i < players.Count && i < 512; i++)
		{
			Player player = players[i];
			if (player == null || !player.IsAI) continue;
			BotOwner bot = player.AIData?.BotOwner;
			if (TerminalGhostDirector.IsDormant(bot)) continue;
			if (TerminalGhostBody.CullerVisible(player)) visible++;
			if (!camera || (player.Position - eye).sqrMagnitude > Radius * Radius) continue;
			Transform head = player.PlayerBones?.Head?.Original;
			if (!head) continue;
			WildSpawnType? role = player.Profile?.Info?.Settings?.Role;
			bool civilian = role.HasValue && CivilianConstants.IsCivilian(role.Value);
			Vector3 local = head.position - player.Transform.Original.position;
			int id = player.GetInstanceID();
			if (!_tracks.TryGetValue(id, out Track track))
			{
				if (_tracks.Count < 512) _tracks[id] = new Track { Local = local, Frame = frame };
				continue;
			}
			Vector3 delta = local - track.Local;
			bool consecutive = frame - track.Frame == 1;
			track.Local = local;
			track.Frame = frame;
			if (!consecutive) { track.Delta = Vector3.zero; continue; }
			if (track.Window != _window) { track.Window = _window; track.Flips = 0; }
			bool flip = delta.sqrMagnitude > MinDelta * MinDelta && track.Delta.sqrMagnitude > MinDelta * MinDelta
				&& Vector3.Dot(delta, track.Delta) < 0f;
			track.Delta = delta;
			if (civilian) { _civFrames++; if (flip) _civFlips++; }
			else { _botFrames++; if (flip) _botFlips++; }
			if (!flip || !civilian) continue;
			track.Flips++;
			if (track.Flips <= _worstFlips) continue;
			_worstFlips = track.Flips;
			string layer = "?";
			try { if (bot != null) layer = BrainManager.GetActiveLayerName(bot) ?? "<none>"; } catch { }
			_worst = $"{player.Profile?.Nickname} flips {track.Flips} layer={layer} state={player.MovementContext?.CurrentState?.Name} pose={player.PoseLevel:F2} hands={player.HandsController?.GetType().Name} moving={(bot?.Mover != null && bot.Mover.IsMoving)}";
		}
		int bucket = 0;
		while (bucket < BucketTop.Length - 1 && visible > BucketTop[bucket]) bucket++;
		_bucketMs[bucket] += Time.unscaledDeltaTime * 1000.0;
		_bucketFrames[bucket]++;
		if (visible > _peakVisible) _peakVisible = visible;
		if (_nextReport == 0f) _nextReport = Time.realtimeSinceStartup + 30f;
		if (Time.realtimeSinceStartup < _nextReport) return;
		_nextReport = Time.realtimeSinceStartup + 30f;
		Plugin.Log.LogInfo($"[CivPose] last30s head flips >4mm per 100 frames: civilians {Rate(_civFlips, _civFrames)} ({_civFrames} frames), other AI {Rate(_botFlips, _botFrames)} ({_botFrames} frames); worst {_worst ?? "-"}");
		var text = new System.Text.StringBuilder("[BotView] last30s avg frame ms by rendered AI bodies:");
		for (int i = 0; i < BucketName.Length; i++)
			text.Append($" {BucketName[i]} {(_bucketFrames[i] == 0 ? "-" : (_bucketMs[i] / _bucketFrames[i]).ToString("F1"))} ({_bucketFrames[i]}f)");
		text.Append($"; peak {_peakVisible}");
		Plugin.Log.LogInfo(text.ToString());
		_civFrames = _civFlips = _botFrames = _botFlips = _peakVisible = _worstFlips = 0;
		_worst = null;
		for (int i = 0; i < _bucketMs.Length; i++) { _bucketMs[i] = 0; _bucketFrames[i] = 0; }
		_window++;
	}

	private static string Rate(int flips, int frames)
	{
		return frames == 0 ? "-" : (flips * 100f / frames).ToString("F1");
	}
}
