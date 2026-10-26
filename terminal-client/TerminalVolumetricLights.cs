using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT.Interactive;
using EFT.Settings;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalVolumetricLights
{
	private class Row
	{
		public string scene;

		public string path;

		public float range;

		public float spot;

		public int sample;

		public float scatter;

		public float extinction;

		public float skybox;

		public float mieG;

		public bool noise;

		public float noiseScale;

		public float noiseIntensity;

		public float noiseVel;

		public float maxRayLength;
	}

	private static readonly FieldInfo _cloVol = AccessTools.Field(typeof(CullingLightObject), "_volumetricLight");

	private static readonly FieldInfo _lampVol = AccessTools.Field(typeof(LampController), "list_2");

	private static List<Row> LoadRows()
	{
		try
		{
			string text = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "plugin-data", "volumetric_lights.json");
			if (!File.Exists(text))
			{
				Plugin.Log.LogWarning("[Volumetric] sidecar missing: " + text + " — no beams restored");
				return null;
			}
			return JsonConvert.DeserializeObject<List<Row>>(File.ReadAllText(text));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Volumetric] sidecar unreadable: " + ex.Message);
			return null;
		}
	}

	internal static void Restore()
	{
		if (!TerminalGate.On)
		{
			return;
		}
		if (!SettingOn())
		{
			Plugin.Log.LogInfo("[Volumetric] graphics setting off — skipping restore");
			return;
		}
		List<Row> list = LoadRows();
		if (list == null || list.Count == 0)
		{
			return;
		}
		Dictionary<string, List<Light>> dictionary = new Dictionary<string, List<Light>>();
		Light[] array = Resources.FindObjectsOfTypeAll<Light>();
		foreach (Light light in array)
		{
			if (light == null)
			{
				continue;
			}
			Scene scene = light.gameObject.scene;
			if (scene.IsValid() && scene.name != null && scene.name.StartsWith("Terminal"))
			{
				string key = PathOf(light.transform);
				if (!dictionary.TryGetValue(key, out var value))
				{
					value = (dictionary[key] = new List<Light>());
				}
				value.Add(light);
			}
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		foreach (Row item in list)
		{
			if (item.scene != null && item.scene.Contains("PortCutscene") && !SceneManager.GetSceneByName(item.scene).isLoaded)
			{
				num4++;
				continue;
			}
			if (!dictionary.TryGetValue(item.path, out var value2))
			{
				num3++;
				continue;
			}
			bool flag = false;
			foreach (Light item2 in value2)
			{
				if (!(Mathf.Abs(item2.range - item.range) > 0.01f) && !(Mathf.Abs(item2.spotAngle - item.spot) > 0.05f))
				{
					flag = true;
					if (item2.GetComponent<VolumetricLight>() != null)
					{
						num2++;
					}
					else if (Attach(item2, item))
					{
						num++;
					}
				}
			}
			if (!flag)
			{
				num3++;
			}
		}
		if (num3 > 0)
		{
			Plugin.Log.LogWarning($"[Volumetric] {num3}/{list.Count} entries matched no light — scene drift, report this");
		}
		Plugin.Log.LogInfo($"[Volumetric] {num} volumetric lights restored" + ((num2 > 0) ? $" ({num2} already had one)" : "") + ((num4 > 0) ? $", {num4} deferred to cutscene load" : ""));
	}

	private static bool Attach(Light light, Row e)
	{
		GameObject gameObject = light.gameObject;
		bool activeSelf = gameObject.activeSelf;
		try
		{
			if (activeSelf)
			{
				gameObject.SetActive(value: false);
			}
			VolumetricLight volumetricLight = gameObject.AddComponent<VolumetricLight>();
			volumetricLight.SampleCount = e.sample;
			volumetricLight.ScatteringCoef = e.scatter;
			volumetricLight.ExtinctionCoef = e.extinction;
			volumetricLight.MieG = e.mieG;
			volumetricLight.Noise = e.noise;
			volumetricLight.NoiseScale = e.noiseScale;
			volumetricLight.NoiseIntensity = e.noiseIntensity;
			volumetricLight.NoiseVelocity = new Vector2(e.noiseVel, e.noiseVel);
			if (e.maxRayLength > 0f)
			{
				volumetricLight.MaxRayLength = e.maxRayLength;
			}
			Rebind(light, volumetricLight);
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Volumetric] attach failed on " + e.path + ": " + ex.Message);
			return false;
		}
		finally
		{
			if (activeSelf)
			{
				gameObject.SetActive(value: true);
			}
		}
	}

	private static void Rebind(Light light, VolumetricLight vl)
	{
		CullingLightObject component = light.GetComponent<CullingLightObject>();
		if (component != null)
		{
			_cloVol?.SetValue(component, vl);
		}
		LampController componentInParent = light.GetComponentInParent<LampController>();
		if (componentInParent == null || _lampVol == null)
		{
			return;
		}
		bool flag = false;
		Light[] lights = componentInParent.Lights;
		if (lights != null)
		{
			Light[] array = lights;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] == light)
				{
					flag = true;
					break;
				}
			}
		}
		if (flag && _lampVol.GetValue(componentInParent) is List<VolumetricLight> list && !list.Contains(vl))
		{
			list.Add(vl);
			vl.enabled = light.enabled;
		}
	}

	private static bool SettingOn()
	{
		try
		{
			return Singleton<SettingsManager>.Instance?.Graphics.Settings.VolumetricLight.Value ?? true;
		}
		catch
		{
			return true;
		}
	}

	private static string PathOf(Transform t)
	{
		StringBuilder stringBuilder = new StringBuilder(t.name);
		Transform parent = t.parent;
		while (parent != null)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}
}
