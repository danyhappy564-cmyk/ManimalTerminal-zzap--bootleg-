using System;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: the only simulated health writer. Scope the guard to this victim, including
// synchronous engine/mod callbacks; unrelated real damage must still wake its squad.
internal static class TerminalGhostCombatDamage
{
	[ThreadStatic] private static Player _victim;
	[ThreadStatic] private static int _depth;

	internal static bool IsSimulated(Player player)
	{
		return _depth > 0 && ReferenceEquals(_victim, player);
	}

	internal static bool Kill(TerminalGhostDirector.BotRecord victim, TerminalGhostDirector.BotRecord killer)
	{
		Player target = victim.Bot.GetPlayer;
		Player attacker = killer.Bot.GetPlayer;
		Weapon weapon = attacker.HandsController?.Item as Weapon;
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		var bridge = world?.GetAlivePlayerBridgeByProfileID(attacker.ProfileId);
		if (weapon == null || bridge?.iPlayer == null || !attacker.HealthController.IsAlive)
			throw new InvalidOperationException("Ghost killer has no live bridge/current weapon");
		Player previous = _victim;
		int previousDepth = _depth;
		_victim = target;
		_depth = previousDepth + 1;
		try
		{
			// Restore render flags AND saved BotState before EFT creates its corpse.
			// This record is no longer dormant, so LateUpdate cannot hide the corpse.
			TerminalGhostDirector.RestoreForCombatDeath(victim);
			var damage = new DamageInfo
			{
				DamageType = EDamageType.Bullet,
				Damage = Mathf.Max(500f, target.HealthController.GetBodyPartHealth(EBodyPart.Common).Maximum * 4f),
				Player = bridge,
				Weapon = weapon,
				Direction = (target.Position - attacker.Position).normalized,
				HitPoint = target.Position + Vector3.up * 1.5f,
				BleedBlock = true
			};
			// EFT.Player.ApplyDamageInfo (installed Assembly-CSharp) sets LastAggressor,
			// LastDamageInfo and LastBodyPart BEFORE ActiveHealthController.ApplyDamage.
			// OnDead then credits the kill and triggers the normal bot-removal ledger.
			bool head = UnityEngine.Random.value < 0.5f;
			target.ApplyDamageInfo(damage, head ? EBodyPart.Head : EBodyPart.Chest,
				head ? EBodyPartColliderType.HeadCommon : EBodyPartColliderType.RibcageUp, 0f);
			if (target.HealthController.IsAlive)
				target.ApplyDamageInfo(damage, head ? EBodyPart.Chest : EBodyPart.Head,
					head ? EBodyPartColliderType.RibcageUp : EBodyPartColliderType.HeadCommon, 0f);
			if (target.HealthController.IsAlive)
				throw new InvalidOperationException("Ghost lethal Player damage was rejected; fight cancelled");
			return true;
		}
		finally { _victim = previous; _depth = previousDepth; }
	}

	internal static bool Wound(TerminalGhostDirector.BotRecord victim, float damage)
	{
		Player target = victim.Bot.GetPlayer;
		var health = target.ActiveHealthController;
		if (health == null || !health.IsAlive) return false;
		Player previous = _victim;
		int previousDepth = _depth;
		_victim = target;
		_depth = previousDepth + 1;
		try
		{
			int first = UnityEngine.Random.Range(0, 4);
			for (int i = 0; i < 4; i++)
			{
				EBodyPart part;
				switch ((first + i) % 4)
				{
					case 0: part = EBodyPart.LeftArm; break;
					case 1: part = EBodyPart.RightArm; break;
					case 2: part = EBodyPart.LeftLeg; break;
					default: part = EBodyPart.RightLeg; break;
				}
				float before = health.GetBodyPartHealth(part).Current;
				float amount = Mathf.Min(damage, before - 1f);
				if (amount <= 0f) continue;
				// Installed ActiveHealthController.ChangeHealth only adjusts HP and
				// notifies HealthChangedEvent. No hit effects, bleeding or fractures.
				health.ChangeHealth(part, -amount, default);
				return health.GetBodyPartHealth(part).Current < before;
			}
			return false;
		}
		finally { _victim = previous; _depth = previousDepth; }
	}
}
