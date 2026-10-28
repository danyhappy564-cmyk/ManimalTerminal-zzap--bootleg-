using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

internal class TerminalGuardLogic : CustomLogic
{
	private float _next;

	public TerminalGuardLogic(BotOwner botOwner)
		: base(botOwner)
	{
	}

	public override void Update(CustomLayer.ActionData data)
	{
		// Same as TerminalHuntLogic: nothing else aims steering inside this logic.
		if (BotOwner.Mover != null && BotOwner.Mover.IsMoving)
		{
			BotOwner.Steering?.LookToMovingDirection();
		}
		if (Time.time < _next)
		{
			return;
		}
		_next = Time.time + Random.Range(6f, 12f);
		TerminalCrewJobs.Rec rec = TerminalCrewJobs.For(BotOwner);
		if (rec != null)
		{
			Bounds zone = rec.Zone;
			if (NavMesh.SamplePosition(new Vector3(Random.Range(zone.min.x, zone.max.x), zone.center.y, Random.Range(zone.min.z, zone.max.z)), out var hit, 4f, -1))
			{
				BotOwner.Mover?.SetTargetMoveSpeed(0.5f);
				BotOwner.Mover?.SetPose(1f);
				BotOwner.Mover?.GoToPoint(hit.position, slowAtTheEnd: true, 0.6f);
			}
		}
	}

	public override void Stop()
	{
		base.Stop();
		_next = 0f;
	}
}
