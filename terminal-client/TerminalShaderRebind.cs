using System;
using System.Collections;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalShaderRebind
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_RebindAtRaidStart
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On)
			{
				new GameObject("Terminal_ShaderRebind").AddComponent<Host>();
			}
		}
	}

	internal class Host : MonoBehaviour
	{
		private void Start()
		{
			StartCoroutine(Run());
		}

		private IEnumerator Run()
		{
			Renderer[] array = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);
			int slice = 3000;
			int rebound = 0;
			int skipped = 0;
			HashSet<Material> seen = new HashSet<Material>();
			Renderer[] array2 = array;
			foreach (Renderer renderer in array2)
			{
				if (renderer != null)
				{
					Rebind(renderer, seen, ref rebound, ref skipped);
				}
				int num = slice - 1;
				slice = num;
				if (num <= 0)
				{
					slice = 3000;
					yield return null;
				}
			}
			Plugin.Log.LogInfo($"[RebindShaders] raid-start pass: {seen.Count} materials, {rebound} rebound, {skipped} skipped{MissingShaderReport()}");
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	private static readonly Dictionary<string, string> ShaderAliases = new Dictionary<string, string>
	{
		{ "Particles/Explosion", "Particles/SuperAdditive" },
		{ "Particles/Emissive", "Particles/AdditiveSimple" },
		{ "Effects/Explosions/Particles/Alpha Blended", "Particles/AdditiveSimple" }
	};

	private static readonly HashSet<string> RebindExclude = new HashSet<string> { "Cloth/ClothShader", "Cloth/ClothShader_backface" };

	private static readonly HashSet<int> _ourMaterials = new HashSet<int>();

	private static readonly HashSet<int> _rebindDone = new HashSet<int>();

	private static readonly Dictionary<string, int> _missingShaders = new Dictionary<string, int>();

	internal static void ResetForRaid()
	{
		_rebindDone.Clear();
		_missingShaders.Clear();
	}

	internal static void CaptureSceneMaterials(Scene scene)
	{
		try
		{
			int num = 0;
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for (int i = 0; i < rootGameObjects.Length; i++)
			{
				Renderer[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<Renderer>(includeInactive: true);
				for (int j = 0; j < componentsInChildren.Length; j++)
				{
					Material[] sharedMaterials = componentsInChildren[j].sharedMaterials;
					foreach (Material material in sharedMaterials)
					{
						if (material != null && _ourMaterials.Add(material.GetInstanceID()))
						{
							num++;
						}
					}
				}
			}
			Plugin.Log.LogDebug($"[RebindShaders] captured {num} scene-owned material(s) from '{scene.name}'");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[RebindShaders] material capture failed for '" + scene.name + "': " + ex.Message);
		}
	}

	internal static void RebindNow()
	{
		int rebound = 0;
		int skipped = 0;
		HashSet<Material> hashSet = new HashSet<Material>();
		Renderer[] array = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);
		for (int i = 0; i < array.Length; i++)
		{
			Rebind(array[i], hashSet, ref rebound, ref skipped);
		}
		Plugin.Log.LogDebug($"[RebindShaders] sync pass: {hashSet.Count} materials, {rebound} rebound, {skipped} skipped{MissingShaderReport()}");
	}

	internal static int RebindShadersUnder(Transform root)
	{
		if (root == null)
		{
			return 0;
		}
		int num = 0;
		HashSet<Material> hashSet = new HashSet<Material>();
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			Material[] sharedMaterials = componentsInChildren[i].sharedMaterials;
			foreach (Material material in sharedMaterials)
			{
				if (material == null || material.shader == null || !hashSet.Add(material))
				{
					continue;
				}
				string text = material.shader.name;
				if (!RebindExclude.Contains(text))
				{
					if (ShaderAliases.TryGetValue(text, out var value))
					{
						text = value;
					}
					Shader shader = FindGameShader(text);
					if (shader != null && shader != material.shader)
					{
						material.shader = shader;
						num++;
					}
				}
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[RebindShaders] scoped: {num} material(s) under '{root.name}' bound to game shaders");
		}
		return num;
	}

	private static void Rebind(Renderer r, HashSet<Material> seen, ref int rebound, ref int skipped)
	{
		Material[] sharedMaterials = r.sharedMaterials;
		foreach (Material material in sharedMaterials)
		{
			if (material == null || material.shader == null || !seen.Add(material))
			{
				continue;
			}
			if (!_ourMaterials.Contains(material.GetInstanceID()))
			{
				skipped++;
			}
			else
			{
				if (!_rebindDone.Add(material.GetInstanceID()))
				{
					continue;
				}
				string text = material.shader.name;
				if (RebindExclude.Contains(text))
				{
					skipped++;
					continue;
				}
				if (ShaderAliases.TryGetValue(text, out var value))
				{
					text = value;
				}
				Shader shader = FindGameShader(text);
				if (shader != null && shader != material.shader)
				{
					material.shader = shader;
					rebound++;
					continue;
				}
				skipped++;
				if (shader == null)
				{
					_missingShaders.TryGetValue(text, out var value2);
					_missingShaders[text] = value2 + 1;
				}
			}
		}
	}

	private static string MissingShaderReport()
	{
		if (_missingShaders.Count == 0)
		{
			return "";
		}
		List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(_missingShaders);
		list.Sort((KeyValuePair<string, int> a, KeyValuePair<string, int> b) => b.Value.CompareTo(a.Value));
		List<string> list2 = new List<string>();
		for (int num = 0; num < list.Count && num < 10; num++)
		{
			list2.Add($"{list[num].Value}x {list[num].Key}");
		}
		return " | NO game counterpart (bundle copy stays, deferred may be dead): " + string.Join(", ", list2);
	}

	private static Shader FindGameShader(string name)
	{
		Shader shader = Shader.Find(name);
		if (shader == null)
		{
			try
			{
				shader = ShadersFinder.Find(name);
			}
			catch
			{
			}
		}
		return shader;
	}
}
