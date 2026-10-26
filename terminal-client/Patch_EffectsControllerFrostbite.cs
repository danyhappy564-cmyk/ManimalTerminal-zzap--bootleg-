using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(EffectsController), "Awake")]
internal static class Patch_EffectsControllerFrostbite
{
	private static void Prefix(EffectsController __instance)
	{
		if (TerminalGate.On)
		{
			if (__instance.GetComponent<FrostbiteEffect>() == null)
			{
				__instance.gameObject.AddComponent<FrostbiteEffect>();
				Plugin.Log.LogWarning("[RaidFix] added missing FrostbiteEffect to fallback camera (Cam2 predates it)");
			}
			HealPostProcessLayer(__instance.gameObject);
		}
	}

	private static void HealPostProcessLayer(GameObject go)
	{
		try
		{
			Type type = AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.PostProcessLayer");
			if (type == null)
			{
				return;
			}
			Behaviour behaviour = go.GetComponent(type) as Behaviour;
			if (behaviour == null)
			{
				Plugin.Log.LogDebug("[RaidFix] no PostProcessLayer on fallback camera");
				return;
			}
			FieldInfo fieldInfo = AccessTools.Field(type, "m_Resources");
			if (fieldInfo == null)
			{
				Plugin.Log.LogWarning("[RaidFix] PostProcessLayer.m_Resources not found — PP version drift?");
				return;
			}
			if (fieldInfo.GetValue(behaviour) as UnityEngine.Object != null)
			{
				Plugin.Log.LogDebug("[RaidFix] PostProcessLayer resources already present — no heal needed");
				return;
			}
			Type type2 = AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.PostProcessResources");
			UnityEngine.Object obj = null;
			if (type2 != null)
			{
				UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(type2);
				int num = 0;
				if (num < array.Length)
				{
					obj = array[num];
				}
			}
			if (obj == null)
			{
				Plugin.Log.LogWarning("[RaidFix] PostProcessLayer has NO resources and none found in memory — layer stays dead (DLSS/FSR upscale will not run)");
				return;
			}
			bool enabled = behaviour.enabled;
			behaviour.enabled = false;
			MethodInfo methodInfo = AccessTools.Method(type, "Init", new Type[1] { type2 });
			if (methodInfo != null)
			{
				methodInfo.Invoke(behaviour, new object[1] { obj });
			}
			else
			{
				fieldInfo.SetValue(behaviour, obj);
			}
			behaviour.enabled = enabled;
			Plugin.Log.LogWarning("[RaidFix] HEALED PostProcessLayer: fed '" + obj.name + "' resources (was null) — DLSS/FSR final pass can run");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[RaidFix] PostProcessLayer heal failed: " + ex.Message);
		}
	}
}
