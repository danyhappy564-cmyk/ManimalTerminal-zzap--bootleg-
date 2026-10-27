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
	private static readonly HashSet<BotsGroup> Inherited = new HashSet<BotsGroup>();

	internal static void ResetForRaid()
	{
		Provoked.Clear();
		Inherited.Clear();
	}

	// Reused by the population birth/reconciliation callbacks; a not-yet-ready
	// group is retried, and a successful group gets each living traitor only once.
	internal static void OnBotCreated(BotOwner bot)
	{
		try { InheritProvocation(bot); }
		catch (Exception ex) { Plugin.Log.LogDebug("[RuafNeutral] new group inheritance failed: " + ex.Message); }
	}

	private static int InheritProvocation(BotOwner bot)
	{
		if (!TerminalGate.On || !Plugin.RuafNeutral.Value || Provoked.Count == 0 || !bot || bot.IsDead) return 0;
		var settings = bot.Profile?.Info?.Settings;
		BotsGroup group = bot.BotsGroup;
		if (settings == null || !TerminalRoles.IsRuaf(settings.Role) || group == null || Inherited.Contains(group)) return 0;
		using var scope = TerminalTickProfiler.Measure("Ruaf.InheritProvocation");
		GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
		List<Player> players = world?.AllAlivePlayersList;
		if (players == null) return 0;
		int added = 0;
		bool ready = true;
		for (int i = 0; i < players.Count; i++)
		{
			Player human = players[i];
			if (!human || human.IsAI || human.HealthController?.IsAlive != true || human.ProfileId == null || !Provoked.Contains(human.ProfileId)) continue;
			if (group.Enemies.ContainsKey(human)) continue;
			try
			{
				group.AddEnemy(human, EBotEnemyCause.followGetHit);
				if (group.Enemies.ContainsKey(human)) added++;
				else ready = false;
			}
			catch { ready = false; }
		}
		if (ready) Inherited.Add(group);
		if (added > 0) Plugin.Log.LogInfo($"[RuafNeutral] new RUAF group hostile to provoked human(s): role={TerminalRoles.Name(settings.Role)}, {added}");
		return added;
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
				// A repeated hit must also repair groups that a late spawn or another
				// mod's loyalty handling left neutral. Existing enemy entries are reused.
				Inherited.Clear();
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
