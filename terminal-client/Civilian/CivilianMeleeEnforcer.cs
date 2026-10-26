using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

internal static class CivilianMeleeEnforcer
{
	private static readonly Dictionary<string, float> NextCheckAt = new Dictionary<string, float>();

	public static void Tick(BotOwner bot)
	{
		try
		{
			Player player = bot?.GetPlayer;
			if (player == null)
			{
				return;
			}
			string text = bot.Profile?.Id;
			if (text == null || (NextCheckAt.TryGetValue(text, out var value) && Time.time < value))
			{
				return;
			}
			NextCheckAt[text] = Time.time + 1.5f;
			if (player.HandsController is Player.FirearmController)
			{
				BotWeaponSelector botWeaponSelector = bot.WeaponManager?.Selector;
				if (botWeaponSelector != null && !botWeaponSelector.IsChanging && !botWeaponSelector.TryChangeToSlot(EquipmentSlot.Scabbard, changeToMain: true, (Result<IHandsController> result) =>
				{
					if (!result.Succeed || player.HandsController is Player.FirearmController)
					{
						Plugin.Log.LogWarning("[Civ] " + bot.Profile?.Nickname + ": melee transition failed " + $"(succeeded={result.Succeed}, hands={player.HandsController?.GetType().Name})");
					}
					else
					{
						Plugin.Log.LogDebug("[Civ] " + bot.Profile?.Nickname + ": melee transition completed");
					}
				}))
				{
					Plugin.Log.LogDebug("[Civ] " + bot.Profile?.Nickname + ": melee request deferred " + $"(ready={botWeaponSelector.IsWeaponReady}, changing={botWeaponSelector.IsChanging}, " + $"melee={botWeaponSelector.CanChangeToMeleeWeapons}, reload={bot.WeaponManager.Reload.Reloading})");
				}
			}
		}
		catch
		{
		}
	}

	public static void ClearAll()
	{
		NextCheckAt.Clear();
	}
}
