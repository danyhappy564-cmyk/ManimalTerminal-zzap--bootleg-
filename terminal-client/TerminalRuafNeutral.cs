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

	internal static void ResetForRaid()
	{
		Provoked.Clear();
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
				Player victim = __instance?.Player;
				var victimSettings = victim?.Profile?.Info?.Settings;
				if (victimSettings == null || !TerminalRoles.IsRuaf(victimSettings.Role))
				{
					return;
				}
				IPlayer attacker = damageInfo.Player?.iPlayer;
				if (attacker == null || attacker.IsAI || attacker.ProfileId == null || !Provoked.Add(attacker.ProfileId))
				{
					return;
				}
				int groups = TurnRuafOn(attacker);
				Plugin.Log.LogInfo($"[RuafNeutral] {attacker.Profile?.Nickname} drew RUAF blood ({TerminalRoles.Name(victimSettings.Role)}) — hostile to {groups} RUAF group(s) for the rest of the raid");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogDebug("[RuafNeutral] provocation check failed: " + ex.Message);
			}
		}

		private static int TurnRuafOn(IPlayer attacker)
		{
			GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
			List<Player> players = world?.AllAlivePlayersList;
			if (players == null)
			{
				return 0;
			}
			var done = new HashSet<BotsGroup>();
			for (int i = 0; i < players.Count; i++)
			{
				Player p = players[i];
				var settings = p?.Profile?.Info?.Settings;
				if (settings == null || !TerminalRoles.IsRuaf(settings.Role))
				{
					continue;
				}
				BotsGroup group = p.AIData?.BotOwner?.BotsGroup;
				if (group != null && done.Add(group))
				{
					try
					{
						group.AddEnemy(attacker, EBotEnemyCause.followGetHit);
					}
					catch
					{
					}
				}
			}
			return done.Count;
		}
	}
}
