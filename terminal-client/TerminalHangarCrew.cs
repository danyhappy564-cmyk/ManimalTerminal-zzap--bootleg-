using System;
using EFT.Game.Spawning;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal class TerminalHangarCrew : MonoBehaviour
{
	private static bool _armedThisRaid;

	private static bool _zoneAugmented;

	internal static void Arm()
	{
		if (TerminalCoop.Authority && !_armedThisRaid && !TerminalSpawnGate.BotsDisabled && Plugin.EventWavesPush.Value)
		{
			_armedThisRaid = true;
			Plugin.Log.LogInfo("[HangarCrew] TB8 uses its authored squad; automatic guard top-ups disabled");
		}
	}

	internal static void ResetForRaid()
	{
		_armedThisRaid = false;
		_zoneAugmented = false;
	}

	internal static void AugmentHangarZone()
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		if (_zoneAugmented || TerminalSpawnGate.BotsDisabled)
		{
			return;
		}
		try
		{
			Bounds? bounds = TerminalCrewJobs.BdHoldBoundsPublic();
			if (!bounds.HasValue)
			{
				return;
			}
			BotZone botZone = ValueEnumerableExtensions.FirstOrDefault<FromArray<BotZone>, BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(UnityEngine.Object.FindObjectsOfType<BotZone>()), (Func<BotZone, bool>)((BotZone z) => (bool)z && z.name == "Zone1BD1HangarBD11"));
			if (!botZone || botZone.SpawnPointMarkers == null || botZone.SpawnPointMarkers.Count == 0)
			{
				return;
			}
			SpawnPointMarker spawnPointMarker = botZone.SpawnPointMarkers[0];
			if (!spawnPointMarker)
			{
				return;
			}
			Bounds value = bounds.Value;
			Vector3[] array = new Vector3[4]
			{
				value.center + new Vector3(value.extents.x * 0.5f, 0f, value.extents.z * 0.5f),
				value.center + new Vector3((0f - value.extents.x) * 0.5f, 0f, value.extents.z * 0.5f),
				value.center + new Vector3(value.extents.x * 0.5f, 0f, (0f - value.extents.z) * 0.5f),
				value.center + new Vector3((0f - value.extents.x) * 0.5f, 0f, (0f - value.extents.z) * 0.5f)
			};
			int num = 0;
			Vector3[] array2 = array;
			for (int num2 = 0; num2 < array2.Length; num2++)
			{
				if (!NavMesh.SamplePosition(array2[num2], out var hit, 6f, -1))
				{
					continue;
				}
				GameObject gameObject = UnityEngine.Object.Instantiate(spawnPointMarker.gameObject, hit.position, Quaternion.identity, spawnPointMarker.transform.parent);
				gameObject.name = $"BP.Terminal_HangarInterior_{num}";
				SpawnPointMarker component = gameObject.GetComponent<SpawnPointMarker>();
				if (!component)
				{
					UnityEngine.Object.Destroy(gameObject);
					continue;
				}
				SphereCollider component2 = gameObject.GetComponent<SphereCollider>();
				if ((bool)component2)
				{
					component2.radius = 3f;
				}
				component.BotZone = botZone;
				botZone.SpawnPointMarkers.Add(component);
				num++;
			}
			if (num > 0)
			{
				AccessTools.Field(typeof(BotZone), "_spawnPoints")?.SetValue(botZone, null);
				_zoneAugmented = true;
				Plugin.Log.LogInfo($"[HangarCrew] hangar zone augmented: +{num} interior spawn marker(s), 3m radius — the TB8 group can land whole");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[HangarCrew] zone augmentation failed: " + ex.Message);
		}
	}
}
