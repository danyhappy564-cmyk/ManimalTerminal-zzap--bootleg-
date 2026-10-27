using System;
using System.Collections.Generic;
using System.IO;
using Audio.AmbientSubsystem.Data;
using Audio.SpatialSystem;
using EFT.Settings.Sound;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Audio;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalAmbientEffectsAssets
{
	internal sealed class EffectsDefinition
	{
		internal string RetailVersion;

		internal string RootName;

		internal AssetDefinition Reverb;

		internal AssetDefinition Delay;

		internal AssetDefinition EffectsSends;

		internal AssetDefinition EffectsVolumes;
	}

	internal sealed class AssetDefinition
	{
		internal string Name;

		internal List<ParameterDefinition> Parameters;
	}

	internal sealed class ParameterDefinition
	{
		internal string Field;

		internal string ParamName;

		internal Dictionary<EAudioRoomTypeMask, float> Values;
	}

	internal delegate bool MixerParameterReader(string parameterName, out float value);

	private readonly struct RoomSpec
	{
		internal string Name { get; }

		internal EAudioRoomTypeMask Mask { get; }

		internal RoomSpec(string name, EAudioRoomTypeMask mask)
		{
			Name = name;
			Mask = mask;
		}
	}

	private const int Schema = 1;

	private const string SidecarName = "terminal_mixer_effects.json";

	private static readonly RoomSpec[] RequiredRooms = new RoomSpec[3]
	{
		new RoomSpec("IndoorGeneric", EAudioRoomTypeMask.IndoorGeneric),
		new RoomSpec("IndoorIsolated", EAudioRoomTypeMask.IndoorIsolated),
		new RoomSpec("Outdoor", EAudioRoomTypeMask.Outdoor)
	};

	private static readonly List<ScriptableObject> OwnedAssets = new List<ScriptableObject>();

	private static MixerEffectsDataSO _active;

	private static AudioMixer _activeMixer;

	internal static MixerEffectsDataSO Active => _active;

	internal static string SidecarPath => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "acoustics", "terminal_mixer_effects.json");

	internal static bool TryBuildFromLiveMixer(out MixerEffectsDataSO effects, out string error)
	{
		effects = null;
		error = null;
		try
		{
			if (!MonoBehaviourSingleton<BetterAudio>.Instantiated || MonoBehaviourSingleton<BetterAudio>.Instance == null || !MonoBehaviourSingleton<BetterAudio>.Instance.Master)
			{
				error = "BetterAudio.Master is unavailable while binding EffectsData";
				return false;
			}
			AudioMixer master = MonoBehaviourSingleton<BetterAudio>.Instance.Master;
			AudioMixer masterMixer = SoundSettingsController.MasterMixer;
			if ((bool)masterMixer && masterMixer != master)
			{
				error = "BetterAudio.Master differs from SoundSettingsController.MasterMixer; refusing to bind a stale mixer";
				return false;
			}
			return TryBuild(master, SidecarPath, out effects, out error);
		}
		catch (Exception ex)
		{
			error = "live master mixer lookup failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	internal static bool TryBuild(AudioMixer mixer, string sidecarPath, out MixerEffectsDataSO effects, out string error)
	{
		effects = null;
		error = null;
		if (!mixer)
		{
			error = "SoundSettingsController.MasterMixer is unavailable";
			return false;
		}
		if ((bool)_active && _activeMixer == mixer)
		{
			effects = _active;
			return true;
		}
		if ((bool)_active || OwnedAssets.Count != 0)
		{
			Reset();
		}
		if (!TryReadDefinition(sidecarPath, out var definition, out error))
		{
			return false;
		}
		if (!ValidateParameterNames(definition, mixer.GetFloat, out error))
		{
			return false;
		}
		try
		{
			MixerEffectsDataSO mixerEffectsDataSO = Create<MixerEffectsDataSO>(definition.RootName);
			mixerEffectsDataSO.reverbMixerData = BuildReverb(definition.Reverb);
			mixerEffectsDataSO.delayMixerData = BuildDelay(definition.Delay);
			mixerEffectsDataSO.effectsSendsMixerData = BuildGeneric(definition.EffectsSends);
			mixerEffectsDataSO.effectsVolumesMixerData = BuildGeneric(definition.EffectsVolumes);
			if (!IsCompleteGraph(mixerEffectsDataSO))
			{
				throw new InvalidOperationException("the constructed graph contains a null child asset");
			}
			_active = mixerEffectsDataSO;
			_activeMixer = mixer;
			effects = mixerEffectsDataSO;
			return true;
		}
		catch (Exception ex)
		{
			Reset();
			error = "retail mixer graph construction failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	internal static bool TryReadDefinition(string path, out EffectsDefinition definition, out string error)
	{
		definition = null;
		error = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			error = "mixer sidecar path is empty";
			return false;
		}
		try
		{
			if (!File.Exists(path))
			{
				error = "mixer sidecar is missing: " + path;
				return false;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(path));
			if (jObject.Value<int?>("schema") != 1)
			{
				error = "mixer sidecar schema is unsupported";
				return false;
			}
			if (!(jObject["root"] is JObject jObject2) || string.IsNullOrWhiteSpace(jObject2.Value<string>("name")))
			{
				error = "mixer sidecar has no root asset name";
				return false;
			}
			EffectsDefinition effectsDefinition = new EffectsDefinition
			{
				RetailVersion = (jObject.Value<string>("retailVersion") ?? string.Empty),
				RootName = jObject2.Value<string>("name")
			};
			if (!TryReadAsset(jObject["reverbMixerData"] as JObject, "reverbMixerData", allowEmpty: false, out effectsDefinition.Reverb, out error) || !TryReadAsset(jObject["delayMixerData"] as JObject, "delayMixerData", allowEmpty: false, out effectsDefinition.Delay, out error) || !TryReadAsset(jObject["effectsSendsMixerData"] as JObject, "effectsSendsMixerData", allowEmpty: false, out effectsDefinition.EffectsSends, out error) || !TryReadAsset(jObject["effectsVolumesMixerData"] as JObject, "effectsVolumesMixerData", allowEmpty: true, out effectsDefinition.EffectsVolumes, out error))
			{
				return false;
			}
			if (!ValidateFieldSet(effectsDefinition.Reverb, "reverbMixerData", new string[13]
			{
				"reverbVolume", "reverbDryLevel", "mix", "reverbDelay", "reflectDelay", "reflectMix", "roomSize", "decayTime", "decayHFRatio", "roomLowFrequencyGain",
				"lowFrequencyReference", "roomHighFrequencyGain", "highFrequencyReference"
			}, out error) || !ValidateFieldSet(effectsDefinition.Delay, "delayMixerData", new string[5] { "delayVolume", "delay", "decayTime", "wetMix", "lowpassFilterFreq" }, out error) || !ValidateFieldSet(effectsDefinition.EffectsSends, "effectsSendsMixerData", new string[8] { "AmbientOutDelaySend", "AmbientOutReverbSend", "AmbCommonEffectsSend", "AmbientOutDayEffectsSend", "AmbientOutNightEffectsSend", "CommonAmbOutEffectsSend", "CommonAmbInEffectsSend", "DelayToReverbSendLevel" }, out error) || !ValidateFieldSet(effectsDefinition.EffectsVolumes, "effectsVolumesMixerData", Array.Empty<string>(), out error))
			{
				return false;
			}
			definition = effectsDefinition;
			return true;
		}
		catch (Exception ex)
		{
			error = "mixer sidecar parse failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	internal static bool ValidateParameterNames(EffectsDefinition definition, MixerParameterReader mixerGetFloat, out string error)
	{
		error = null;
		if (definition == null)
		{
			error = "mixer definition is null";
			return false;
		}
		if (mixerGetFloat == null)
		{
			error = "mixer parameter validator is null";
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		AssetDefinition[] array = new AssetDefinition[4] { definition.Reverb, definition.Delay, definition.EffectsSends, definition.EffectsVolumes };
		foreach (AssetDefinition assetDefinition in array)
		{
			if (assetDefinition == null)
			{
				continue;
			}
			for (int j = 0; j < assetDefinition.Parameters.Count; j++)
			{
				ParameterDefinition parameterDefinition = assetDefinition.Parameters[j];
				if (parameterDefinition == null || string.IsNullOrWhiteSpace(parameterDefinition.ParamName))
				{
					error = "mixer definition contains an empty parameter name in group " + assetDefinition.Name;
					return false;
				}
				if (!hashSet.Add(parameterDefinition.ParamName))
				{
					error = "mixer definition contains duplicate parameter '" + parameterDefinition.ParamName + "'";
					return false;
				}
				if (!mixerGetFloat(parameterDefinition.ParamName, out var _))
				{
					error = "retail ambient mixer parameter is missing from the live MasterMixer: '" + parameterDefinition.ParamName + "'";
					return false;
				}
			}
		}
		return true;
	}

	internal static void Reset(bool immediate = true)
	{
		_active = null;
		_activeMixer = null;
		for (int num = OwnedAssets.Count - 1; num >= 0; num--)
		{
			ScriptableObject scriptableObject = OwnedAssets[num];
			if ((bool)scriptableObject)
			{
				if (immediate) UnityEngine.Object.DestroyImmediate(scriptableObject);
				else TerminalRaidCleanup.DestroyOwned(scriptableObject);
			}
		}
		OwnedAssets.Clear();
	}

	private static bool TryReadAsset(JObject value, string name, bool allowEmpty, out AssetDefinition definition, out string error)
	{
		definition = null;
		error = null;
		if (value == null)
		{
			error = "mixer sidecar asset '" + name + "' is missing";
			return false;
		}
		if (!(value["parameters"] is JArray jArray) || (!allowEmpty && jArray.Count == 0))
		{
			error = "mixer sidecar asset '" + name + "' has no authored parameters";
			return false;
		}
		AssetDefinition assetDefinition = new AssetDefinition
		{
			Name = (value.Value<string>("name") ?? name),
			Parameters = new List<ParameterDefinition>(jArray.Count)
		};
		for (int i = 0; i < jArray.Count; i++)
		{
			if (!(jArray[i] is JObject jObject))
			{
				error = $"mixer sidecar asset '{name}' has a non-object parameter at index {i}";
				return false;
			}
			string text = jObject.Value<string>("field");
			string text2 = jObject.Value<string>("paramName");
			JObject jObject2 = jObject["parametersByRoom"] as JObject;
			if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2) || jObject2 == null)
			{
				error = $"mixer sidecar asset '{name}' has an incomplete parameter at index {i}";
				return false;
			}
			JArray jArray2 = jObject2["keys"] as JArray;
			JArray jArray3 = jObject2["values"] as JArray;
			if (jArray2 == null || jArray3 == null || jArray2.Count != jArray3.Count || jArray2.Count != RequiredRooms.Length)
			{
				error = "mixer sidecar parameter '" + text2 + "' has an invalid room dictionary";
				return false;
			}
			Dictionary<EAudioRoomTypeMask, float> dictionary = new Dictionary<EAudioRoomTypeMask, float>();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			for (int j = 0; j < jArray2.Count; j++)
			{
				string text3 = jArray2[j]?.Value<string>();
				if (string.IsNullOrWhiteSpace(text3) || !hashSet.Add(text3))
				{
					error = "mixer sidecar parameter '" + text2 + "' has duplicate/empty room keys";
					return false;
				}
				if (!Enum.TryParse<EAudioRoomTypeMask>(text3, ignoreCase: false, out var result) || !IsRequiredRoom(result))
				{
					error = "mixer sidecar parameter '" + text2 + "' has unsupported room key '" + text3 + "'";
					return false;
				}
				float num;
				try
				{
					num = jArray3[j].Value<float>();
				}
				catch
				{
					error = "mixer sidecar parameter '" + text2 + "' has a non-scalar room value";
					return false;
				}
				if (float.IsNaN(num) || float.IsInfinity(num))
				{
					error = "mixer sidecar parameter '" + text2 + "' has a non-finite room value";
					return false;
				}
				dictionary.Add(result, num);
			}
			for (int k = 0; k < RequiredRooms.Length; k++)
			{
				if (!dictionary.ContainsKey(RequiredRooms[k].Mask))
				{
					error = "mixer sidecar parameter '" + text2 + "' omits room '" + RequiredRooms[k].Name + "'";
					return false;
				}
			}
			assetDefinition.Parameters.Add(new ParameterDefinition
			{
				Field = text,
				ParamName = text2,
				Values = dictionary
			});
		}
		definition = assetDefinition;
		return true;
	}

	private static bool ValidateFieldSet(AssetDefinition definition, string name, string[] expected, out string error)
	{
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		error = null;
		if (definition == null || definition.Parameters.Count != expected.Length)
		{
			error = "mixer sidecar asset '" + name + "' does not contain the complete authored field set";
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < definition.Parameters.Count; i++)
		{
			string field = definition.Parameters[i].Field;
			if (!hashSet.Add(field))
			{
				error = "mixer sidecar asset '" + name + "' contains duplicate field '" + field + "'";
				return false;
			}
			if (!ValueEnumerableExtensions.Any<string>(ValueEnumerable.AsValueEnumerable<string>(expected), (Func<string, bool>)((string candidate) => string.Equals(field, candidate, StringComparison.Ordinal))))
			{
				error = "mixer sidecar asset '" + name + "' contains unknown field '" + field + "'";
				return false;
			}
		}
		return true;
	}

	private static bool IsRequiredRoom(EAudioRoomTypeMask room)
	{
		if (room != EAudioRoomTypeMask.IndoorGeneric && room != EAudioRoomTypeMask.IndoorIsolated)
		{
			return room == EAudioRoomTypeMask.Outdoor;
		}
		return true;
	}

	private static T Create<T>(string name) where T : ScriptableObject
	{
		T val = ScriptableObject.CreateInstance<T>();
		if (!val)
		{
			throw new InvalidOperationException("Unity could not create " + typeof(T).Name);
		}
		val.name = name;
		val.hideFlags = HideFlags.DontUnloadUnusedAsset;
		OwnedAssets.Add(val);
		return val;
	}

	private static ReverbMixerDataSO BuildReverb(AssetDefinition definition)
	{
		ReverbMixerDataSO reverbMixerDataSO = Create<ReverbMixerDataSO>(definition.Name);
		for (int i = 0; i < definition.Parameters.Count; i++)
		{
			MixerEnvironmentParameter mixerEnvironmentParameter = BuildParameter(definition.Parameters[i]);
			switch (definition.Parameters[i].Field)
			{
			case "reverbVolume":
				reverbMixerDataSO.reverbVolume = mixerEnvironmentParameter;
				break;
			case "reverbDryLevel":
				reverbMixerDataSO.reverbDryLevel = mixerEnvironmentParameter;
				break;
			case "mix":
				reverbMixerDataSO.mix = mixerEnvironmentParameter;
				break;
			case "reverbDelay":
				reverbMixerDataSO.reverbDelay = mixerEnvironmentParameter;
				break;
			case "reflectDelay":
				reverbMixerDataSO.reflectDelay = mixerEnvironmentParameter;
				break;
			case "reflectMix":
				reverbMixerDataSO.reflectMix = mixerEnvironmentParameter;
				break;
			case "roomSize":
				reverbMixerDataSO.roomSize = mixerEnvironmentParameter;
				break;
			case "decayTime":
				reverbMixerDataSO.decayTime = mixerEnvironmentParameter;
				break;
			case "decayHFRatio":
				reverbMixerDataSO.decayHFRatio = mixerEnvironmentParameter;
				break;
			case "roomLowFrequencyGain":
				reverbMixerDataSO.roomLowFrequencyGain = mixerEnvironmentParameter;
				break;
			case "lowFrequencyReference":
				reverbMixerDataSO.lowFrequencyReference = mixerEnvironmentParameter;
				break;
			case "roomHighFrequencyGain":
				reverbMixerDataSO.roomHighFrequencyGain = mixerEnvironmentParameter;
				break;
			case "highFrequencyReference":
				reverbMixerDataSO.highFrequencyReference = mixerEnvironmentParameter;
				break;
			default:
				throw new InvalidOperationException("unknown retail reverb field '" + definition.Parameters[i].Field + "'");
			}
		}
		return reverbMixerDataSO;
	}

	private static DelayMixerDataSO BuildDelay(AssetDefinition definition)
	{
		DelayMixerDataSO delayMixerDataSO = Create<DelayMixerDataSO>(definition.Name);
		for (int i = 0; i < definition.Parameters.Count; i++)
		{
			MixerEnvironmentParameter mixerEnvironmentParameter = BuildParameter(definition.Parameters[i]);
			switch (definition.Parameters[i].Field)
			{
			case "delayVolume":
				delayMixerDataSO.delayVolume = mixerEnvironmentParameter;
				break;
			case "delay":
				delayMixerDataSO.delay = mixerEnvironmentParameter;
				break;
			case "decayTime":
				delayMixerDataSO.decayTime = mixerEnvironmentParameter;
				break;
			case "wetMix":
				delayMixerDataSO.wetMix = mixerEnvironmentParameter;
				break;
			case "lowpassFilterFreq":
				delayMixerDataSO.lowpassFilterFreq = mixerEnvironmentParameter;
				break;
			default:
				throw new InvalidOperationException("unknown retail delay field '" + definition.Parameters[i].Field + "'");
			}
		}
		return delayMixerDataSO;
	}

	private static GenericVolumeParamsMixerDataSO BuildGeneric(AssetDefinition definition)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		GenericVolumeParamsMixerDataSO genericVolumeParamsMixerDataSO = Create<GenericVolumeParamsMixerDataSO>(definition.Name);
		MixerEnvironmentParameter[] mixerEnvironmentParameters = ValueEnumerableExtensions.ToArray<ListSelect<ParameterDefinition, MixerEnvironmentParameter>, MixerEnvironmentParameter>(ValueEnumerableExtensions.Select<ParameterDefinition, MixerEnvironmentParameter>(ValueEnumerable.AsValueEnumerable<ParameterDefinition>(definition.Parameters), (Func<ParameterDefinition, MixerEnvironmentParameter>)BuildParameter));
		genericVolumeParamsMixerDataSO.mixerEnvironmentParameters = mixerEnvironmentParameters;
		return genericVolumeParamsMixerDataSO;
	}

	private static MixerEnvironmentParameter BuildParameter(ParameterDefinition definition)
	{
		MixerEnvironmentParameter mixerEnvironmentParameter = new MixerEnvironmentParameter
		{
			paramName = definition.ParamName,
			parametersByRoom = new MixerEnvironmentParametersByRoom()
		};
		for (int i = 0; i < RequiredRooms.Length; i++)
		{
			mixerEnvironmentParameter.parametersByRoom[RequiredRooms[i].Mask] = definition.Values[RequiredRooms[i].Mask];
		}
		return mixerEnvironmentParameter;
	}

	private static bool IsCompleteGraph(MixerEffectsDataSO root)
	{
		if ((bool)root && (bool)root.reverbMixerData && (bool)root.delayMixerData && (bool)root.effectsSendsMixerData && (bool)root.effectsVolumesMixerData && root.reverbMixerData.reverbVolume != null && root.reverbMixerData.reverbDryLevel != null && root.reverbMixerData.mix != null && root.reverbMixerData.reverbDelay != null && root.reverbMixerData.reflectDelay != null && root.reverbMixerData.reflectMix != null && root.reverbMixerData.roomSize != null && root.reverbMixerData.decayTime != null && root.reverbMixerData.decayHFRatio != null && root.reverbMixerData.roomLowFrequencyGain != null && root.reverbMixerData.lowFrequencyReference != null && root.reverbMixerData.roomHighFrequencyGain != null && root.reverbMixerData.highFrequencyReference != null && root.delayMixerData.delayVolume != null && root.delayMixerData.delay != null && root.delayMixerData.decayTime != null && root.delayMixerData.wetMix != null && root.delayMixerData.lowpassFilterFreq != null && root.effectsSendsMixerData.mixerEnvironmentParameters != null)
		{
			return root.effectsVolumesMixerData.mixerEnvironmentParameters != null;
		}
		return false;
	}
}
