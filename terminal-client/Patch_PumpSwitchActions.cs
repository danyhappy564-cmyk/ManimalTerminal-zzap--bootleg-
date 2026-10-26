using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(InteractionContextHelper), "GetAvailableActions", new Type[]
{
	typeof(GamePlayerOwner),
	typeof(Switch)
})]
internal static class Patch_PumpSwitchActions
{
	private static void Replace(ref AvailableInteractionState result, InteractionAction act)
	{
		if (result == null)
		{
			result = new AvailableInteractionState
			{
				Actions = new List<InteractionAction> { act }
			};
		}
		else
		{
			result.Actions.Clear();
			result.Actions.Add(act);
		}
	}

	private static bool Ours(Switch sw)
	{
		try
		{
			return sw.gameObject.scene.name?.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase) ?? false;
		}
		catch
		{
			return false;
		}
	}

	private static void Postfix(ref AvailableInteractionState __result, GamePlayerOwner owner, Switch interactiveSwitch)
	{
		try
		{
			// zzap perf: cached map flag first (see Patch_GateSwitchActions)
			if (!TerminalGate.On || !Plugin.PumpStation.Value || interactiveSwitch == null || !Ours(interactiveSwitch))
			{
				return;
			}
			switch (interactiveSwitch.gameObject.name)
			{
			case "Electric_box_Switch":
			{
				TerminalPumpStation.CabinetInfo cab = TerminalPumpStation.CabinetForSwitch(interactiveSwitch.transform);
				if (cab == null)
				{
					break;
				}
				if (!TerminalPumpStation.IsBroken(cab))
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					break;
				}
				Player player = Singleton<GameWorld>.Instance?.MainPlayer;
				bool hasTool = player != null && TerminalPumpStation.HasMultitool(player);
				Switch sw = interactiveSwitch;
				Replace(ref __result, new InteractionAction
				{
					Name = (hasTool ? "Repair" : "Repair without tools (risky)"),
					Disabled = false,
					Action = () =>
					{
						string repairMode = (hasTool ? "safe" : "unsafe");
						TerminalGatesExplosion.Emit("start_" + repairMode + "_repair_electricity_terminal_" + cab.Suffix);
						GateHoldSession gateHoldSession = new GameObject("Terminal_CabinetRepair").AddComponent<GateHoldSession>();
						gateHoldSession.Owner = owner;
						gateHoldSession.Anchor = sw.transform;
						gateHoldSession.Seconds = 5f;
						gateHoldSession.PanelText = "Repairing {0:F1}";
						gateHoldSession.OnCancel = () =>
						{
							TerminalGatesExplosion.Emit("dropped_" + repairMode + "_repair_electricity_" + cab.Suffix);
						};
						gateHoldSession.OnSuccess = () =>
						{
							bool flag = false;
							try
							{
								Player player2 = Singleton<GameWorld>.Instance?.MainPlayer;
								flag = player2 != null && TerminalPumpStation.HasMultitool(player2);
							}
							catch
							{
							}
							if (TerminalCoop.Active)
							{
								TerminalCoop.Request(TerminalEvent.PumpRepair, cab.Path);
							}
							else if (flag || UnityEngine.Random.value < 0.1f)
							{
								TerminalPumpStation.OnRepaired(cab);
							}
							else
							{
								TerminalPumpStation.OnUnsafeRepairFailed(cab);
							}
							try
							{
								owner?.ClearInteractionState();
							}
							catch
							{
							}
						};
					}
				});
				break;
			}
			case "Interactive_Button":
				if (TerminalPumpStation.WaterDrained)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					break;
				}
				Replace(ref __result, new InteractionAction
				{
					Name = "Drain the reservoir",
					Disabled = false,
					Action = () =>
					{
						TerminalPumpStation.Drain();
						try
						{
							owner?.ClearInteractionState();
						}
						catch
						{
						}
					}
				});
				break;
			case "Interactive_Button_off":
				Replace(ref __result, new InteractionAction
				{
					Name = "Inspect panel",
					Disabled = false,
					Action = () =>
					{
						NotificationManager.DisplayMessageNotification("The panel has no power — repair the broken electrical cabinet first", ENotificationDurationType.Default, ENotificationIconType.Default, Color.white);
					}
				});
				break;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Pump] actions patch threw: " + ex.Message);
		}
	}
}
