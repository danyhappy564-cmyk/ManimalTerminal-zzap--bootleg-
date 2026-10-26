using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal sealed class TerminalFirefightAssets : IDisposable
{
	internal readonly Dictionary<string, SoundBank> Banks = new Dictionary<string, SoundBank>();

	internal readonly Dictionary<SoundBank, int> FadeTypes = new Dictionary<SoundBank, int>();

	private readonly Dictionary<string, UnityEngine.Object> _objects = new Dictionary<string, UnityEngine.Object>();

	private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

	private AssetBundle _bundle;

	internal JObject Spec { get; private set; }

	internal static TerminalFirefightAssets Load(string directory)
	{
		TerminalFirefightAssets terminalFirefightAssets = new TerminalFirefightAssets();
		try
		{
			terminalFirefightAssets.Spec = JObject.Parse(File.ReadAllText(Path.Combine(directory, "plugin-data", "firefight.json")));
			if (terminalFirefightAssets.Spec.Value<int>("schema") != 1)
			{
				throw new InvalidDataException("Unsupported firefight authoring schema");
			}
			terminalFirefightAssets._bundle = AssetBundle.LoadFromFile(Path.Combine(directory, "terminal_firefight.bundle"));
			if (!terminalFirefightAssets._bundle)
			{
				throw new InvalidDataException("Firefight audio bundle failed to load");
			}
			AudioClip[] array = terminalFirefightAssets._bundle.LoadAllAssets<AudioClip>();
			foreach (AudioClip audioClip in array)
			{
				if ((bool)audioClip)
				{
					terminalFirefightAssets._clips.Add(audioClip.name, audioClip);
				}
			}
			if (terminalFirefightAssets._clips.Count != 104)
			{
				throw new InvalidDataException($"Expected 104 firefight clips, loaded {terminalFirefightAssets._clips.Count}");
			}
			foreach (KeyValuePair<string, JToken> item in (JObject)terminalFirefightAssets.Spec["blends"])
			{
				terminalFirefightAssets._objects.Add(item.Key, ScriptableObject.CreateInstance<DistanceBlendOptions>());
			}
			foreach (KeyValuePair<string, JToken> item2 in (JObject)terminalFirefightAssets.Spec["banks"])
			{
				SoundBank soundBank = ScriptableObject.CreateInstance<SoundBank>();
				soundBank.name = "Terminal_Firefight_" + item2.Key;
				terminalFirefightAssets.Banks.Add(item2.Key, soundBank);
				terminalFirefightAssets._objects.Add(item2.Key, soundBank);
			}
			foreach (KeyValuePair<string, JToken> item3 in (JObject)terminalFirefightAssets.Spec["blends"])
			{
				terminalFirefightAssets.Fill(terminalFirefightAssets._objects[item3.Key], (JObject)item3.Value);
			}
			foreach (KeyValuePair<string, JToken> item4 in (JObject)terminalFirefightAssets.Spec["banks"])
			{
				terminalFirefightAssets.Fill(terminalFirefightAssets._objects[item4.Key], (JObject)item4.Value);
				string text = item4.Value.Value<string>("BlendOptions");
				int num = ((text != null) ? terminalFirefightAssets.Spec["blends"][text].Value<int>("FadeType") : 0);
				if (num != 0 && num != 3)
				{
					throw new InvalidDataException("Unsupported firefight fade " + num);
				}
				terminalFirefightAssets.FadeTypes.Add(terminalFirefightAssets.Banks[item4.Key], num);
			}
			Plugin.Log.LogInfo($"[Firefight] restored {terminalFirefightAssets.Banks.Count} authored banks and {terminalFirefightAssets._clips.Count} clips with environment/distance layers");
			return terminalFirefightAssets;
		}
		catch
		{
			terminalFirefightAssets.Dispose();
			throw;
		}
	}

	private void Fill(object target, JObject fields)
	{
		foreach (KeyValuePair<string, JToken> field in fields)
		{
			FieldInfo fieldInfo = AccessTools.Field(target.GetType(), field.Key);
			if ((!(fieldInfo == null) || !(target is SoundBank) || fields.Value<bool>("AllowLimitedPlay") || (!(field.Key == "LimitIfVisible") && !(field.Key == "LimitRadius") && !(field.Key == "LimitPercentOfClip"))) && (!(fieldInfo == null) || !(target is DistanceBlendOptions) || !(field.Key == "FadeType")))
			{
				if (fieldInfo == null)
				{
					throw new MissingFieldException(target.GetType().FullName, field.Key);
				}
				fieldInfo.SetValue(target, ConvertValue(field.Value, fieldInfo.FieldType));
			}
		}
	}

	private object ConvertValue(JToken token, Type type)
	{
		if (token == null || token.Type == JTokenType.Null)
		{
			return null;
		}
		if (type == typeof(AudioClip))
		{
			string text = token.Value<string>();
			if (!_clips.TryGetValue(text, out var value))
			{
				throw new InvalidDataException("Missing firefight clip: " + text);
			}
			return value;
		}
		if (typeof(UnityEngine.Object).IsAssignableFrom(type))
		{
			string text2 = token.Value<string>();
			if (!_objects.TryGetValue(text2, out var value2) || !type.IsInstanceOfType(value2))
			{
				throw new InvalidDataException("Invalid firefight asset reference: " + text2);
			}
			return value2;
		}
		if (type.IsArray)
		{
			JArray jArray = (JArray)token;
			Type elementType = type.GetElementType();
			Array array = Array.CreateInstance(elementType, jArray.Count);
			for (int i = 0; i < jArray.Count; i++)
			{
				array.SetValue(ConvertValue(jArray[i], elementType), i);
			}
			return array;
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, token.Value<int>());
		}
		if (type == typeof(bool))
		{
			return (token.Type == JTokenType.Boolean) ? token.Value<bool>() : (token.Value<int>() != 0);
		}
		if (token is JObject fields)
		{
			object obj = Activator.CreateInstance(type);
			Fill(obj, fields);
			return obj;
		}
		return token.ToObject(type);
	}

	public void Dispose()
	{
		foreach (UnityEngine.Object value in _objects.Values)
		{
			if ((bool)value)
			{
				UnityEngine.Object.Destroy(value);
			}
		}
		_objects.Clear();
		Banks.Clear();
		FadeTypes.Clear();
		_clips.Clear();
		if ((bool)_bundle)
		{
			_bundle.Unload(unloadAllLoadedObjects: true);
		}
		_bundle = null;
	}
}
