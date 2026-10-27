using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalAIBake
{
	[HarmonyPatch(typeof(AICoversData), "RestoreData")]
	internal static class Patch_RestoreCoversData
	{
		[HarmonyPrefix]
		private static void Prefix(AICoversData __instance)
		{
			if (TerminalGate.On && Plugin.RetailAIBake.Value)
			{
				TryFill(__instance);
			}
		}

		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception, AICoversData __instance)
		{
			if (__exception == null || !TerminalGate.On)
			{
				return __exception;
			}
			Plugin.Log.LogWarning("[AIBake] swallowed AICoversData.RestoreData: " + __exception.Message);
			try
			{
				if (__instance != null)
				{
					__instance.Points = new List<GroupPoint>();
					__instance.Ways = new List<GroupPointWay>();
					__instance.Pathes = new List<GroupPointPath>();
					if (__instance.AIPlaceInfoHolder != null && __instance.AIPlaceInfoHolder.Places == null)
					{
						__instance.AIPlaceInfoHolder.Places = new List<AIPlaceInfo>();
					}
				}
				Loaded = false;
			}
			catch
			{
			}
			return null;
		}
	}

	private static JObject _sidecar;

	private static bool _sidecarTried;

	private static GameObject _marker;

	internal static bool Loaded;

	internal static void ReleaseAfterRaid()
	{
		_marker = null;
		_comps.Clear();
		Loaded = false;
	}

	private static readonly Dictionary<long, Component> _comps = new Dictionary<long, Component>();

	private const float SnapRadius = 1.5f;

	private const float RescueRadius = 6f;

	private static string DataDir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", Path.Combine("plugin-data", "aibake"));

	private static JObject Sidecar()
	{
		if (_sidecarTried)
		{
			return _sidecar;
		}
		_sidecarTried = true;
		try
		{
			string text = Path.Combine(DataDir, "terminal_aibake.json");
			if (File.Exists(text))
			{
				_sidecar = JObject.Parse(File.ReadAllText(text));
			}
			else
			{
				Plugin.Log.LogDebug("[AIBake] no sidecar at " + text + " — synthesized AI graph stays");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[AIBake] sidecar parse failed: " + ex.Message);
		}
		return _sidecar;
	}

	public static void TryFill(AICoversData covers)
	{
		Loaded = false;
		if (_marker != null)
		{
			Loaded = true;
			return;
		}
		JObject jObject = Sidecar();
		if (jObject == null || covers == null || !TerminalGate.On)
		{
			return;
		}
		try
		{
			JObject comps = jObject["components"] as JObject;
			_comps.Clear();
			Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>();
			Scene sceneByName = SceneManager.GetSceneByName("Terminal_AI");
			if (!sceneByName.IsValid() || !sceneByName.isLoaded)
			{
				Plugin.Log.LogDebug("[AIBake] AI scene not loaded");
				return;
			}
			GameObject[] rootGameObjects = sceneByName.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				Walk(gameObject.transform, gameObject.name, dictionary);
			}
			AICorePointHolder aICorePointHolder = UnityEngine.Object.FindObjectOfType<AICorePointHolder>();
			List<AICorePoint> list = new List<AICorePoint>();
			foreach (JToken item3 in Rows(comps, "AICorePoint"))
			{
				if (dictionary.TryGetValue(item3.Value<string>("go") ?? "", out var value))
				{
					AICorePoint aICorePoint = value.gameObject.GetComponent<AICorePoint>() ?? value.gameObject.AddComponent<AICorePoint>();
					FillFields(aICorePoint, item3["fields"] as JObject, (string name) => name != "ConnectionsAtNet");
					_comps[item3.Value<long>("path_id")] = aICorePoint;
					list.Add(aICorePoint);
				}
			}
			foreach (JToken item4 in Rows(comps, "AICorePoint"))
			{
				AICorePoint aICorePoint2 = Comp<AICorePoint>(item4);
				if ((object)aICorePoint2 == null || !(item4["fields"]?["ConnectionsAtNet"] is JArray jArray))
				{
					continue;
				}
				List<AICorePoint> list2 = new List<AICorePoint>();
				foreach (JToken item5 in jArray)
				{
					if (Ref(item5) is AICorePoint item)
					{
						list2.Add(item);
					}
				}
				AccessTools.Field(typeof(AICorePoint), "ConnectionsAtNet")?.SetValue(aICorePoint2, list2);
			}
			if (aICorePointHolder != null)
			{
				aICorePointHolder.CorePoints = list;
			}
			RebuildOnGos<AIMinePoint>(comps, "AIMinePoint", dictionary);
			RebuildOnGos<NavMeshDoorLink>(comps, "NavMeshDoorLink", dictionary);
			int num = 0;
			int num2 = 0;
			NavMeshDoorLink[] array = UnityEngine.Object.FindObjectsOfType<NavMeshDoorLink>();
			foreach (NavMeshDoorLink navMeshDoorLink in array)
			{
				navMeshDoorLink.ShallTryInteract = true;
				navMeshDoorLink.MidOpen = (navMeshDoorLink.Open1 + navMeshDoorLink.Open2) / 2f;
				navMeshDoorLink.MidClose = (navMeshDoorLink.Close1 + navMeshDoorLink.Close2_Normal) / 2f;
				if (!(navMeshDoorLink.Carver_Opened != null) || !(navMeshDoorLink.Carver_Breached != null) || !(navMeshDoorLink.Carver_Closed != null))
				{
					try
					{
						navMeshDoorLink.TryCreateCrave();
						num++;
					}
					catch
					{
						num2++;
						UnityEngine.Object.Destroy(navMeshDoorLink);
					}
				}
			}
			if (num + num2 > 0)
			{
				Plugin.Log.LogInfo($"[AIBake] door link carvers rebuilt: {num} ok, {num2} dropped");
			}
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			List<PatrolPoint> list3 = new List<PatrolPoint>();
			List<PatrolWay> list4 = new List<PatrolWay>();
			foreach (JToken item6 in Rows(comps, "PatrolPoint"))
			{
				if (dictionary.TryGetValue(item6.Value<string>("go") ?? "", out var value2))
				{
					PatrolPoint patrolPoint = value2.gameObject.GetComponent<PatrolPoint>() ?? value2.gameObject.AddComponent<PatrolPoint>();
					_comps[item6.Value<long>("path_id")] = patrolPoint;
					num3++;
					list3.Add(patrolPoint);
				}
			}
			foreach (JToken item7 in Rows(comps, "PatrolWay"))
			{
				if (dictionary.TryGetValue(item7.Value<string>("go") ?? "", out var value3))
				{
					PatrolWay patrolWay = value3.gameObject.GetComponent<PatrolWay>() ?? value3.gameObject.AddComponent<PatrolWay>();
					_comps[item7.Value<long>("path_id")] = patrolWay;
					num4++;
					list4.Add(patrolWay);
				}
			}
			foreach (JToken item8 in Rows(comps, "PatrolPoint"))
			{
				PatrolPoint patrolPoint2 = Comp<PatrolPoint>(item8);
				if ((object)patrolPoint2 != null)
				{
					FillFields(patrolPoint2, item8["fields"] as JObject, null);
				}
			}
			foreach (JToken item9 in Rows(comps, "PatrolWay"))
			{
				PatrolWay patrolWay2 = Comp<PatrolWay>(item9);
				if ((object)patrolWay2 != null)
				{
					FillFields(patrolWay2, item9["fields"] as JObject, null);
				}
			}
			SnapPatrolPointsToNavMesh(list3, list4);
			foreach (JToken item10 in Rows(comps, "BotZone"))
			{
				if (!dictionary.TryGetValue(item10.Value<string>("go") ?? "", out var value4))
				{
					continue;
				}
				BotZone component = value4.GetComponent<BotZone>();
				if (component == null)
				{
					continue;
				}
				List<PatrolWay> list5 = new List<PatrolWay>();
				foreach (JToken item11 in (item10["fields"]?["PatrolWays"] as JArray) ?? new JArray())
				{
					if (Ref(item11) is PatrolWay item2)
					{
						list5.Add(item2);
					}
				}
				component.PatrolWays = list5.ToArray();
				num5++;
				num6 += list5.Count;
			}
			Plugin.Log.LogDebug($"[AIBake] patrol routes rebuilt: {num3} points, {num4} ways, " + $"{num5} zones wired ({num6} memberships)");
			AIVoxelesData aIVoxelesData = UnityEngine.Object.FindObjectOfType<AIVoxelesData>();
			JToken first = Rows(comps, "AIVoxelesData").First;
			if (aIVoxelesData != null && first?["fields"] is JObject jObject2)
			{
				List<NavGraphVoxelSimple> list6 = new List<NavGraphVoxelSimple>();
				foreach (JToken item12 in (jObject2["VoxelsList"] as JArray) ?? new JArray())
				{
					NavGraphVoxelSimple navGraphVoxelSimple = new NavGraphVoxelSimple(V3(item12["Position"]), item12.Value<int?>("IndexX").GetValueOrDefault(), item12.Value<int?>("IndexY").GetValueOrDefault(), item12.Value<int?>("IndexZ").GetValueOrDefault(), item12.Value<int?>("Id").GetValueOrDefault());
					FillFields(navGraphVoxelSimple, item12 as JObject, (string name) => name != "Position" && name != "IndexX" && name != "IndexY" && name != "IndexZ" && name != "Id");
					if (navGraphVoxelSimple.DoorLinks == null)
					{
						navGraphVoxelSimple.DoorLinks = new List<NavMeshDoorLink>();
					}
					list6.Add(navGraphVoxelSimple);
				}
				aIVoxelesData.VoxelsList = list6;
				aIVoxelesData.MaxX = jObject2.Value<int?>("MaxX").GetValueOrDefault();
				aIVoxelesData.MaxY = jObject2.Value<int?>("MaxY").GetValueOrDefault();
				aIVoxelesData.MaxZ = jObject2.Value<int?>("MaxZ").GetValueOrDefault();
				aIVoxelesData.MinVoxelesValues = V3(jObject2["MinVoxelesValues"]);
				aIVoxelesData.MaxVoxelesValues = V3(jObject2["MaxVoxelesValues"]);
			}
			AIPatrolsData aIPatrolsData = UnityEngine.Object.FindObjectOfType<AIPatrolsData>();
			JToken first2 = Rows(comps, "AIPatrolsData").First;
			if (aIPatrolsData != null && first2?["fields"] is JObject fields)
			{
				FillFields(aIPatrolsData, fields, null);
			}
			FillSingle<AIMinesPositionsHolder>(comps, "AIMinesPositionsHolder");
			FillSingle<AIManualPointsHolder>(comps, "AIManualPointsHolder");
			FillSingle<AIDangerPlacesHolder>(comps, "AIDangerPlacesHolder");
			FillSingle<BotZoneEntranceInfo>(comps, "BotZoneEntranceInfo");
			if (Rows(comps, "AICoversData").First?["fields"] is JObject jObject3)
			{
				HashSet<int> hashSet = new HashSet<int>();
				foreach (JToken item13 in (jObject3["Points"] as JArray) ?? new JArray())
				{
					hashSet.Add(item13.Value<int?>("Id").GetValueOrDefault());
				}
				List<GroupPointWay> list7 = new List<GroupPointWay>();
				HashSet<int> hashSet2 = new HashSet<int>();
				int num7 = 0;
				foreach (JToken item14 in (jObject3["Ways"] as JArray) ?? new JArray())
				{
					int valueOrDefault = item14.Value<int?>("IdTarget").GetValueOrDefault();
					if (!hashSet.Contains(valueOrDefault))
					{
						num7++;
						continue;
					}
					GroupPointWay groupPointWay = (GroupPointWay)FormatterServices.GetUninitializedObject(typeof(GroupPointWay));
					FillFields(groupPointWay, item14 as JObject, null);
					list7.Add(groupPointWay);
					hashSet2.Add(item14.Value<int?>("Id").GetValueOrDefault());
				}
				List<GroupPoint> list8 = new List<GroupPoint>();
				int num8 = 0;
				long num9 = 0L;
				foreach (JToken item15 in (jObject3["Points"] as JArray) ?? new JArray())
				{
					if (item15["NeighbourhoodsWaysIds"] is JArray jArray2)
					{
						JArray jArray3 = new JArray();
						foreach (JToken item16 in jArray2)
						{
							if (hashSet2.Contains(item16.Value<int>()))
							{
								jArray3.Add(item16.Value<int>());
							}
							else
							{
								num9++;
							}
						}
						item15["NeighbourhoodsWaysIds"] = jArray3;
					}
					GroupPoint groupPoint = (GroupPoint)FormatterServices.GetUninitializedObject(typeof(GroupPoint));
					FillFields(groupPoint, item15 as JObject, null);
					list8.Add(groupPoint);
					int valueOrDefault2 = item15.Value<int?>("Id").GetValueOrDefault();
					if (valueOrDefault2 > num8)
					{
						num8 = valueOrDefault2;
					}
				}
				if (num7 > 0 || num9 > 0)
				{
					Plugin.Log.LogDebug($"[AIBake] id-space reconcile: {num7} orphan way(s) dropped, " + $"{num9} dangling neighborhood refs stripped (retail serialized {list7.Count} of the referenced ways)");
				}
				covers.Points = list8;
				covers.Ways = list7;
				covers.Pathes = new List<GroupPointPath>();
				AccessTools.Field(typeof(AICoversData), "_lastId")?.SetValue(covers, num8 + 1);
			}
			covers.Voxels = aIVoxelesData;
			covers.Patrols = aIPatrolsData;
			covers.AICorePointsHolder = aICorePointHolder;
			covers.AIManualPointsHolder = UnityEngine.Object.FindObjectOfType<AIManualPointsHolder>();
			covers.AIMinesPositions = UnityEngine.Object.FindObjectOfType<AIMinesPositionsHolder>();
			covers.AIDangerPlacesHolder = UnityEngine.Object.FindObjectOfType<AIDangerPlacesHolder>();
			covers.AIPlaceInfoHolder = UnityEngine.Object.FindObjectOfType<AIPlaceInfoHolder>();
			if (covers.AIPlaceInfoHolder != null && covers.AIPlaceInfoHolder.Places == null)
			{
				covers.AIPlaceInfoHolder.Places = new List<AIPlaceInfo>();
			}
			covers.EntranceInfo = UnityEngine.Object.FindObjectOfType<BotZoneEntranceInfo>();
			_marker = new GameObject("Terminal_AIBakeStaged");
			SceneManager.MoveGameObjectToScene(_marker, sceneByName);
			Loaded = true;
			Plugin.Log.LogDebug($"[AIBake] RETAIL AI BAKE LOADED: {covers.Points.Count} covers, {covers.Ways.Count} ways, " + $"{((aIVoxelesData != null) ? aIVoxelesData.VoxelsList.Count : 0)} voxels, {list.Count} cores — RestoreData gets the real thing");
		}
		catch (Exception arg)
		{
			Loaded = false;
			Plugin.Log.LogWarning($"[AIBake] fill failed — synthesized graph takes over: {arg}");
		}
	}

	private static void SnapPatrolPointsToNavMesh(List<PatrolPoint> points, List<PatrolWay> ways)
	{
		if (points == null || points.Count == 0)
		{
			return;
		}
		try
		{
			bool flag = false;
			try
			{
				flag = NavMesh.CalculateTriangulation().indices.Length != 0;
			}
			catch
			{
			}
			if (!flag)
			{
				Plugin.Log.LogError("[AIBake] navmesh has no triangulation at patrol rebuild — patrol snap SKIPPED (sampling an unbuilt navmesh would drop every route)");
				return;
			}
			List<PatrolPoint> list = new List<PatrolPoint>();
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			float num4 = 0f;
			foreach (PatrolPoint point in points)
			{
				if (point == null)
				{
					continue;
				}
				Vector3 position = point.transform.position;
				float maxDistance = 1.5f;
				bool flag2 = NavMesh.SamplePosition(position, out var hit, maxDistance, -1);
				if (!flag2)
				{
					maxDistance = 6f;
					flag2 = NavMesh.SamplePosition(position, out hit, maxDistance, -1);
					if (flag2)
					{
						num3++;
					}
				}
				if (!flag2)
				{
					list.Add(point);
					continue;
				}
				float num5 = Vector3.Distance(position, hit.position);
				if (num5 > num4)
				{
					num4 = num5;
				}
				if (num5 > 0.05f)
				{
					point.transform.position = hit.position;
					num2++;
				}
				else
				{
					num++;
				}
			}
			float num6 = 1f - (float)list.Count / (float)points.Count;
			if (num6 < 0.25f)
			{
				Plugin.Log.LogError($"[AIBake] patrol snap ABORTED: only {num6:P0} of {points.Count} points found navmesh " + "— that reads as a navmesh problem, not a patrol-data problem. nothing dropped");
				return;
			}
			int num7 = 0;
			if (list.Count > 0 && ways != null)
			{
				HashSet<PatrolPoint> dead = new HashSet<PatrolPoint>(list);
				foreach (PatrolWay way in ways)
				{
					if (!(way == null) && way.Points != null)
					{
						num7 += way.Points.RemoveAll((PatrolPoint p) => p == null || dead.Contains(p));
					}
				}
			}
			Plugin.Log.LogDebug($"[AIBake] patrol navmesh reconcile: {num} already on-mesh, {num2} snapped " + $"({num3} needed the {6f}m rescue, worst {num4:F2}m), " + $"{list.Count} off-mesh -> {num7} way membership(s) pruned");
			if (num2 == 0 && list.Count == 0)
			{
				Plugin.Log.LogDebug("[AIBake] every patrol point was already on the navmesh — the standing-still stall is NOT patrol placement");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[AIBake] patrol snap failed: " + ex.Message + " — points left as authored");
		}
	}

	private static void Walk(Transform t, string path, Dictionary<string, Transform> index)
	{
		index[path] = t;
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			Walk(child, path + "/" + child.name, index);
		}
	}

	private static JArray Rows(JObject comps, string cls)
	{
		return (comps?[cls] as JArray) ?? new JArray();
	}

	private static T Comp<T>(JToken row) where T : Component
	{
		if (!_comps.TryGetValue(row.Value<long>("path_id"), out var value))
		{
			return null;
		}
		return value as T;
	}

	private static Component Ref(JToken tok)
	{
		long? num = (tok as JObject)?.Value<long?>("ref");
		if (!num.HasValue || !_comps.TryGetValue(num.Value, out var value))
		{
			return null;
		}
		return value;
	}

	private static void RebuildOnGos<T>(JObject comps, string cls, Dictionary<string, Transform> index) where T : Component
	{
		int num = 0;
		foreach (JToken item in Rows(comps, cls))
		{
			if (index.TryGetValue(item.Value<string>("go") ?? "", out var value))
			{
				T val = value.gameObject.GetComponent<T>() ?? value.gameObject.AddComponent<T>();
				FillFields(val, item["fields"] as JObject, null);
				_comps[item.Value<long>("path_id")] = val;
				num++;
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[AIBake] {cls}: {num} rebuilt");
		}
	}

	private static void FillSingle<T>(JObject comps, string cls) where T : Component
	{
		JToken first = Rows(comps, cls).First;
		T val = UnityEngine.Object.FindObjectOfType<T>();
		if (first?["fields"] is JObject fields && val != null)
		{
			FillFields(val, fields, null);
			_comps[first.Value<long>("path_id")] = val;
		}
	}

	private static void FillFields(object target, JObject fields, Func<string, bool> filter)
	{
		if (target == null || fields == null)
		{
			return;
		}
		Type type = target.GetType();
		foreach (JProperty item in fields.Properties())
		{
			if ((filter != null && !filter(item.Name)) || item.Name.StartsWith("retailExtra"))
			{
				continue;
			}
			FieldInfo fieldInfo = AccessTools.Field(type, item.Name);
			if (fieldInfo == null)
			{
				continue;
			}
			try
			{
				object obj = ConvertToken(item.Value, fieldInfo.FieldType, fieldInfo.GetValue(target));
				if (obj != null || !fieldInfo.FieldType.IsValueType)
				{
					fieldInfo.SetValue(target, obj);
				}
			}
			catch
			{
			}
		}
	}

	private static object ConvertToken(JToken tok, Type type, object existing)
	{
		if (tok == null || tok.Type == JTokenType.Null)
		{
			return null;
		}
		if (type == typeof(string))
		{
			return tok.Value<string>();
		}
		if (type == typeof(bool))
		{
			return (tok.Type == JTokenType.Boolean) ? tok.Value<bool>() : (tok.Value<int>() != 0);
		}
		if (type == typeof(short))
		{
			return (short)tok.Value<int>();
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, tok.Value<long>());
		}
		if (type.IsPrimitive)
		{
			return Convert.ChangeType(((JValue)tok).Value, type, CultureInfo.InvariantCulture);
		}
		if (tok is JObject jObject)
		{
			if (jObject["ref"] != null && jObject.Count <= 2)
			{
				Component component = Ref(jObject);
				if (!(component != null) || !type.IsInstanceOfType(component))
				{
					return null;
				}
				return component;
			}
			if (type == typeof(Vector2))
			{
				return new Vector2(jObject.Value<float>("x"), jObject.Value<float>("y"));
			}
			if (type == typeof(Vector3))
			{
				return V3(jObject);
			}
			if (type == typeof(Quaternion))
			{
				return new Quaternion(jObject.Value<float>("x"), jObject.Value<float>("y"), jObject.Value<float>("z"), jObject.Value<float>("w"));
			}
			if (type == typeof(Color))
			{
				return new Color(jObject.Value<float>("r"), jObject.Value<float>("g"), jObject.Value<float>("b"), jObject.Value<float>("a"));
			}
			if (type == typeof(Bounds))
			{
				return new Bounds(V3(jObject["m_Center"]), V3(jObject["m_Extent"]) * 2f);
			}
			if (typeof(UnityEngine.Object).IsAssignableFrom(type))
			{
				return null;
			}
			object obj = existing;
			if (obj == null)
			{
				try
				{
					obj = Activator.CreateInstance(type, nonPublic: true);
				}
				catch
				{
					obj = FormatterServices.GetUninitializedObject(type);
				}
			}
			FillFields(obj, jObject, null);
			return obj;
		}
		if (tok is JArray jArray)
		{
			Type type2;
			if (type.IsArray)
			{
				type2 = type.GetElementType();
			}
			else
			{
				type2 = ((type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) ? type.GetGenericArguments()[0] : null);
			}
			if (type2 == null)
			{
				return null;
			}
			List<object> list = new List<object>(jArray.Count);
			foreach (JToken item2 in jArray)
			{
				object item;
				if (item2 is JObject jObject2 && jObject2["ref"] == null && !type2.IsPrimitive && !type2.IsEnum && type2 != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type2) && type2.IsClass)
				{
					object obj3;
					try
					{
						obj3 = Activator.CreateInstance(type2, nonPublic: true);
					}
					catch
					{
						obj3 = FormatterServices.GetUninitializedObject(type2);
					}
					FillFields(obj3, jObject2, null);
					item = obj3;
				}
				else
				{
					item = ConvertToken(item2, type2, null);
				}
				list.Add(item);
			}
			if (type.IsArray)
			{
				Array array = Array.CreateInstance(type2, list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					array.SetValue(list[i], i);
				}
				return array;
			}
			IList list2 = (IList)Activator.CreateInstance(type);
			{
				foreach (object item3 in list)
				{
					list2.Add(item3);
				}
				return list2;
			}
		}
		return null;
	}

	private static Vector3 V3(JToken t)
	{
		if (t is JArray jArray)
		{
			return new Vector3(jArray[0].Value<float>(), jArray[1].Value<float>(), jArray[2].Value<float>());
		}
		return new Vector3(t.Value<float>("x"), t.Value<float>("y"), t.Value<float>("z"));
	}
}
