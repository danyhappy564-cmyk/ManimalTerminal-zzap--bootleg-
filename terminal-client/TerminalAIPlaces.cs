using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalAIPlaces
{
	internal sealed class Patch_BuildSpawnTriggers : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(BotsController), "Init");
		}

		[PatchPostfix]
		private static void Postfix(BotsController __instance)
		{
			if (!TerminalGate.On || !TerminalCoop.Authority)
			{
				return;
			}
			try
			{
				TryBuild(__instance);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[AIPlaces] build failed: " + ex.Message);
			}
			try
			{
				TerminalCrewJobs.Reset();
				if (__instance.BotSpawner != null)
				{
					__instance.BotSpawner.OnBotCreated += TerminalCrewJobs.OnBotCreated;
				}
				TerminalHangarCrew.AugmentHangarZone();
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[AIPlaces] crew hook failed: " + ex2.Message);
			}
		}
	}

	private static JObject _sidecar;

	private static bool _sidecarTried;

	private static GameObject _marker;

	private static string DataDir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "aiplaces");

	internal static void ResetForNewRaid()
	{
		if (_marker != null)
		{
			UnityEngine.Object.Destroy(_marker);
		}
		_marker = null;
		TerminalTierEventLogic.ResetFireCounts();
		TerminalBlackDivisionPlaces.ResetForNewRaid();
	}

	private static JObject Sidecar()
	{
		if (_sidecarTried)
		{
			return _sidecar;
		}
		_sidecarTried = true;
		try
		{
			string text = Path.Combine(DataDir, "terminal_aiplaces.json");
			if (File.Exists(text))
			{
				_sidecar = JObject.Parse(File.ReadAllText(text));
			}
			else
			{
				Plugin.Log.LogDebug("[AIPlaces] no sidecar at " + text + " — spawn triggers stay off");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[AIPlaces] sidecar parse failed: " + ex.Message);
		}
		return _sidecar;
	}

	public static void TryBuild(BotsController controller)
	{
		//IL_066c: Unknown result type (might be due to invalid IL or missing references)
		if (_marker != null)
		{
			return;
		}
		JObject jObject = Sidecar();
		if (jObject == null || !TerminalGate.On)
		{
			return;
		}
		try
		{
			GameObject gameObject = GameObject.Find("AIPlaceInfoHolder");
			if (gameObject == null)
			{
				Plugin.Log.LogDebug("[AIPlaces] AIPlaceInfoHolder GO not in scene — aborting");
				return;
			}
			Dictionary<string, Transform> index = new Dictionary<string, Transform>();
			Walk(gameObject.transform, "AIPlaceInfoHolder");
			Dictionary<long, AIPlaceInfoLogic> dictionary = new Dictionary<long, AIPlaceInfoLogic>();
			foreach (JToken item in (jObject["components"]?["AIPlaceLogicEventRaise"] as JArray) ?? new JArray())
			{
				if (index.TryGetValue(item.Value<string>("go") ?? "", out var value))
				{
					AIPlaceLogicEventRaise component = value.gameObject.GetComponent<AIPlaceLogicEventRaise>();
					if (component != null)
					{
						UnityEngine.Object.Destroy(component);
					}
					TerminalTierEventLogic terminalTierEventLogic = value.gameObject.GetComponent<TerminalTierEventLogic>() ?? value.gameObject.AddComponent<TerminalTierEventLogic>();
					JToken jToken = item["fields"];
					terminalTierEventLogic.EventName = jToken?.Value<string>("EventName") ?? "none";
					terminalTierEventLogic.EnterRaise = (jToken?.Value<int?>("EnterRaise") ?? 1) != 0;
					terminalTierEventLogic.ExitRaise = (jToken?.Value<int?>("ExitRaise")).GetValueOrDefault() != 0;
					terminalTierEventLogic.OnlyPlayerNotAI = (jToken?.Value<int?>("OnlyPlayerNotAI") ?? jToken?.Value<int?>("retailExtra_0") ?? 1) != 0;
					dictionary[item.Value<long>("path_id")] = terminalTierEventLogic;
				}
			}
			AIPlaceInfoHolder aIPlaceInfoHolder = ((controller.CoversData != null) ? controller.CoversData.AIPlaceInfoHolder : null);
			if (aIPlaceInfoHolder == null)
			{
				aIPlaceInfoHolder = UnityEngine.Object.FindObjectOfType<AIPlaceInfoHolder>();
			}
			if (aIPlaceInfoHolder != null && aIPlaceInfoHolder.Places == null)
			{
				aIPlaceInfoHolder.Places = new List<AIPlaceInfo>();
			}
			TerminalBlackDivisionPlaces.SetHolder(aIPlaceInfoHolder);
			Door[] allDoors = UnityEngine.Object.FindObjectsOfType<Door>();
			int num = 0;
			int num2 = 0;
			foreach (JToken item2 in (jObject["components"]?["AIPlaceInfo"] as JArray) ?? new JArray())
			{
				string key = item2.Value<string>("go") ?? "";
				if (!index.TryGetValue(key, out var value2))
				{
					continue;
				}
				GameObject gameObject2 = value2.gameObject;
				BoxCollider component2 = gameObject2.GetComponent<BoxCollider>();
				if (component2 == null)
				{
					continue;
				}
				if (!component2.isTrigger || !component2.enabled)
				{
					Plugin.Log.LogDebug($"[AIPlaces] '{gameObject2.name}' collider healed (isTrigger={component2.isTrigger}, enabled={component2.enabled})");
					component2.isTrigger = true;
					component2.enabled = true;
				}
				if (!gameObject2.activeInHierarchy)
				{
					Plugin.Log.LogWarning("[AIPlaces] '" + gameObject2.name + "' is INACTIVE in hierarchy — trigger will not fire");
				}
				AIPlaceInfo aIPlaceInfo = gameObject2.GetComponent<AIPlaceInfo>() ?? gameObject2.AddComponent<AIPlaceInfo>();
				JToken jToken2 = item2["fields"];
				aIPlaceInfo.AreaId = (jToken2?.Value<int?>("AreaId")).GetValueOrDefault();
				aIPlaceInfo.Id = jToken2?.Value<string>("Id") ?? "";
				aIPlaceInfo.IsOnTrigger = (jToken2?.Value<int?>("IsOnTrigger") ?? 1) != 0;
				aIPlaceInfo.BlockGrenade = (jToken2?.Value<int?>("BlockGrenade")).GetValueOrDefault() != 0;
				aIPlaceInfo.IsDark = (jToken2?.Value<int?>("IsDark")).GetValueOrDefault() != 0;
				aIPlaceInfo.IsMute = (jToken2?.Value<int?>("IsMute")).GetValueOrDefault() != 0;
				aIPlaceInfo.IsInside = (jToken2?.Value<int?>("IsInside")).GetValueOrDefault() != 0;
				aIPlaceInfo.AtrZone = (jToken2?.Value<int?>("AtrZone")).GetValueOrDefault() != 0;
				aIPlaceInfo.CoversSpecial = (ECoverPointSpecial)(jToken2?.Value<int?>("CoversSpecial")).GetValueOrDefault();
				aIPlaceInfo.UseAsCoverGroupId = (jToken2?.Value<int?>("UseAsCoverGroupId") ?? 1) != 0;
				aIPlaceInfo.GrenadePlaces = Array.Empty<ThrowGrenadePlace>();
				aIPlaceInfo.Collider = gameObject2.GetComponent<BoxCollider>();
				long valueOrDefault = ((jToken2?["InfoLogicAllEnemy"] as JObject)?.Value<long?>("ref")).GetValueOrDefault();
				if (dictionary.TryGetValue(valueOrDefault, out var value3))
				{
					aIPlaceInfo.InfoLogicAllEnemy = value3;
					num2++;
				}
				int num3 = jToken2?.Value<int?>("ConnectionGroupId") ?? (jToken2?.Value<int?>("retailExtra_0")).GetValueOrDefault();
				string[] array = ((!(jToken2?["BlackDivisionPossibleZones"] is JArray jArray)) ? Array.Empty<string>() : ValueEnumerableExtensions.ToArray<FromEnumerable<string>, string>(ValueEnumerable.AsValueEnumerable<string>(jArray.Values<string>())));
				if (num3 > 0 && array.Length != 0)
				{
					TerminalBlackDivisionLogic terminalBlackDivisionLogic = TerminalBlackDivisionPlaces.Bind(aIPlaceInfo, num3, array);
					if (terminalBlackDivisionLogic != null)
					{
						aIPlaceInfo.InfoLogicAllEnemy = terminalBlackDivisionLogic;
						Plugin.Log.LogDebug("[AIPlaces] Black Division ownership logic bound to '" + gameObject2.name + "' " + $"area={aIPlaceInfo.AreaId} connectionGroup={num3}");
					}
				}
				if (aIPlaceInfoHolder != null && (aIPlaceInfoHolder.Places == null || !aIPlaceInfoHolder.Places.Contains(aIPlaceInfo)))
				{
					aIPlaceInfoHolder.AddPlace(aIPlaceInfo);
				}
				aIPlaceInfo.Init(allDoors, controller);
				if (aIPlaceInfo.InfoLogicAllEnemy != null)
				{
					aIPlaceInfo.InfoLogicAllEnemy.Init(aIPlaceInfo, controller);
				}
				num++;
				Plugin.Log.LogDebug($"[AIPlaces] trigger '{gameObject2.name}' live at {value2.position} size {Vector3.Scale(component2.size, value2.lossyScale)}");
			}
			_marker = new GameObject("Terminal_AIPlacesStaged");
			Scene sceneByName = SceneManager.GetSceneByName("Terminal_AI");
			if (sceneByName.IsValid() && sceneByName.isLoaded)
			{
				SceneManager.MoveGameObjectToScene(_marker, sceneByName);
			}
			Plugin.Log.LogInfo($"[AIPlaces] SPAWN TRIGGERS REBUILT: {num} places, {num2} event logics — botEvent waves armed");
			void Walk(Transform t, string p)
			{
				index[p] = t;
				for (int i = 0; i < t.childCount; i++)
				{
					Transform child = t.GetChild(i);
					Walk(child, p + "/" + child.name);
				}
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[AIPlaces] rebuild failed: {arg}");
		}
	}
}
