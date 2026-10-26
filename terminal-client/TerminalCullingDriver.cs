using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.CameraControl;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal class TerminalCullingDriver : MonoBehaviour
{
	[HarmonyPatch(typeof(CameraManager), "SetCamera", new Type[] { typeof(Camera) })]
	internal static class Patch_CaptureCamera
	{
		[HarmonyPostfix]
		private static void Postfix(Camera camera)
		{
			if (!TerminalGate.On)
			{
				CameraRef = null;
				TerminalRenderProfiler.Disable();
				TerminalFramePhase.Disable();
				return;
			}
			if (Instance == null)
			{
				GameObject gameObject = new GameObject("Manimal_TerminalCullingDriver");
				UnityEngine.Object.DontDestroyOnLoad(gameObject);
				Instance = gameObject.AddComponent<TerminalCullingDriver>();
			}
			CameraRef = camera;
			TerminalRenderProfiler.EnsureSubscribed();
			TerminalFramePhase.EnsureSpawned();
		}
	}

	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_AttachAtRaidStart
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On && !(Instance == null))
			{
				Instance.StartCoroutine(Instance.AttachSoon());
			}
		}
	}

	private class PcVol
	{
		public UnityEngine.Object Vol;

		public int LastCell = int.MinValue;

		public MethodInfo GetIndex;

		public MethodInfo GetIndices;

		public MethodInfo QueueAll;

		public MethodInfo Execute;

		public object[] Groups;

		public HashSet<int> VisSet;
	}

	private class XVol
	{
		public string Name;

		public Bounds B;

		public object[] Groups;

		public Bounds[] GB;

		public bool[] Forced;

		public bool[] OpenSightline;
	}

	internal static TerminalCullingDriver Instance;

	internal static Camera CameraRef;

	private int _frames;

	private static float _biasOrig = -1f;

	private static float _lastBiasWritten = float.NaN;

	private static Dictionary<string, List<Renderer>> _rendererPosMap;

	private static bool _cullingNeutered;

	private static FieldInfo _groupRenderersField;

	private const int PcApplyBudget = 350;

	private const int PcShowBudget = 600;

	private const int PcGroupBudget = 200;

	private static readonly Dictionary<Renderer, bool> _pcPending = new Dictionary<Renderer, bool>();

	private static readonly List<Renderer> _pcDrainScratch = new List<Renderer>(600);

	private static readonly Dictionary<object, bool> _pcGroupPending = new Dictionary<object, bool>();

	private static readonly List<object> _pcGroupScratch = new List<object>(1024);

	private static readonly string[] PcNeverCull = new string[3] { "curtains_", "combination_lock", "cpu_panel" };

	private static readonly Dictionary<object, bool> _pcWhitelistCache = new Dictionary<object, bool>();

	private static HashSet<Transform> _pcLcRoots;

	private static readonly List<PcVol> _pcVols = new List<PcVol>();

	private static readonly List<ushort> _pcIndices = new List<ushort>(2048);

	private static int _pcvCursor;

	internal static int _cullNothingFrames;

	private static float _lastCullNothingLog;

	private static bool _pcDriverWasOff;

	private static readonly List<XVol> _xvols = new List<XVol>();

	private static readonly HashSet<object> _crossForced = new HashSet<object>();

	private static int _xcullTick;

	private static readonly HashSet<string> CannotHostOnCam2 = new HashSet<string> { "UltimateBloom", "GlobalFog" };

	private static readonly HashSet<Behaviour> _strippedEffects = new HashSet<Behaviour>();

	private static readonly List<Component> _guardScratch = new List<Component>(80);

	private bool _amandsStateLogged;

	private static bool _amResolved;

	private static bool _amPresent;

	private static ConfigEntryBase _amMbCfg;

	private static Type _amVolType;

	private static PropertyInfo _amProfileProp;

	private static object _amCachedProfile;

	private static object _amEnabledParam;

	private static FieldInfo _amValueField;

	private static MethodInfo _amOverride;

	internal static void ResetForNewRaid()
	{
		_rendererPosMap = null;
		_pcPending.Clear();
		_pcGroupPending.Clear();
		_pcWhitelistCache.Clear();
		_pcLcRoots = null;
		_pcVols.Clear();
		_xvols.Clear();
		_crossForced.Clear();
		_amCachedProfile = null;
		_amEnabledParam = null;
		if (Instance != null)
		{
			Instance._frames = 0;
			Instance._amandsStateLogged = false;
		}
	}

	private IEnumerator AttachSoon()
	{
		yield return null;
		AttachCullingCamera();
		TerminalLootBounds.ResetForRaid();
		yield return Instance.StartCoroutine(TerminalLodCullFloor.Apply());
	}

	private static void RestoreLodBias()
	{
		if (!(_biasOrig < 0f))
		{
			QualitySettings.lodBias = _biasOrig;
			Plugin.Log.LogInfo($"[LOD] left the terminal — lodBias restored to the game's {_biasOrig:F2}");
			_biasOrig = -1f;
			_lastBiasWritten = float.NaN;
		}
	}

	private void TickLootBounds()
	{
		try
		{
			float value = Plugin.LodBiasClamp.Value;
			if (value > 0f)
			{
				if (_biasOrig < 0f)
				{
					_biasOrig = QualitySettings.lodBias;
				}
				if (!Mathf.Approximately(QualitySettings.lodBias, value) || !Mathf.Approximately(_lastBiasWritten, value))
				{
					QualitySettings.lodBias = value;
					_lastBiasWritten = value;
				}
			}
			else
			{
				RestoreLodBias();
			}
			long timestamp = Stopwatch.GetTimestamp();
			if (CameraRef != null)
			{
				TerminalLodCullFloor.Tick(CameraRef.transform.position);
			}
			TerminalTickProfiler.Add("LodFloor", Stopwatch.GetTimestamp() - timestamp);
			timestamp = Stopwatch.GetTimestamp();
			TerminalLootBounds.Tick();
			TerminalTickProfiler.Add("LootScan", Stopwatch.GetTimestamp() - timestamp);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[LodCullFloor] tick failed: " + ex.Message);
		}
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F8))
		{
			TerminalEnvDump.Dump(TerminalGate.On ? "terminal" : "vanilla");
		}
		if (!TerminalGate.On)
		{
			RestoreLodBias();
			return;
		}
		_frames++;
		long timestamp = Stopwatch.GetTimestamp();
		TickLootBounds();
		TerminalTickProfiler.Add("LootBnds", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		TickPcDriver();
		TerminalTickProfiler.Add("PcDrv", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		DrainPcGroupToggles();
		TerminalTickProfiler.Add("PcGrpDrn", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		DrainPcToggles();
		TerminalTickProfiler.Add("PcTogDrn", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		TickCrossCull();
		TerminalTickProfiler.Add("XCull", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		TickDeadEffectGuard();
		TerminalTickProfiler.Add("DeadFxGd", Stopwatch.GetTimestamp() - timestamp);
		timestamp = Stopwatch.GetTimestamp();
		TickAmandsReapply();
		TerminalTickProfiler.Add("Amands", Stopwatch.GetTimestamp() - timestamp);
	}

	private static void AttachCullingCamera()
	{
		try
		{
			Type type = Type.GetType("Koenigz.PerfectCulling.EFT.PerfectCullingAdaptiveGrid, Assembly-CSharp");
			object obj = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			if (obj != null && !obj.Equals(null))
			{
				bool flag;
				try
				{
					object obj2 = type.GetProperty("PackedData")?.GetValue(obj);
					flag = obj2 != null && object.Equals(obj2.GetType().GetProperty("IsValid")?.GetValue(obj2), true);
				}
				catch
				{
					flag = true;
				}
				if (flag)
				{
					Plugin.Log.LogDebug("[Culling] NATIVE BSG culling grid alive with valid packed data — sidecar driver standing down");
					return;
				}
				Plugin.Log.LogWarning("[Culling] native grid present but packed bake NOT loaded — falling back to sidecar driver");
			}
			Type type2 = Type.GetType("Koenigz.PerfectCulling.PerfectCullingCamera, PerfectCullingRuntime");
			if (type2 == null)
			{
				Plugin.Log.LogDebug("[Culling] PerfectCullingRuntime not loaded — no culling");
				return;
			}
			Type type3 = Type.GetType("Koenigz.PerfectCulling.PerfectCullingVolume, PerfectCullingRuntime");
			if (type3 == null)
			{
				Plugin.Log.LogWarning("[Culling] volume type missing");
				return;
			}
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "culling");
			if (Directory.Exists(path))
			{
				string[] files = Directory.GetFiles(path, "*.pcbake");
				for (int i = 0; i < files.Length; i++)
				{
					RehydrateVolumeFromSidecar(files[i], type3);
				}
			}
			UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(type3);
			if (array == null || array.Length == 0)
			{
				Plugin.Log.LogDebug("[Culling] no live volumes (no sidecars yet?) — no occlusion culling this raid");
				return;
			}
			NeuterEditorOnlyCallbacks(type2);
			try
			{
				BuildPcDriver(array);
				Plugin.Log.LogInfo($"[Culling] sliced PC driver armed — {array.Length} volume(s), 1 volume/frame, per-volume cell gating");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Culling] driver build failed: " + ex.Message);
			}
			BuildCrossCull(type3);
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogWarning("[Culling] attach failed: " + ex2.Message);
		}
	}

	private static string PosKey(string name, Vector3 p)
	{
		return $"{name}|{Mathf.RoundToInt(p.x * 20f)}|{Mathf.RoundToInt(p.y * 20f)}|{Mathf.RoundToInt(p.z * 20f)}";
	}

	private static void DisableInvalidVolume(string volName, Type volType)
	{
		UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(volType);
		for (int i = 0; i < array.Length; i++)
		{
			Component component = array[i] as Component;
			if (component != null && component.name == volName)
			{
				component.gameObject.SetActive(value: false);
			}
		}
	}

	private static void RehydrateVolumeFromSidecar(string file, Type volType)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
		try
		{
			Assembly assembly = volType.Assembly;
			Type type = assembly.GetType("Koenigz.PerfectCulling.PerfectCullingVolumeBakeData");
			Type nestedType = type.GetNestedType("VisibilitySet");
			Type type2 = assembly.GetType("Koenigz.PerfectCulling.PerfectCullingBakeGroup");
			using BinaryReader binaryReader = new BinaryReader(File.OpenRead(file));
			if (binaryReader.ReadString() != "PCBK3")
			{
				Plugin.Log.LogWarning("[Culling] " + fileNameWithoutExtension + ": old/bad sidecar format — re-export from the editor");
				return;
			}
			binaryReader.ReadString();
			Vector3 position = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Quaternion rotation = new Quaternion(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Vector3 vector = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Vector3 vector2 = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Vector3 vector3 = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Vector3 vector4 = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			Quaternion quaternion = new Quaternion(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			int num = binaryReader.ReadInt32();
			int num2 = binaryReader.ReadInt32();
			if (num2 <= 0)
			{
				throw new InvalidDataException($"invalid cell count {num2}");
			}
			long position2 = binaryReader.BaseStream.Position;
			int num3 = 0;
			for (int i = 0; i < num2; i++)
			{
				if (binaryReader.BaseStream.Length - binaryReader.BaseStream.Position < 6)
				{
					throw new EndOfStreamException($"cell {i} header is truncated");
				}
				ushort num4 = binaryReader.ReadUInt16();
				int num5 = binaryReader.ReadInt32();
				if (num5 < 0 || num5 > binaryReader.BaseStream.Length - binaryReader.BaseStream.Position)
				{
					throw new InvalidDataException($"cell {i} has invalid payload length {num5}");
				}
				if (num4 > 0 && num5 > 0)
				{
					num3++;
				}
				binaryReader.BaseStream.Position += num5;
			}
			if (num3 == 0)
			{
				DisableInvalidVolume(fileNameWithoutExtension, volType);
				Plugin.Log.LogWarning($"[Culling] REJECTED {fileNameWithoutExtension}: {num2} allocated cells but zero contain visibility data (interrupted/corrupt bake); volume disabled until rebaked");
				return;
			}
			binaryReader.BaseStream.Position = position2;
			Component component = null;
			UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(volType);
			foreach (UnityEngine.Object obj in array)
			{
				if ((obj as Component)?.name == fileNameWithoutExtension)
				{
					component = obj as Component;
					break;
				}
			}
			if (component == null)
			{
				GameObject gameObject = new GameObject(fileNameWithoutExtension);
				gameObject.SetActive(value: false);
				component = gameObject.AddComponent(volType);
			}
			component.transform.SetPositionAndRotation(position, rotation);
			volType.GetField("volumeSize").SetValue(component, vector);
			volType.GetField("bakeCellSize").SetValue(component, vector2);
			Array array2 = Array.CreateInstance(nestedType, num2);
			FieldInfo field = nestedType.GetField("compressed");
			FieldInfo field2 = nestedType.GetField("len");
			for (int k = 0; k < num2; k++)
			{
				ushort num6 = binaryReader.ReadUInt16();
				byte[] value = binaryReader.ReadBytes(binaryReader.ReadInt32());
				object obj2 = Activator.CreateInstance(nestedType);
				field2.SetValue(obj2, num6);
				field.SetValue(obj2, value);
				array2.SetValue(obj2, k);
			}
			if (_rendererPosMap == null)
			{
				_rendererPosMap = new Dictionary<string, List<Renderer>>();
				Renderer[] array3 = Resources.FindObjectsOfTypeAll<Renderer>();
				foreach (Renderer renderer in array3)
				{
					if (renderer.gameObject.scene.isLoaded)
					{
						string key = PosKey(renderer.name, renderer.transform.position);
						if (!_rendererPosMap.TryGetValue(key, out var value2))
						{
							value2 = (_rendererPosMap[key] = new List<Renderer>(1));
						}
						value2.Add(renderer);
					}
				}
			}
			int num7 = binaryReader.ReadInt32();
			int num8 = 0;
			Array array4 = Array.CreateInstance(type2, num7);
			FieldInfo field3 = type2.GetField("renderers");
			FieldInfo field4 = type2.GetField("groupType");
			object value3 = Enum.Parse(field4.FieldType, "User");
			for (int l = 0; l < num7; l++)
			{
				int num9 = binaryReader.ReadInt32();
				List<Renderer> list2 = new List<Renderer>(num9);
				for (int m = 0; m < num9; m++)
				{
					string text = binaryReader.ReadString();
					Vector3 p = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
					if (_rendererPosMap.TryGetValue(PosKey(text, p), out var value4) && value4.Count > 0)
					{
						Renderer renderer2 = value4[value4.Count - 1];
						value4.RemoveAt(value4.Count - 1);
						if (renderer2 != null)
						{
							list2.Add(renderer2);
						}
						else
						{
							num8++;
						}
					}
					else
					{
						num8++;
					}
				}
				object obj3 = Activator.CreateInstance(type2);
				field3.SetValue(obj3, list2.ToArray());
				field4.SetValue(obj3, value3);
				array4.SetValue(obj3, l);
			}
			ScriptableObject scriptableObject = ScriptableObject.CreateInstance(type);
			type.GetField("cellCount").SetValue(scriptableObject, vector3);
			type.GetField("cellSize").SetValue(scriptableObject, vector4);
			type.GetField("orientation").SetValue(scriptableObject, quaternion);
			type.GetField("numberOfGroups").SetValue(scriptableObject, num);
			type.GetField("data").SetValue(scriptableObject, array2);
			volType.GetField("volumeBakeData").SetValue(component, scriptableObject);
			volType.GetField("bakeGroups", BindingFlags.Instance | BindingFlags.Public)?.SetValue(component, array4);
			volType.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public)?.Invoke(component, null);
			if (!component.gameObject.activeSelf)
			{
				component.gameObject.SetActive(value: true);
			}
			else
			{
				Behaviour behaviour = component as Behaviour;
				if (behaviour != null)
				{
					behaviour.enabled = false;
					behaviour.enabled = true;
				}
			}
			Plugin.Log.LogInfo($"[Culling] rehydrated {fileNameWithoutExtension}: {num2} cells ({num3} populated), {num7} groups ({num8} renderers unresolved)");
		}
		catch (Exception arg)
		{
			DisableInvalidVolume(fileNameWithoutExtension, volType);
			Plugin.Log.LogWarning($"[Culling] rehydrate {fileNameWithoutExtension} failed; volume disabled: {arg}");
		}
	}

	private static void NeuterEditorOnlyCallbacks(Type camType)
	{
		if (_cullingNeutered)
		{
			return;
		}
		_cullingNeutered = true;
		Harmony harmony = new Harmony("com.manimal.terminal.culling");
		string[] array = new string[2] { "LateUpdate", "OnGUI" };
		foreach (string text in array)
		{
			MethodInfo methodInfo = AccessTools.Method(camType, text);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(TerminalCullingDriver), "SkipOriginal"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		Type type = camType.Assembly.GetType("Koenigz.PerfectCulling.PerfectCullingBakeGroup");
		MethodInfo methodInfo2 = ((type != null) ? AccessTools.Method(type, "Toggle") : null);
		if (methodInfo2 != null)
		{
			_groupRenderersField = type.GetField("renderers");
			harmony.Patch((MethodBase)methodInfo2, new HarmonyMethod(typeof(TerminalCullingDriver), "ToggleGroupViaEnabled"), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			Plugin.Log.LogDebug("[Culling] BakeGroup.Toggle replaced with renderer.enabled path (static-batching compatible)");
		}
		Plugin.Log.LogDebug("[Culling] editor-only callbacks neutered on PerfectCullingCamera");
	}

	private static bool SkipOriginal()
	{
		return false;
	}

	private static HashSet<Transform> BuildLootContainerRoots()
	{
		HashSet<Transform> hashSet = new HashSet<Transform>();
		LootableContainer[] array = UnityEngine.Object.FindObjectsOfType<LootableContainer>(includeInactive: true);
		foreach (LootableContainer lootableContainer in array)
		{
			Transform item = ((lootableContainer.transform.parent != null) ? lootableContainer.transform.parent : lootableContainer.transform);
			Transform parent = lootableContainer.transform;
			int num = 0;
			while (parent != null && num < 4)
			{
				if (parent.GetComponent<LODGroup>() != null)
				{
					item = parent;
					break;
				}
				num++;
				parent = parent.parent;
			}
			hashSet.Add(item);
		}
		return hashSet;
	}

	private static bool IsNeverCullGroup(object group, Renderer[] rs)
	{
		if (_pcWhitelistCache.TryGetValue(group, out var value))
		{
			return value;
		}
		value = false;
		if (rs != null)
		{
			foreach (Renderer renderer in rs)
			{
				if (renderer == null)
				{
					continue;
				}
				string text = renderer.name.ToLowerInvariant();
				string[] pcNeverCull = PcNeverCull;
				foreach (string value2 in pcNeverCull)
				{
					if (text.Contains(value2))
					{
						value = true;
						break;
					}
				}
				if (!value)
				{
					if (_pcLcRoots == null)
					{
						_pcLcRoots = BuildLootContainerRoots();
					}
					Transform parent = renderer.transform;
					while (parent != null && !value)
					{
						if (_pcLcRoots.Contains(parent))
						{
							value = true;
						}
						parent = parent.parent;
					}
				}
				if (value)
				{
					break;
				}
			}
		}
		_pcWhitelistCache[group] = value;
		return value;
	}

	private static bool ToggleGroupViaEnabled(object __instance, bool isVisible)
	{
		if (isVisible && _crossForced.Contains(__instance))
		{
			isVisible = false;
		}
		Renderer[] array = _groupRenderersField?.GetValue(__instance) as Renderer[];
		if (!isVisible && IsNeverCullGroup(__instance, array))
		{
			isVisible = true;
		}
		if (array != null)
		{
			Renderer[] array2 = array;
			foreach (Renderer renderer in array2)
			{
				if (renderer != null && renderer.enabled != isVisible)
				{
					_pcPending[renderer] = isVisible;
				}
				else if (renderer != null)
				{
					_pcPending.Remove(renderer);
				}
			}
		}
		return false;
	}

	private static void BuildPcDriver(UnityEngine.Object[] vols)
	{
		_pcVols.Clear();
		_pcGroupPending.Clear();
		_pcWhitelistCache.Clear();
		_pcLcRoots = null;
		foreach (UnityEngine.Object obj in vols)
		{
			Type type = obj.GetType();
			Array array = type.GetField("bakeGroups", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as Array;
			object[] array2 = new object[array?.Length ?? 0];
			array?.CopyTo(array2, 0);
			_pcVols.Add(new PcVol
			{
				Vol = obj,
				GetIndex = AccessTools.Method(type, "GetIndexForWorldPos", new Type[2]
				{
					typeof(Vector3),
					typeof(bool).MakeByRefType()
				}),
				GetIndices = AccessTools.Method(type, "GetIndicesForWorldPos"),
				QueueAll = AccessTools.Method(type, "QueueToggleAllRenderers"),
				Execute = AccessTools.Method(type, "ExecuteQueue"),
				Groups = array2
			});
		}
	}

	private static void TickPcDriver()
	{
		if (!Plugin.PcDriverEnabled.Value)
		{
			if (_pcDriverWasOff)
			{
				return;
			}
			_pcDriverWasOff = true;
			foreach (PcVol pcVol2 in _pcVols)
			{
				if (!(pcVol2.Vol == null) && !(pcVol2.QueueAll == null) && !(pcVol2.Execute == null))
				{
					try
					{
						pcVol2.QueueAll.Invoke(pcVol2.Vol, new object[1] { true });
						pcVol2.Execute.Invoke(pcVol2.Vol, new object[1] { true });
						pcVol2.LastCell = int.MinValue;
						pcVol2.VisSet = null;
					}
					catch
					{
					}
				}
			}
			Plugin.Log.LogDebug("[Culling] PC driver DISABLED (live) — occlusion-culled renderers restored");
			return;
		}
		if (_pcDriverWasOff)
		{
			_pcDriverWasOff = false;
			Plugin.Log.LogDebug("[Culling] PC driver re-enabled (live)");
		}
		if (_pcVols.Count == 0 || CameraRef == null)
		{
			return;
		}
		_pcvCursor = (_pcvCursor + 1) % _pcVols.Count;
		PcVol pcVol = _pcVols[_pcvCursor];
		if (pcVol.Vol == null || pcVol.GetIndex == null)
		{
			return;
		}
		try
		{
			Vector3 position = CameraRef.transform.position;
			object[] parameters = new object[2] { position, false };
			int num = (int)pcVol.GetIndex.Invoke(pcVol.Vol, parameters);
			if (num == pcVol.LastCell)
			{
				return;
			}
			pcVol.LastCell = num;
			_pcIndices.Clear();
			pcVol.GetIndices.Invoke(pcVol.Vol, new object[2] { position, _pcIndices });
			HashSet<int> hashSet = new HashSet<int>();
			foreach (ushort pcIndex in _pcIndices)
			{
				hashSet.Add(pcIndex);
			}
			bool flag = hashSet.Count == 0;
			if (flag)
			{
				_cullNothingFrames++;
				if (Time.realtimeSinceStartup - _lastCullNothingLog > 5f)
				{
					_lastCullNothingLog = Time.realtimeSinceStartup;
					Component component = pcVol.Vol as Component;
					Plugin.Log.LogWarning(string.Format("[Culling] UNBAKED CELL on '{0}' cell={1} — ", (component != null) ? component.name : "?", num) + $"culling OFF for this volume, all {pcVol.Groups.Length} group(s) rendering " + $"({_cullNothingFrames} such cell-entries this raid)");
				}
			}
			if (pcVol.VisSet == null)
			{
				for (int i = 0; i < pcVol.Groups.Length; i++)
				{
					_pcGroupPending[pcVol.Groups[i]] = flag || hashSet.Contains(i);
				}
			}
			else
			{
				for (int j = 0; j < pcVol.Groups.Length; j++)
				{
					bool num2 = pcVol.VisSet.Count == 0 || pcVol.VisSet.Contains(j);
					bool flag2 = flag || hashSet.Contains(j);
					if (num2 != flag2)
					{
						_pcGroupPending[pcVol.Groups[j]] = flag2;
					}
				}
			}
			pcVol.VisSet = hashSet;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Culling] driver failed on volume: " + ex.Message);
			pcVol.Vol = null;
		}
	}

	private static void DrainPcGroupToggles()
	{
		if (_pcGroupPending.Count == 0)
		{
			return;
		}
		_pcGroupScratch.Clear();
		int num = 200;
		foreach (KeyValuePair<object, bool> item in _pcGroupPending)
		{
			if (num-- <= 0)
			{
				break;
			}
			ToggleGroupViaEnabled(item.Key, item.Value);
			_pcGroupScratch.Add(item.Key);
		}
		foreach (object item2 in _pcGroupScratch)
		{
			_pcGroupPending.Remove(item2);
		}
	}

	private static void DrainPcToggles()
	{
		if (_pcPending.Count == 0)
		{
			return;
		}
		_pcDrainScratch.Clear();
		int num = 600;
		foreach (KeyValuePair<Renderer, bool> item in _pcPending)
		{
			if (item.Value)
			{
				if (num-- <= 0)
				{
					break;
				}
				Renderer key = item.Key;
				if (key != null && !key.enabled)
				{
					key.enabled = true;
				}
				_pcDrainScratch.Add(key);
			}
		}
		int num2 = 350;
		foreach (KeyValuePair<Renderer, bool> item2 in _pcPending)
		{
			if (!item2.Value)
			{
				if (num2-- <= 0)
				{
					break;
				}
				Renderer key2 = item2.Key;
				if (key2 != null && key2.enabled)
				{
					key2.enabled = false;
				}
				_pcDrainScratch.Add(key2);
			}
		}
		foreach (Renderer item3 in _pcDrainScratch)
		{
			_pcPending.Remove(item3);
		}
	}

	private static void BuildCrossCull(Type volType)
	{
		_xvols.Clear();
		_crossForced.Clear();
		if (_groupRenderersField == null)
		{
			return;
		}
		FieldInfo field = volType.GetField("bakeGroups", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(volType);
		foreach (UnityEngine.Object obj in array)
		{
			Component component = obj as Component;
			if (component == null || !component.name.Contains("Indoor") || !(field?.GetValue(obj) is Array { Length: not 0 } array2))
			{
				continue;
			}
			XVol xVol = new XVol
			{
				Name = component.name,
				Groups = new object[array2.Length],
				GB = new Bounds[array2.Length],
				Forced = new bool[array2.Length],
				OpenSightline = new bool[array2.Length]
			};
			bool flag = false;
			int num = 0;
			for (int j = 0; j < array2.Length; j++)
			{
				object value = array2.GetValue(j);
				xVol.Groups[j] = value;
				Renderer[] array3 = _groupRenderersField.GetValue(value) as Renderer[];
				if (component.name == "CullingVolume_Terminal_Area_04_MarineStation_Indoor" && IsMarineStationInteriorShell(array3))
				{
					xVol.OpenSightline[j] = true;
					num++;
				}
				Bounds bounds = default;
				bool flag2 = false;
				if (array3 != null)
				{
					Renderer[] array4 = array3;
					foreach (Renderer renderer in array4)
					{
						if (renderer != null)
						{
							if (!flag2)
							{
								bounds = renderer.bounds;
								flag2 = true;
							}
							else
							{
								bounds.Encapsulate(renderer.bounds);
							}
						}
					}
				}
				xVol.GB[j] = bounds;
				if (flag2)
				{
					if (!flag)
					{
						xVol.B = bounds;
						flag = true;
					}
					else
					{
						xVol.B.Encapsulate(bounds);
					}
				}
			}
			xVol.B.Expand(3f);
			_xvols.Add(xVol);
			Plugin.Log.LogDebug($"[XCull] interior volume '{xVol.Name}': {xVol.Groups.Length} groups, bounds {xVol.B.size}" + ((num > 0) ? $", {num} open-hangar shell group(s) exempt" : ""));
		}
	}

	private static bool IsMarineStationInteriorShell(Renderer[] renderers)
	{
		if (renderers == null)
		{
			return false;
		}
		foreach (Renderer renderer in renderers)
		{
			if (!(renderer == null) && (renderer.name.StartsWith("Marine_Station_01_INDOOR") || renderer.name.StartsWith("Marine_Station_01_Fakeindoor")))
			{
				return true;
			}
		}
		return false;
	}

	private static void TickCrossCull()
	{
		if (!Plugin.InteriorCrossCull.Value || _xvols.Count == 0 || CameraRef == null || ++_xcullTick % 15 != 0)
		{
			return;
		}
		Vector3 position = CameraRef.transform.position;
		float num = Plugin.CrossCullDistance.Value * Plugin.CrossCullDistance.Value;
		foreach (XVol xvol in _xvols)
		{
			bool flag = xvol.B.Contains(position);
			for (int i = 0; i < xvol.Groups.Length; i++)
			{
				bool flag2 = !xvol.OpenSightline[i] && !flag && xvol.GB[i].SqrDistance(position) > num;
				if (flag2 != xvol.Forced[i])
				{
					xvol.Forced[i] = flag2;
					if (flag2)
					{
						_crossForced.Add(xvol.Groups[i]);
					}
					else
					{
						_crossForced.Remove(xvol.Groups[i]);
					}
					ToggleGroupViaEnabled(xvol.Groups[i], !flag2);
				}
			}
		}
	}

	private void TickDeadEffectGuard()
	{
		if (_frames < 120 || _frames % 60 != 0)
		{
			return;
		}
		try
		{
			Camera camera = ((CameraRef != null) ? CameraRef : Camera.main);
			if (camera == null)
			{
				return;
			}
			camera.GetComponents(_guardScratch);
			foreach (Component item in _guardScratch)
			{
				if (!(item == null) && item is Behaviour { enabled: not false } behaviour && CannotHostOnCam2.Contains(item.GetType().Name))
				{
					behaviour.enabled = false;
					if (_strippedEffects.Add(behaviour))
					{
						Plugin.Log.LogWarning("[RaidFix] disabled " + item.GetType().Name + " on the terminal camera — the Cam2 fallback has no retail configuration for it; unconfigured at the END of the effect chain it swallows the whole frame (black screen + HUD burn-in). that mod's bloom is off on this map only.");
					}
					else
					{
						Plugin.Log.LogInfo("[RaidFix] re-parked " + item.GetType().Name + " — its owner re-enabled it (new raid on the persistent camera)");
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[RaidFix] dead-effect guard: " + ex.Message);
		}
	}

	private void TickAmandsReapply()
	{
		if (_frames < 300 || _frames % 120 != 0)
		{
			return;
		}
		try
		{
			if (!_amResolved)
			{
				_amResolved = true;
				_amPresent = Chainloader.PluginInfos.ContainsKey("com.Amanda.Graphics");
				if (_amPresent)
				{
					_amMbCfg = Type.GetType("AmandsGraphics.AmandsGraphicsPlugin, AmandsGraphics")?.GetProperty("MotionBlur", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as ConfigEntryBase;
					_amVolType = TerminalTypes.ByName("UnityEngine.Rendering.PostProcessing.PostProcessVolume");
					if (_amVolType != null)
					{
						_amProfileProp = AccessTools.Property(_amVolType, "profile");
					}
					_amPresent = _amMbCfg != null && _amProfileProp != null;
				}
			}
			if (!_amPresent)
			{
				return;
			}
			bool flag = _amMbCfg.BoxedValue?.ToString() == "On";
			Camera camera = ((CameraRef != null) ? CameraRef : Camera.main);
			if (camera == null)
			{
				return;
			}
			Component component = camera.GetComponent(_amVolType);
			if (component == null)
			{
				return;
			}
			object value = _amProfileProp.GetValue(component);
			if (value == null)
			{
				return;
			}
			if (value != _amCachedProfile)
			{
				_amCachedProfile = value;
				_amEnabledParam = null;
				_amValueField = null;
				_amOverride = null;
				if (AccessTools.Field(value.GetType(), "settings")?.GetValue(value) is IEnumerable enumerable)
				{
					foreach (object item in enumerable)
					{
						if (item != null && item.GetType().Name == "MotionBlur")
						{
							_amEnabledParam = AccessTools.Field(item.GetType(), "enabled")?.GetValue(item);
							if (_amEnabledParam != null)
							{
								_amValueField = AccessTools.Field(_amEnabledParam.GetType(), "value");
								_amOverride = AccessTools.Method(_amEnabledParam.GetType(), "Override");
							}
							break;
						}
					}
				}
			}
			if (!_amandsStateLogged)
			{
				_amandsStateLogged = true;
				bool flag2 = _amEnabledParam != null && _amValueField != null && (bool)_amValueField.GetValue(_amEnabledParam);
				ManualLogSource log = Plugin.Log;
				string text = $"[Amands] reconcile armed — config MotionBlur={_amMbCfg.BoxedValue}, live profile MotionBlur ";
				string text2;
				if (_amEnabledParam == null)
				{
					text2 = "ABSENT";
				}
				else
				{
					text2 = (flag2 ? "enabled" : "disabled");
				}
				log.LogInfo(text + text2);
			}
			if (_amEnabledParam != null && !(_amValueField == null) && (bool)_amValueField.GetValue(_amEnabledParam) && !flag)
			{
				_amOverride?.Invoke(_amEnabledParam, new object[1] { false });
				Plugin.Log.LogWarning("[Amands] live profile MotionBlur was ON with config Off — overridden off (Amands disabled a stale profile while Terminal's camera repair installed the live one)");
			}
		}
		catch (Exception ex)
		{
			if (!_amandsStateLogged)
			{
				_amandsStateLogged = true;
				Plugin.Log.LogWarning("[Amands] reconcile failed (cosmetic only): " + ex.Message);
			}
		}
	}
}
