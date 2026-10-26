using System;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalInteractables
{
	internal class ProbeHost : MonoBehaviour
	{
		private void Update()
		{
			if (!Plugin.InteractProbeKey.Value.IsDown())
			{
				return;
			}
			try
			{
				Player player = Singleton<GameWorld>.Instance?.MainPlayer;
				if (player == null)
				{
					Plugin.Log.LogWarning("[InteractProbe] no player");
					return;
				}
				int num = LayerMask.NameToLayer("Interactive");
				int num2 = 0;
				WorldInteractiveObject[] array = UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(includeInactive: true);
				foreach (WorldInteractiveObject worldInteractiveObject in array)
				{
					if (worldInteractiveObject == null)
					{
						continue;
					}
					float magnitude = (worldInteractiveObject.transform.position - player.Position).magnitude;
					if (!(magnitude > 6f))
					{
						num2++;
						Collider component = worldInteractiveObject.GetComponent<Collider>();
						string text = worldInteractiveObject.name;
						Transform parent = worldInteractiveObject.transform.parent;
						while (parent != null)
						{
							text = parent.name + "/" + text;
							parent = parent.parent;
						}
						Plugin.Log.LogWarning($"[InteractProbe] '{text}' ({worldInteractiveObject.GetType().Name}) d={magnitude:0.0}m " + $"layer={LayerMask.LayerToName(worldInteractiveObject.gameObject.layer)}({worldInteractiveObject.gameObject.layer}" + ((worldInteractiveObject.gameObject.layer == num) ? "=Interactive OK" : " NOT Interactive") + ") " + $"activeInHierarchy={worldInteractiveObject.gameObject.activeInHierarchy} " + "collider=" + ((component == null) ? "NONE" : (component.enabled ? "on" : "OFF")) + " " + $"state={worldInteractiveObject.DoorState} snap={worldInteractiveObject.Snap} keyId='{worldInteractiveObject.KeyId}' " + $"OPERATABLE={worldInteractiveObject.Operatable} " + $"noInteractions={worldInteractiveObject.NoInteractionsAllowed} id='{worldInteractiveObject.Id}'");
					}
				}
				Plugin.Log.LogWarning($"[InteractProbe] {num2} interactive object(s) within 6m");
				try
				{
					Player player2 = player;
					int mask = LayerMask.GetMask("Interactive", "Deadbody", "Player", "Loot");
					int mask2 = LayerMask.GetMask("HighPolyCollider", "TransparentCollider");
					Plugin.Log.LogWarning($"[InteractProbe] GATE 1 CanInteract={player2.CurrentState?.CanInteract} " + "handsController=" + ((player2.HandsController == null) ? "NULL" : player2.HandsController.CanInteract().ToString()) + "  <- both must be true or NOTHING is interactable");
					Camera main = Camera.main;
					if (main != null)
					{
						Ray ray = new Ray(main.transform.position, main.transform.forward);
						if (Physics.Raycast(ray, out var hitInfo, 3f, mask, QueryTriggerInteraction.Ignore))
						{
							InteractableObject componentInParent = hitInfo.collider.gameObject.GetComponentInParent<InteractableObject>();
							bool flag = Physics.Linecast(ray.origin, hitInfo.point, mask2);
							Plugin.Log.LogWarning($"[InteractProbe] GATE 2 raycast hit '{hitInfo.collider.name}' at {hitInfo.distance:0.00}m " + "layer=" + LayerMask.LayerToName(hitInfo.collider.gameObject.layer));
							Plugin.Log.LogWarning($"[InteractProbe] GATE 3 occlusion linecast blocked={flag}" + (flag ? "  <- THIS kills the prompt (something on HighPoly/TransparentCollider is in the way)" : ""));
							Plugin.Log.LogWarning("[InteractProbe] GATE 4 InteractableObject=" + ((componentInParent == null) ? "NONE FOUND" : componentInParent.GetType().Name));
							if (componentInParent != null)
							{
								Plugin.Log.LogWarning("[InteractProbe] GATE 5 InteractsFromAppropriateDirection=" + $"{componentInParent.InteractsFromAppropriateDirection(player2.LookDirection)}" + "  <- false = authored interaction direction/dot rejects your angle");
							}
						}
						else
						{
							Plugin.Log.LogWarning("[InteractProbe] GATE 2 raycast (Interactive/Deadbody/Player/Loot) hit NOTHING within 3m");
						}
					}
					Plugin.Log.LogWarning("[InteractProbe] engine currently holds InteractableObject=" + ((player2.InteractableObject == null) ? "NULL (no prompt)" : player2.InteractableObject.GetType().Name));
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[InteractProbe] gate replay failed: " + ex.Message);
				}
				Camera main2 = Camera.main;
				if (main2 != null)
				{
					RaycastHit[] array2 = Physics.RaycastAll(main2.transform.position, main2.transform.forward, 6f, -1, QueryTriggerInteraction.Collide);
					Array.Sort(array2, (RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance));
					if (array2.Length == 0)
					{
						Plugin.Log.LogWarning("[InteractProbe] crosshair ray hit NOTHING within 6m");
					}
					for (int num3 = 0; num3 < array2.Length && num3 < 6; num3++)
					{
						RaycastHit raycastHit = array2[num3];
						WorldInteractiveObject componentInParent2 = raycastHit.collider.GetComponentInParent<WorldInteractiveObject>();
						Plugin.Log.LogWarning($"[InteractProbe]   ray[{num3}] {raycastHit.distance:0.00}m '{raycastHit.collider.name}' " + "layer=" + LayerMask.LayerToName(raycastHit.collider.gameObject.layer) + " " + $"trigger={raycastHit.collider.isTrigger} " + "owner=" + ((componentInParent2 == null) ? "none" : (componentInParent2.GetType().Name + " '" + componentInParent2.Id + "'")));
					}
				}
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[InteractProbe] failed: " + ex2.Message);
			}
		}
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_HealInteractables
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!TerminalGate.On)
			{
				return;
			}
			if (UnityEngine.Object.FindObjectOfType<ProbeHost>() == null)
			{
				new GameObject("Terminal_InteractProbe").AddComponent<ProbeHost>();
			}
			try
			{
				int num = 0;
				int num2 = 0;
				LootableContainer[] array = UnityEngine.Object.FindObjectsOfType<LootableContainer>(includeInactive: true);
				foreach (LootableContainer lootableContainer in array)
				{
					if (lootableContainer == null)
					{
						continue;
					}
					if (TerminalContainerTemplates.Apply(lootableContainer))
					{
						num2++;
					}
					if (lootableContainer.DoorState == EDoorState.None)
					{
						try
						{
							lootableContainer.DoorState = EDoorState.Shut;
							num++;
						}
						catch
						{
						}
					}
				}
				if (num > 0 || num2 > 0)
				{
					Plugin.Log.LogInfo($"[Interactables] {num} container(s) healed None -> Shut (Search prompt requires Shut), {num2} container template(s) restored from server authoring");
				}
				int num3 = 0;
				Door[] array2 = UnityEngine.Object.FindObjectsOfType<Door>(includeInactive: true);
				foreach (Door door in array2)
				{
					if (door == null)
					{
						continue;
					}
					Animator[] componentsInChildren = door.GetComponentsInChildren<Animator>(includeInactive: true);
					foreach (Animator animator in componentsInChildren)
					{
						if (animator != null && animator.enabled)
						{
							animator.enabled = false;
							num3++;
							Plugin.Log.LogInfo("[Interactables] door '" + door.name + "': enabled Animator on '" + animator.gameObject.name + "' DISABLED (transform stomper)");
						}
					}
				}
				if (num3 > 0)
				{
					Plugin.Log.LogInfo($"[Interactables] {num3} door animator(s) stripped");
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Interactables] heal failed: " + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(WorldInteractiveObject), "SetDoorState")]
	internal static class Patch_DoorProbe
	{
		[HarmonyPostfix]
		private static void Postfix(WorldInteractiveObject __instance, EDoorState state)
		{
			if (!TerminalGate.On || !(__instance is Door) || (state != EDoorState.Open && state != EDoorState.Shut))
			{
				return;
			}
			try
			{
				DoorProbeHost doorProbeHost = new GameObject("Terminal_DoorProbe").AddComponent<DoorProbeHost>();
				doorProbeHost.Door = __instance;
				doorProbeHost.Angle0 = __instance.CurrentAngle;
				doorProbeHost.Target = state;
				doorProbeHost.Rot0 = __instance.transform.localRotation;
			}
			catch
			{
			}
		}
	}

	internal class DoorProbeHost : MonoBehaviour
	{
		internal WorldInteractiveObject Door;

		internal float Angle0;

		internal Quaternion Rot0;

		internal EDoorState Target;

		private float _t;

		private void Update()
		{
			_t += Time.deltaTime;
			if (_t < 1.5f)
			{
				return;
			}
			try
			{
				if (Door != null)
				{
					float currentAngle = Door.CurrentAngle;
					float num = Quaternion.Angle(Rot0, Door.transform.localRotation);
					ManualLogSource log = Plugin.Log;
					string text = $"[DoorProbe] '{Door.name}' -> {Target}: angle {Angle0:0.#} -> {currentAngle:0.#} ";
					string text2 = $"(open={Door.OpenAngle:0.#}) transformDelta={num:0.#}deg state={Door.DoorState} ";
					string text3;
					if (Mathf.Approximately(currentAngle, Angle0))
					{
						text3 = "VERDICT: swing never ran (state machine)";
					}
					else
					{
						text3 = ((num < 1f) ? "VERDICT: angle moved, transform stomped" : "moved OK");
					}
					log.LogWarning(text + text2 + text3);
				}
			}
			catch
			{
			}
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	internal static void ReassertDoors(string when)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		try
		{
			int num = LayerMask.NameToLayer("Interactive");
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			Door[] array = UnityEngine.Object.FindObjectsOfType<Door>(includeInactive: true);
			foreach (Door door in array)
			{
				if (!(door == null))
				{
					if (num >= 0 && door.gameObject.layer != num && door.gameObject.layer != LayerMask.NameToLayer("Default"))
					{
						door.gameObject.layer = num;
						num2++;
					}
					Collider component = door.GetComponent<Collider>();
					if (component != null && !component.enabled)
					{
						component.enabled = true;
						num3++;
					}
					if (door.DoorState == EDoorState.Interacting)
					{
						door.DoorState = EDoorState.Shut;
						num4++;
					}
				}
			}
			if (num2 + num3 + num4 > 0)
			{
				Plugin.Log.LogWarning($"[Interactables] doors re-asserted {when}: {num2} relayered, " + $"{num3} collider(s) re-enabled, {num4} unstuck from Interacting");
			}
			else
			{
				Plugin.Log.LogDebug("[Interactables] doors checked " + when + " — all healthy");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Interactables] door re-assert failed: " + ex.Message);
		}
	}
}
