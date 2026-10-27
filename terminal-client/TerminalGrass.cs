using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using EFT;
using GPUInstancer;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalGrass
{
	internal sealed class Patch_RestoreAtRaidStart : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(GameWorld), "OnGameStarted");
		}

		[PatchPostfix]
		private static void Postfix()
		{
			if (TerminalGate.On && Plugin.GrassEnabled.Value)
			{
				new GameObject("Manimal_TerminalGrassLoader").AddComponent<Loader>();
			}
		}
	}

	private sealed class Loader : MonoBehaviour
	{
		private IEnumerator Start()
		{
			yield return null;
			Restore();
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	private const string BundleFile = "terminal_grass.bundle";

	private const string PlacementFile = "TerminalGrassPrefabs.pcl";

	private const string FoliageShader = "GPUInstancer/Foliage_Simple";

	private const string VariationBuffer = "_ArrayIndexBuffer";

	private static AssetBundle _bundle;

	private static GameObject _host;

	private static GPUInstancerPrefabPrototype _prototype;

	internal static void ReleaseAfterRaid()
	{
		GameObject host = _host;
		_host = null;
		TerminalRaidCleanup.DestroyOwned(_prototype);
		_prototype = null;
		TerminalRaidCleanup.DestroyOwned(host);
	}

	private static void Restore()
	{
		try
		{
			if (_host != null)
			{
				Plugin.Log.LogDebug("[Grass] retail grass is already active");
				return;
			}
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "terminal_grass.bundle");
			string text = Path.Combine(Application.streamingAssetsPath, "Grass", "TerminalGrassPrefabs.pcl");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[Grass] terminal_grass.bundle is missing beside the client DLL; grass remains disabled");
				return;
			}
			if (!File.Exists(text))
			{
				Plugin.Log.LogWarning("[Grass] retail placement file is missing: " + text);
				return;
			}
			Shader shader = FindGameShader("GPUInstancer/Foliage_Simple");
			Material nativeMain = FindNativeMaterial("Foliage_Simple", shader);
			Material nativeBillboard = FindNativeMaterial("Foliage_Simple_Billboard", shader);
			if (_bundle == null)
			{
				_bundle = AssetBundle.LoadFromFile(path);
			}
			if (_bundle == null)
			{
				throw new InvalidOperationException("Unity could not load the grass asset bundle");
			}
			GameObject gameObject = null;
			GameObject[] array = _bundle.LoadAllAssets<GameObject>();
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != null && array[i].name == "crossGrass_clean")
				{
					gameObject = array[i];
					break;
				}
			}
			if (gameObject == null)
			{
				throw new InvalidOperationException("crossGrass_clean is missing from the grass bundle");
			}
			GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject);
			gameObject2.name = "crossGrass_TerminalRetailTemplate";
			RebindTemplateMaterials(gameObject2, shader, nativeMain, nativeBillboard);
			Renderer[] componentsInChildren = gameObject2.GetComponentsInChildren<Renderer>(includeInactive: true);
			for (int j = 0; j < componentsInChildren.Length; j++)
			{
				componentsInChildren[j].enabled = false;
			}
			_prototype = BuildPrototype(gameObject2);
			GPUInstancerPrefab gPUInstancerPrefab = gameObject2.GetComponent<GPUInstancerPrefab>();
			if (gPUInstancerPrefab == null)
			{
				gPUInstancerPrefab = gameObject2.AddComponent<GPUInstancerPrefab>();
			}
			gPUInstancerPrefab.prefabPrototype = _prototype;
			_host = new GameObject("Manimal_RestoredTerminalGrass");
			_host.SetActive(value: false);
			MoveIntoGrassScene(_host);
			GPUInstancerPrefabManager manager = AddManager(_host, gameObject2, optic: true);
			GPUInstancerPrefabManager manager2 = AddManager(_host, gameObject2, optic: false);
			_host.SetActive(value: true);
			byte[] array2 = File.ReadAllBytes(text);
			int num = Marshal.SizeOf(typeof(GrassPrefabData));
			if (array2.Length == 0 || array2.Length % num != 0)
			{
				throw new InvalidDataException($"PCL length {array2.Length} is not divisible by native stride {num}");
			}
			int num2 = array2.Length / num;
			GrassData grassData = new GrassData
			{
				bytes = array2
			};
			Matrix4x4[] array3 = new Matrix4x4[num2];
			int[] array4 = new int[num2];
			for (int k = 0; k < num2; k++)
			{
				array3[k] = grassData.prefabs[k].matrix;
				array4[k] = grassData.prefabs[k].arrayIndex;
			}
			_prototype.enableRuntimeModifications = true;
			InitializeManager(manager, _prototype, array3, array4);
			InitializeManager(manager2, _prototype, array3, array4);
			gameObject2.transform.SetParent(_host.transform, worldPositionStays: false);
			Plugin.Log.LogInfo($"[Grass] restored {num2:N0} retail placements (world + optic managers, max distance 150m)");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[Grass] restoration failed: {arg}");
			if (_host != null)
			{
				UnityEngine.Object.Destroy(_host);
			}
			_host = null;
		}
	}

	private static GPUInstancerPrefabPrototype BuildPrototype(GameObject template)
	{
		GPUInstancerPrefabPrototype gPUInstancerPrefabPrototype = ScriptableObject.CreateInstance<GPUInstancerPrefabPrototype>();
		gPUInstancerPrefabPrototype.name = "crossGrass_65406_TerminalRetail";
		gPUInstancerPrefabPrototype.prefabObject = template;
		gPUInstancerPrefabPrototype.isShadowCasting = true;
		gPUInstancerPrefabPrototype.useCustomShadowDistance = true;
		gPUInstancerPrefabPrototype.shadowDistance = 25f;
		gPUInstancerPrefabPrototype.shadowLODMap = new float[16]
		{
			0f, 4f, 0f, 0f, 9f, 5f, 0f, 0f, 2f, 6f,
			0f, 0f, 3f, 7f, 0f, 0f
		};
		gPUInstancerPrefabPrototype.useOriginalShaderForShadow = true;
		gPUInstancerPrefabPrototype.cullShadows = true;
		gPUInstancerPrefabPrototype.minDistance = 0f;
		gPUInstancerPrefabPrototype.maxDistance = 150f;
		gPUInstancerPrefabPrototype.minDistanceOptic = 0f;
		gPUInstancerPrefabPrototype.maxDistanceOptic = 500f;
		gPUInstancerPrefabPrototype.isFrustumCulling = true;
		gPUInstancerPrefabPrototype.isOcclusionCulling = true;
		gPUInstancerPrefabPrototype.minCullingDistance = 0f;
		gPUInstancerPrefabPrototype.occlusionOffset = 0f;
		gPUInstancerPrefabPrototype.occlusionAccuracy = 1;
		gPUInstancerPrefabPrototype.boundsOffset = Vector3.zero;
		gPUInstancerPrefabPrototype.isLODCrossFade = false;
		gPUInstancerPrefabPrototype.isLODCrossFadeAnimate = false;
		gPUInstancerPrefabPrototype.lodFadeTransitionWidth = 0.1f;
		gPUInstancerPrefabPrototype.lodBiasAdjustment = 1f;
		gPUInstancerPrefabPrototype.billboard = new GPUInstancerBillboard();
		gPUInstancerPrefabPrototype.isBillboardDisabled = false;
		gPUInstancerPrefabPrototype.useGeneratedBillboard = false;
		gPUInstancerPrefabPrototype.checkedForBillboardExtensions = true;
		gPUInstancerPrefabPrototype.autoUpdateTransformData = false;
		gPUInstancerPrefabPrototype.treeType = GPUInstancerTreeType.None;
		gPUInstancerPrefabPrototype.enableRuntimeModifications = true;
		gPUInstancerPrefabPrototype.startWithRigidBody = false;
		gPUInstancerPrefabPrototype.addRemoveInstancesAtRuntime = false;
		gPUInstancerPrefabPrototype.extraBufferSize = 0;
		gPUInstancerPrefabPrototype.addRuntimeHandlerScript = false;
		gPUInstancerPrefabPrototype.hasRigidBody = false;
		gPUInstancerPrefabPrototype.meshRenderersDisabled = false;
		gPUInstancerPrefabPrototype.isTransformsSerialized = false;
		return gPUInstancerPrefabPrototype;
	}

	private static GPUInstancerPrefabManager AddManager(GameObject host, GameObject template, bool optic)
	{
		GPUInstancerPrefabManager gPUInstancerPrefabManager = host.AddComponent<GPUInstancerPrefabManager>();
		gPUInstancerPrefabManager.IsOptic = optic;
		gPUInstancerPrefabManager.prototypeList = new List<GPUInstancerPrototype> { _prototype };
		gPUInstancerPrefabManager.prefabList = new List<GameObject> { template };
		gPUInstancerPrefabManager.instancingBounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
		gPUInstancerPrefabManager.isFrustumCulling = true;
		gPUInstancerPrefabManager.isOcclusionCulling = true;
		gPUInstancerPrefabManager.minCullingDistance = 0f;
		gPUInstancerPrefabManager.maxThreads = 3;
		gPUInstancerPrefabManager.layerMask = -1;
		gPUInstancerPrefabManager.enableMROnManagerDisable = true;
		gPUInstancerPrefabManager.enableMROnRemoveInstance = true;
		gPUInstancerPrefabManager.IsActive = true;
		return gPUInstancerPrefabManager;
	}

	private static void InitializeManager(GPUInstancerPrefabManager manager, GPUInstancerPrefabPrototype prototype, Matrix4x4[] matrices, int[] variants)
	{
		manager.SetColorBuffers(new List<Color>
		{
			new Color(1f, 240f / 255f, 0.0784314f, 0f),
			new Color(121f / 255f, 0.5607843f, 91f / 255f, 0f)
		}, new List<Color>
		{
			new Color(1f, 157f / 255f, 0.20000002f, 0f),
			Color.white
		});
		GPUInstancerAPI.InitializeWithMatrix4x4Array(manager, prototype, matrices);
		GPUInstancerAPI.SetInstanceCount(manager, prototype, matrices.Length);
		GPUInstancerAPI.DefineAndAddVariationFromArray(manager, prototype, "_ArrayIndexBuffer", variants);
	}

	private static void MoveIntoGrassScene(GameObject host)
	{
		Scene sceneByName = SceneManager.GetSceneByName("Terminal_Grass");
		if (sceneByName.IsValid() && sceneByName.isLoaded)
		{
			SceneManager.MoveGameObjectToScene(host, sceneByName);
		}
		else
		{
			Plugin.Log.LogWarning("[Grass] Terminal_Grass scene was not found; manager attached to the active raid scene");
		}
	}

	private static void RebindTemplateMaterials(GameObject template, Shader gameShader, Material nativeMain, Material nativeBillboard)
	{
		if (gameShader == null)
		{
			throw new InvalidOperationException("game shader 'GPUInstancer/Foliage_Simple' is unavailable");
		}
		Renderer[] componentsInChildren = template.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			Material[] sharedMaterials = componentsInChildren[i].sharedMaterials;
			for (int j = 0; j < sharedMaterials.Length; j++)
			{
				Material material = sharedMaterials[j];
				if (!(material == null))
				{
					Material material2 = ((material.name.IndexOf("Billboard", StringComparison.OrdinalIgnoreCase) >= 0) ? nativeBillboard : nativeMain);
					if (material2 != null)
					{
						sharedMaterials[j] = material2;
						continue;
					}
					Material material3 = new Material(material)
					{
						name = material.name + "_TerminalRuntime",
						shader = gameShader
					};
					sharedMaterials[j] = material3;
				}
			}
			componentsInChildren[i].sharedMaterials = sharedMaterials;
		}
	}

	private static Shader FindGameShader(string name)
	{
		if (ShadersFinder.SHADERS.TryGetValue(name, out var value) && value != null && value.isSupported)
		{
			return value;
		}
		Shader shader = Shader.Find(name);
		if (shader == null || !shader.isSupported)
		{
			try
			{
				shader = ShadersFinder.Find(name);
			}
			catch
			{
			}
		}
		if (!(shader != null) || !shader.isSupported)
		{
			return null;
		}
		return shader;
	}

	private static Material FindNativeMaterial(string name, Shader shader)
	{
		if (shader == null)
		{
			return null;
		}
		Material[] array = Resources.FindObjectsOfTypeAll<Material>();
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] != null && array[i].name == name && array[i].shader == shader)
			{
				return array[i];
			}
		}
		return null;
	}
}
