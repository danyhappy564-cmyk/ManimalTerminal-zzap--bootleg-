using System;
using System.Collections;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalEnvDump
{
	internal static void Dump(string tag)
	{
		try
		{
			DumpInner(tag);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[RenderEnv:" + tag + "] dump failed: " + ex.Message);
		}
	}

	private static void DumpInner(string tag)
	{
		ManualLogSource log = Plugin.Log;
		string text = "<no world>";
		try
		{
			GameWorld instance = Singleton<GameWorld>.Instance;
			if (instance != null)
			{
				text = instance.LocationId;
			}
		}
		catch
		{
		}
		log.LogWarning("===== [RenderEnv:" + tag + "] loc=" + text + " =====");
		Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
		if (camera != null)
		{
			log.LogWarning($"[RenderEnv] camera '{camera.name}': HDR={camera.allowHDR} MSAA={camera.allowMSAA} clear={camera.clearFlags} bg={camera.backgroundColor} fov={camera.fieldOfView:F1} cullMask=0x{camera.cullingMask:X} renderingPath={camera.actualRenderingPath}");
			StringBuilder stringBuilder = new StringBuilder();
			Component[] components = camera.GetComponents<Component>();
			foreach (Component component in components)
			{
				if (component == null)
				{
					stringBuilder.Append(" <missing-script>");
					continue;
				}
				Behaviour behaviour = component as Behaviour;
				StringBuilder stringBuilder2 = stringBuilder;
				string name = component.GetType().Name;
				string text2;
				if (behaviour != null)
				{
					text2 = (behaviour.enabled ? "" : "(OFF)");
				}
				else
				{
					text2 = "";
				}
				stringBuilder2.Append(" " + name + text2);
			}
			log.LogWarning($"[RenderEnv] camera chain:{stringBuilder}");
		}
		else
		{
			log.LogWarning("[RenderEnv] no camera!");
		}
		log.LogWarning($"[RenderEnv] ambient: mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity:F2} " + $"light={RenderSettings.ambientLight} sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} gnd={RenderSettings.ambientGroundColor}");
		log.LogWarning("[RenderEnv] skybox=" + ((RenderSettings.skybox != null) ? (RenderSettings.skybox.name + "/" + ((RenderSettings.skybox.shader != null) ? RenderSettings.skybox.shader.name : "?")) : "<null>") + " " + $"reflMode={RenderSettings.defaultReflectionMode} reflIntensity={RenderSettings.reflectionIntensity:F2} " + $"fog={RenderSettings.fog} fogColor={RenderSettings.fogColor} fogMode={RenderSettings.fogMode} fogDensity={RenderSettings.fogDensity:F4}");
		try
		{
			Light[] array = UnityEngine.Object.FindObjectsOfType<Light>();
			Array.Sort(array, (Light a, Light b) => b.intensity.CompareTo(a.intensity));
			StringBuilder stringBuilder3 = new StringBuilder();
			int num = 0;
			Light[] array2 = array;
			foreach (Light light in array2)
			{
				if (light.type == LightType.Directional && light.enabled)
				{
					num++;
				}
			}
			for (int num2 = 0; num2 < array.Length && num2 < 8; num2++)
			{
				stringBuilder3.Append($"{array[num2].name}({array[num2].type},on={array[num2].enabled},i={array[num2].intensity:F1}) ");
			}
			log.LogWarning($"[RenderEnv] {array.Length} lights ({num} directional enabled), brightest: {stringBuilder3}");
		}
		catch (Exception ex)
		{
			log.LogWarning("[RenderEnv] lights failed: " + ex.Message);
		}
		DumpPostVolumes(log);
		log.LogWarning("===== [RenderEnv:" + tag + "] end =====");
	}

	private static void DumpPostVolumes(ManualLogSource L)
	{
		Type type = TerminalTypes.ByName("UnityEngine.Rendering.PostProcessing.PostProcessVolume");
		if (type == null)
		{
			L.LogWarning("[RenderEnv] PostProcessVolume type not found");
			return;
		}
		UnityEngine.Object[] array = UnityEngine.Object.FindObjectsOfType(type);
		L.LogWarning($"[RenderEnv] {array.Length} PostProcessVolume(s)");
		UnityEngine.Object[] array2 = array;
		foreach (UnityEngine.Object obj in array2)
		{
			try
			{
				bool flag = (bool)(GetMember(obj, "isGlobal") ?? ((object)false));
				float num = ToF(GetMember(obj, "priority"));
				float num2 = ToF(GetMember(obj, "weight"));
				object obj2 = GetMember(obj, "sharedProfile") ?? GetMember(obj, "profile");
				string text = ((obj2 == null) ? "<null>" : (obj2 as UnityEngine.Object)?.name);
				L.LogWarning($"[RenderEnv]  vol '{obj?.name}' global={flag} prio={num:F0} weight={num2:F2} enabled={(obj as Behaviour)?.enabled} profile={text}");
				if (obj2 == null || !(GetMember(obj2, "settings") is IEnumerable enumerable))
				{
					continue;
				}
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						bool flag2 = (bool)(GetMember(item, "active") ?? ((object)true));
						L.LogWarning($"[RenderEnv]    {item.GetType().Name} active={flag2} {DumpOverriddenParams(item)}");
					}
				}
			}
			catch (Exception ex)
			{
				L.LogWarning("[RenderEnv]  vol dump failed: " + ex.Message);
			}
		}
	}

	private static string DumpOverriddenParams(object effect)
	{
		StringBuilder stringBuilder = new StringBuilder();
		FieldInfo[] fields = effect.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
		foreach (FieldInfo fieldInfo in fields)
		{
			object value = fieldInfo.GetValue(effect);
			if (value == null)
			{
				continue;
			}
			FieldInfo field = fieldInfo.FieldType.GetField("overrideState");
			FieldInfo field2 = fieldInfo.FieldType.GetField("value");
			if (field == null || field2 == null)
			{
				continue;
			}
			try
			{
				if ((bool)field.GetValue(value))
				{
					stringBuilder.Append($"{fieldInfo.Name}={field2.GetValue(value)} ");
				}
			}
			catch
			{
			}
		}
		return stringBuilder.ToString();
	}

	private static object GetMember(object o, string name)
	{
		if (o == null)
		{
			return null;
		}
		Type type = o.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
		if (property != null && property.CanRead)
		{
			return property.GetValue(o);
		}
		return type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(o);
	}

	private static float ToF(object o)
	{
		if (o is float)
		{
			return (float)o;
		}
		return 0f;
	}
}
