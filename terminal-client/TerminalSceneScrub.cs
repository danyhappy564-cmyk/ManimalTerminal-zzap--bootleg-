using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalSceneScrub
{
	private static readonly string[] BrokenTypes = new string[6] { "EFT.Weather.WeatherController", "EFT.Weather.ToDController", "RainController", "RippleController", "EFT.EnvironmentEffect.EnvironmentManager", "AmbientLight" };

	private static Type[] _resolved;

	private static bool _lateSwept;

	private static JObject _todSidecar;

	private static bool _todTried;

	private static int HideBlockers(Scene scene)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = -1;
		GameObject[] rootGameObjects = scene.GetRootGameObjects();
		for (int i = 0; i < rootGameObjects.Length; i++)
		{
			MeshRenderer[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<MeshRenderer>(includeInactive: true);
			foreach (MeshRenderer meshRenderer in componentsInChildren)
			{
				HideOne(meshRenderer, ref num, ref num2, ref num3, ref num4);
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[Scrub] '{scene.name}': {num} level-border blocker renderer(s) hidden");
		}
		else if (num3 > 0)
		{
			Plugin.Log.LogWarning($"[Scrub] '{scene.name}': 0 blockers matched but {num3} Cube renderer(s) present " + $"(sample layer {num4} '{LayerMask.LayerToName(num4)}')");
		}
		if (num2 > 0)
		{
			Plugin.Log.LogInfo($"[Scrub] '{scene.name}': {num2} stencil helper renderer(s) hidden (colliders preserved)");
		}
		return num + num2;
	}

	// one renderer of the blocker/stencil scrub (body unchanged, split out so the
	// late sweep can run it a few milliseconds per frame)
	private static void HideOne(MeshRenderer meshRenderer, ref int blockers, ref int stencils, ref int cubes, ref int cubeLayer)
	{
		if (!meshRenderer || meshRenderer.forceRenderingOff)
		{
			return;
		}
		MeshFilter component = meshRenderer.GetComponent<MeshFilter>();
		if (IsStencilHelper(meshRenderer.name) || ((bool)component && (bool)component.sharedMesh && IsStencilHelper(component.sharedMesh.name)))
		{
			meshRenderer.forceRenderingOff = true;
			stencils++;
			return;
		}
		bool flag = LayerMask.LayerToName(meshRenderer.gameObject.layer) == "LevelBorder";
		if (!flag)
		{
			Transform transform = meshRenderer.transform;
			while (transform != null)
			{
				if (transform.name == "BLOCKERS" || transform.name == "Blockers")
				{
					flag = true;
					break;
				}
				transform = transform.parent;
			}
		}
		if (!flag && meshRenderer.name.StartsWith("Cube"))
		{
			Material sharedMaterial = meshRenderer.sharedMaterial;
			if ((bool)sharedMaterial && sharedMaterial.name.StartsWith("Default-Material"))
			{
				flag = true;
			}
		}
		if (!flag && meshRenderer.name.StartsWith("Cube"))
		{
			cubes++;
			if (cubeLayer < 0)
			{
				cubeLayer = meshRenderer.gameObject.layer;
			}
		}
		if (flag)
		{
			meshRenderer.forceRenderingOff = true;
			blockers++;
		}
	}

	private static bool IsStencilHelper(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		int num = name.IndexOf("_STENCIL", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return false;
		}
		int num2 = num + "_STENCIL".Length;
		if (num2 != name.Length && name[num2] != '_' && name[num2] != ' ')
		{
			return name[num2] == '(';
		}
		return true;
	}

	internal static void ResetForRaid()
	{
		_lateSwept = false;
	}

	internal static void TickLateSweep()
	{
		if (_lateSwept || !TerminalGate.On)
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.MainPlayer == null)
		{
			return;
		}
		_lateSwept = true;
		// zzap perf: the same sweep cost ~1.1s in the raid's first frame (and found 0
		// stragglers in the 10/26 log) — run it 3ms per frame instead
		new GameObject("Terminal_LateScrubSweep").AddComponent<LateSweepHost>();
	}

	private sealed class LateSweepHost : MonoBehaviour
	{
		private void Start()
		{
			StartCoroutine(Run());
		}

		private System.Collections.IEnumerator Run()
		{
			int hidden = 0;
			int scenes = 0;
			int stencils = 0;
			int cubes = 0;
			int cubeLayer = -1;
			var budget = System.Diagnostics.Stopwatch.StartNew();
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene sceneAt = SceneManager.GetSceneAt(i);
				if (!sceneAt.isLoaded || sceneAt.name == null || !sceneAt.name.StartsWith("Terminal"))
				{
					continue;
				}
				scenes++;
				GameObject[] roots;
				try
				{
					roots = sceneAt.GetRootGameObjects();
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("[Scrub] late sweep '" + sceneAt.name + "': " + ex.Message);
					continue;
				}
				foreach (GameObject root in roots)
				{
					if (!root)
					{
						continue;
					}
					MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
					for (int r = 0; r < renderers.Length; r++)
					{
						try
						{
							HideOne(renderers[r], ref hidden, ref stencils, ref cubes, ref cubeLayer);
						}
						catch (Exception ex2)
						{
							Plugin.Log.LogWarning("[Scrub] late sweep '" + sceneAt.name + "': " + ex2.Message);
						}
						if (budget.ElapsedMilliseconds >= 3)
						{
							yield return null;
							budget.Restart();
						}
					}
				}
			}
			Plugin.Log.LogInfo($"[Scrub] late blocker sweep over {scenes} scene(s): {hidden + stencils} straggler renderer(s) hidden");
			// same quiet slot: pre-compute the hangar crew's armory box so the first
			// hangar bot does not pay a whole-scene search mid-raid
			yield return TerminalCrewJobs.WarmArmoryBounds();
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	internal static void Scrub(Scene scene)
	{
		try
		{
			HideBlockers(scene);
			if (_resolved == null)
			{
				List<Type> list = new List<Type>();
				string[] brokenTypes = BrokenTypes;
				foreach (string text in brokenTypes)
				{
					Type type = TerminalTypes.ByName(text);
					if (type != null && typeof(Component).IsAssignableFrom(type))
					{
						list.Add(type);
					}
					else
					{
						Plugin.Log.LogDebug("[Scrub] type '" + text + "' not found — skipped");
					}
				}
				_resolved = list.ToArray();
			}
			int num = 0;
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			foreach (GameObject gameObject in rootGameObjects)
			{
				Type[] resolved = _resolved;
				foreach (Type type2 in resolved)
				{
					Component[] componentsInChildren = gameObject.GetComponentsInChildren(type2, includeInactive: true);
					for (int k = 0; k < componentsInChildren.Length; k++)
					{
						UnityEngine.Object.DestroyImmediate(componentsInChildren[k]);
						num++;
						dictionary.TryGetValue(type2.Name, out var value);
						dictionary[type2.Name] = value + 1;
					}
				}
			}
			if (num > 0)
			{
				List<string> list2 = new List<string>();
				foreach (KeyValuePair<string, int> item in dictionary)
				{
					list2.Add($"{item.Value}x {item.Key}");
				}
				Plugin.Log.LogWarning(string.Format("[Scrub] '{0}': destroyed {1} broken ripped component(s) — {2}", scene.name, num, string.Join(", ", list2)));
			}
			HealSky(scene);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Scrub] '" + scene.name + "' failed: " + ex.Message);
		}
	}

	private static void HealSky(Scene scene)
	{
		try
		{
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for (int i = 0; i < rootGameObjects.Length; i++)
			{
				TOD_Sky[] componentsInChildren = rootGameObjects[i].GetComponentsInChildren<TOD_Sky>(includeInactive: true);
				foreach (TOD_Sky tOD_Sky in componentsInChildren)
				{
					int num = 0;
					int num2 = 0;
					Component[] components = tOD_Sky.GetComponents<Component>();
					foreach (Component component in components)
					{
						if (!(component == null) && component.GetType().Name.StartsWith("TOD_"))
						{
							num += HealNullFields(component);
							num2 += RewireChildRefs(component, tOD_Sky.transform);
						}
					}
					int num3 = FillSkyResources(tOD_Sky);
					int num4 = RebindSkyShaders(tOD_Sky);
					ApplyTodAuthoring(tOD_Sky);
					HealTodComponents(tOD_Sky);
					Plugin.Log.LogWarning($"[Sky] '{scene.name}': TOD rig on '{tOD_Sky.name}' — {num} null field(s) defaulted, {num2} child ref(s) rewired, {num3} resource asset(s) reverse-filled, {num4} shader(s) rebound to native, re-initializing");
					try
					{
						tOD_Sky.Initialize();
						Plugin.Log.LogWarning("[Sky] TOD_Sky re-initialized OK — sun/sky driven at the authored hour");
					}
					catch (Exception arg)
					{
						Plugin.Log.LogError($"[Sky] TOD_Sky.Initialize still fails — sky stays undriven. FULL: {arg}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Sky] heal failed: " + ex.Message);
		}
	}

	private static void HealTodComponents(TOD_Sky sky)
	{
		try
		{
			TOD_Components component = sky.GetComponent<TOD_Components>();
			if (!component)
			{
				return;
			}
			if (!component.Light)
			{
				GameObject gameObject = null;
				Light[] componentsInChildren = sky.GetComponentsInChildren<Light>(includeInactive: true);
				foreach (Light light in componentsInChildren)
				{
					if ((bool)light && light.type == LightType.Directional)
					{
						gameObject = light.gameObject;
						break;
					}
				}
				if (!gameObject)
				{
					Light sun = RenderSettings.sun;
					if ((bool)sun && sun.gameObject.scene.IsValid())
					{
						gameObject = sun.gameObject;
					}
				}
				if (!gameObject)
				{
					gameObject = new GameObject("TOD Light");
					gameObject.transform.SetParent(sky.transform, worldPositionStays: false);
					Light light2 = gameObject.AddComponent<Light>();
					light2.type = LightType.Directional;
					light2.shadows = LightShadows.Soft;
					light2.intensity = 0f;
					Plugin.Log.LogWarning("[Sky] TOD had no light reference and the scene had no directional light — created one");
				}
				component.Light = gameObject;
			}
			component.LightTransform = component.Light.transform;
			component.LightSource = component.Light.GetComponent<Light>();
			if (!component.LightSource)
			{
				component.LightSource = component.Light.AddComponent<Light>();
			}
			if (component.LightSource.type != LightType.Directional)
			{
				component.LightSource.type = LightType.Directional;
			}
			if (!component.DomeTransform)
			{
				component.DomeTransform = sky.transform;
			}
			Transform domeTransform = component.DomeTransform;
			Vector3 localScale = domeTransform.localScale;
			if (localScale.x < 0.01f || localScale.y < 0.01f || localScale.z < 0.01f)
			{
				domeTransform.localScale = Vector3.one * Mathf.Max(localScale.x, localScale.y, localScale.z, 1f);
				Plugin.Log.LogWarning($"[Sky] dome scale was degenerate {localScale} — reset to {domeTransform.localScale} (frozen-sun fix)");
			}
			if ((bool)component.SunTransform && component.SunTransform.parent != domeTransform)
			{
				Plugin.Log.LogWarning("[Sky] Sun parented under '" + (component.SunTransform.parent ? component.SunTransform.parent.name : "NULL") + "' not the dome — reparenting (orbital positions are dome-local)");
				component.SunTransform.SetParent(domeTransform, worldPositionStays: false);
			}
			if ((bool)component.MoonTransform && component.MoonTransform.parent != domeTransform)
			{
				component.MoonTransform.SetParent(domeTransform, worldPositionStays: false);
			}
			if (!component.SunTransform && (bool)component.Sun)
			{
				component.SunTransform = component.Sun.transform;
			}
			if (!component.MoonTransform && (bool)component.Moon)
			{
				component.MoonTransform = component.Moon.transform;
			}
			if (!component.SpaceTransform && (bool)component.Space)
			{
				component.SpaceTransform = component.Space.transform;
			}
			Plugin.Log.LogWarning("[Sky] TOD component refs healed — light='" + component.Light.name + "' " + $"lightTransform={(bool)component.LightTransform} sun={(bool)component.SunTransform} " + $"moon={(bool)component.MoonTransform} dome={(bool)component.DomeTransform}" + "  <- a null LightTransform is what froze the sky at its authored 13:00");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Sky] TOD component heal failed: " + ex.Message);
		}
	}

	private static JObject TodSidecar()
	{
		if (_todTried)
		{
			return _todSidecar;
		}
		_todTried = true;
		try
		{
			string text = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_tod_sky.json");
			if (File.Exists(text))
			{
				_todSidecar = JObject.Parse(File.ReadAllText(text));
			}
			else
			{
				Plugin.Log.LogDebug("[Sky] no TOD sidecar at " + text);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Sky] TOD sidecar parse failed: " + ex.Message);
		}
		return _todSidecar;
	}

	private static int ApplyTodAuthoring(TOD_Sky sky)
	{
		JObject comps = TodSidecar()?["components"] as JObject;
		if (comps == null)
		{
			return 0;
		}
		int applied = 0;
		int missed = 0;
		ApplyRows("TOD_Sky", sky);
		ApplyRows("TOD_Time", sky.GetComponent<TOD_Time>());
		try
		{
			Plugin.Log.LogWarning($"[Sky] retail TOD authoring restored ({applied} value(s)): " + $"lat={sky.World.Latitude:0.0} long={sky.World.Longitude:0.0} UTC={sky.World.UTC:0.00} " + $"atmoBrightness={sky.Atmosphere.Brightness:0.000} " + $"dayLenMin={((sky.GetComponent<TOD_Time>() != null) ? sky.GetComponent<TOD_Time>().DayLengthInMinutes : (-1f)):0}" + $" gradients/colors/curves included, {missed} member(s) missed" + "  <- the old shallow loader left the retail sky gradients at default black");
		}
		catch
		{
		}
		return applied;
		void ApplyRows(string cls, Component target)
		{
			if (target == null || !((comps[cls] as JArray)?.First?["fields"] is JObject jObject))
			{
				return;
			}
			foreach (JProperty item in jObject.Properties())
			{
				string text = (item.Name.StartsWith("<") ? item.Name.Substring(1, item.Name.IndexOf('>') - 1) : item.Name);
				if (!(text == "Cycle"))
				{
					ApplyTodMember(target, text, item.Value, ref applied, ref missed);
				}
			}
		}
	}

	private static void ApplyTodMember(object target, string name, JToken token, ref int applied, ref int missed)
	{
		Type type = target.GetType();
		FieldInfo fieldInfo = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo propertyInfo = ((fieldInfo == null) ? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null);
		if (fieldInfo == null && propertyInfo == null)
		{
			missed++;
			return;
		}
		Type memberType = ((fieldInfo != null) ? fieldInfo.FieldType : propertyInfo.PropertyType);
		object existing = null;
		try
		{
			existing = ((fieldInfo != null) ? fieldInfo.GetValue(target) : propertyInfo.GetValue(target, null));
		}
		catch
		{
		}
		object obj2 = ConvertTodValue(token, memberType, existing, ref applied, ref missed);
		if (obj2 == null)
		{
			return;
		}
		try
		{
			if (fieldInfo != null)
			{
				fieldInfo.SetValue(target, obj2);
			}
			else
			{
				if (!propertyInfo.CanWrite)
				{
					missed++;
					return;
				}
				propertyInfo.SetValue(target, obj2, null);
			}
			applied++;
		}
		catch
		{
			missed++;
		}
	}

	private static object ConvertTodValue(JToken token, Type memberType, object existing, ref int applied, ref int missed)
	{
		try
		{
			if (memberType == typeof(float))
			{
				return token.Value<float>();
			}
			if (memberType == typeof(double))
			{
				return token.Value<double>();
			}
			if (memberType == typeof(int))
			{
				return token.Value<int>();
			}
			if (memberType == typeof(bool))
			{
				return (token.Type == JTokenType.Boolean) ? token.Value<bool>() : (token.Value<int>() != 0);
			}
			if (memberType.IsEnum)
			{
				return Enum.ToObject(memberType, token.Value<int>());
			}
			JObject obj = token as JObject;
			if (obj == null)
			{
				missed++;
				return null;
			}
			if (memberType == typeof(Color))
			{
				return new Color(F("r"), F("g"), F("b"), F("a", 1f));
			}
			if (memberType == typeof(Vector2))
			{
				return new Vector2(F("x"), F("y"));
			}
			if (memberType == typeof(Vector3))
			{
				return new Vector3(F("x"), F("y"), F("z"));
			}
			if (memberType == typeof(Vector4))
			{
				return new Vector4(F("x"), F("y"), F("z"), F("w"));
			}
			if (memberType == typeof(AnimationCurve))
			{
				if (!(obj["m_Curve"] is JArray jArray))
				{
					missed++;
					return null;
				}
				Keyframe[] array = new Keyframe[jArray.Count];
				for (int i = 0; i < jArray.Count; i++)
				{
					JObject key = jArray[i] as JObject;
					if (key != null)
					{
						array[i] = new Keyframe(KF("time"), KF("value"), KF("inSlope"), KF("outSlope"), KF("inWeight", 1f / 3f), KF("outWeight", 1f / 3f));
						array[i].weightedMode = (WeightedMode)key.Value<int?>("weightedMode").GetValueOrDefault();
					}
					float KF(string n, float d = 0f)
					{
						return key.Value<float?>(n) ?? d;
					}
				}
				return new AnimationCurve(array)
				{
					preWrapMode = (WrapMode)(obj.Value<int?>("m_PreInfinity") ?? 8),
					postWrapMode = (WrapMode)(obj.Value<int?>("m_PostInfinity") ?? 8)
				};
			}
			if (memberType == typeof(Gradient))
			{
				int num = Mathf.Clamp(obj.Value<int?>("m_NumColorKeys") ?? 2, 1, 8);
				int num2 = Mathf.Clamp(obj.Value<int?>("m_NumAlphaKeys") ?? 2, 1, 8);
				GradientColorKey[] array2 = new GradientColorKey[num];
				for (int j = 0; j < num; j++)
				{
					if (!(obj[$"key{j}"] is JObject jObject))
					{
						missed++;
						return null;
					}
					array2[j] = new GradientColorKey(new Color(jObject.Value<float?>("r").GetValueOrDefault(), jObject.Value<float?>("g").GetValueOrDefault(), jObject.Value<float?>("b").GetValueOrDefault()), (float)obj.Value<int?>($"ctime{j}").GetValueOrDefault() / 65535f);
				}
				GradientAlphaKey[] array3 = new GradientAlphaKey[num2];
				for (int k = 0; k < num2; k++)
				{
					if (!(obj[$"key{k}"] is JObject jObject2))
					{
						missed++;
						return null;
					}
					array3[k] = new GradientAlphaKey(jObject2.Value<float?>("a") ?? 1f, (float)obj.Value<int?>($"atime{k}").GetValueOrDefault() / 65535f);
				}
				Gradient gradient = new Gradient();
				gradient.SetKeys(array2, array3);
				return gradient;
			}
			object obj2 = existing;
			if (obj2 == null && memberType.IsClass && !memberType.IsAbstract && memberType.GetConstructor(Type.EmptyTypes) != null)
			{
				obj2 = Activator.CreateInstance(memberType);
			}
			if (obj2 != null && memberType.IsClass)
			{
				foreach (JProperty item in obj.Properties())
				{
					string name = (item.Name.StartsWith("<") ? item.Name.Substring(1, item.Name.IndexOf('>') - 1) : item.Name);
					ApplyTodMember(obj2, name, item.Value, ref applied, ref missed);
				}
				return (existing == null) ? obj2 : null;
			}
			float F(string key2, float fallback = 0f)
			{
				return obj.Value<float?>(key2) ?? fallback;
			}
		}
		catch
		{
			missed++;
			return null;
		}
		missed++;
		return null;
	}

	private static int RebindSkyShaders(TOD_Sky sky)
	{
		int rebound = 0;
		HashSet<Material> seen = new HashSet<Material>();
		TOD_Resources component = sky.GetComponent<TOD_Resources>();
		if (component != null)
		{
			Rebind(component.SpaceMaterial);
			Rebind(component.AtmosphereMaterial);
			Rebind(component.ClearMaterial);
			Rebind(component.SunMaterial);
			Rebind(component.MoonMaterial);
			Rebind(component.SkyboxMaterial);
		}
		Renderer[] componentsInChildren = sky.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			Material[] sharedMaterials = componentsInChildren[i].sharedMaterials;
			for (int j = 0; j < sharedMaterials.Length; j++)
			{
				Rebind(sharedMaterials[j]);
			}
		}
		return rebound;
		void Rebind(Material m)
		{
			if (!(m == null) && seen.Add(m) && !(m.shader == null))
			{
				Shader shader = null;
				try
				{
					shader = ShadersFinder.Find(m.shader.name);
				}
				catch
				{
				}
				if (shader == null || !shader.isSupported)
				{
					Plugin.Log.LogWarning("[Sky] no native shader for '" + m.name + "' ('" + m.shader.name + "') — stays on the bundle copy");
				}
				else
				{
					int renderQueue = m.renderQueue;
					if ((object)shader != m.shader)
					{
						m.shader = shader;
						rebound++;
					}
					m.renderQueue = -1;
					Plugin.Log.LogDebug($"[Sky] '{m.name}' -> '{shader.name}' queue {renderQueue} -> {m.renderQueue}" + " (blend src=" + (m.HasProperty("_SrcBlend") ? m.GetFloat("_SrcBlend").ToString() : "n/a") + " dst=" + (m.HasProperty("_DstBlend") ? m.GetFloat("_DstBlend").ToString() : "n/a") + " zwrite=" + (m.HasProperty("_ZWrite") ? m.GetFloat("_ZWrite").ToString() : "n/a") + ")");
				}
			}
		}
	}

	private static int FillSkyResources(TOD_Sky sky)
	{
		TOD_Resources component = sky.GetComponent<TOD_Resources>();
		if (component == null)
		{
			return 0;
		}
		int n = 0;
		SetMat(ref component.SpaceMaterial, Mat("Space"));
		SetMat(ref component.AtmosphereMaterial, Mat("Atmosphere"));
		SetMat(ref component.ClearMaterial, Mat("Clear"));
		SetMat(ref component.SunMaterial, Mat("Sun"));
		SetMat(ref component.MoonMaterial, Mat("Moon"));
		SetMat(ref component.SkyboxMaterial, Mat("Atmosphere"));
		Mesh mesh = MeshOf("Atmosphere") ?? MeshOf("Space");
		Mesh v = MeshOf("Clear") ?? mesh;
		Mesh mesh2 = MeshOf("Moon") ?? mesh;
		Mesh v2 = MeshOf("Sun") ?? mesh2;
		SetMesh(ref component.IcosphereHigh, mesh);
		SetMesh(ref component.IcosphereMedium, mesh);
		SetMesh(ref component.IcosphereLow, mesh);
		SetMesh(ref component.HalfIcosphereHigh, v);
		SetMesh(ref component.HalfIcosphereMedium, v);
		SetMesh(ref component.HalfIcosphereLow, v);
		SetMesh(ref component.SphereHigh, mesh2);
		SetMesh(ref component.SphereMedium, mesh2);
		SetMesh(ref component.SphereLow, mesh2);
		SetMesh(ref component.Quad, v2);
		return n;
		Transform Child(string name)
		{
			Transform[] componentsInChildren = sky.transform.GetComponentsInChildren<Transform>(includeInactive: true);
			foreach (Transform transform in componentsInChildren)
			{
				if (transform.name == name)
				{
					return transform;
				}
			}
			return null;
		}
		Material Mat(string name)
		{
			Transform transform = Child(name);
			Renderer renderer = ((transform != null) ? transform.GetComponent<Renderer>() : null);
			if (!(renderer != null))
			{
				return null;
			}
			return renderer.sharedMaterial;
		}
		Mesh MeshOf(string name)
		{
			Transform transform = Child(name);
			MeshFilter meshFilter = ((transform != null) ? transform.GetComponent<MeshFilter>() : null);
			if (!(meshFilter != null))
			{
				return null;
			}
			return meshFilter.sharedMesh;
		}
		void SetMat(ref Material field, Material material)
		{
			if (field == null && material != null)
			{
				field = material;
				n++;
			}
		}
		void SetMesh(ref Mesh field, Mesh mesh3)
		{
			if (field == null && mesh3 != null)
			{
				field = mesh3;
				n++;
			}
		}
	}

	private static int RewireChildRefs(Component c, Transform rigRoot)
	{
		int num = 0;
		FieldInfo[] fields = c.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			Type fieldType = fieldInfo.FieldType;
			if (fieldType != typeof(GameObject) && fieldType != typeof(Transform))
			{
				continue;
			}
			try
			{
				if (fieldInfo.GetValue(c) is UnityEngine.Object obj && obj != null)
				{
					continue;
				}
				Transform transform = null;
				Transform[] componentsInChildren = rigRoot.GetComponentsInChildren<Transform>(includeInactive: true);
				foreach (Transform transform2 in componentsInChildren)
				{
					if (transform2.name == fieldInfo.Name)
					{
						transform = transform2;
						break;
					}
				}
				if (!(transform == null))
				{
					fieldInfo.SetValue(c, (fieldType == typeof(GameObject)) ? ((UnityEngine.Object)transform.gameObject) : ((UnityEngine.Object)transform));
					num++;
				}
			}
			catch
			{
			}
		}
		return num;
	}

	private static int HealNullFields(Component c)
	{
		int num = 0;
		FieldInfo[] fields = c.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			Type fieldType = fieldInfo.FieldType;
			try
			{
				if (fieldInfo.GetValue(c) == null && !typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
				{
					if (fieldType.IsArray && fieldType.GetArrayRank() == 1)
					{
						fieldInfo.SetValue(c, Array.CreateInstance(fieldType.GetElementType(), 0));
						num++;
					}
					else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
					{
						fieldInfo.SetValue(c, Activator.CreateInstance(fieldType));
						num++;
					}
					else if (fieldType.IsClass && !fieldType.IsAbstract && fieldType.GetConstructor(Type.EmptyTypes) != null)
					{
						fieldInfo.SetValue(c, Activator.CreateInstance(fieldType));
						num++;
					}
				}
			}
			catch
			{
			}
		}
		return num;
	}
}
