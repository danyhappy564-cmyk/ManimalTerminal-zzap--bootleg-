using System;
using System.Collections;
using System.Reflection;
using EFT;
using EFT.Game.Spawning;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalLootBind
{
	[HarmonyPatch(typeof(GameWorld), "SpawnLoot")]
	internal static class Patch_DeferLootBind
	{
		[HarmonyPrefix]
		private static bool Prefix(GameWorld __instance, object[] __args)
		{
			try
			{
				if (!TerminalGate.On || _passThrough)
				{
					return true;
				}
				TerminalContainerTemplates.ApplyLoadedContainers();
				int num = BiggestContainerRegistry();
				if (num >= 150)
				{
					return true;
				}
				Plugin.Log.LogWarning("[LootBind] loot arrived before the scene registries " + $"(biggest container registry: {num}) — DEFERRING the bind");
				DeferHost deferHost = new GameObject("Terminal_LootBindDefer").AddComponent<DeferHost>();
				deferHost.Game = __instance;
				deferHost.Args = __args;
				return false;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[LootBind] prefix threw, binding normally: " + ex.Message);
				return true;
			}
		}
	}

	internal class DeferHost : MonoBehaviour
	{
		public GameWorld Game;

		public object[] Args;

		private void Start()
		{
			StartCoroutine(Run());
		}

		private IEnumerator Run()
		{
			float deadline = Time.realtimeSinceStartup + 6f;
			while (Time.realtimeSinceStartup < deadline && BiggestContainerRegistry() < 150)
			{
				yield return null;
			}
			if (BiggestContainerRegistry() < 150)
			{
				DumpAuthoredState();
				BuildSyntheticRegistry();
				yield return null;
			}
			int num = BiggestContainerRegistry();
			Plugin.Log.LogInfo($"[LootBind] releasing the deferred bind (registry: {num})");
			try
			{
				_passThrough = true;
				AccessTools.Method(typeof(GameWorld), "SpawnLoot").Invoke(Game, Args);
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[LootBind] deferred bind FAILED: {arg}");
			}
			finally
			{
				_passThrough = false;
			}
			UnityEngine.Object.Destroy(gameObject);
		}

		private static void DumpAuthoredState()
		{
			try
			{
				Scene sceneByName = SceneManager.GetSceneByName("Terminal_Design_Stuff");
				if (!sceneByName.IsValid() || !sceneByName.isLoaded)
				{
					Plugin.Log.LogWarning("[LootBind] Design_Stuff scene not loaded?!");
					return;
				}
				GameObject[] rootGameObjects = sceneByName.GetRootGameObjects();
				foreach (GameObject gameObject in rootGameObjects)
				{
					if (gameObject.name != "Location_scene")
					{
						continue;
					}
					Component[] components = gameObject.GetComponents<Component>();
					int num = 0;
					Component[] array = components;
					for (int j = 0; j < array.Length; j++)
					{
						if (array[j] == null)
						{
							num++;
						}
					}
					LocationScene component = gameObject.GetComponent<LocationScene>();
					Plugin.Log.LogWarning($"[LootBind] authored Location_scene: active={gameObject.activeInHierarchy} " + string.Format("comps={0} nullComps={1} LocationScene={2}", components.Length, num, component ? $"alive enabled={component.enabled} containers={component.LootableContainers?.Length ?? (-1)}" : "MISSING/HUSK"));
					return;
				}
				Plugin.Log.LogWarning("[LootBind] no 'Location_scene' root in Design_Stuff at runtime");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[LootBind] state dump failed: " + ex.Message);
			}
		}

		private static void BuildSyntheticRegistry()
		{
			try
			{
				GameObject gameObject = new GameObject("Terminal_SyntheticLocationScene");
				gameObject.SetActive(value: false);
				LocationScene locationScene = gameObject.AddComponent<LocationScene>();
				locationScene.LootableContainers = UnityEngine.Object.FindObjectsOfType<LootableContainer>(includeInactive: true);
				locationScene.StaticLoot = UnityEngine.Object.FindObjectsOfType<StaticLoot>(includeInactive: true);
				locationScene.WorldInteractiveObjects = UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(includeInactive: true);
				locationScene.SyncAbles = Array.Empty<ISyncAble>();
				locationScene.TriggerEntities = Array.Empty<GameObject>();
				locationScene.ControlledLampGroups = Array.Empty<ControlledLampGroup>();
				locationScene.NavMeshLinks = Array.Empty<NavMeshDoorLink>();
				locationScene.SpawnPointMarkers = Array.Empty<SpawnPointMarker>();
				locationScene.BotZones = Array.Empty<BotZone>();
				locationScene.ExfiltrationPoints = Array.Empty<ExfiltrationPoint>();
				FieldInfo fieldInfo = AccessTools.Field(typeof(LocationScene), "AIPlaceInfos");
				if (fieldInfo != null && fieldInfo.GetValue(locationScene) == null)
				{
					fieldInfo.SetValue(locationScene, Array.CreateInstance(fieldInfo.FieldType.GetElementType(), 0));
				}
				gameObject.SetActive(value: true);
				Plugin.Log.LogWarning($"[LootBind] SYNTHETIC registry built: {locationScene.LootableContainers.Length} containers, " + $"{locationScene.StaticLoot.Length} static loot, {locationScene.WorldInteractiveObjects.Length} interactives");
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[LootBind] synthetic registry FAILED: {arg}");
			}
		}
	}

	private static bool _passThrough;

	internal static void ResetForRaid()
	{
		_passThrough = false;
	}

	private static int BiggestContainerRegistry()
	{
		int num = 0;
		try
		{
			foreach (LocationScene loadedScene in LocationScene.LoadedScenes)
			{
				if (loadedScene == null || loadedScene.LootableContainers == null)
				{
					continue;
				}
				int num2 = 0;
				LootableContainer[] lootableContainers = loadedScene.LootableContainers;
				for (int i = 0; i < lootableContainers.Length; i++)
				{
					if ((bool)lootableContainers[i])
					{
						num2++;
					}
				}
				if (num2 > num)
				{
					num = num2;
				}
			}
		}
		catch
		{
		}
		return num;
	}
}
