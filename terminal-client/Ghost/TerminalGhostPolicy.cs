using System;
using System.Collections.Generic;
using EFT;
using Manimal.Terminal.Civilian;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: reads existing Terminal ownership; never assigns missions or changes sides.
internal static class TerminalGhostPolicy
{
	internal static bool Cutscene => TerminalIntroCutscene.PlayingNow || TerminalAttackCutscene.PlayingNow || TerminalEndingCutscene.PlayingNow;

	internal static bool Protected(TerminalGhostDirector.BotRecord record)
	{
		BotOwner bot = record.Bot;
		var settings = bot.Profile?.Info?.Settings;
		if (settings == null || CivilianConstants.IsCivilian(settings.Role) || TerminalRoles.IsRuaf(settings.Role)) return true;
		if (bot.Boss == null || bot.Boss.IamBoss || bot.BotsGroup?.BossGroup != null || bot.BotFollower?.BossToFollow != null) return true;
		if (TerminalCrewJobs.GhostHasPendingOrder(bot.ProfileId) || bot.PriorityAxeTarget?.AllPursuit == true) return true;
		if (TerminalCrewJobs.ByProfile.TryGetValue(bot.ProfileId, out var job) && job != null
			&& (job.Job != TerminalCrewJobs.Job.None || job.RushTo.HasValue || job.RushUntil > Time.time)) return true;
		return bot.SpawnBotZone != record.SpawnZone || TerminalStageWait.OwnsVisibility(bot) || TerminalStageDirector.GhostShouldHold(record.SpawnZoneName)
			|| TerminalBotMigration.IsInFlight(bot) || TerminalPopulationDirector.GhostPlacementBusy(bot);
	}

	internal static bool CanSleep(TerminalGhostDirector.BotRecord record, float now)
	{
		BotOwner bot = record.Bot;
		if (!bot || bot.IsDead || !bot.GetPlayer || bot.HealthController == null || !bot.HealthController.IsAlive) return false;
		if (now - record.CreatedAt < 15f || now - record.LastDamageAt < 30f || Protected(record)) return false;
		if (bot.BotState != EBotState.Active || !bot.gameObject.activeInHierarchy || !bot.GetPlayer.gameObject.activeInHierarchy
			|| bot.Memory == null || bot.Brain?.Agent == null || bot.Mover == null) return false;
		var hands = bot.GetPlayer.HandsController;
		if (hands == null || hands.IsInInteractionStrictCheck() || !hands.CanInteract()
			|| bot.WeaponManager?.Selector == null || bot.WeaponManager.Selector.IsChanging || bot.Medecine == null || bot.Medecine.Using) return false;
		// Only current human combat blocks entry. Bot-vs-bot combat may pause.
		foreach (var pair in bot.EnemiesController.EnemyInfos)
		{
			EnemyInfo enemy = pair.Value;
			if (enemy?.Person != null && !enemy.Person.IsAI && enemy.Person.HealthController.IsAlive
				&& (enemy.IsVisible || enemy.CanShoot || (bot.Memory.GoalEnemy == enemy && now - enemy.TimeLastSeenReal < 30f))) return false;
		}
		return TerminalGhostPatches.HumanCombatSafe(record);
	}

	internal static bool NearHuman(BotOwner bot, List<Player> humans, float distanceSquared)
	{
		for (int i = 0; i < humans.Count; i++)
			if ((bot.Position - humans[i].Position).sqrMagnitude <= distanceSquared) return true;
		return false;
	}
}
