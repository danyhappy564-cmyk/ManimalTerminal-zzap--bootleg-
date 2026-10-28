using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalRuafNeutral
{
	// zzap: humans who have shot or killed a RUAF soldier this raid. The design is
	// "neutral until you draw blood", but the enemy-add filter below only let three
	// specific EBotEnemyCause values through, and the hit/kill reactions (vanilla and
	// SAIN) do not always use those causes — so RUAF never turned on a player who shot
	// or killed them, and could be looted for free (10/26). Drawing blood now marks the
	// shooter as hostile to every RUAF group for the rest of the raid.
	private static readonly HashSet<string> Provoked = new HashSet<string>();
	// Groups already reported once, so a rejecting group does not spam every second.
	private static readonly HashSet<BotsGroup> Reported = new HashSet<BotsGroup>();
	// Read and reset by the Ghost 30s report ([GhostShoot] line).
	internal static int BlockedAdds;

	internal static void ResetForRaid()
	{
		Provoked.Clear();
		Reported.Clear();
	}

	// Reused by the population birth callback and the 1s native-list reconciliation,
	// so every living RUAF bot is re-verified each second while a traitor is alive.
	internal static void OnBotCreated(BotOwner bot)
	{
		try { InheritProvocation(bot); }
		catch (Exception ex) { Plugin.Log.LogDebug("[RuafNeutral] new group inheritance failed: " + ex.Message); }
	}

	// zzap: the group-level check alone was not enough (10/28 15시): RUAF born after the
	// betrayal never logged an inheritance and stared at the player without firing.
	// A group can hold the enemy while a member activated later never received it in
	// its own BotMemory, and SAIN only builds enemies from each bot's EnemyInfos.
	// Verify group AND member every pass; only cheap dictionary lookups when all is set.
	private static int InheritProvocation(BotOwner bot)
	{
		if (!TerminalGate.On || !Plugin.RuafNeutral.Value || Provoked.Count == 0 || !bot || bot.IsDead || bot.BotState != EBotState.Active) return 0;
		var settings = bot.Profile?.Info?.Settings;
		BotsGroup group = bot.BotsGroup;
		if (settings == null || !TerminalRoles.IsRuaf(settings.Role) || group == null || bot.EnemiesController?.EnemyInfos == null || bot.Memory == null) return 0;
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		List<Player> players = world?.AllAlivePlayersList;
		if (players == null) return 0;
		int groupAdded = 0, memberAdded = 0, rejected = 0;
		for (int i = 0; i < players.Count && i < 512; i++)
		{
			Player human = players[i];
			if (!human || human.IsAI || human.HealthController?.IsAlive != true || human.ProfileId == null || !Provoked.Contains(human.ProfileId)) continue;
			if (!group.Enemies.TryGetValue(human, out var info) || info == null)
			{
				using var scope = TerminalTickProfiler.Measure("Ruaf.InheritProvocation");
				group.Allies.Remove(human);
				try { group.AddEnemy(human, EBotEnemyCause.followGetHit); }
				catch { }
				if (!group.Enemies.TryGetValue(human, out info) || info == null) { rejected++; continue; }
				groupAdded++;
			}
			if (!bot.EnemiesController.EnemyInfos.ContainsKey(human))
			{
				using var scope = TerminalTickProfiler.Measure("Ruaf.InheritProvocation");
				bot.Memory.AddEnemy(human, info, false);
				if (bot.EnemiesController.EnemyInfos.ContainsKey(human)) memberAdded++;
				else rejected++;
			}
		}
		if ((groupAdded > 0 || memberAdded > 0 || rejected > 0) && Reported.Add(group))
			Plugin.Log.LogInfo($"[RuafNeutral] RUAF group hostile to provoked human(s): role={TerminalRoles.Name(settings.Role)}, groupAdded {groupAdded}, memberAdded {memberAdded}, rejected {rejected}");
		return groupAdded + memberAdded;
	}

	[HarmonyPatch(typeof(BotsGroup), "AddEnemy")]
	internal static class Patch_RuafNeutralToHumans
	{
		[HarmonyPrefix]
		private static bool Prefix(BotsGroup __instance, IPlayer person, EBotEnemyCause cause, ref bool __result)
		{
			try
			{
				if (!TerminalGate.On || !Plugin.RuafNeutral.Value)
				{
					return true;
				}
				if (person == null || person.IsAI)
				{
					return true;
				}
				// zzap perf: cached role test instead of Role.ToString() per AddEnemy
				var settings = __instance?._initialBot?.Profile?.Info?.Settings;
				if (settings == null || !TerminalRoles.IsRuaf(settings.Role))
				{
					return true;
				}
				if (person.ProfileId != null && Provoked.Contains(person.ProfileId))
				{
					return true;
				}
				string text = TerminalRoles.Name(settings.Role);
				if (cause == EBotEnemyCause.followGetHit || cause == EBotEnemyCause.byKill || cause == EBotEnemyCause.death)
				{
					return true;
				}
				BlockedAdds++;
				Plugin.Log.LogDebug($"[RuafNeutral] blocked enemy-add: role='{text}' cause={cause} target=human");
				__result = false;
				return false;
			}
			catch
			{
				return true;
			}
		}
	}

	// a human damaged a RUAF soldier -> that human is hostile to all RUAF, now
	[HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage")]
	internal static class Patch_RuafProvokedByDamage
	{
		[HarmonyPostfix]
		private static void Postfix(ActiveHealthController __instance, DamageInfo damageInfo)
		{
			try
			{
				if (!TerminalGate.On || !Plugin.RuafNeutral.Value)
				{
					return;
				}
				using var scope = TerminalTickProfiler.Measure("Ruaf.ProvokedByDamage");
				Player victim = __instance?.Player;
				var victimSettings = victim?.Profile?.Info?.Settings;
				if (victimSettings == null || !TerminalRoles.IsRuaf(victimSettings.Role))
				{
					return;
				}
				IPlayer attacker = damageInfo.Player?.iPlayer;
				if (attacker == null || attacker.IsAI || attacker.ProfileId == null)
				{
					return;
				}
				bool first = Provoked.Add(attacker.ProfileId);
				// A repeated hit also repairs any RUAF that a late spawn left neutral.
				Reported.Clear();
				int groups = TurnRuafOn();
				if (first || groups > 0) Plugin.Log.LogInfo($"[RuafNeutral] {attacker.Profile?.Nickname} drew RUAF blood ({TerminalRoles.Name(victimSettings.Role)}) — hostile to {groups} RUAF group(s) for the rest of the raid");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogDebug("[RuafNeutral] provocation check failed: " + ex.Message);
			}
		}

		private static int TurnRuafOn()
		{
			GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
			List<Player> players = world?.AllAlivePlayersList;
			if (players == null)
			{
				return 0;
			}
			int changed = 0;
			for (int i = 0; i < players.Count; i++)
			{
				Player p = players[i];
				var settings = p?.Profile?.Info?.Settings;
				if (settings == null || !TerminalRoles.IsRuaf(settings.Role))
				{
					continue;
				}
				if (InheritProvocation(p.AIData?.BotOwner) > 0) changed++;
			}
			return changed;
		}
	}
}
