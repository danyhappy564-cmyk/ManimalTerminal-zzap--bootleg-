using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Manimal.Terminal;

internal static class TerminalCraneFalling
{
	private const string FallTrigger = "Fall_01_2944538615";

	private static bool _staged;

	private static readonly HashSet<string> NativeSet = new HashSet<string> { "TriggerZone", "HandlerDamage", "HandlerGOState", "HandlerGameObjectState", "HandlerEffect", "HandlerAnimator", "HandlerFilterOnce", "HandlerPlaySoundAdvanced", "HandlerPlaySoundSequenced" };

	internal static bool Ready => _staged;

	internal static void ResetForRaid()
	{
		_staged = false;
	}

	internal static void TryStage()
	{
		if (_staged || !Plugin.CraneFalling.Value || !TerminalGate.On)
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.TriggersEmitter == null)
		{
			return;
		}
		Transform transform = TerminalRigFill.FindRootNamed("Terminal_Crane_Falling");
		if (!transform)
		{
			return;
		}
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_crane.json");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[Crane] terminal_crane.json missing — crane stays inert");
				_staged = true;
				return;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(path));
			Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>();
			TerminalRigFill.IndexTree(transform, "Terminal_Crane_Falling", dictionary);
			TerminalRigFill.StageDelayShims(jObject["rows"] as JArray, "Crane");
			TerminalRigFill.StageRows(jObject["rows"] as JArray, NativeSet, dictionary, "Crane", (Component comp, JObject row) =>
			{
				if (row.Value<string>("cls") == "HandlerPlaySoundSequenced")
				{
					FieldInfo fieldInfo = AccessTools.Field(comp.GetType(), "_playTriggerId");
					if (fieldInfo != null && string.IsNullOrEmpty(fieldInfo.GetValue(comp) as string))
					{
						fieldInfo.SetValue(comp, "Fall_01_2944538615");
					}
				}
			});
			if (dictionary.TryGetValue("Terminal_Crane_Falling/INTERACTIVE_Terminal_Crash_Set_VFX/Terminal_Crash_set", out var value))
			{
				value.gameObject.SetActive(value: false);
			}
			int num = 0;
			Renderer[] componentsInChildren = transform.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren)
			{
				if ((bool)renderer && renderer.name.Contains("SHADOW") && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
				{
					renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
					num++;
				}
			}
			try
			{
				TerminalShaderRebind.RebindNowUnder(transform);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Crane] post-stage rebind failed: " + ex.Message);
			}
			if (num > 0)
			{
				Plugin.Log.LogInfo($"[Crane] {num} SHADOW mesh(es) set caster-only");
			}
			int num3 = 0;
			HashSet<Material> hashSet = new HashSet<Material>();
			componentsInChildren = transform.GetComponentsInChildren<Renderer>(includeInactive: false);
			foreach (Renderer renderer2 in componentsInChildren)
			{
				if (renderer2.name.Contains("SHADOW"))
				{
					continue;
				}
				Material[] sharedMaterials = renderer2.sharedMaterials;
				foreach (Material material in sharedMaterials)
				{
					if ((bool)material && hashSet.Add(material) && num3 < 20)
					{
						num3++;
						Plugin.Log.LogInfo("[Crane] mat '" + material.name + "' on '" + renderer2.name + "': shader " + string.Format("'{0}' supported={1}", material.shader ? material.shader.name : "NULL", (bool)material.shader && material.shader.isSupported));
					}
				}
			}
			try
			{
				TriggersEmitter.Instance.Subscribe("Switch_crane_enter", () =>
				{
					Plugin.Log.LogInfo("[Crane] TRIGGERED — player under the crane, fall chain should run");
				});
				TriggersEmitter.Instance.Subscribe("Fall_01_2944538615", () =>
				{
					Plugin.Log.LogInfo("[Crane] fall trigger fired — animator should be dropping the crane");
				});
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[Crane] diag subscribe failed: " + ex2.Message);
			}
			_staged = true;
			TerminalRigFill.ScheduleSubscriberDump("Crane", "Switch_crane_enter", "Fall_01_2944538615", "Crane_fall_01", "Mortar_Crane_Play");
			Plugin.Log.LogInfo("[Crane] CRANE_FALLING RESTORED — walk-under trigger armed, fall/damage/foley native");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Crane] staging failed: {arg}");
			_staged = true;
		}
	}
}
