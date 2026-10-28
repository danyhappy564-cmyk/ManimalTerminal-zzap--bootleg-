using System;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal.Civilian.Behavior;

internal sealed class CivilianFleeLogic(BotOwner botOwner) : CustomLogic(botOwner)
{
	private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Terminal.Civ");

	private const float ArrivalDistance = 2.5f;

	private const float FleeConeDegrees = 75f;

	private Vector3 _destination;

	private bool _hasDestination;

	private bool _commandIssued;

	private bool _sprintStateKnown;

	private bool _sprintRequested;

	private readonly System.Random _rng = new System.Random();

	public override void Start()
	{
		_hasDestination = false;
		_commandIssued = false;
		_sprintStateKnown = false;
		CivilianFleeState.Get(BotOwner).MotionRequested = false;
		if (BotOwner != null)
		{
			SetSprint(sprint: true);
			BotOwner.Mover?.SetTargetMoveSpeed(1f);
			SetCrouch(crouch: false);
		}
	}

	public override void Stop()
	{
		if (BotOwner != null)
		{
			SetSprint(sprint: false);
			SetCrouch(crouch: false);
		}
		CivilianFleeState.State state = CivilianFleeState.Get(BotOwner);
		state.InCover = false;
		state.CoweringUntil = 0f;
		state.MotionRequested = false;
		_hasDestination = false;
		_commandIssued = false;
		CivilianUnstickHelper.Reset(BotOwner);
	}

	public override void Update(CustomLayer.ActionData data)
	{
		if (BotOwner == null || BotOwner.Mover == null)
		{
			return;
		}
		CivilianDoorHelper.CheckAndOpenNearbyDoor(BotOwner);
		CivilianMeleeEnforcer.Tick(BotOwner);
		CivilianFleeState.State state = CivilianFleeState.Get(BotOwner);
		Vector3 position = BotOwner.Position;
		if (state.InCover && state.CoweringUntil <= Time.time)
		{
			state.InCover = false;
			state.CoweringUntil = 0f;
			state.MotionRequested = false;
			_hasDestination = false;
			_commandIssued = false;
			SetCrouch(crouch: false);
		}
		if (state.InCover && state.CoweringUntil > Time.time)
		{
			state.MotionRequested = false;
			return;
		}
		CivilianThreatScanner.ThreatResult threatResult = CivilianThreatScanner.FindNearestThreat(BotOwner, 25f);
		if (threatResult.HasThreat)
		{
			Vector3 threatPosition = threatResult.ThreatPosition;
			if (state.InCover)
			{
				state.InCover = false;
				state.CoweringUntil = 0f;
				_hasDestination = false;
				SetCrouch(crouch: false);
				state.MotionRequested = false;
			}
			SetSprint(sprint: true);
			if (!_hasDestination || ArrivedAt(_destination, position))
			{
				PickRandomFleeDestination(threatPosition, position);
			}
			IssueMoveIfNeeded();
			CheckStuck();
		}
		else if (_hasDestination && ArrivedAt(_destination, position))
		{
			state.MotionRequested = false;
			if (!state.InCover)
			{
				SetSprint(sprint: false);
				SetCrouch(crouch: true);
				state.InCover = true;
				state.CoweringUntil = Time.time + 5f;
			}
		}
		else if (_hasDestination)
		{
			SetSprint(sprint: false);
			IssueMoveIfNeeded();
			CheckStuck();
		}
	}

	private void CheckStuck()
	{
		if (_hasDestination && CivilianUnstickHelper.Check(BotOwner, _destination) == CivilianUnstickHelper.Status.NeedsRepath)
		{
			_hasDestination = false;
			_commandIssued = false;
			CivilianFleeState.Get(BotOwner).MotionRequested = false;
		}
	}

	private void IssueMoveIfNeeded()
	{
		if (_hasDestination && !_commandIssued)
		{
			if (BotOwner.Mover.GoToPoint(_destination, slowAtTheEnd: false, 1f, mustHaveWay: false) == NavMeshPathStatus.PathInvalid)
			{
				_hasDestination = false;
				CivilianFleeState.Get(BotOwner).MotionRequested = false;
			}
			else
			{
				_commandIssued = true;
				CivilianFleeState.Get(BotOwner).MotionRequested = true;
			}
		}
	}

	private void PickRandomFleeDestination(Vector3 threatPos, Vector3 botPos)
	{
		Vector3 vector = botPos - threatPos;
		vector.y = 0f;
		if (vector.sqrMagnitude < 0.01f)
		{
			vector = new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f);
		}
		Vector3 normalized = vector.normalized;
		Vector3 vector2 = Quaternion.AngleAxis(((float)_rng.NextDouble() * 2f - 1f) * 75f, Vector3.up) * normalized;
		float num = 12f + (float)_rng.NextDouble() * 10f;
		Vector3 vector3 = botPos + vector2 * num;
		Vector3 vector4 = (BotOwner.Covers?.FindClosestPoint(vector3, 0f, threatPos))?.Position ?? vector3;
		CivilianFleeState.State state = CivilianFleeState.Get(BotOwner);
		if (state.HasHome)
		{
			Vector3 homePosition = state.HomePosition;
			Vector3 vector5 = vector4 - homePosition;
			if (vector5.magnitude > 30f)
			{
				vector4 = homePosition + vector5.normalized * 30f;
			}
		}
		// zzap: the home clamp can leave the cover point; keep the goal on the mesh
		// (GoToPoint runs with mustHaveWay=false so a miss re-picks instead of teleporting).
		if (NavMesh.SamplePosition(vector4, out NavMeshHit hit, 2f, NavMesh.AllAreas))
		{
			vector4 = hit.position;
		}
		_destination = vector4;
		_hasDestination = true;
		_commandIssued = false;
	}

	private static bool ArrivedAt(Vector3 dest, Vector3 botPos)
	{
		Vector3 vector = dest - botPos;
		vector.y = 0f;
		return vector.sqrMagnitude < 6.25f;
	}

	private void SetCrouch(bool crouch)
	{
		(BotOwner?.GetPlayer?.MovementContext)?.SetPoseLevel(crouch ? 0f : 1f);
	}

	private void SetSprint(bool sprint)
	{
		if (!(BotOwner == null) && (!_sprintStateKnown || _sprintRequested != sprint))
		{
			BotOwner.Sprint(sprint);
			_sprintRequested = sprint;
			_sprintStateKnown = true;
		}
	}
}
