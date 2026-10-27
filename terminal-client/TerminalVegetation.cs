using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal sealed class TerminalVegetation : MonoBehaviour
{
	internal sealed class TramplerAwakePatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(GrassTrampler), "Awake");
		}

		[PatchPrefix]
		private static void Prefix(GrassTrampler __instance)
		{
			if (!Owns(__instance.gameObject.scene))
			{
				return;
			}
			try
			{
				RestoreTrampler(__instance);
			}
			catch (Exception arg)
			{
				Plugin.Log.LogError($"[Vegetation] trampler restoration failed: {arg}");
			}
		}
	}

	internal sealed class RaidStartedPatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(GameWorld), "OnGameStarted");
		}

		[PatchPostfix]
		private static void Postfix(GameWorld __instance)
		{
			if (TerminalGate.On && !(_instance != null))
			{
				GameObject gameObject = new GameObject("Manimal_TerminalVegetation");
				gameObject.transform.SetParent(__instance.transform, worldPositionStays: false);
				_instance = gameObject.AddComponent<TerminalVegetation>();
			}
		}
	}

	private static TerminalVegetation _instance;

	internal static void ReleaseAfterRaid()
	{
		TerminalVegetation instance = _instance;
		_instance = null;
		if (instance)
		{
			SceneManager.sceneLoaded -= instance.OnSceneLoaded;
			instance.StopAllCoroutines();
			instance.enabled = false;
			TerminalRaidCleanup.DestroyOwned(instance.gameObject);
		}
	}

	private static JObject _data;

	private SoundBank _bank;

	private readonly List<AudioClip> _clips = new List<AudioClip>();

	private readonly List<TreeInteractive> _repaired = new List<TreeInteractive>();

	private static string DataDirectory => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "plugin-data");

	private static JObject Data
	{
		get
		{
			if (_data != null)
			{
				return _data;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(Path.Combine(DataDirectory, "terminal_vegetation.json")));
			if (jObject.Value<int>("Version") != 1 || !(jObject["GrassTrampler"] is JObject) || !(jObject["BushBank"] is JObject) || !(jObject["BushClips"] is JArray { Count: not 0 }))
			{
				throw new InvalidDataException("Invalid Terminal vegetation data");
			}
			_data = jObject;
			return jObject;
		}
	}

	private static bool Owns(Scene scene)
	{
		if (scene.IsValid())
		{
			return scene.name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	internal static void RestoreTrampler(GrassTrampler trampler)
	{
		if (trampler._angleCurve == null || trampler._angleCurve.length <= 0 || !(trampler.TransitionTimeBetweenStates > 0f) || !(trampler.StandGrassValues.ReturnTime > 0f) || !(trampler.DuckGrassValues.ReturnTime > 0f) || !(trampler.ProneGrassValues.ReturnTime > 0f))
		{
			JObject jObject = (JObject)Data["GrassTrampler"];
			JsonUtility.FromJsonOverwrite(jObject.ToString(), trampler);
			trampler._angleCurve = TerminalSerializedCurves.FromUnity(jObject["_angleCurve"]);
			if (trampler._angleCurve == null || trampler._angleCurve.length == 0)
			{
				throw new InvalidDataException("Terminal grass bending curve is empty");
			}
			Plugin.Log.LogInfo("[Vegetation] restored authored grass bending for standing, crouching and prone movement");
		}
	}

	private IEnumerator Start()
	{
		JObject data;
		try
		{
			data = Data;
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[Vegetation] {arg}");
			yield break;
		}
		foreach (JToken item in (JArray)data["BushClips"])
		{
			string name = (string?)item;
			if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
			{
				Plugin.Log.LogError("[Vegetation] invalid bush clip filename");
				yield break;
			}
			string text = Path.Combine(DataDirectory, "vegetation", name);
			if (!File.Exists(text))
			{
				Plugin.Log.LogError("[Vegetation] missing bush clip: " + text);
				yield break;
			}
			using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(text).AbsoluteUri, AudioType.WAV);
			yield return request.SendWebRequest();
			if (request.result != UnityWebRequest.Result.Success)
			{
				Plugin.Log.LogError("[Vegetation] bush clip load failed: " + name + ": " + request.error);
				yield break;
			}
			AudioClip content = DownloadHandlerAudioClip.GetContent(request);
			if (content == null || content.samples == 0)
			{
				Plugin.Log.LogError("[Vegetation] bush clip is empty: " + name);
				yield break;
			}
			content.name = Path.GetFileNameWithoutExtension(name);
			_clips.Add(content);
		}
		_bank = ScriptableObject.CreateInstance<SoundBank>();
		JsonUtility.FromJsonOverwrite(data["BushBank"].ToString(), _bank);
		_bank.name = "TreeInteractive_TerminalRuntime";
		_bank.Environments = new EnvironmentVariety[2]
		{
			new EnvironmentVariety(),
			new EnvironmentVariety()
		};
		_bank.Environments[0].Clips[0].Clips = _clips.ToArray();
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (sceneAt.isLoaded && Owns(sceneAt))
			{
				RestoreBushes(sceneAt);
			}
			yield return null;
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
		Plugin.Log.LogInfo($"[Vegetation] restored {_repaired.Count} bush sound banks with {_clips.Count} native clips; native rustle and AI tree callbacks active");
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (Owns(scene))
		{
			RestoreBushes(scene);
		}
	}

	private void RestoreBushes(Scene scene)
	{
		GameObject[] rootGameObjects = scene.GetRootGameObjects();
		for (int i = 0; i < rootGameObjects.Length; i++)
		{
			TreeInteractive[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<TreeInteractive>(includeInactive: true);
			foreach (TreeInteractive treeInteractive in componentsInChildren)
			{
				if (!HasPlayableBank(treeInteractive._soundBank))
				{
					treeInteractive._soundBank = _bank;
					_repaired.Add(treeInteractive);
				}
			}
		}
	}

	internal static bool HasPlayableBank(SoundBank bank)
	{
		if (bank == null || bank.Rolloff <= 0f || bank.Environments == null || bank.Environments.Length == 0)
		{
			return false;
		}
		EnvironmentVariety environmentVariety = bank.Environments[0];
		if (environmentVariety == null || environmentVariety.Clips == null || environmentVariety.Clips.Length == 0)
		{
			return false;
		}
		DistanceVarity distanceVarity = environmentVariety.Clips[0];
		if (distanceVarity == null || distanceVarity.Clips == null || distanceVarity.Clips.Length == 0)
		{
			return false;
		}
		for (int i = 0; i < distanceVarity.Clips.Length; i++)
		{
			if (distanceVarity.Clips[i] == null)
			{
				return false;
			}
		}
		return true;
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		for (int i = 0; i < _repaired.Count; i++)
		{
			if (_repaired[i] != null && _repaired[i]._soundBank == _bank)
			{
				_repaired[i]._soundBank = null;
			}
		}
		if (_bank != null)
		{
			UnityEngine.Object.Destroy(_bank);
		}
		for (int j = 0; j < _clips.Count; j++)
		{
			if (_clips[j] != null)
			{
				UnityEngine.Object.Destroy(_clips[j]);
			}
		}
		if (_instance == this)
		{
			_instance = null;
		}
	}
}
