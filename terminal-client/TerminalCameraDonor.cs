using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalCameraDonor
{
	[HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
	internal static class Patch_DumpDonorCamera
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			try
			{
				if (TerminalGate.On)
				{
					Camera camera = CameraManager.Instance?.Camera;
					if (camera != null)
					{
						TryGraft(camera.gameObject, "OnGameStarted");
					}
				}
				if (!TerminalGate.On && Plugin.DevMode.Value && !File.Exists(DonorPath))
				{
					Camera camera2 = CameraManager.Instance?.Camera;
					if (camera2 == null)
					{
						Plugin.Log.LogWarning("[CamDonor] no EFT.CameraControl.CameraManager.Camera to dump");
					}
					else
					{
						Dump(camera2.gameObject);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[CamDonor] dump failed: " + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(CameraManager), "SetCamera", new Type[] { typeof(Camera) })]
	internal static class Patch_GraftDonorCamera
	{
		[HarmonyPrefix]
		private static void Prefix(Camera camera)
		{
			if (TerminalGate.On && !(camera == null))
			{
				TryGraft(camera.gameObject, "SetCamera");
			}
		}
	}

	private static readonly HashSet<string> SkipTypes = new HashSet<string> { "Transform", "Camera", "AudioListener", "FlareLayer" };

	private static readonly HashSet<string> AllowGraft = new HashSet<string> { "DesaturateEffect", "Antialiasing", "Tonemapping", "PerfectCullingCamera" };

	private static readonly HashSet<string> NeverGraft = new HashSet<string> { "MBOIT_Scattering", "ContactShadows", "PerfectCullingCrossSceneSampler", "StreamingController", "ScreenWater", "InfectionEffect", "RainScreenDrops" };

	private static GameObject _graftedGo;

	internal static Behaviour ProvisionedBloom;

	private static string DonorPath => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "camera", "camera_donor.json");

	private static void Dump(GameObject go)
	{
		JArray jArray = new JArray();
		Component[] components = go.GetComponents<Component>();
		foreach (Component component in components)
		{
			if (component == null)
			{
				continue;
			}
			Type type = component.GetType();
			if (SkipTypes.Contains(type.Name))
			{
				continue;
			}
			JObject jObject = new JObject();
			int num = 0;
			foreach (FieldInfo item in SerializedFields(type))
			{
				try
				{
					JToken jToken = Encode(item.GetValue(component));
					if (jToken != null)
					{
						jObject[item.Name] = jToken;
					}
					else
					{
						num++;
					}
				}
				catch
				{
					num++;
				}
			}
			jArray.Add(new JObject
			{
				["type"] = type.FullName,
				["enabled"] = !(component is Behaviour behaviour) || behaviour.enabled,
				["fields"] = jObject,
				["unencodable"] = num
			});
		}
		JObject jObject2 = new JObject
		{
			["map"] = Singleton<GameWorld>.Instance?.LocationId ?? "?",
			["go"] = go.name,
			["components"] = jArray
		};
		Directory.CreateDirectory(Path.GetDirectoryName(DonorPath));
		File.WriteAllText(DonorPath, jObject2.ToString());
		Plugin.Log.LogWarning($"[CamDonor] DUMPED '{go.name}' ({jArray.Count} components) from " + string.Format("'{0}' -> {1} — this is now the terminal camera donor", jObject2["map"], DonorPath));
	}

	internal static void TryGraft(GameObject go, string site)
	{
		if (go == null || _graftedGo == go)
		{
			return;
		}
		if (go.GetComponent<EffectsController>() == null)
		{
			Plugin.Log.LogDebug("[CamDonor] " + site + ": camera not furnished yet (no EffectsController) — deferring graft");
			return;
		}
		try
		{
			if (!File.Exists(DonorPath))
			{
				Plugin.Log.LogWarning("[CamDonor] no plugin-data/camera/camera_donor.json — run one vanilla raid (any map) with DevMode on to record a donor. staying on plain Cam2.");
				return;
			}
			Graft(go, JObject.Parse(File.ReadAllText(DonorPath)));
			_graftedGo = go;
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[CamDonor] graft failed — plain Cam2 stands: {arg}");
		}
	}

	private static void Graft(GameObject go, JObject donor)
	{
		if (!(donor["components"] is JArray jArray))
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = (Plugin.CamDonorSkip?.Value ?? "").Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			hashSet.Add(text.Trim());
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<(Behaviour, bool)> list3 = new List<(Behaviour, bool)>();
		bool activeSelf = go.activeSelf;
		go.SetActive(value: false);
		try
		{
			foreach (JToken item2 in jArray)
			{
				if (!(item2 is JObject jObject))
				{
					continue;
				}
				Type type = AccessTools.TypeByName((string?)jObject["type"]);
				if (type == null || !typeof(Component).IsAssignableFrom(type))
				{
					num2++;
				}
				else
				{
					if (SkipTypes.Contains(type.Name))
					{
						continue;
					}
					bool flag = type.Name == "UltimateBloom" && (Chainloader.PluginInfos.ContainsKey("com.janky.hollywoodgraphics") || Chainloader.PluginInfos.ContainsKey("com.janky.hollywoodfx"));
					if (!AllowGraft.Contains(type.Name) && !flag)
					{
						num5++;
						continue;
					}
					if (hashSet.Contains(type.Name))
					{
						num5++;
						continue;
					}
					if (go.GetComponent(type) != null)
					{
						num4++;
						continue;
					}
					Component component = go.AddComponent(type);
					if (component == null)
					{
						num2++;
						continue;
					}
					int refMiss = 0;
					List<string> list4 = new List<string>();
					if (jObject["fields"] is JObject jObject2)
					{
						foreach (KeyValuePair<string, JToken> item3 in jObject2)
						{
							if (flag && item3.Key == "m_DustTexture")
							{
								continue;
							}
							FieldInfo fieldInfo = FindSerializedField(type, item3.Key);
							if (fieldInfo == null)
							{
								continue;
							}
							try
							{
								int num6 = refMiss;
								object obj = Decode(item3.Value, fieldInfo.FieldType, go, ref refMiss);
								if (refMiss > num6)
								{
									list4.Add(item3.Key);
								}
								if (obj != null || !fieldInfo.FieldType.IsValueType)
								{
									fieldInfo.SetValue(component, obj);
									num++;
								}
							}
							catch
							{
							}
						}
					}
					if (flag && refMiss == 0)
					{
						try
						{
							FieldInfo fieldInfo2 = FindSerializedField(type, "m_UseLensDust");
							if (fieldInfo2 != null)
							{
								fieldInfo2.SetValue(component, false);
							}
						}
						catch
						{
						}
						ProvisionedBloom = component as Behaviour;
						Plugin.Log.LogWarning("[CamDonor] UltimateBloom provisioned for HollywoodGraphics (keeps its init alive)");
					}
					if (refMiss > 0)
					{
						num3 += refMiss;
						list2.Add(type.Name + "(" + string.Join("+", list4) + ")");
						UnityEngine.Object.DestroyImmediate(component);
						continue;
					}
					list.Add(type.Name);
					if (component is Behaviour behaviour)
					{
						bool item = jObject["enabled"] == null || (bool)jObject["enabled"];
						list3.Add((behaviour, item));
						behaviour.enabled = false;
					}
				}
			}
		}
		finally
		{
			go.SetActive(activeSelf);
		}
		foreach (var (behaviour2, enabled) in list3)
		{
			if (behaviour2 != null)
			{
				behaviour2.enabled = enabled;
			}
		}
		Plugin.Log.LogWarning("[CamDonor] GRAFTED (add-only) onto '" + go.name + "': " + string.Format("added [{0}] ({1} fields), ", string.Join(", ", list), num) + $"{num4} chassis component(s) left untouched, {num5} skipped by config, " + $"{num2} type(s) unresolved" + ((list2.Count > 0) ? ("; WITHHELD (refs owed): " + string.Join(", ", list2)) : ""));
	}

	private static FieldInfo FindSerializedField(Type type, string name)
	{
		foreach (FieldInfo item in SerializedFields(type))
		{
			if (item.Name == name)
			{
				return item;
			}
		}
		return null;
	}

	private static IEnumerable<FieldInfo> SerializedFields(Type t)
	{
		Type cur = t;
		while (cur != null && cur != typeof(MonoBehaviour) && cur != typeof(Behaviour) && cur != typeof(Component))
		{
			FieldInfo[] fields = cur.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!fieldInfo.IsInitOnly && !fieldInfo.IsLiteral && (fieldInfo.IsPublic || fieldInfo.GetCustomAttribute<SerializeField>() != null) && !typeof(Delegate).IsAssignableFrom(fieldInfo.FieldType))
				{
					yield return fieldInfo;
				}
			}
			cur = cur.BaseType;
		}
	}

	private static JToken Encode(object v)
	{
		if (v != null)
		{
			if (!(v is bool) && !(v is string))
			{
				if (!(v is int) && !(v is long) && !(v is short) && !(v is byte) && !(v is sbyte) && !(v is uint) && !(v is ushort) && !(v is ulong) && !(v is float) && !(v is double))
				{
					if (!(v is Enum value))
					{
						if (!(v is Vector2 vector))
						{
							if (!(v is Vector3 vector2))
							{
								if (!(v is Vector4 vector3))
								{
									if (!(v is Color color))
									{
										if (!(v is LayerMask layerMask))
										{
											if (!(v is AnimationCurve curve))
											{
												if (v is UnityEngine.Object obj)
												{
													return new JObject
													{
														["ref"] = obj.GetType().Name,
														["name"] = obj.name
													};
												}
												return null;
											}
											return TerminalSerializedCurves.ToDonor(curve);
										}
										return new JObject { ["mask"] = layerMask.value };
									}
									return new JObject
									{
										["r"] = color.r,
										["g"] = color.g,
										["b"] = color.b,
										["a"] = color.a
									};
								}
								return new JObject
								{
									["x"] = vector3.x,
									["y"] = vector3.y,
									["z"] = vector3.z,
									["w"] = vector3.w
								};
							}
							return new JObject
							{
								["x"] = vector2.x,
								["y"] = vector2.y,
								["z"] = vector2.z
							};
						}
						return new JObject
						{
							["x"] = vector.x,
							["y"] = vector.y
						};
					}
					return new JValue(Convert.ToInt64(value));
				}
				return new JValue(v);
			}
			return new JValue(v);
		}
		return JValue.CreateNull();
	}

	private static object Decode(JToken tok, Type want, GameObject host, ref int refMiss)
	{
		if (tok == null || tok.Type == JTokenType.Null)
		{
			return null;
		}
		if (want.IsEnum)
		{
			return Enum.ToObject(want, (long)tok);
		}
		if (want == typeof(bool))
		{
			return (bool)tok;
		}
		if (want == typeof(string))
		{
			return (string?)tok;
		}
		if (want == typeof(int))
		{
			return (int)tok;
		}
		if (want == typeof(long))
		{
			return (long)tok;
		}
		if (want == typeof(float))
		{
			return (float)tok;
		}
		if (want == typeof(double))
		{
			return (double)tok;
		}
		if (want == typeof(byte))
		{
			return (byte)tok;
		}
		if (want == typeof(Vector2))
		{
			return new Vector2((float)tok["x"], (float)tok["y"]);
		}
		if (want == typeof(Vector3))
		{
			return new Vector3((float)tok["x"], (float)tok["y"], (float)tok["z"]);
		}
		if (want == typeof(Vector4))
		{
			return new Vector4((float)tok["x"], (float)tok["y"], (float)tok["z"], (float)tok["w"]);
		}
		if (want == typeof(Color))
		{
			return new Color((float)tok["r"], (float)tok["g"], (float)tok["b"], (float)tok["a"]);
		}
		if (want == typeof(LayerMask))
		{
			return (LayerMask)(int)tok["mask"];
		}
		if (want == typeof(AnimationCurve))
		{
			return TerminalSerializedCurves.FromDonor(tok);
		}
		if (typeof(UnityEngine.Object).IsAssignableFrom(want) && tok["ref"] != null)
		{
			string name = (string?)tok["name"];
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			if (typeof(Component).IsAssignableFrom(want))
			{
				Component component = host.GetComponent(want);
				if (component != null)
				{
					return component;
				}
			}
			if (want == typeof(Shader))
			{
				Shader shader = Shader.Find(name);
				if (shader != null)
				{
					return shader;
				}
			}
			UnityEngine.Object obj = Array.Find(Resources.FindObjectsOfTypeAll(want), (UnityEngine.Object o) => o.name == name);
			if (obj == null)
			{
				refMiss++;
				return null;
			}
			if (obj is Material material && material.shader != null)
			{
				try
				{
					Shader shader2 = Shader.Find(material.shader.name);
					if (shader2 != null && shader2 != material.shader)
					{
						material.shader = shader2;
					}
				}
				catch
				{
				}
			}
			return obj;
		}
		return null;
	}
}
