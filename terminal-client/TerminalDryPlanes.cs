using System;
using System.IO;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Manimal.Terminal;

internal static class TerminalDryPlanes
{
	private static bool _staged;

	internal static void ResetForRaid()
	{
		_staged = false;
	}

	internal static void TryStage()
	{
		if (_staged || !TerminalGate.On || (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null) == null)
		{
			return;
		}
		_staged = true;
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_dryplanes.json");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[DryPlanes] terminal_dryplanes.json missing — rain falls indoors");
			}
			else if (JObject.Parse(File.ReadAllText(path))["planes"] is JArray { Count: not 0 } jArray)
			{
				Vector3[] array = new Vector3[jArray.Count * 4];
				int[] array2 = new int[jArray.Count * 12];
				int num = 0;
				int num2 = 0;
				foreach (JObject item in jArray)
				{
					Vector3 vector = ReadV3(item["p"]);
					Quaternion quaternion = ReadQ(item["r"]);
					Vector3 b = ReadV3(item["s"]);
					int num3 = num;
					array[num++] = vector + quaternion * Vector3.Scale(new Vector3(-0.5f, -0.5f, 0f), b);
					array[num++] = vector + quaternion * Vector3.Scale(new Vector3(0.5f, -0.5f, 0f), b);
					array[num++] = vector + quaternion * Vector3.Scale(new Vector3(-0.5f, 0.5f, 0f), b);
					array[num++] = vector + quaternion * Vector3.Scale(new Vector3(0.5f, 0.5f, 0f), b);
					array2[num2++] = num3;
					array2[num2++] = num3 + 2;
					array2[num2++] = num3 + 1;
					array2[num2++] = num3 + 2;
					array2[num2++] = num3 + 3;
					array2[num2++] = num3 + 1;
					array2[num2++] = num3;
					array2[num2++] = num3 + 1;
					array2[num2++] = num3 + 2;
					array2[num2++] = num3 + 1;
					array2[num2++] = num3 + 3;
					array2[num2++] = num3 + 2;
				}
				Mesh mesh = new Mesh
				{
					name = "Terminal_DryPlanes_Combined"
				};
				mesh.indexFormat = IndexFormat.UInt32;
				mesh.vertices = array;
				mesh.triangles = array2;
				mesh.RecalculateBounds();
				GameObject gameObject = new GameObject("Terminal_WeatherObstacle");
				WeatherObstacle weatherObstacle = gameObject.AddComponent<WeatherObstacle>();
				MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();
				if (!meshCollider)
				{
					meshCollider = gameObject.AddComponent<MeshCollider>();
				}
				meshCollider.sharedMesh = mesh;
				weatherObstacle.MeshCollider = meshCollider;
				Plugin.Log.LogInfo($"[DryPlanes] rain occluder rebuilt: {jArray.Count} authored quads, " + $"{array.Length} verts — indoor rain and wet-status now respect roofs");
			}
			else
			{
				Plugin.Log.LogWarning("[DryPlanes] no planes in sidecar");
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[DryPlanes] staging failed: {arg}");
		}
	}

	private static Vector3 ReadV3(JToken t)
	{
		return new Vector3((float)t[0], (float)t[1], (float)t[2]);
	}

	private static Quaternion ReadQ(JToken t)
	{
		return new Quaternion((float)t[0], (float)t[1], (float)t[2], (float)t[3]);
	}
}
