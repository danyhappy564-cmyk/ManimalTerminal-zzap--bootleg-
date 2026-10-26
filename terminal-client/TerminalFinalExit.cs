using System;
using System.IO;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalFinalExit
{
	[HarmonyPatch(typeof(ExfiltrationController), "InitAllExfiltrationPoints")]
	internal static class Patch_BuildZubrExit
	{
		[HarmonyPostfix]
		private static void Postfix(ExfiltrationController __instance, MongoID locationId, BackendExitTriggerSettings[] settings, bool giveAuthority)
		{
			try
			{
				if (!TerminalGate.On || !Plugin.FinalExit.Value || __instance == null)
				{
					return;
				}
				ExfiltrationPoint[] array = __instance.ExfiltrationPoints ?? new ExfiltrationPoint[0];
				ExfiltrationPoint[] array2 = array;
				foreach (ExfiltrationPoint exfiltrationPoint in array2)
				{
					if (exfiltrationPoint != null && exfiltrationPoint.Settings != null && exfiltrationPoint.Settings.Name == "Terminal_Zubr_Exit")
					{
						return;
					}
				}
				BackendExitTriggerSettings backendExitTriggerSettings = null;
				BackendExitTriggerSettings[] array3 = settings ?? new BackendExitTriggerSettings[0];
				foreach (BackendExitTriggerSettings backendExitTriggerSettings2 in array3)
				{
					if (backendExitTriggerSettings2 != null && backendExitTriggerSettings2.Name == "Terminal_Zubr_Exit")
					{
						backendExitTriggerSettings = backendExitTriggerSettings2;
						break;
					}
				}
				if (backendExitTriggerSettings == null)
				{
					Plugin.Log.LogWarning("[FinalExit] no 'Terminal_Zubr_Exit' exits row from the server — cannot build the point");
					return;
				}
				Transform transform = TerminalRigFill.FindRootNamed("FinalExitZone");
				BoxCollider boxCollider = (transform ? transform.GetComponent<BoxCollider>() : null);
				if (!boxCollider)
				{
					Plugin.Log.LogWarning("[FinalExit] authored FinalExitZone box not found — cannot build the point");
					return;
				}
				GameObject gameObject = new GameObject("Terminal_Zubr_Exit");
				gameObject.SetActive(value: false);
				gameObject.layer = transform.gameObject.layer;
				gameObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
				BoxCollider boxCollider2 = gameObject.AddComponent<BoxCollider>();
				boxCollider2.center = boxCollider.center;
				boxCollider2.size = Vector3.Scale(boxCollider.size, transform.lossyScale);
				boxCollider2.isTrigger = true;
				ExfiltrationPoint exfiltrationPoint2 = gameObject.AddComponent<ExfiltrationPoint>();
				exfiltrationPoint2.Settings.Name = "Terminal_Zubr_Exit";
				gameObject.SetActive(value: true);
				exfiltrationPoint2.LoadSettings(locationId.Add(array.Length + 2), backendExitTriggerSettings, giveAuthority);
				ExfiltrationPoint[] array4 = new ExfiltrationPoint[array.Length + 1];
				array.CopyTo(array4, 0);
				array4[array.Length] = exfiltrationPoint2;
				__instance.ExfiltrationPoints = array4;
				Plugin.Log.LogInfo(string.Format("[FinalExit] '{0}' point BUILT at the authored zone ({1}) — status {2}", "Terminal_Zubr_Exit", transform.position, exfiltrationPoint2.Status));
			}
			catch (Exception arg)
			{
				Plugin.Log.LogWarning($"[FinalExit] exfil build failed: {arg}");
			}
		}
	}

	private const float EvacSeconds = 180f;

	internal const string ExitName = "Terminal_Zubr_Exit";

	private static bool _staged;

	private static bool _timerCut;

	private static Door _door;

	private static WorldInteractiveObjectInteract _handler;

	internal static void ResetForRaid()
	{
		if (_door != null && _handler != null)
		{
			_door.OnDoorStateChanged -= _handler;
		}
		_staged = false;
		_timerCut = false;
		_door = null;
		_handler = null;
	}

	internal static void TryStage()
	{
		if (_staged || !Plugin.FinalExit.Value || !TerminalGate.On || (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null) == null)
		{
			return;
		}
		Transform transform = TerminalRigFill.FindRootNamed("FinalExitZone");
		if (!transform)
		{
			return;
		}
		try
		{
			string text = "door_plastic2_door_plastic_prefab_10";
			try
			{
				foreach (JToken item in (JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_endgame.json")))["rows"] as JArray) ?? new JArray())
				{
					if (item.Value<string>("cls") == "FinalGateOpenHandler" && item["fields"]?["doorID"] != null)
					{
						text = item["fields"].Value<string>("doorID");
					}
				}
			}
			catch
			{
			}
			BoxCollider component = transform.GetComponent<BoxCollider>();
			ExfiltrationPoint exfiltrationPoint = null;
			ExfiltrationPoint[] array = UnityEngine.Object.FindObjectsOfType<ExfiltrationPoint>(includeInactive: true);
			foreach (ExfiltrationPoint exfiltrationPoint2 in array)
			{
				if (exfiltrationPoint2.Settings != null && exfiltrationPoint2.Settings.Name == "Terminal_Zubr_Exit")
				{
					exfiltrationPoint = exfiltrationPoint2;
					break;
				}
			}
			if ((bool)exfiltrationPoint && (bool)component)
			{
				exfiltrationPoint.transform.SetPositionAndRotation(transform.position, transform.rotation);
				BoxCollider component2 = exfiltrationPoint.GetComponent<BoxCollider>();
				if ((bool)component2)
				{
					component2.center = component.center;
					component2.size = Vector3.Scale(component.size, transform.lossyScale);
				}
				Plugin.Log.LogInfo(string.Format("[FinalExit] {0} snapped to the authored FinalExitZone box at {1}", "Terminal_Zubr_Exit", transform.position));
			}
			else
			{
				Plugin.Log.LogWarning($"[FinalExit] snap incomplete (exfil={(bool)exfiltrationPoint} zoneBox={(bool)component}) — exfil stays where authored");
			}
			Door[] array2 = UnityEngine.Object.FindObjectsOfType<Door>(includeInactive: true);
			foreach (Door door in array2)
			{
				if (!(door.Id != text))
				{
					_door = door;
					_handler = OnDoorStateChanged;
					door.OnDoorStateChanged += _handler;
					break;
				}
			}
			if (_door == null)
			{
				Plugin.Log.LogWarning("[FinalExit] gate 3 door '" + text + "' not found — no evac timer");
			}
			else
			{
				Plugin.Log.LogInfo($"[FinalExit] watching gate 3 door '{text}' — opening it cuts raid time to {3f:0} min");
			}
			_staged = true;
			if (_door != null && _door.DoorState == EDoorState.Open)
			{
				OnDoorStateChanged(_door, EDoorState.Shut, EDoorState.Open);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[FinalExit] staging failed: {arg}");
			_staged = true;
		}
	}

	private static void OnDoorStateChanged(WorldInteractiveObject obj, EDoorState prevState, EDoorState nextState)
	{
		if (TerminalCoop.Active)
		{
			if (!_timerCut && nextState == EDoorState.Open && TerminalCoop.Authority)
			{
				TerminalCoop.Request(TerminalEvent.Evac, obj.Id);
			}
		}
		else if (!_timerCut && nextState == EDoorState.Open)
		{
			ApplyCountdown(180.0, announce: true);
		}
	}

	internal static bool ApplyCountdown(double seconds, bool announce)
	{
		GameTimer gameTimer = Singleton<AbstractGame>.Instance?.GameTimer;
		if (gameTimer == null)
		{
			return false;
		}
		float num = (float)Math.Max(0.0, Math.Min(180.0, seconds));
		bool flag = !_timerCut;
		try
		{
			_timerCut = true;
			TimeSpan timeSpan = (gameTimer.SessionTime ?? TimeSpan.Zero) - gameTimer.PastTime;
			if (timeSpan > TimeSpan.FromSeconds((double)num + (flag ? 0.0 : 0.25)))
			{
				gameTimer.ChangeSessionTime(gameTimer.PastTime + TimeSpan.FromSeconds(num));
				try
				{
					object obj = MonoBehaviourSingleton<GameUI>.Instance?.TimerPanel;
					if (obj != null && !(obj is TimerPanel))
					{
						obj = AccessTools.Field(obj.GetType(), "_mainTimerPanel")?.GetValue(obj);
					}
					if (obj is TimerPanel obj2)
					{
						AccessTools.Field(typeof(TimerPanel), "_dateTime").SetValue(obj2, DateTimeExtensions.UtcNow.AddSeconds(num));
						if (flag)
						{
							Plugin.Log.LogInfo($"[FinalExit] HUD timer synchronized to {num:F1}s remaining");
						}
					}
					else if (flag && TerminalCoop.LocalHuman)
					{
						Plugin.Log.LogWarning("[FinalExit] no TimerPanel reachable — HUD keeps the old countdown");
					}
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[FinalExit] timer UI poke failed: " + ex.Message);
				}
				if (flag)
				{
					Plugin.Log.LogInfo($"[FinalExit] GATE 3 OPEN — {num:F1}s evacuation window");
				}
			}
			else if (flag)
			{
				Plugin.Log.LogInfo($"[FinalExit] gate 3 open with {timeSpan:mm\\:ss} left — already inside the evac window");
			}
			try
			{
				if (!flag || !announce || !TerminalCoop.LocalHuman)
				{
					return true;
				}
				TerminalSubtitles.ShowStandalone("<b>Loudspeaker:</b> The enemy has broken through to the loading zone! Evacuation will end in 3 minutes!", 8f);
				AudioClip audioClip = TerminalFxBundle.FindClip("vsrf_evac_3min");
				if (audioClip == null)
				{
					audioClip = TerminalFxBundle.FindClip("amb_terminal_siren");
				}
				if (audioClip != null)
				{
					int num2 = 0;
					float num3 = float.MaxValue;
					Player player = Singleton<GameWorld>.Instance?.MainPlayer;
					Transform transform = TerminalRigFill.FindRootNamed("AnnouncementSystem");
					if ((bool)transform)
					{
						Transform[] componentsInChildren = transform.GetComponentsInChildren<Transform>(includeInactive: true);
						foreach (Transform transform2 in componentsInChildren)
						{
							if (transform2.name.StartsWith("announcement_speaker"))
							{
								TerminalGatesExplosion.PlayAt(audioClip, transform2.position, 90f);
								if (player != null)
								{
									num3 = Mathf.Min(num3, (transform2.position - player.Position).magnitude);
								}
								num2++;
							}
						}
					}
					if (num2 == 0 && (bool)_door)
					{
						TerminalGatesExplosion.PlayAt(audioClip, _door.transform.position, 200f);
					}
					TerminalGatesExplosion.PlayFlat(audioClip, 0.55f);
					Plugin.Log.LogInfo($"[FinalExit] evac announcement over {num2} speaker(s) + 2D bed" + " ('" + audioClip.name + "', nearest speaker " + ((num3 < float.MaxValue) ? $"{num3:F0}m" : "n/a") + ")");
				}
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[FinalExit] announcement failed: " + ex2.Message);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[FinalExit] timer cut failed: {arg}");
		}
		return true;
	}
}
