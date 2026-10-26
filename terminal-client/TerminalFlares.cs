using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MultiFlare;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalFlares
{
	private static bool _built;

	private static string Dir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "flares");

	internal static void ResetForRaid()
	{
		_built = false;
	}

	internal static void TryBuild()
	{
		if (_built)
		{
			return;
		}
		if (!Plugin.LensFlares.Value)
		{
			_built = true;
			Plugin.Log.LogInfo("[Flares] disabled by config (perf A/B lever) — no flares this raid");
			return;
		}
		string path = Path.Combine(Dir, "flares.json");
		if (!File.Exists(path))
		{
			Plugin.Log.LogWarning("[Flares] no flares.json sidecar — lens flares skipped");
			return;
		}
		_built = true;
		JObject root = JObject.Parse(File.ReadAllText(path));
		Texture2D atlasTex = LoadPng(Path.Combine(Dir, "flare_atlas.png"));
		Texture2D texture2D = LoadPng(Path.Combine(Dir, "flare_shit.png"));
		if (atlasTex == null)
		{
			Plugin.Log.LogWarning("[Flares] atlas png missing/unreadable — skipped");
			return;
		}
		ProFlareAtlas proFlareAtlas = ScriptableObject.CreateInstance<ProFlareAtlas>();
		proFlareAtlas.name = "FlareAtlas";
		Type nestedType = typeof(ProFlareAtlas).GetNestedType("Container", BindingFlags.Public | BindingFlags.NonPublic);
		JArray jArray = (root["atlas"]?["containers"] as JArray) ?? new JArray();
		Array array = Array.CreateInstance(nestedType, jArray.Count);
		FieldInfo fieldInfo = AccessTools.Field(nestedType, "_name");
		FieldInfo fieldInfo2 = AccessTools.Field(nestedType, "_rect");
		for (int i = 0; i < jArray.Count; i++)
		{
			object obj = Activator.CreateInstance(nestedType);
			fieldInfo.SetValue(obj, jArray[i].Value<string>("name") ?? $"c{i}");
			JToken jToken = jArray[i]["rect"];
			fieldInfo2.SetValue(obj, new Rect(jToken.Value<float>("x"), jToken.Value<float>("y"), jToken.Value<float>("width"), jToken.Value<float>("height")));
			array.SetValue(obj, i);
		}
		AccessTools.Field(typeof(ProFlareAtlas), "_containers").SetValue(proFlareAtlas, array);
		Material material = MakeSlot("normal", "Custom/multiFlare", "MultiFlare");
		Material material2 = MakeSlot("shit", "Custom/ShitOnScreen", "ShitOnScreen");
		Material material3 = MakeSlot("overlap", "Custom/multiFlare", "FlashLightFlares");
		if (material2 != null && texture2D != null)
		{
			material2.SetTexture("_ShitTex", texture2D);
		}
		if (material == null)
		{
			return;
		}
		JToken jToken2 = root["settings"]?["_settings"];
		JToken jToken3 = jToken2?["_flareOverlapSettings"];
		FlareOverlapSettings flareOverlapSettings = new FlareOverlapSettings
		{
			_capacity = (jToken3?.Value<int?>("_capacity") ?? 256),
			_material = material3,
			_blindProtectionAlphaFactor = (jToken3?.Value<float?>("_blindProtectionAlphaFactor") ?? 1f),
			_blindProtectionSizeFactor = (jToken3?.Value<float?>("_blindProtectionSizeFactor") ?? 1f),
			_alphaMultiplier = (jToken3?.Value<float?>("AlphaMultiplier_1") ?? 1f),
			_sizeMultiplier = (jToken3?.Value<float?>("SizeMultiplier_1") ?? 1f),
			_maxNeighborCount = (jToken3?.Value<int?>("_maxNeighborCount") ?? 5),
			_searchRange = (jToken3?.Value<float?>("_searchRange") ?? 0.5f),
			_maxScaleMultiplier = (jToken3?.Value<float?>("_maxScaleMultiplier") ?? 1f)
		};
		FlareSettings value = new FlareSettings
		{
			_atlas = proFlareAtlas,
			_normalBatch = Batch(jToken2?["_normalBatch"], material, 16384),
			_shitBatch = Batch(jToken2?["_shitBatch"], material2 ?? material, 512),
			_flareOverlapSettings = flareOverlapSettings
		};
		GameObject gameObject = new GameObject("Terminal_FlareSettings");
		gameObject.SetActive(value: false);
		FlareSceneSettings obj2 = gameObject.AddComponent<FlareSceneSettings>();
		AccessTools.Field(typeof(FlareSceneSettings), "_settings").SetValue(obj2, value);
		gameObject.SetActive(value: true);
		Dictionary<string, Transform> dictionary = BuildPathIndex();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		FieldInfo fieldInfo3 = AccessTools.Field(typeof(FlareLight), "_destroyed");
		FieldInfo fieldInfo4 = AccessTools.Field(typeof(FlareLight), "_totalScale");
		FieldInfo fieldInfo5 = AccessTools.Field(typeof(FlareLight), "_totalAlpha");
		FieldInfo fieldInfo6 = AccessTools.Field(typeof(FlareLight), "_flares");
		foreach (JToken item in (root["flares"] as JArray) ?? new JArray())
		{
			string text = item.Value<string>("go");
			if (text == null || !dictionary.TryGetValue(text, out var value2))
			{
				num2++;
				continue;
			}
			if (item["world"] is JArray jArray2)
			{
				Vector3 vector = new Vector3(jArray2.Value<float>(0), jArray2.Value<float>(1), jArray2.Value<float>(2));
				if ((value2.position - vector).sqrMagnitude > 1f)
				{
					num3++;
					continue;
				}
			}
			JToken jToken4 = item["fields"];
			JArray jArray3 = (jToken4?["_flares"] as JArray) ?? new JArray();
			MultiFlare.Flare[] array2 = new MultiFlare.Flare[jArray3.Count];
			for (int j = 0; j < jArray3.Count; j++)
			{
				JToken jToken5 = jArray3[j];
				array2[j] = new MultiFlare.Flare
				{
					_scale = new Vector2(jToken5["_scale"].Value<float>("x"), jToken5["_scale"].Value<float>("y")),
					_alpha = (jToken5.Value<float?>("_alpha") ?? 1f),
					_color = new Color(jToken5["_color"].Value<float>("r"), jToken5["_color"].Value<float>("g"), jToken5["_color"].Value<float>("b"), jToken5["_color"].Value<float>("a")),
					_flareType = (FlareType)jToken5.Value<int?>("_flareType").GetValueOrDefault(),
					_minDist = jToken5.Value<float?>("_minDist").GetValueOrDefault(),
					_maxDist = (jToken5.Value<float?>("_maxDist") ?? 100f),
					_minScale = (jToken5.Value<float?>("_minScale") ?? 1f),
					_maxScale = (jToken5.Value<float?>("_maxScale") ?? 1f),
					_minAlpha = (jToken5.Value<float?>("_minAlpha") ?? 1f),
					_maxAlpha = (jToken5.Value<float?>("_maxAlpha") ?? 1f),
					_texId = jToken5.Value<int?>("_texId").GetValueOrDefault()
				};
			}
			GameObject gameObject2 = value2.gameObject;
			bool activeSelf = gameObject2.activeSelf;
			if (activeSelf)
			{
				gameObject2.SetActive(value: false);
			}
			FlareLight obj3 = gameObject2.AddComponent<FlareLight>();
			fieldInfo3?.SetValue(obj3, jToken4?.Value<bool?>("bool_0") ?? true);
			fieldInfo4?.SetValue(obj3, jToken4?.Value<float?>("_totalScale") ?? 1f);
			fieldInfo5?.SetValue(obj3, jToken4?.Value<float?>("_totalAlpha") ?? 1f);
			fieldInfo6?.SetValue(obj3, array2);
			if (activeSelf)
			{
				gameObject2.SetActive(value: true);
			}
			num++;
		}
		Plugin.Log.LogWarning($"[Flares] LENS FLARES LIVE: {num} placed, {num2} paths unmatched, {num3} position-drifted (skipped)");
		static BatchSettings Batch(JToken t, Material mat, int fallbackCap)
		{
			return new BatchSettings
			{
				_capacity = (t?.Value<int?>("_capacity") ?? fallbackCap),
				_material = mat,
				_blindProtectionAlphaFactor = (t?.Value<float?>("_blindProtectionAlphaFactor") ?? 1f),
				_blindProtectionSizeFactor = (t?.Value<float?>("_blindProtectionSizeFactor") ?? 1f),
				_alphaMultiplier = (t?.Value<float?>("AlphaMultiplier_1") ?? 1f),
				_sizeMultiplier = (t?.Value<float?>("SizeMultiplier_1") ?? 1f)
			};
		}
		Material MakeMat(string shaderName, string matName)
		{
			Shader shader = null;
			try
			{
				shader = ShadersFinder.Find(shaderName);
			}
			catch
			{
			}
			if (shader == null || !shader.isSupported)
			{
				shader = Shader.Find(shaderName);
			}
			if (shader == null || !shader.isSupported)
			{
				Plugin.Log.LogWarning("[Flares] shader '" + shaderName + "' not found — flares degraded");
				return null;
			}
			return new Material(shader)
			{
				name = matName,
				mainTexture = atlasTex
			};
		}
		Material MakeSlot(string slot, string fallbackShader, string matName)
		{
			JToken jToken6 = root["materials"]?[slot];
			Material material4 = MakeMat(jToken6?.Value<string>("shader") ?? fallbackShader, matName);
			if (material4 == null)
			{
				return null;
			}
			if (jToken6?["floats"] is JObject jObject)
			{
				foreach (KeyValuePair<string, JToken> item2 in jObject)
				{
					if (material4.HasProperty(item2.Key))
					{
						material4.SetFloat(item2.Key, item2.Value.Value<float>());
					}
				}
			}
			if (jToken6?["colors"] is JObject jObject2)
			{
				foreach (KeyValuePair<string, JToken> item3 in jObject2)
				{
					if (material4.HasProperty(item3.Key))
					{
						JArray jArray4 = item3.Value as JArray;
						material4.SetColor(item3.Key, new Color(jArray4.Value<float>(0), jArray4.Value<float>(1), jArray4.Value<float>(2), jArray4.Value<float>(3)));
					}
				}
			}
			return material4;
		}
	}

	private static Texture2D LoadPng(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
		if (!texture2D.LoadImage(File.ReadAllBytes(path)))
		{
			return null;
		}
		return texture2D;
	}

	private static Dictionary<string, Transform> BuildPathIndex()
	{
		Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>(131072);
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (sceneAt.isLoaded)
			{
				GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
				foreach (GameObject gameObject in rootGameObjects)
				{
					Walk(gameObject.transform, gameObject.name, dictionary);
				}
			}
		}
		return dictionary;
	}

	private static void Walk(Transform t, string path, Dictionary<string, Transform> index)
	{
		index[path] = t;
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			dictionary.TryGetValue(child.name, out var value);
			dictionary[child.name] = value + 1;
			Walk(child, path + "/" + ((value == 0) ? child.name : (child.name + "~" + value)), index);
		}
	}
}
