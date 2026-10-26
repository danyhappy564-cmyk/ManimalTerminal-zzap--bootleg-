using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal;

internal sealed class Patch_GateSwitchActions : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(InteractionContextHelper), "GetAvailableActions", new Type[2]
		{
			typeof(GamePlayerOwner),
			typeof(Switch)
		});
	}

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

	[PatchPostfix]
	private static void Postfix(ref AvailableInteractionState __result, GamePlayerOwner owner, Switch interactiveSwitch)
	{
		try
		{
			if (!Plugin.GatesExplosion.Value || interactiveSwitch == null || !Ours(interactiveSwitch))
			{
				return;
			}
			string name = interactiveSwitch.gameObject.name;
			if (name == "Switch_bomb")
			{
				if (TerminalGatesExplosion.DuoSessionActive)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					return;
				}
				if (TerminalGatesExplosion.Planted || TerminalGatesExplosion.Opened)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					return;
				}
				Player player = Singleton<GameWorld>.Instance?.MainPlayer;
				bool flag = player != null && TerminalGatesExplosion.FindCharge(player) != null;
				Switch sw = interactiveSwitch;
				Replace(ref __result, new InteractionAction
				{
					Name = "Plant",
					Disabled = !flag,
					Action = () =>
					{
						GateHoldSession gateHoldSession = new GameObject("Terminal_GatePlant").AddComponent<GateHoldSession>();
						gateHoldSession.Owner = owner;
						gateHoldSession.Anchor = sw.transform;
						gateHoldSession.Seconds = 5f;
						gateHoldSession.PanelText = "Planting charge {0:F1}";
						gateHoldSession.FoleyClip = "amb_terminal_interactive_c6_plant_activate";
						gateHoldSession.OnSuccess = () =>
						{
							Player player3 = Singleton<GameWorld>.Instance?.MainPlayer;
							if (TerminalCoop.Active)
							{
								TerminalCoop.Plant?.Invoke(player3);
							}
							else
							{
								if (!(player3 == null) && TerminalGatesExplosion.ConsumeCharge(player3))
								{
									TerminalGatesExplosion.Planted = true;
									TerminalGatesExplosion.PlayAt(TerminalGatesExplosion.FindClip("amb_terminal_interactive_c6_timer"), sw.transform.position, 30f);
									TerminalGatesExplosion.Emit("place_bomb_3037225082");
									try
									{
										owner?.ClearInteractionState();
										return;
									}
									catch
									{
										return;
									}
								}
								Plugin.Log.LogDebug("[Gates] hold finished but no charge — cancelled");
							}
						};
					}
				});
			}
			else
			{
				if (!(name == "SwitchBoard"))
				{
					return;
				}
				if (TerminalGatesExplosion.DuoSessionActive)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					return;
				}
				if (TerminalGatesExplosion.Opened || TerminalGatesExplosion.Planted)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					return;
				}
				Player player2 = Singleton<GameWorld>.Instance?.MainPlayer;
				bool elite = false;
				try
				{
					elite = player2 != null && player2.Skills.Strength.IsEliteLevel;
				}
				catch
				{
				}
				bool coop = TerminalCoop.Active;
				bool soloOpeningBranchActive = TerminalGatesExplosion.SoloOpeningBranchActive;
				if (elite && !soloOpeningBranchActive)
				{
					if (__result != null)
					{
						__result.Actions.Clear();
					}
					__result = null;
					return;
				}
				Switch sw2 = interactiveSwitch;
				Replace(ref __result, new InteractionAction
				{
					Name = (elite ? "Open" : (coop ? "Open with teammate" : "Open (requires Elite Strength)")),
					Disabled = (!elite && !coop),
					Action = () =>
					{
						if (coop && !elite)
						{
							if (!TerminalGatesExplosion.DuoSessionActive)
							{
								TerminalGatesExplosion.BeginDuoWait(owner, sw2.transform);
							}
						}
						else if (elite && TerminalGatesExplosion.SoloOpeningBranchActive)
						{
							GateHoldSession gateHoldSession = new GameObject("Terminal_GateOpen").AddComponent<GateHoldSession>();
							gateHoldSession.Owner = owner;
							gateHoldSession.Anchor = sw2.transform;
							gateHoldSession.Seconds = 5f;
							gateHoldSession.PanelText = "Opening gate {0:F1}";
							gateHoldSession.FoleyClip = "amb_terminal_interactive_gates_02_handle_open";
							gateHoldSession.OnSuccess = () =>
							{
								if (coop)
								{
									TerminalCoop.Request(TerminalEvent.GateOpen);
									return;
								}
								TerminalGatesExplosion.Opened = true;
								TerminalGatesExplosion.Emit("gates_opened_solo_3037225082");
								try
								{
									owner?.ClearInteractionState();
								}
								catch
								{
								}
							};
						}
					}
				});
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Gates] actions patch threw: " + ex.Message);
		}
	}
}
