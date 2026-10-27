using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalWater
{
	private sealed class RendererState
	{
		internal Renderer Renderer;

		internal bool Enabled;

		internal bool ForceRenderingOff;

		internal Material SharedMaterial;
	}

	[HarmonyPatch(typeof(WaterForSSR), "InitWaterMatrices")]
	internal static class Patch_PerPlaneMatrices
	{
		private static bool Prefix(WaterForSSR __instance, ref List<WaterForSSR.WaterObject> ____waterHolder)
		{
			if (!__instance.GetComponent<TerminalWaterMatrixDriver>())
			{
				return true;
			}
			MeshFilter[] waterPlanes = __instance.WaterPlanes;
			if (____waterHolder == null)
			{
				____waterHolder = new List<WaterForSSR.WaterObject>(waterPlanes.Length);
			}
			int num = 0;
			MeshFilter[] array = waterPlanes;
			foreach (MeshFilter meshFilter in array)
			{
				if ((bool)meshFilter && (bool)meshFilter.sharedMesh)
				{
					if (num == ____waterHolder.Count)
					{
						____waterHolder.Add(new WaterForSSR.WaterObject(meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix));
					}
					else
					{
						____waterHolder[num].Mesh = meshFilter.sharedMesh;
						____waterHolder[num].Matrix = meshFilter.transform.localToWorldMatrix;
					}
					num++;
				}
			}
			if (num < ____waterHolder.Count)
			{
				____waterHolder.RemoveRange(num, ____waterHolder.Count - num);
			}
			return false;
		}
	}

	private const string NativeWaterShader = "Hidden/WaterForSSR";

	private const string DirtDecalShader = "Decal/Ultra Deferred Decal Of God 3000";

	private static bool _staged;

	private static bool _ready;

	private static bool _staging;

	private static int _stageFailures;

	private static float _nextStageAttemptAt;

	private static GameObject _runtimeWaterRoot;

	private static GameObject _runtimeRendererRoot;

	private static WaterRenderer _nativeRenderer;

	private static bool _nativeRendererWasEnabled;

	private static bool _nativeRendererEnabledByUs;

	private static Material _ownedDirtMaterial;

	private static readonly List<RendererState> RendererStates = new List<RendererState>(16);

	private static readonly List<TerminalWaterMatrixDriver> OwnedContainers = new List<TerminalWaterMatrixDriver>(2);

	internal static void ReleaseAfterRaid()
	{
		RendererStates.Clear();
		OwnedContainers.Clear();
		_texIndex = null;
		_nativeRenderer = null;
		TerminalRaidCleanup.DestroyOwned(_ownedDirtMaterial);
		TerminalRaidCleanup.DestroyOwned(_runtimeWaterRoot);
		TerminalRaidCleanup.DestroyOwned(_runtimeRendererRoot);
		_ownedDirtMaterial = null;
		_runtimeWaterRoot = _runtimeRendererRoot = null;
		_staged = _ready = _staging = false;
	}

	internal static void ResetForRaid()
	{
		CleanupAttempt();
		_texIndex = null;
		_staged = false;
		_ready = false;
		_staging = false;
		_stageFailures = 0;
		_nextStageAttemptAt = 0f;
	}

	internal static void TryStage()
	{
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		if (_staged || _ready || _staging || !TerminalGate.On || !TerminalWaterLifecycle.CanAttemptStage(_stageFailures) || Time.realtimeSinceStartup < _nextStageAttemptAt || (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null) == null)
		{
			return;
		}
		Transform transform = TerminalRigFill.FindRootNamed("Water_planes_withoutculling");
		if (!transform)
		{
			_nextStageAttemptAt = Time.realtimeSinceStartup + 1f;
			return;
		}
		Material material = null;
		_staging = true;
		try
		{
			List<MeshFilter> list = new List<MeshFilter>(9);
			List<Renderer> list2 = new List<Renderer>(9);
			List<MeshFilter> list3 = new List<MeshFilter>(6);
			List<Renderer> list4 = new List<Renderer>(6);
			CollectWaterGeometry(transform, list, list2);
			CollectWaterGeometry(TerminalRigFill.FindRootNamed("Pamp_Station"), list3, list4);
			CollectWaterGeometry(TerminalRigFill.FindRootNamed("Pumping_station_water_dirt"), list3, list4);
			List<Renderer> list5 = ValueEnumerableExtensions.ToList<Distinct<Where<Concat<FromList<Renderer>, FromEnumerable<Renderer>, Renderer>, Renderer>, Renderer>, Renderer>(ValueEnumerableExtensions.Distinct<Where<Concat<FromList<Renderer>, FromEnumerable<Renderer>, Renderer>, Renderer>, Renderer>(ValueEnumerableExtensions.Where<Concat<FromList<Renderer>, FromEnumerable<Renderer>, Renderer>, Renderer>(ValueEnumerableExtensions.Concat<FromList<Renderer>, Renderer>(ValueEnumerable.AsValueEnumerable<Renderer>(list2), (IEnumerable<Renderer>)list4), (Func<Renderer, bool>)((Renderer r) => r))));
			if (list5.Count == 0)
			{
				throw new InvalidOperationException("water root contains no usable renderers");
			}
			bool value = Plugin.WaterPlanes.Value;
			MeshRenderer meshRenderer = null;
			for (int num = 0; num < list5.Count; num++)
			{
				MeshRenderer meshRenderer2 = list5[num] as MeshRenderer;
				if ((bool)meshRenderer2 && meshRenderer2.name == "Pumping_station_water_dirt_LOD0")
				{
					meshRenderer = meshRenderer2;
				}
			}
			if (!value)
			{
				CommitRendererState(list5, null, null);
				_staged = true;
				_ready = true;
				_stageFailures = 0;
				_nextStageAttemptAt = 0f;
				TerminalPerfWatch.WatchWater(list5);
				Plugin.Log.LogInfo($"[Water] water disabled by WaterPlanes config ({list.Count + list3.Count} plane(s) staged off)");
				return;
			}
			Texture2D texture2D = FindTexture("Lighthouse_Swamp_Water_NM");
			if (!texture2D)
			{
				texture2D = Texture2D.normalTexture;
			}
			Shader shader = FindShader("Hidden/WaterForSSR");
			if (!shader || !shader.isSupported)
			{
				throw new InvalidOperationException("native shader 'Hidden/WaterForSSR' unavailable");
			}
			Texture2D texture2D2 = FindTexture("Lighthouse_Swamp_Water") ?? FindTexture("perlin_noise");
			Texture2D texture2D3 = FindTexture("WaterFlowsOld") ?? FindTexture("perlin_noise") ?? texture2D2;
			if (!texture2D2)
			{
				texture2D2 = Texture2D.whiteTexture;
			}
			if (!texture2D3)
			{
				texture2D3 = Texture2D.grayTexture;
			}
			if (!texture2D || !texture2D2 || !texture2D3)
			{
				throw new InvalidOperationException("pump water textures unavailable");
			}
			_runtimeWaterRoot = new GameObject("Terminal_RuntimeWaterSSR");
			_runtimeWaterRoot.SetActive(value: false);
			SceneManager.MoveGameObjectToScene(_runtimeWaterRoot, transform.gameObject.scene);
			_runtimeWaterRoot.transform.SetParent(transform.parent, worldPositionStays: false);
			TerminalWaterAssets assets = _runtimeWaterRoot.AddComponent<TerminalWaterAssets>();
			_runtimeWaterRoot.AddComponent<TerminalWaterReflectionDriver>();
			Texture2D texture2D4 = LoadWaterTexture(assets, "Static Voronoi - Tileable - NM2", normalMap: true);
			Texture2D texture2D5 = LoadWaterTexture(assets, "detailedWavesMap hd2", normalMap: true);
			Texture2D texture2D6 = LoadWaterTexture(assets, "Foam 01", normalMap: false);
			Texture2D texture2D7 = LoadWaterTexture(assets, "circles", normalMap: false);
			if (!texture2D4 || !texture2D5 || !texture2D6 || !texture2D7)
			{
				throw new InvalidOperationException("recovered sea textures incomplete; check plugin-data/water deployment");
			}
			WaterForSSR waterForSSR = CreateContainer("Sea", shader, texture2D4, texture2D5, texture2D6, texture2D7, list);
			WaterForSSR waterForSSR2 = CreateContainer("Pump", shader, texture2D, texture2D, texture2D2, texture2D3, list3);
			if (!waterForSSR && !waterForSSR2)
			{
				throw new InvalidOperationException("water geometry did not produce a container");
			}
			EnsureNativeRenderer(_runtimeWaterRoot.transform);
			if ((bool)meshRenderer)
			{
				material = BuildPumpDirtDecal();
			}
			CommitRendererState(list5, meshRenderer, material);
			material = null;
			ActivateRuntime();
			_staged = true;
			_ready = true;
			_stageFailures = 0;
			_nextStageAttemptAt = 0f;
			TerminalPerfWatch.WatchWater(list5);
			Plugin.Log.LogInfo($"[Water] restored native water: sea={list.Count} +" + $" pump={list3.Count} deferred '{shader.name}', per-plane matrices=yes," + " seaNormal='" + texture2D4.name + "', detail='" + texture2D5.name + "', foam='" + texture2D6.name + "', ripple='" + texture2D7.name + "', pump material preserved, dirtDecal=" + (((bool)meshRenderer && (bool)_ownedDirtMaterial) ? "yes" : "missing"));
		}
		catch (Exception ex)
		{
			if ((bool)material && (object)material != _ownedDirtMaterial)
			{
				UnityEngine.Object.Destroy(material);
			}
			CleanupAttempt();
			_staged = false;
			_ready = false;
			_stageFailures++;
			float num2 = Mathf.Min(4f, 0.5f * (float)(1 << Math.Min(_stageFailures - 1, 3)));
			_nextStageAttemptAt = Time.realtimeSinceStartup + num2;
			Plugin.Log.LogWarning(TerminalWaterLifecycle.CanAttemptStage(_stageFailures) ? $"[Water] native restore failed; retrying in {num2:F1}s: {ex.Message}" : $"[Water] native restore stopped after {_stageFailures} attempts; original renderers restored: {ex.Message}");
		}
		finally
		{
			_staging = false;
			_texIndex = null; // don't keep every texture referenced after staging
		}
	}

	private static WaterForSSR CreateContainer(string label, Shader shader, Texture2D normal, Texture2D details, Texture2D foam, Texture2D ripple, List<MeshFilter> filters)
	{
		if (filters.Count == 0)
		{
			return null;
		}
		GameObject gameObject = new GameObject("Terminal_Water_" + label);
		gameObject.transform.SetParent(_runtimeWaterRoot.transform, worldPositionStays: false);
		TerminalWaterMatrixDriver terminalWaterMatrixDriver = gameObject.AddComponent<TerminalWaterMatrixDriver>();
		OwnedContainers.Add(terminalWaterMatrixDriver);
		WaterForSSR waterForSSR = (terminalWaterMatrixDriver.Water = gameObject.AddComponent<WaterForSSR>());
		ConfigureRetailWater(waterForSSR, shader, normal, details, foam, ripple, filters);
		try
		{
			waterForSSR.OnValidate();
			return waterForSSR;
		}
		finally
		{
			terminalWaterMatrixDriver.CaptureMaterial();
		}
	}

	private static void CommitRendererState(List<Renderer> renderers, MeshRenderer dirtRenderer, Material dirtMaterial)
	{
		RendererStates.Clear();
		for (int i = 0; i < renderers.Count; i++)
		{
			Renderer renderer = renderers[i];
			if ((bool)renderer)
			{
				RendererStates.Add(new RendererState
				{
					Renderer = renderer,
					Enabled = renderer.enabled,
					ForceRenderingOff = renderer.forceRenderingOff,
					SharedMaterial = renderer.sharedMaterial
				});
				renderer.enabled = false;
				renderer.forceRenderingOff = true;
			}
		}
		if ((bool)dirtRenderer && (bool)dirtMaterial)
		{
			dirtRenderer.sharedMaterial = dirtMaterial;
			_ownedDirtMaterial = dirtMaterial;
			dirtRenderer.forceRenderingOff = false;
			dirtRenderer.enabled = true;
		}
	}

	private static void ActivateRuntime()
	{
		if (!_runtimeWaterRoot)
		{
			throw new InvalidOperationException("runtime water root was not created");
		}
		_runtimeWaterRoot.SetActive(value: true);
		if ((bool)_nativeRenderer)
		{
			if (!_nativeRenderer.enabled)
			{
				_nativeRenderer.enabled = true;
				_nativeRendererEnabledByUs = true;
			}
		}
		else
		{
			if (!_runtimeRendererRoot)
			{
				throw new InvalidOperationException("native water renderer was not created");
			}
			_runtimeRendererRoot.SetActive(value: true);
		}
		WaterRenderer waterRenderer = (_nativeRenderer ? _nativeRenderer : _runtimeRendererRoot.GetComponent<WaterRenderer>());
		if (!waterRenderer || !waterRenderer.isActiveAndEnabled)
		{
			throw new InvalidOperationException("water renderer is inactive after activation");
		}
		List<WaterForSSR> list = AccessTools.Field(typeof(WaterRenderer), "list_0")?.GetValue(waterRenderer) as List<WaterForSSR>;
		foreach (TerminalWaterMatrixDriver ownedContainer in OwnedContainers)
		{
			ownedContainer.CaptureMaterial();
			WaterForSSR water = ownedContainer.Water;
			List<WaterForSSR.WaterObject> list2 = AccessTools.Field(typeof(WaterForSSR), "_waterHolder")?.GetValue(water) as List<WaterForSSR.WaterObject>;
			if (!water || !water.isActiveAndEnabled || !ownedContainer.MaterialReady || list2 == null || list2.Count != water.WaterPlanes.Length || list == null || !list.Contains(water))
			{
				throw new InvalidOperationException("water container did not initialize and register completely");
			}
		}
	}

	private static void CleanupAttempt()
	{
		RestoreRendererStates();
		DestroyOwnedDirtMaterial();
		CleanupRuntimeObjects();
	}

	private static void RestoreRendererStates()
	{
		for (int num = RendererStates.Count - 1; num >= 0; num--)
		{
			RendererState rendererState = RendererStates[num];
			if ((bool)rendererState.Renderer)
			{
				try
				{
					rendererState.Renderer.sharedMaterial = rendererState.SharedMaterial;
					rendererState.Renderer.forceRenderingOff = rendererState.ForceRenderingOff;
					rendererState.Renderer.enabled = rendererState.Enabled;
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[Water] renderer state restore skipped: " + ex.Message);
				}
			}
		}
		RendererStates.Clear();
	}

	private static void DestroyOwnedDirtMaterial()
	{
		if ((bool)_ownedDirtMaterial)
		{
			UnityEngine.Object.Destroy(_ownedDirtMaterial);
		}
		_ownedDirtMaterial = null;
	}

	private static void CleanupRuntimeObjects()
	{
		if ((bool)_runtimeWaterRoot)
		{
			if (_runtimeWaterRoot.activeSelf)
			{
				_runtimeWaterRoot.SetActive(value: false);
			}
			foreach (TerminalWaterMatrixDriver ownedContainer in OwnedContainers)
			{
				if ((bool)ownedContainer)
				{
					ownedContainer.ReleaseMaterial();
				}
			}
			_runtimeWaterRoot.GetComponent<TerminalWaterAssets>()?.ReleaseTextures();
			UnityEngine.Object.Destroy(_runtimeWaterRoot);
		}
		OwnedContainers.Clear();
		if ((bool)_runtimeRendererRoot)
		{
			WaterRenderer component = _runtimeRendererRoot.GetComponent<WaterRenderer>();
			if ((bool)component)
			{
				ReleaseNativeCommandBuffers(component);
			}
			if (_runtimeRendererRoot.activeSelf)
			{
				_runtimeRendererRoot.SetActive(value: false);
			}
			UnityEngine.Object.Destroy(_runtimeRendererRoot);
		}
		if ((bool)_nativeRenderer && _nativeRendererEnabledByUs && !_nativeRendererWasEnabled)
		{
			_nativeRenderer.enabled = false;
		}
		_runtimeWaterRoot = null;
		_runtimeRendererRoot = null;
		_nativeRenderer = null;
		_nativeRendererWasEnabled = false;
		_nativeRendererEnabledByUs = false;
	}

	private static void ReleaseNativeCommandBuffers(WaterRenderer renderer)
	{
		try
		{
			object obj = AccessTools.Field(typeof(WaterRenderer), "commandBufferManager")?.GetValue(renderer);
			obj?.GetType().GetMethod("DestroyBuffers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(obj, null);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Water] owned Draw Water buffers could not be released: " + ex.Message);
		}
	}

	private static Texture2D LoadWaterTexture(TerminalWaterAssets assets, string name, bool normalMap)
	{
		string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "water", name + ".png");
		if (!File.Exists(path))
		{
			return null;
		}
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, normalMap)
		{
			name = "Terminal_" + name,
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Trilinear,
			anisoLevel = 4
		};
		assets.Textures.Add(texture2D);
		if (!texture2D.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
		{
			return null;
		}
		if (normalMap)
		{
			Color32[] pixels = texture2D.GetPixels32();
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color32(byte.MaxValue, pixels[i].g, byte.MaxValue, pixels[i].r);
			}
			texture2D.SetPixels32(pixels);
		}
		texture2D.Apply(updateMipmaps: true, makeNoLongerReadable: true);
		return texture2D;
	}

	private static void CollectWaterGeometry(Transform root, List<MeshFilter> filters, List<Renderer> renderers)
	{
		if (!root)
		{
			return;
		}
		MeshFilter[] componentsInChildren = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
		foreach (MeshFilter meshFilter in componentsInChildren)
		{
			if ((bool)meshFilter && meshFilter.name.IndexOf("BALLISTIC", StringComparison.OrdinalIgnoreCase) < 0 && (bool)meshFilter.sharedMesh && !filters.Contains(meshFilter))
			{
				filters.Add(meshFilter);
				MeshRenderer component = meshFilter.GetComponent<MeshRenderer>();
				if ((bool)component && !renderers.Contains(component))
				{
					renderers.Add(component);
				}
			}
		}
	}

	private static void ConfigureRetailWater(WaterForSSR water, Shader shader, Texture2D normal, Texture2D details, Texture2D foam, Texture2D ripple, List<MeshFilter> filters)
	{
		water.WaterShader = shader;
		water.RippleTexture = ripple;
		water.Normals = normal;
		water.NormalsDetails = details;
		water.NormalsDetailsMipMapBias = -2f;
		water.Foam = foam;
		water.NormalsA = Blend(0.071f, 7.9f, -5.3f, 0.36708263f);
		water.NormalsB = Blend(0.0973f, -6.7f, 4.7f, 0.36067685f);
		water.NormalsDetails0 = Blend(0.5f, -2.3f, 1.7f, 0.27224058f);
		water.FoamA = Blend(0.3f, -2f, 2f, 0.5110404f);
		water.FoamB = Blend(1f, 2f, -2f, 0.48895958f);
		water.BorderFade = 2f;
		water.BorderFadeDistStart = 5f;
		water.BorderFadeDistRange = 66f;
		water.DepthFade = 2f;
		water.DepthColorFade = 0.09f;
		water.DepthRefractions = 0.1f;
		water.DepthColorDeep = new Color(0f, 0.13626835f, 0.14150941f, 1f);
		water.DepthColorShallow = new Color(0.25062302f, 0.31132078f, 0.17475082f, 1f);
		water.Bumpiness = 0.5f;
		water.RippleScale = 11f;
		water.RippleBumpness = 0.25f;
		water.FoamSize = 0.12f;
		water.FoamIntensity = 7f;
		water.FoamColor = new Color(1f, 1f, 1f, 0f);
		water.FresnelIntensity = 0.44f;
		water.FresnelPower = 5f;
		water.AdditionalCubemapReflectionMinWetting = 0.05f;
		water.AdditionalCubemapReflectionMaxWetting = 0.35f;
		water.ReflectionWettingFunc = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
		water.ReflectionColor = Color.white;
		water.DiffuseColor = new Color(0f, 0f, 0f, 1f);
		water.WaterPlanes = filters.ToArray();
	}

	private static WaterForSSR.TextureBlendSetting Blend(float scale, float x, float y, float blend)
	{
		return new WaterForSSR.TextureBlendSetting
		{
			Scale = scale,
			MovementDirection = new Vector2(x, y),
			Blend = blend
		};
	}

	private static void EnsureNativeRenderer(Transform parent)
	{
		_nativeRenderer = null;
		_nativeRendererWasEnabled = false;
		_nativeRendererEnabledByUs = false;
		WaterRenderer[] array = Resources.FindObjectsOfTypeAll<WaterRenderer>();
		foreach (WaterRenderer waterRenderer in array)
		{
			if ((bool)waterRenderer && waterRenderer.gameObject.scene.IsValid() && waterRenderer.gameObject.activeInHierarchy)
			{
				_nativeRenderer = waterRenderer;
				_nativeRendererWasEnabled = waterRenderer.enabled;
				Plugin.Log.LogInfo("[Water] using native WaterRenderer '" + waterRenderer.name + "'");
				return;
			}
		}
		_runtimeRendererRoot = new GameObject("Terminal_RuntimeWaterRenderer");
		_runtimeRendererRoot.SetActive(value: false);
		SceneManager.MoveGameObjectToScene(_runtimeRendererRoot, parent.gameObject.scene);
		_runtimeRendererRoot.transform.SetParent(parent, worldPositionStays: false);
		_runtimeRendererRoot.AddComponent<WaterRenderer>();
		Plugin.Log.LogInfo("[Water] prepared native WaterRenderer ('Draw Water')");
	}

	private static Material BuildPumpDirtDecal()
	{
		Shader shader = FindShader("Decal/Ultra Deferred Decal Of God 3000");
		if (!shader || !shader.isSupported)
		{
			Plugin.Log.LogWarning("[Water] pump dirt decal shader 'Decal/Ultra Deferred Decal Of God 3000' unavailable");
			return null;
		}
		Material material = new Material(shader)
		{
			name = "Terminal_City_Decal_Dirt_Large_green",
			renderQueue = 2005
		};
		Texture2D texture2D = FindTexture("City_Dirt_Decal_Large_white_D");
		Texture2D texture2D2 = FindTexture("Reserve_Dirt_Decal_Largel_N");
		if ((bool)texture2D && material.HasProperty("_MainTex"))
		{
			material.SetTexture("_MainTex", texture2D);
		}
		if ((bool)texture2D2 && material.HasProperty("_BumpMap"))
		{
			material.SetTexture("_BumpMap", texture2D2);
		}
		SetFloat(material, "_AlphaMultiplier", 1.39f);
		SetFloat(material, "_BumpScale", 1f);
		SetFloat(material, "_Cutoff", 0.5f);
		SetFloat(material, "_NormalIntensity", 0.631f);
		SetFloat(material, "_NormalPower", 0.52f);
		SetFloat(material, "_SpecSmoothness", 0f);
		SetFloat(material, "_ZWrite", 1f);
		SetColor(material, "_Color", new Color(0.766578f, 0.803922f, 0.225098f, 0.647059f));
		SetColor(material, "_SpecColor", new Color(1f, 1f, 1f, 0f));
		SetColor(material, "_SpecularColor", new Color(0.189f, 0.189f, 0.189f, 1f));
		SetColor(material, "_Temperature", new Color(0.1f, 0.12f, 0.28f, 0f));
		Plugin.Log.LogInfo("[Water] pump dirt decal restored on '" + shader.name + "' (main='" + (texture2D ? texture2D.name : "missing") + "', bump='" + (texture2D2 ? texture2D2.name : "missing") + "')");
		return material;
	}

	private static Shader FindShader(string exactName)
	{
		Shader shader = Shader.Find(exactName);
		if ((bool)shader)
		{
			return shader;
		}
		Shader[] array = Resources.FindObjectsOfTypeAll<Shader>();
		foreach (Shader shader2 in array)
		{
			if ((bool)shader2 && shader2.name == exactName)
			{
				return shader2;
			}
		}
		return null;
	}

	// zzap perf: TryStage asks for ~5 textures in one frame; scan all Texture2D once
	// per frame instead of once per name (first match by name, same as before)
	private static Dictionary<string, Texture2D> _texIndex;

	private static int _texIndexFrame = -1;

	private static Texture2D FindTexture(string exactName)
	{
		if (_texIndex == null || _texIndexFrame != Time.frameCount)
		{
			_texIndexFrame = Time.frameCount;
			_texIndex = new Dictionary<string, Texture2D>();
			Texture2D[] array = Resources.FindObjectsOfTypeAll<Texture2D>();
			foreach (Texture2D texture2D in array)
			{
				if ((bool)texture2D && !_texIndex.ContainsKey(texture2D.name))
				{
					_texIndex[texture2D.name] = texture2D;
				}
			}
		}
		_texIndex.TryGetValue(exactName, out var found);
		return found;
	}

	private static void SetFloat(Material material, string property, float value)
	{
		if (material.HasProperty(property))
		{
			material.SetFloat(property, value);
		}
	}

	private static void SetColor(Material material, string property, Color value)
	{
		if (material.HasProperty(property))
		{
			material.SetColor(property, value);
		}
	}

	private static void SetTexture(Material material, string property, Texture value)
	{
		if (material.HasProperty(property))
		{
			material.SetTexture(property, value);
		}
	}

	private static void SetVector(Material material, string property, Vector4 value)
	{
		if (material.HasProperty(property))
		{
			material.SetVector(property, value);
		}
	}
}
