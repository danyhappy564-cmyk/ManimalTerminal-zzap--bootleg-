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
		Sain, Near, Block, Cutscene, Civilian, Ruaf, Placement, Invalid, NoHuman, Type, Scope, Count
	}

	[Flags]
	internal enum HandsReason
	{
		None = 0, Missing = 1, Interaction = 2, CannotInteract = 4,
		SelectorMissing = 8, Changing = 16, MedicineMissing = 32, MedicineUsing = 64
	}

	private enum Kind { Scav, Goon, Boss, Cultist, Raider, Bloodhound, Ruaf, BlackDivision, Other }
	private static readonly Dictionary<WildSpawnType, Kind> Roles = new Dictionary<WildSpawnType, Kind>();

	// Native IsBoss/IamBoss also describes ordinary PMC leaders. Classify the role
	// once at registration; hot checks only read this enum and the live toggles.
	private static Kind Classify(WildSpawnType role)
	{
		if (Roles.TryGetValue(role, out Kind kind)) return kind;
		string name = TerminalRoles.Name(role);
		if (TerminalRoles.IsRuaf(role)) kind = Kind.Ruaf;
		else if (name.IndexOf("blackdiv", StringComparison.OrdinalIgnoreCase) >= 0) kind = Kind.BlackDivision;
		else if (role == WildSpawnType.bossKnight || role == WildSpawnType.followerBigPipe || role == WildSpawnType.followerBirdEye) kind = Kind.Goon;
		else if (role == WildSpawnType.cursedAssault || name.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("sectact", StringComparison.OrdinalIgnoreCase)) kind = Kind.Cultist;
		else if (role == WildSpawnType.pmcBot || role == WildSpawnType.exUsec) kind = Kind.Raider;
		else if (role == WildSpawnType.arenaFighter || role == WildSpawnType.arenaFighterEvent || role == WildSpawnType.crazyAssaultEvent) kind = Kind.Bloodhound;
		else if (role == WildSpawnType.marksman || role == WildSpawnType.assault || role == WildSpawnType.assaultGroup) kind = Kind.Scav;
		else if (name.StartsWith("boss", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("follower", StringComparison.OrdinalIgnoreCase)
			|| role == WildSpawnType.peacefullZryachiyEvent || role == WildSpawnType.ravangeZryachiyEvent
			|| role == WildSpawnType.infectedTagilla || role == WildSpawnType.tagillaHelperAgro) kind = Kind.Boss;
		else kind = Kind.Other;
		Roles[role] = kind;
		return kind;
	}

	internal static void PrepareRole(WildSpawnType role) { Classify(role); }

	private static bool Enabled(WildSpawnType role)
	{
		switch (Classify(role))
		{
			case Kind.Scav: return Plugin.GhostScavs.Value;
			case Kind.Goon: return Plugin.GhostGoons.Value;
			case Kind.Boss: return Plugin.GhostBosses.Value;
			case Kind.Cultist: return Plugin.GhostCultists.Value;
			case Kind.Raider: return Plugin.GhostRaiders.Value;
			case Kind.Bloodhound: return Plugin.GhostBloodhounds.Value;
			case Kind.Ruaf: return Plugin.GhostRuaf.Value;
			case Kind.BlackDivision: return Plugin.GhostBlackDivision.Value;
			default: return Plugin.GhostOthers.Value;
		}
	}

	internal static bool Cutscene => TerminalIntroCutscene.PlayingNow || TerminalAttackCutscene.PlayingNow || TerminalEndingCutscene.PlayingNow;

	internal static bool Protected(TerminalGhostDirector.BotRecord record) => ProtectionBlock(record) != BlockReason.None;

	private static BlockReason ProtectionBlock(TerminalGhostDirector.BotRecord record)
	{
		BotOwner bot = record.Bot;
		var settings = bot.Profile?.Info?.Settings;
		if (settings == null) return BlockReason.Invalid;
		if (CivilianConstants.IsCivilian(settings.Role)) return BlockReason.Civilian;
		// zzap: Terminal's distant population is mostly static RUAF posts (10/28 log:
		// 5 of 7 awake squads). Their defense layer only runs with a goal enemy, which
		// dormancy clears; neutrality/provocation live on the BotsGroup and survive sleep.
		if (TerminalRoles.IsRuaf(settings.Role) && !Plugin.GhostRuaf.Value) return BlockReason.Ruaf;
		if (!Enabled(settings.Role)) return BlockReason.Type;
		if (TerminalCrewJobs.GhostHasPendingOrder(bot.ProfileId) || bot.PriorityAxeTarget?.AllPursuit == true) return BlockReason.Crew;
		// Active rushes still block unless hidden movement can replay their existing
		// destination. Native pending orders/pursuit remain protected above.
		if (TerminalCrewJobs.ByProfile.TryGetValue(bot.ProfileId, out var job) && job != null
			&& job.Job != TerminalCrewJobs.Job.None && job.RushUntil > Time.time
			&& !TerminalGhostMovement.CanRepresent(bot, job)) return BlockReason.Crew;
		if (bot.SpawnBotZone != record.SpawnZone) return BlockReason.Zone;
		if (TerminalStageWait.OwnsVisibility(bot) || TerminalStageDirector.GhostShouldHold(record.SpawnZoneName)) return BlockReason.Stage;
		if (TerminalBotMigration.IsInFlight(bot) || TerminalPopulationDirector.GhostPlacementBusy(bot)) return BlockReason.Placement;
		return BlockReason.None;
	}

	internal static BlockReason SleepBlock(TerminalGhostDirector.BotRecord record, float now)
	{
		HandsReason previousHands = record.HandsState;
		record.HandsState = HandsReason.None;
		BotOwner bot = record.Bot;
		if (!bot || bot.IsDead || !bot.GetPlayer || bot.HealthController == null || !bot.HealthController.IsAlive) return BlockReason.Invalid;
		BlockReason protection = ProtectionBlock(record);
		if (protection != BlockReason.None) return protection;
		if (now - record.CreatedAt < 15f) return BlockReason.Young;
		if (now - record.LastDamageAt < 30f) return BlockReason.Damaged;
		if (bot.BotState != EBotState.Active || !bot.gameObject.activeInHierarchy || !bot.GetPlayer.gameObject.activeInHierarchy
			|| bot.Memory == null || bot.Brain?.Agent == null || bot.Mover == null || bot.EnemiesController == null) return BlockReason.NotActive;
		var hands = bot.GetPlayer.HandsController;
		HandsReason busy = HandsReason.None;
		if (hands == null) busy |= HandsReason.Missing;
		else
		{
			if (hands.IsInInteractionStrictCheck()) busy |= HandsReason.Interaction;
			if (!hands.CanInteract()) busy |= HandsReason.CannotInteract;
		}
		if (bot.WeaponManager?.Selector == null) busy |= HandsReason.SelectorMissing;
		else if (bot.WeaponManager.Selector.IsChanging) busy |= HandsReason.Changing;
		if (bot.Medecine == null) busy |= HandsReason.MedicineMissing;
		else if (bot.Medecine.Using) busy |= HandsReason.MedicineUsing;
		record.HandsState = busy;
		if (busy != previousHands) record.HandsSince = now;
		// A timer cannot distinguish a stuck animator from a real reload/heal.
		// Keep the guard; the report splits sustained blockers after 20 seconds.
		if (busy != HandsReason.None) return BlockReason.Hands;
		// Only current human combat blocks entry. Bot-vs-bot combat may pause.
		int enemies = 0;
		foreach (var pair in bot.EnemiesController.EnemyInfos)
		{
			if (++enemies > TerminalGhostDirector.MaxBots) return BlockReason.HumanFight;
			EnemyInfo enemy = pair.Value;
			if (enemy?.Person != null && !enemy.Person.IsAI && enemy.Person.HealthController?.IsAlive == true
				&& (enemy.IsVisible || enemy.CanShoot || (bot.Memory.GoalEnemy == enemy && now - enemy.TimeLastSeenReal < 30f))) return BlockReason.HumanFight;
		}
		return TerminalGhostPatches.HumanCombatSafe(record) ? BlockReason.None : BlockReason.Sain;
	}

	internal static bool NearHuman(BotOwner bot, List<Player> humans, float distanceSquared)
	{
		for (int i = 0; i < humans.Count && i < TerminalGhostDirector.MaxHumans; i++)
			if ((bot.Position - humans[i].Position).sqrMagnitude <= distanceSquared) return true;
		return false;
	}
}
