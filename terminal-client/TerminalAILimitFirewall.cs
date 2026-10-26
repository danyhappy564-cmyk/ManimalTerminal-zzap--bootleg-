using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalAILimitFirewall
{
	private const string ComponentTypeName = "AILimit.AILimitComponent";

	private static readonly object Sync = new object();

	private static Harmony _harmony;

	private static Type _componentType;

	private static bool _installed;

	internal static void Install(Harmony harmony)
	{
		_harmony = harmony;
		TryInstall();
	}

	internal static void TryInstall()
	{
		if (_installed || _harmony == null)
		{
			return;
		}
		lock (Sync)
		{
			if (_installed)
			{
				return;
			}
			Type type = TerminalTypes.ByName("AILimit.AILimitComponent");
			if (type == null)
			{
				return;
			}
			try
			{
				MethodInfo methodInfo = AccessTools.Method(type, "Enable");
				MethodInfo methodInfo2 = AccessTools.Method(type, "Update");
				if (methodInfo == null || methodInfo2 == null)
				{
					Plugin.Log.LogWarning("[Compat] AI Limit detected, but its Enable/Update layout is unknown; Terminal firewall could not arm");
					return;
				}
				HarmonyMethod harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(TerminalAILimitFirewall), "AllowAILimitOutsideTerminal"));
				_harmony.Patch((MethodBase)methodInfo, harmonyMethod, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				_harmony.Patch((MethodBase)methodInfo2, harmonyMethod, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				_componentType = type;
				_installed = true;
				Plugin.Log.LogWarning("[Compat] AI Limit detected — Terminal-only firewall armed; AI Limit remains unchanged on every other map");
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[Compat] AI Limit firewall patch failed: {arg}");
			}
		}
	}

	private static bool AllowAILimitOutsideTerminal()
	{
		return !TerminalGate.On;
	}

	internal static void EnforceAtRaidStart()
	{
		TryInstall();
		if (!TerminalGate.On || !_installed)
		{
			return;
		}
		int num = 0;
		try
		{
			GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
			if (!gameWorld)
			{
				return;
			}
			Behaviour behaviour = gameWorld.GetComponent(_componentType) as Behaviour;
			if (behaviour != null)
			{
				behaviour.enabled = false;
			}
			IEnumerable<BotOwner> enumerable = ((!Singleton<IBotGame>.Instantiated) ? null : Singleton<IBotGame>.Instance.BotsController?.BotSpawner)?._bots?.BotOwners;
			if (enumerable != null)
			{
				foreach (BotOwner item in enumerable)
				{
					try
					{
						if (!item || item.IsDead)
						{
							continue;
						}
						Player getPlayer = item.GetPlayer;
						if ((bool)getPlayer && getPlayer.HealthController != null && getPlayer.HealthController.IsAlive)
						{
							bool num2 = !getPlayer.gameObject.activeSelf || item.BotState != EBotState.Active;
							BotStandBy standBy = item.StandBy;
							if (standBy != null)
							{
								standBy.Activate();
								standBy._nextCheckTime = Time.time + 10f;
							}
							if (!getPlayer.gameObject.activeSelf)
							{
								getPlayer.gameObject.SetActive(value: true);
							}
							item.BotState = EBotState.Active;
							if (num2)
							{
								num++;
							}
						}
					}
					catch (Exception ex)
					{
						Plugin.Log.LogDebug("[Compat] could not wake one AI Limit bot: " + ex.Message);
					}
				}
			}
			Plugin.Log.LogWarning($"[Compat] AI Limit suppressed for Terminal raid; restored {num} previously limited bot(s)");
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Compat] AI Limit Terminal enforcement failed: " + ex2.Message);
		}
	}
}
