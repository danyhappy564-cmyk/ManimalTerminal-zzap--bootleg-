using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

internal static class CivilianThreatScanner
{
	public struct ThreatResult
	{
		public IPlayer Threat;

		public Vector3 ThreatPosition;

		public float Distance;

		public bool HasThreat;
	}

	public static ThreatResult FindNearestThreat(BotOwner civilian, float radius)
	{
		ThreatResult result = default;
		if (civilian == null)
		{
			return result;
		}
		GameWorld instance = Singleton<GameWorld>.Instance;
		if (instance == null)
		{
			return result;
		}
		Vector3 position = civilian.Position;
		float num = float.MaxValue;
		IPlayer player = null;
		List<Player> allAlivePlayersList = instance.AllAlivePlayersList;
		if (allAlivePlayersList == null)
		{
			return result;
		}
		for (int i = 0; i < allAlivePlayersList.Count; i++)
		{
			Player player2 = allAlivePlayersList[i];
			if (!(player2 == null) && player2.HealthController.IsAlive && (object)player2 != civilian.GetPlayer && !CivilianConstants.IsCivilian(player2.Profile.Info.Settings.Role) && HasRangedWeaponDrawn(player2))
			{
				float num2 = Vector3.Distance(position, player2.Position);
				if (!(num2 > radius) && !(num2 >= num) && HasLineOfSight(civilian, player2))
				{
					num = num2;
					player = player2;
				}
			}
		}
		if (player != null)
		{
			result.Threat = player;
			result.ThreatPosition = player.Position;
			result.Distance = num;
			result.HasThreat = true;
			return result;
		}
		CivilianFleeState.State state = CivilianFleeState.Get(civilian);
		if (Time.time - state.LastGunshotAt < 6f)
		{
			result.ThreatPosition = state.LastGunshotPos;
			result.Distance = Vector3.Distance(position, state.LastGunshotPos);
			result.HasThreat = true;
		}
		return result;
	}

	private static bool HasRangedWeaponDrawn(IPlayer player)
	{
		if (player is Player player2)
		{
			return player2.HandsController is Player.FirearmController;
		}
		return false;
	}

	private static bool HasLineOfSight(BotOwner civilian, IPlayer threat)
	{
		BifacialTransform weaponRoot = civilian.WeaponRoot;
		if (weaponRoot == null)
		{
			return true;
		}
		Vector3 position = weaponRoot.position;
		Vector3 end = threat.Position + Vector3.up * 1.3f;
		return !Physics.Linecast(position, end, LayersMaskController.HighPolyWithTerrainMask);
	}
}
