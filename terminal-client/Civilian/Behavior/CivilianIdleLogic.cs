using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal.Civilian.Behavior;

internal sealed class CivilianIdleLogic : CustomLogic
{
	private const float MinPauseSeconds = 6f;

	private const float MaxPauseSeconds = 15f;

	private const float WanderMinDistance = 3f;

	private const float WanderMaxDistance = 8f;

	private const float ArrivalDistance = 1.75f;

	private Vector3 _target;

	private bool _hasTarget;

	private float _nextMoveAt;

	private readonly System.Random _rng = new System.Random();

	public CivilianIdleLogic(BotOwner botOwner)
		: base(botOwner)
	{
		_nextMoveAt = Time.time + RandomRange(6f, 15f);
	}

	public override void Stop()
	{
		if (BotOwner != null)
		{
			BotOwner.Sprint(val: false);
		}
		CivilianFleeState.Get(BotOwner).MotionRequested = false;
		_hasTarget = false;
	}

	public override void Update(CustomLayer.ActionData data)
	{
		if (BotOwner == null || BotOwner.Mover == null)
		{
			return;
		}
		CivilianDoorHelper.CheckAndOpenNearbyDoor(BotOwner);
		CivilianMeleeEnforcer.Tick(BotOwner);
		Vector3 position = BotOwner.Position;
		if (_hasTarget && Vector3.Distance(position, _target) < 1.75f)
		{
			_hasTarget = false;
			_nextMoveAt = Time.time + RandomRange(6f, 15f);
			CivilianFleeState.Get(BotOwner).MotionRequested = false;
		}
		if (_hasTarget || Time.time < _nextMoveAt)
		{
			return;
		}
		float y = (float)_rng.NextDouble() * 360f;
		float num = RandomRange(3f, 8f);
		Vector3 vector = Quaternion.Euler(0f, y, 0f) * Vector3.forward * num;
		Vector3 vector2 = position + vector;
		CivilianFleeState.State state = CivilianFleeState.Get(BotOwner);
		if (state.HasHome)
		{
			Vector3 vector3 = vector2 - state.HomePosition;
			if (vector3.magnitude > 30f)
			{
				vector2 = state.HomePosition + vector3.normalized * 30f;
			}
		}
		// zzap: a random 3-8m point is often inside geometry or off the mesh. With the
		// default mustHaveWay=true, BotPathFinderCorePoints.GoToPosition answers a failed
		// path with Player.Teleport to the nearest cover point — the 0.5-10m civilian
		// "jumps" ([CivTeleport] stack, 10/28 15시). Snap to the mesh and never teleport.
		if (NavMesh.SamplePosition(vector2, out NavMeshHit hit, 2f, NavMesh.AllAreas))
		{
			vector2 = hit.position;
		}
		if (BotOwner.Mover.GoToPoint(vector2, slowAtTheEnd: false, 1f, mustHaveWay: false) != NavMeshPathStatus.PathInvalid)
		{
			_target = vector2;
			_hasTarget = true;
			CivilianFleeState.Get(BotOwner).MotionRequested = true;
		}
		else
		{
			CivilianFleeState.Get(BotOwner).MotionRequested = false;
			_nextMoveAt = Time.time + 2f;
		}
	}

	private float RandomRange(float min, float max)
	{
		return min + (float)_rng.NextDouble() * (max - min);
	}
}
