using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalRigFill
{
	internal class DelayShimHost : MonoBehaviour
	{
		public void Run(string input, string output, float secs, string tag)
		{
			if (!TerminalCoop.Active || (TerminalCoop.Authority && !TerminalCoop.Replaying))
			{
				StartCoroutine(Fire(input, output, secs, tag));
			}
		}

		private IEnumerator Fire(string input, string output, float secs, string tag)
		{
			yield return new WaitForSeconds(secs);
			Plugin.Log.LogInfo($"[{tag}] delay shim: '{input}' + {secs:0.##}s -> '{output}'");
			TerminalGatesExplosion.Emit(output);
		}
	}

	internal class TriggerDiagHost : MonoBehaviour
	{
		public string Tag;

		public string[] Ids;

		private float _at;

		private void Start()
		{
			_at = Time.time + 3f;
		}

		private void Update()
		{
			if (!(Time.time < _at))
			{
				DumpSubscribers(Tag, Ids ?? new string[0]);
				UnityEngine.Object.Destroy(gameObject);
			}
		}
	}

	internal static Transform FindRootNamed(string name)
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (!sceneAt.isLoaded || sceneAt.name == null || !sceneAt.name.StartsWith("Terminal"))
			{
				continue;
			}
			GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
			for (int j = 0; j < rootGameObjects.Length; j++)
			{
				Transform transform = FindChildNamed(rootGameObjects[j].transform, name);
				if (transform != null)
				{
					return transform;
				}
			}
		}
		return null;
	}

	internal static Transform FindChildNamed(Transform root, string name)
	{
		if (root.name == name)
		{
			return root;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform transform = FindChildNamed(root.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	internal static void IndexTree(Transform t, string path, Dictionary<string, Transform> index)
	{
		index[path] = t;
		for (int i = 0; i < t.childCount; i++)
		{
			Transform child = t.GetChild(i);
			IndexTree(child, path + "/" + child.name, index);
		}
	}

	internal static Type TypeFor(string cls)
	{
		return AccessTools.TypeByName("EFT.GameTriggers." + cls) ?? AccessTools.TypeByName("CommonAssets.Scripts.Game.GameTriggers.Handlers." + cls) ?? AccessTools.TypeByName(cls);
	}

	internal static int StageRows(JArray rows, HashSet<string> nativeSet, Dictionary<string, Transform> index, string tag, Action<Component, JObject> postFill = null)
	{
		int num = 0;
		int num2 = 0;
		foreach (JToken item in rows ?? new JArray())
		{
			if (!(item is JObject jObject))
			{
				continue;
			}
			string text = jObject.Value<string>("cls") ?? "";
			if (!nativeSet.Contains(text))
			{
				continue;
			}
			string text2 = jObject.Value<string>("path") ?? "";
			if (!index.TryGetValue(text2, out var value))
			{
				Plugin.Log.LogWarning("[" + tag + "] '" + text2 + "' not in scene — " + text + " skipped");
				continue;
			}
			Type type = TypeFor(text);
			if (type == null)
			{
				Plugin.Log.LogWarning("[" + tag + "] type '" + text + "' missing from 4.0 — skipped");
				continue;
			}
			if ((jObject.Value<bool?>("active") ?? true) && !value.gameObject.activeSelf)
			{
				value.gameObject.SetActive(value: true);
			}
			int valueOrDefault = jObject.Value<int?>("cocc").GetValueOrDefault();
			Component[] components = value.gameObject.GetComponents(type);
			Component component = ((valueOrDefault < components.Length) ? components[valueOrDefault] : null);
			if (!component)
			{
				component = value.gameObject.AddComponent(type);
				num++;
			}
			if ((bool)component)
			{
				if (jObject["fields"] is JObject fields)
				{
					FillObject(component, fields, index);
				}
				Behaviour behaviour = component as Behaviour;
				if ((bool)behaviour)
				{
					behaviour.enabled = true;
				}
				postFill?.Invoke(component, jObject);
				num2++;
			}
		}
		Plugin.Log.LogInfo($"[{tag}] {num2} native handler(s) live ({num} added)");
		return num2;
	}

	internal static void FillObject(object target, JObject fields, Dictionary<string, Transform> index)
	{
		Type type = target.GetType();
		foreach (JProperty item in fields.Properties())
		{
			if (item.Name == "m_Enabled")
			{
				continue;
			}
			FieldInfo fieldInfo = AccessTools.Field(type, item.Name);
			if (fieldInfo == null)
			{
				continue;
			}
			try
			{
				object obj = Convert(item.Value, fieldInfo.FieldType, index);
				if (obj != null || !fieldInfo.FieldType.IsValueType)
				{
					fieldInfo.SetValue(target, obj);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogDebug("[RigFill] " + type.Name + "." + item.Name + " failed: " + ex.Message);
			}
		}
	}

	internal static object Convert(JToken tok, Type want, Dictionary<string, Transform> index)
	{
		if (tok == null || tok.Type == JTokenType.Null)
		{
			return null;
		}
		if (tok is JObject jObject && jObject["$ref"] != null)
		{
			return ResolveRef(jObject["$ref"], want, index);
		}
		if (tok is JObject jObject2 && jObject2["$ext"] != null)
		{
			return ResolveExt(jObject2["$ext"], want);
		}
		if (want == typeof(string))
		{
			if (tok.Type != JTokenType.String)
			{
				return tok.ToString();
			}
			return tok.Value<string>();
		}
		if (want == typeof(float))
		{
			return tok.Value<float>();
		}
		if (want == typeof(int))
		{
			return tok.Value<int>();
		}
		if (want == typeof(long))
		{
			return tok.Value<long>();
		}
		if (want == typeof(bool))
		{
			return (tok.Type == JTokenType.Boolean) ? tok.Value<bool>() : (tok.Value<int>() != 0);
		}
		if (want.IsEnum)
		{
			return Enum.ToObject(want, tok.Value<long>());
		}
		if (want == typeof(Vector3) && tok is JObject jObject3)
		{
			return new Vector3(jObject3.Value<float>("x"), jObject3.Value<float>("y"), jObject3.Value<float>("z"));
		}
		if (want == typeof(Vector2) && tok is JObject jObject4)
		{
			return new Vector2(jObject4.Value<float>("x"), jObject4.Value<float>("y"));
		}
		if (want == typeof(Color) && tok is JObject jObject5)
		{
			return new Color(jObject5.Value<float>("r"), jObject5.Value<float>("g"), jObject5.Value<float>("b"), jObject5.Value<float>("a"));
		}
		if (want == typeof(AnimationCurve) && tok is JObject cv)
		{
			return BuildCurve(cv);
		}
		if (tok is JArray jArray && want.IsGenericType && want.GetGenericTypeDefinition() == typeof(List<>))
		{
			Type want2 = want.GetGenericArguments()[0];
			IList list = (IList)Activator.CreateInstance(want);
			{
				foreach (JToken item in jArray)
				{
					list.Add(Convert(item, want2, index));
				}
				return list;
			}
		}
		if (tok is JArray jArray2 && want.IsArray)
		{
			Type elementType = want.GetElementType();
			Array array = Array.CreateInstance(elementType, jArray2.Count);
			for (int i = 0; i < jArray2.Count; i++)
			{
				array.SetValue(Convert(jArray2[i], elementType, index), i);
			}
			return array;
		}
		if (tok is JObject fields && !typeof(UnityEngine.Object).IsAssignableFrom(want))
		{
			object obj = Activator.CreateInstance(want);
			FillObject(obj, fields, index);
			return obj;
		}
		return null;
	}

	private static AnimationCurve BuildCurve(JObject cv)
	{
		AnimationCurve animationCurve = new AnimationCurve();
		if (cv["m_Curve"] is JArray jArray)
		{
			foreach (JToken item in jArray)
			{
				if (item is JObject jObject)
				{
					Keyframe keyframe = new Keyframe(jObject.Value<float>("time"), jObject.Value<float>("value"), jObject.Value<float>("inSlope"), jObject.Value<float>("outSlope"), jObject.Value<float>("inWeight"), jObject.Value<float>("outWeight"));
					keyframe.weightedMode = (WeightedMode)jObject.Value<int?>("weightedMode").GetValueOrDefault();
					Keyframe key = keyframe;
					animationCurve.AddKey(key);
				}
			}
		}
		animationCurve.preWrapMode = WrapMode.ClampForever;
		animationCurve.postWrapMode = WrapMode.ClampForever;
		return animationCurve;
	}

	private static object ResolveExt(JToken ext, Type want)
	{
		string text = (ext as JObject)?.Value<string>("name");
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		if (want == typeof(AudioClip))
		{
			return TerminalFxBundle.FindClip(text);
		}
		if (want.Name == "AudioMixerGroup")
		{
			UnityEngine.Object[] array = Resources.FindObjectsOfTypeAll(want);
			foreach (UnityEngine.Object obj in array)
			{
				if ((bool)obj && obj.name == text)
				{
					return obj;
				}
			}
			Plugin.Log.LogDebug("[RigFill] mixer group '" + text + "' not found — sound keeps default routing");
		}
		return null;
	}

	internal static void StageDelayShims(JArray rows, string tag)
	{
		int num = 0;
		DelayShimHost host = new GameObject("Terminal_DelayShims_" + tag).AddComponent<DelayShimHost>();
		foreach (JToken item in rows ?? new JArray())
		{
			if (!(item is JObject jObject) || jObject.Value<string>("cls") != "HandlerDelay" || !(jObject["fields"] is JObject jObject2))
			{
				continue;
			}
			string input = jObject2.Value<string>("_inputTrigger");
			string output = jObject2.Value<string>("_outputTrigger");
			float secs = jObject2.Value<float?>("_secondsDelay").GetValueOrDefault();
			if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
			{
				continue;
			}
			try
			{
				Singleton<GameWorld>.Instance.TriggersEmitter.Subscribe(input, () =>
				{
					host.Run(input, output, secs, tag);
				});
				num++;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[" + tag + "] delay shim '" + input + "' subscribe failed: " + ex.Message);
			}
		}
		if (num > 0)
		{
			Plugin.Log.LogInfo($"[{tag}] {num} delay shim(s) armed (native HandlerDelay is gutted in this build)");
		}
		else
		{
			UnityEngine.Object.Destroy(host.gameObject);
		}
	}

	internal static int StableHash(string s)
	{
		int num = 23;
		foreach (char c in s)
		{
			num = num * 31 + c;
		}
		return num;
	}

	internal static void DumpSubscribers(string tag, string[] ids)
	{
		try
		{
			GameWorld instance = Singleton<GameWorld>.Instance;
			TriggersEmitter triggersEmitter = ((instance != null) ? instance.TriggersEmitter : null);
			if (triggersEmitter == null)
			{
				Plugin.Log.LogWarning("[" + tag + "] no emitter to dump");
				return;
			}
			List<string> list = new List<string>();
			foreach (string text in ids)
			{
				triggersEmitter._actionsMap.TryGetValue(StableHash(text), out var value);
				list.Add($"{text}={value?.Count ?? 0}");
			}
			Plugin.Log.LogInfo("[" + tag + "] SUBSCRIBERS: " + string.Join(", ", list));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[" + tag + "] subscriber dump failed: " + ex.Message);
		}
	}

	internal static void ScheduleSubscriberDump(string tag, params string[] ids)
	{
		TriggerDiagHost triggerDiagHost = new GameObject("Terminal_TriggerDiag_" + tag).AddComponent<TriggerDiagHost>();
		triggerDiagHost.Tag = tag;
		triggerDiagHost.Ids = ids;
	}

	internal static object ResolveRef(JToken r, Type want, Dictionary<string, Transform> index)
	{
		if (r == null || r.Type == JTokenType.Null)
		{
			return null;
		}
		string text = r.Value<string>("path");
		if (text == null || !index.TryGetValue(text, out var value))
		{
			return null;
		}
		string text2 = r.Value<string>("kind");
		if (text2 == "go")
		{
			return value.gameObject;
		}
		if (text2 == "transform")
		{
			return value;
		}
		string text3 = r.Value<string>("cls");
		Type type = TypeFor(text3) ?? AccessTools.TypeByName("UnityEngine." + text3 + ", UnityEngine.CoreModule");
		if (type == null && text3 == "Animator")
		{
			type = typeof(Animator);
		}
		if (type == null)
		{
			return null;
		}
		int valueOrDefault = r.Value<int?>("cocc").GetValueOrDefault();
		Component[] components = value.gameObject.GetComponents(type);
		if (valueOrDefault >= components.Length)
		{
			if (components.Length == 0)
			{
				return null;
			}
			return components[0];
		}
		return components[valueOrDefault];
	}
}
