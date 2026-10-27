using System;
using System.Collections.Generic;
using EFT;
using Manimal.Terminal.Civilian;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: reads existing Terminal ownership; never assigns missions or changes sides.
internal static class TerminalGhostPolicy
{
	internal enum BlockReason
	{
		None, Boss, Crew, Zone, Stage, Young, Damaged, NotActive, Hands, HumanFight,
		Sain, Near, Block, Cutscene, Civilian, Ruaf, Placement, Invalid, NoHuman, Count
	}

	private static readonly Dictionary<WildSpawnType, bool> BossRoles = new Dictionary<WildSpawnType, bool>();

	// EFT's IsBoss/IsFollower and BotBoss.IamBoss also describe ordinary PMC squads.
	// Cache actual boss/cultist roles, including registered custom boss/follower names.
	private static bool IsBossRole(WildSpawnType role)
	{
		if (BossRoles.TryGetValue(role, out bool boss)) return boss;
		string name = TerminalRoles.Name(role);
		boss = name.StartsWith("boss", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("follower", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("sectact", StringComparison.OrdinalIgnoreCase)
			|| role == WildSpawnType.peacefullZryachiyEvent || role == WildSpawnType.ravangeZryachiyEvent
			|| role == WildSpawnType.infectedTagilla || role == WildSpawnType.tagillaHelperAgro;
		BossRoles[role] = boss;
		return boss;
	}

	internal static void PrepareRole(WildSpawnType role) { IsBossRole(role); }

	internal static bool Cutscene => TerminalIntroCutscene.PlayingNow || TerminalAttackCutscene.PlayingNow || TerminalEndingCutscene.PlayingNow;

	internal static bool Protected(TerminalGhostDirector.BotRecord record) => ProtectionBlock(record) != BlockReason.None;

	private static BlockReason ProtectionBlock(TerminalGhostDirector.BotRecord record)
	{
		BotOwner bot = record.Bot;
		var settings = bot.Profile?.Info?.Settings;
		if (settings == null) return BlockReason.Invalid;
		if (CivilianConstants.IsCivilian(settings.Role)) return BlockReason.Civilian;
		if (TerminalRoles.IsRuaf(settings.Role)) return BlockReason.Ruaf;
		if (IsBossRole(settings.Role)) return BlockReason.Boss;
		if (TerminalCrewJobs.GhostHasPendingOrder(bot.ProfileId) || bot.PriorityAxeTarget?.AllPursuit == true) return BlockReason.Crew;
		if (TerminalCrewJobs.ByProfile.TryGetValue(bot.ProfileId, out var job) && job != null
			&& (job.Job != TerminalCrewJobs.Job.None || job.RushTo.HasValue || job.RushUntil > Time.time)) return BlockReason.Crew;
		if (bot.SpawnBotZone != record.SpawnZone) return BlockReason.Zone;
		if (TerminalStageWait.OwnsVisibility(bot) || TerminalStageDirector.GhostShouldHold(record.SpawnZoneName)) return BlockReason.Stage;
		if (TerminalBotMigration.IsInFlight(bot) || TerminalPopulationDirector.GhostPlacementBusy(bot)) return BlockReason.Placement;
		return BlockReason.None;
	}

	internal static BlockReason SleepBlock(TerminalGhostDirector.BotRecord record, float now)
	{
		BotOwner bot = record.Bot;
		if (!bot || bot.IsDead || !bot.GetPlayer || bot.HealthController == null || !bot.HealthController.IsAlive) return BlockReason.Invalid;
		BlockReason protection = ProtectionBlock(record);
		if (protection != BlockReason.None) return protection;
		if (now - record.CreatedAt < 15f) return BlockReason.Young;
		if (now - record.LastDamageAt < 30f) return BlockReason.Damaged;
		if (bot.BotState != EBotState.Active || !bot.gameObject.activeInHierarchy || !bot.GetPlayer.gameObject.activeInHierarchy
			|| bot.Memory == null || bot.Brain?.Agent == null || bot.Mover == null || bot.EnemiesController == null) return BlockReason.NotActive;
		var hands = bot.GetPlayer.HandsController;
		if (hands == null || hands.IsInInteractionStrictCheck() || !hands.CanInteract()
			|| bot.WeaponManager?.Selector == null || bot.WeaponManager.Selector.IsChanging || bot.Medecine == null || bot.Medecine.Using) return BlockReason.Hands;
		// Only current human combat blocks entry. Bot-vs-bot combat may pause.
		foreach (var pair in bot.EnemiesController.EnemyInfos)
		{
			EnemyInfo enemy = pair.Value;
			if (enemy?.Person != null && !enemy.Person.IsAI && enemy.Person.HealthController?.IsAlive == true
				&& (enemy.IsVisible || enemy.CanShoot || (bot.Memory.GoalEnemy == enemy && now - enemy.TimeLastSeenReal < 30f))) return BlockReason.HumanFight;
		}
		return TerminalGhostPatches.HumanCombatSafe(record) ? BlockReason.None : BlockReason.Sain;
	}

	internal static bool NearHuman(BotOwner bot, List<Player> humans, float distanceSquared)
	{
		for (int i = 0; i < humans.Count; i++)
			if ((bot.Position - humans[i].Position).sqrMagnitude <= distanceSquared) return true;
		return false;
	}
}
