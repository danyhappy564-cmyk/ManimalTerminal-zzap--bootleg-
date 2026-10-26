using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

internal class TerminalHuntLogic : CustomLogic
{
	private float _next;

	private float _nextRushLog;

	private readonly Vector3 _approachOffset;

	public TerminalHuntLogic(BotOwner botOwner)
		: base(botOwner)
	{
		uint num = (uint)(botOwner?.ProfileId?.GetHashCode() ?? botOwner?.Id ?? 0);
		float f = (float)(num % 3600) * 0.0017453292f;
		float num2 = 5f + (float)(num / 3600 % 4) * 1.5f;
		_approachOffset = new Vector3(Mathf.Cos(f) * num2, 0f, Mathf.Sin(f) * num2);
	}

	public override void Update(CustomLayer.ActionData data)
	{
		if (Time.time < _next)
		{
			return;
		}
		_next = Time.time + (TerminalSainCompat.Detected ? 1.25f : 3f);
		TerminalCrewJobs.Rec rec = TerminalCrewJobs.For(BotOwner);
		Vector3? vector = rec?.RushTo;
		if (vector.HasValue)
		{
			Vector3 valueOrDefault = vector.GetValueOrDefault();
			float sqrMagnitude = (valueOrDefault - BotOwner.Position).sqrMagnitude;
			if (sqrMagnitude < 196f)
			{
				rec.RushUntil = 0f;
				try
				{
					BotOwner.Sprint(val: false);
					return;
				}
				catch
				{
					return;
				}
			}
			if (Time.time >= _nextRushLog)
			{
				_nextRushLog = Time.time + 20f;
				Plugin.Log.LogDebug($"[CrewLayer] {BotOwner.name}: rushing, {Mathf.Sqrt(sqrMagnitude):0}m to anchor");
			}
			bool val = sqrMagnitude > 100f;
			BotOwner.Mover?.SetPose(1f);
			BotOwner.Mover?.SetTargetMoveSpeed(1f);
			try
			{
				BotOwner.Sprint(val);
			}
			catch
			{
			}
			BotOwner.Mover?.GoToPoint(valueOrDefault, slowAtTheEnd: true, 6f);
			return;
		}
		Player player = TerminalCoop.NearestHuman(BotOwner.Position);
		if ((bool)player && player.HealthController != null && player.HealthController.IsAlive)
		{
			Vector3 vector2 = player.Position + _approachOffset;
			if (NavMesh.SamplePosition(vector2, out var hit, 3f, -1))
			{
				vector2 = hit.position;
			}
			bool flag = (player.Position - BotOwner.Position).sqrMagnitude > 225f;
			BotOwner.Mover?.SetPose(1f);
			BotOwner.Mover?.SetTargetMoveSpeed(flag ? 1f : 0.85f);
			try
			{
				BotOwner.Sprint(flag);
			}
			catch
			{
			}
			BotOwner.Mover?.GoToPoint(vector2, slowAtTheEnd: true, 2.5f);
		}
	}

	public override void Stop()
	{
		base.Stop();
		try
		{
			BotOwner.Sprint(val: false);
		}
		catch
		{
		}
		_next = 0f;
	}
}
