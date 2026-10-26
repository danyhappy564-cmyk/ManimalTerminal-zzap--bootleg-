using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class GunshotHearingPatch : ModulePatch
{
	private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Player.FirearmController), "_player");

	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(Player.FirearmController), "Shot");
	}

	[PatchPostfix]
	private static void PatchPostfix(Player.FirearmController __instance)
	{
		Player player = PlayerField?.GetValue(__instance) as Player;
		if (player == null || !player.HealthController.IsAlive || CivilianConstants.IsCivilian(player.Profile.Info.Settings.Role))
		{
			return;
		}
		GameWorld instance = Singleton<GameWorld>.Instance;
		if (instance == null)
		{
			return;
		}
		List<Player> allAlivePlayersList = instance.AllAlivePlayersList;
		if (allAlivePlayersList == null)
		{
			return;
		}
		Vector3 position = player.Position;
		float num = 2025f;
		for (int i = 0; i < allAlivePlayersList.Count; i++)
		{
			Player player2 = allAlivePlayersList[i];
			if (!(player2 == null) && player2.HealthController.IsAlive && CivilianConstants.IsCivilian(player2.Profile.Info.Settings.Role))
			{
				BotOwner botOwner = player2.AIData?.BotOwner;
				if (!(botOwner == null) && !((player2.Position - position).sqrMagnitude > num))
				{
					CivilianFleeState.State state = CivilianFleeState.Get(botOwner);
					state.LastGunshotAt = Time.time;
					state.LastGunshotPos = position;
				}
			}
		}
	}
}
