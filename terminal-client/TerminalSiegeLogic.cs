using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

internal class TerminalSiegeLogic : CustomLogic
{
	private float _next;

	private float _reroll;

	private Vector3 _offset;

	private CustomNavigationPoint _claim;

	private bool _arrived;

	private static float _quietUntil;

	private bool InteriorBot => BotOwner.Id % 3 == 0;

	public TerminalSiegeLogic(BotOwner botOwner)
		: base(botOwner)
	{
		RollOffset();
	}

	private void RollOffset()
	{
		Vector2 vector = UnityEngine.Random.insideUnitCircle * 15f;
		_offset = new Vector3(vector.x, 0f, vector.y);
		_reroll = Time.time + UnityEngine.Random.Range(45f, 90f);
	}

	public override void Update(CustomLayer.ActionData data)
	{
		// Same as TerminalHuntLogic: face the path while walking; the arrival look stays.
		if (BotOwner.Mover != null && BotOwner.Mover.IsMoving)
		{
			BotOwner.Steering?.LookToMovingDirection();
		}
		if (Time.time < _next)
		{
			return;
		}
		_next = Time.time + 4f;
		TerminalCrewJobs.Rec rec = TerminalCrewJobs.For(BotOwner);
		if (rec == null)
		{
			return;
		}
		Bounds armory = rec.Zone;
		try
		{
			if (Time.time >= _reroll)
			{
				RollOffset();
				Release();
			}
			if (_claim == null)
			{
				_arrived = false;
				BotCoversData covers = BotOwner.Covers;
				CustomNavigationPoint customNavigationPoint = null;
				if (covers != null)
				{
					if (InteriorBot)
					{
						customNavigationPoint = covers.FindHidePoint(armory.center, 0f, null, onlyWithInsideCover: true);
					}
					if (customNavigationPoint == null || !armory.Contains(customNavigationPoint.Position) || !customNavigationPoint.IsFreeById(BotOwner.Id))
					{
						customNavigationPoint = covers.FindClosestPoint(armory.center + _offset, 0f, armory.center, noRestrictions: false, (GroupPoint gp) => armory.Contains(gp.Position) && gp.IsFreeById(BotOwner.Id));
					}
				}
				if (customNavigationPoint != null && armory.Contains(customNavigationPoint.Position) && customNavigationPoint.IsFreeById(BotOwner.Id))
				{
					customNavigationPoint.SetOwner(BotOwner);
					_claim = customNavigationPoint;
				}
			}
			NavMeshHit hit;
			if (_claim != null)
			{
				if ((_claim.Position - BotOwner.Position).sqrMagnitude > 2.25f)
				{
					_arrived = false;
					BotOwner.Mover?.SetPose(1f);
					BotOwner.Mover?.SetTargetMoveSpeed(0.55f);
					BotOwner.Mover?.GoToPoint(_claim.Position, slowAtTheEnd: true, 1f);
				}
				else if (!_arrived)
				{
					_arrived = true;
					Vector3 vector = _claim.Position - armory.center;
					vector.y = 0f;
					if (vector.sqrMagnitude < 1f)
					{
						vector = BotOwner.LookDirection;
					}
					BotOwner.Steering?.LookToPoint(_claim.Position + vector.normalized * 20f + Vector3.up * 1.5f);
				}
			}
			else if (NavMesh.SamplePosition(new Vector3(UnityEngine.Random.Range(armory.min.x, armory.max.x), armory.center.y, UnityEngine.Random.Range(armory.min.z, armory.max.z)), out hit, 4f, -1))
			{
				BotOwner.Mover?.SetPose(1f);
				BotOwner.Mover?.SetTargetMoveSpeed(0.5f);
				BotOwner.Mover?.GoToPoint(hit.position, slowAtTheEnd: true, 0.6f);
			}
		}
		catch (Exception ex)
		{
			if (Time.time > _quietUntil)
			{
				_quietUntil = Time.time + 60f;
				Plugin.Log.LogWarning("[CrewLayer] siege logic error: " + ex.Message);
			}
		}
	}

	private void Release()
	{
		try
		{
			_claim?.SetFree();
		}
		catch
		{
		}
		_claim = null;
		_arrived = false;
	}

	public override void Stop()
	{
		base.Stop();
		Release();
		_next = 0f;
	}
}
