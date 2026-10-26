using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalSerializedCurves
{
	[Serializable]
	private sealed class Container
	{
		public AnimationCurve curve;
	}

	internal static AnimationCurve FromUnity(JToken token)
	{
		if (!(token is JObject jObject) || !(jObject["m_Curve"] is JArray))
		{
			return null;
		}
		JObject jObject2 = (JObject)jObject.DeepClone();
		jObject2["serializedVersion"] = "2";
		foreach (JToken item in (JArray)jObject2["m_Curve"])
		{
			((item as JObject) ?? throw new JsonException("Curve key must be an object"))["serializedVersion"] = "3";
		}
		return JsonUtility.FromJson<Container>(new JObject { ["curve"] = jObject2 }.ToString(Formatting.None))?.curve;
	}

	internal static AnimationCurve FromDonor(JToken token)
	{
		if (!(token?["curve"] is JArray jArray))
		{
			return null;
		}
		Keyframe[] array = new Keyframe[jArray.Count];
		for (int i = 0; i < array.Length; i++)
		{
			JToken jToken = jArray[i];
			array[i] = new Keyframe(jToken.Value<float>("t"), jToken.Value<float>("v"), jToken.Value<float>("i"), jToken.Value<float>("o"))
			{
				weightedMode = (WeightedMode)jToken.Value<int?>("weightedMode").GetValueOrDefault(),
				inWeight = (jToken.Value<float?>("inWeight") ?? (1f / 3f)),
				outWeight = (jToken.Value<float?>("outWeight") ?? (1f / 3f))
			};
		}
		return new AnimationCurve(array)
		{
			preWrapMode = (WrapMode)(token.Value<int?>("pre") ?? 8),
			postWrapMode = (WrapMode)(token.Value<int?>("post") ?? 8)
		};
	}

	internal static JObject ToDonor(AnimationCurve curve)
	{
		JArray jArray = new JArray();
		Keyframe[] keys = curve.keys;
		for (int i = 0; i < keys.Length; i++)
		{
			Keyframe keyframe = keys[i];
			jArray.Add(new JObject
			{
				["t"] = keyframe.time,
				["v"] = keyframe.value,
				["i"] = keyframe.inTangent,
				["o"] = keyframe.outTangent,
				["weightedMode"] = (int)keyframe.weightedMode,
				["inWeight"] = keyframe.inWeight,
				["outWeight"] = keyframe.outWeight
			});
		}
		return new JObject
		{
			["curve"] = jArray,
			["pre"] = (int)curve.preWrapMode,
			["post"] = (int)curve.postWrapMode
		};
	}
}
