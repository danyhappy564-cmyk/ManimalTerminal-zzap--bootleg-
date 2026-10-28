using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal class TerminalRuafDefenseLogic : CustomLogic
{
	private float _next;

	private float _nextHeal;

	private CustomNavigationPoint _claim;

	public TerminalRuafDefenseLogic(BotOwner botOwner)
		: base(botOwner)
	{
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
		_next = Time.time + 3f;
		try
		{
			Vector3 anchor = (BotOwner.BotFollower?.BossToFollow)?.Position ?? BotOwner.Position;
			Vector3 valueOrDefault = (BotOwner.Memory?.GoalEnemy?.EnemyLastPosition).GetValueOrDefault(anchor);
			if (_claim == null || (_claim.Position - anchor).sqrMagnitude > 900f)
			{
				Release();
				CustomNavigationPoint customNavigationPoint = BotOwner.Covers?.FindClosestPoint(anchor, 3f, valueOrDefault, noRestrictions: false, (GroupPoint gp) => gp.IsFreeById(BotOwner.Id) && (gp.Position - anchor).sqrMagnitude <= 900f);
				if (customNavigationPoint != null && customNavigationPoint.IsFreeById(BotOwner.Id))
				{
					customNavigationPoint.SetOwner(BotOwner);
					_claim = customNavigationPoint;
				}
			}
			Vector3 vector = ((_claim != null) ? _claim.Position : anchor);
			if ((vector - BotOwner.Position).sqrMagnitude > 2.25f)
			{
				BotOwner.Mover?.SetPose(1f);
				BotOwner.Mover?.SetTargetMoveSpeed(0.9f);
				BotOwner.Mover?.GoToPoint(vector, slowAtTheEnd: true, 1f);
				return;
			}
			BotOwner.Mover?.SetPose(0.6f);
			BotOwner.Steering?.LookToPoint(valueOrDefault + Vector3.up * 1.5f);
			if (Time.time >= _nextHeal && BotOwner.Memory != null && !BotOwner.Memory.IsUnderFire)
			{
				BotFirstAid botFirstAid = BotOwner.Medecine?.FirstAid;
				if (botFirstAid != null && botFirstAid.Have2Do)
				{
					_nextHeal = Time.time + 25f;
					botFirstAid.TryApplyToCurrentPart();
				}
			}
		}
		catch
		{
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
	}

	public override void Stop()
	{
		base.Stop();
		Release();
		_next = 0f;
	}
}
