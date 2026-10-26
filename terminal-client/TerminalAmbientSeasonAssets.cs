using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Audio.AmbientSubsystem;
using Audio.AmbientSubsystem.Data;
using EFT.Weather;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalAmbientSeasonAssets
{
	internal enum SeasonAmbientAssetState
	{
		Idle,
		Loading,
		Ready,
		Failed
	}

	private enum Phase
	{
		Idle,
		Loading,
		Ready,
		Failed
	}

	private sealed class DictionaryData
	{
		internal List<string> Keys;

		internal List<JToken> Values;
	}

	private sealed class ClipLoadResult
	{
		internal AudioClip Clip;

		internal bool Loaded;
	}

	internal sealed class SeasonAmbientDefinition
	{
		internal string RetailVersion;

		internal string RootName;

		internal string PrecipitationName;

		internal JObject RootFields;

		internal JObject PrecipitationFields;

		internal Dictionary<ClipKey, ClipEntry> Manifest;

		internal List<ClipEntry> RequiredClips;
	}

	internal sealed class ClipEntry
	{
		internal ClipKey Key { get; }

		internal string Name { get; }

		internal string Path { get; }

		internal string Sha256 { get; }

		internal string AbsolutePath { get; }

		internal ClipEntry(ClipKey key, string name, string path, string sha256, string absolutePath)
		{
			Key = key;
			Name = name;
			Path = path;
			Sha256 = sha256;
			AbsolutePath = absolutePath;
		}
	}

	internal readonly struct ClipKey : IEquatable<ClipKey>
	{
		internal string File { get; }

		internal long PathId { get; }

		internal ClipKey(string file, long pathId)
		{
			File = file;
			PathId = pathId;
		}

		public bool Equals(ClipKey other)
		{
			if (PathId == other.PathId)
			{
				return string.Equals(File, other.File, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		public override bool Equals(object obj)
		{
			if (obj is ClipKey other)
			{
				return Equals(other);
			}
			return false;
		}

		public override int GetHashCode()
		{
			return (StringComparer.OrdinalIgnoreCase.GetHashCode(File ?? string.Empty) * 397) ^ PathId.GetHashCode();
		}

		public override string ToString()
		{
			return File + ":" + PathId;
		}
	}

	private const int Schema = 1;

	private const string SidecarName = "terminal_season_ambient.json";

	private static readonly ESeasonStatus[] WindSeasons = new ESeasonStatus[7]
	{
		ESeasonStatus.Summer,
		ESeasonStatus.Autumn,
		ESeasonStatus.Winter,
		ESeasonStatus.Spring,
		ESeasonStatus.Storm,
		ESeasonStatus.AutumnLate,
		ESeasonStatus.SpringEarly
	};

	private static readonly ESeasonStatus[] PrecipitationSeasons = new ESeasonStatus[6]
	{
		ESeasonStatus.Summer,
		ESeasonStatus.Autumn,
		ESeasonStatus.Spring,
		ESeasonStatus.AutumnLate,
		ESeasonStatus.SpringEarly,
		ESeasonStatus.Winter
	};

	private static readonly EWindSpeed[] WindSpeeds = new EWindSpeed[3]
	{
		EWindSpeed.Light,
		EWindSpeed.Moderate,
		EWindSpeed.Strong
	};

	private static readonly RainController.ERainIntensity[] PrecipitationIntensities = new RainController.ERainIntensity[3]
	{
		RainController.ERainIntensity.Low,
		RainController.ERainIntensity.Med,
		RainController.ERainIntensity.High
	};

	private static readonly List<UnityEngine.Object> OwnedAssets = new List<UnityEngine.Object>();

	private static SeasonAmbientSoundDataSO _active;

	private static SeasonAmbientDefinition _definition;

	private static Phase _phase;

	private static string _lastError;

	private static Coroutine _loadRoutine;

	private static MonoBehaviour _runner;

	private static UnityWebRequest _loadingRequest;

	private static bool _audioEventSubscribed;

	private static int _generation;

	internal static SeasonAmbientAssetState State => _phase switch
	{
		Phase.Loading => SeasonAmbientAssetState.Loading, 
		Phase.Ready => SeasonAmbientAssetState.Ready, 
		Phase.Failed => SeasonAmbientAssetState.Failed, 
		_ => SeasonAmbientAssetState.Idle, 
	};

	internal static bool AssetsReady
	{
		get
		{
			if (_phase == Phase.Ready)
			{
				return _active;
			}
			return false;
		}
	}

	internal static bool Loading => _phase == Phase.Loading;

	internal static string LastError => _lastError;

	internal static SeasonAmbientSoundDataSO Active
	{
		get
		{
			if (!AssetsReady)
			{
				return null;
			}
			return _active;
		}
	}

	internal static int RequiredClipCount => _definition?.RequiredClips.Count ?? 0;

	internal static string SidecarPath => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "acoustics", "terminal_season_ambient.json");

	internal static event Action Invalidating;

	internal static bool EnsureLoading(MonoBehaviour runner)
	{
		if (AssetsReady)
		{
			return true;
		}
		if (_phase == Phase.Loading)
		{
			return false;
		}
		if (_phase == Phase.Failed)
		{
			return false;
		}
		if (!runner)
		{
			return false;
		}
		if (!TryReadDefinition(SidecarPath, out var definition, out var error))
		{
			Fail(error);
			return false;
		}
		_definition = definition;
		_lastError = null;
		_runner = runner;
		_phase = Phase.Loading;
		_generation++;
		SubscribeAudioReset();
		try
		{
			_loadRoutine = runner.StartCoroutine(LoadRoutine(definition, _generation));
		}
		catch (Exception ex)
		{
			_loadRoutine = null;
			Fail("seasonal ambient loading could not start: " + ex.GetBaseException().Message);
		}
		return false;
	}

	internal static bool TryGetLoaded(out SeasonAmbientSoundDataSO asset, out string error)
	{
		asset = Active;
		error = _lastError;
		if ((bool)asset)
		{
			return true;
		}
		if (string.IsNullOrEmpty(error))
		{
			error = ((_phase == Phase.Loading) ? "seasonal ambient assets are still loading" : "seasonal ambient assets are not ready");
		}
		return false;
	}

	internal static void ResetForRaid()
	{
		_generation++;
		if ((bool)_runner && _loadRoutine != null)
		{
			try
			{
				_runner.StopCoroutine(_loadRoutine);
			}
			catch
			{
			}
		}
		_loadRoutine = null;
		_runner = null;
		DisposeRequest();
		UnsubscribeAudioReset();
		DestroyOwnedAssets();
		_active = null;
		_definition = null;
		_phase = Phase.Idle;
		_lastError = null;
	}

	internal static bool TryBuildFromClipMap(string sidecarPath, IDictionary<string, AudioClip> clips, out SeasonAmbientSoundDataSO asset, out string error)
	{
		asset = null;
		error = null;
		ResetForRaid();
		if (!TryReadDefinition(sidecarPath, out var definition, out error))
		{
			return false;
		}
		if (clips == null)
		{
			error = "seasonal ambient test clip map is null";
			return false;
		}
		foreach (ClipEntry requiredClip in definition.RequiredClips)
		{
			if (!clips.TryGetValue(requiredClip.Key.ToString(), out var value) || !value)
			{
				DestroyOwnedAssets();
				error = "seasonal ambient required clip is absent from the loaded map: " + requiredClip.Key;
				return false;
			}
			if (!OwnedAssets.Contains(value))
			{
				OwnedAssets.Add(value);
			}
		}
		try
		{
			if (!TryProject(definition, (ClipKey key) => (!clips.TryGetValue(key.ToString(), out var value2)) ? null : value2, out asset, out error))
			{
				return false;
			}
			_definition = definition;
			_active = asset;
			_phase = Phase.Ready;
			SubscribeAudioReset();
			return true;
		}
		catch (Exception ex)
		{
			DestroyOwnedAssets();
			asset = null;
			error = "seasonal ambient graph construction failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	internal static bool TryReadDefinition(string sidecarPath, out SeasonAmbientDefinition definition, out string error)
	{
		definition = null;
		error = null;
		if (string.IsNullOrWhiteSpace(sidecarPath))
		{
			error = "seasonal ambient sidecar path is empty";
			return false;
		}
		try
		{
			if (!File.Exists(sidecarPath))
			{
				error = "seasonal ambient sidecar is missing: " + sidecarPath;
				return false;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(sidecarPath));
			if (jObject.Value<int?>("schema") != 1)
			{
				error = "seasonal ambient sidecar schema is unsupported";
				return false;
			}
			JObject jObject2 = jObject["root"] as JObject;
			JObject jObject3 = jObject["precipitation"] as JObject;
			JObject jObject4 = jObject2?["fields"] as JObject;
			JObject jObject5 = jObject3?["fields"] as JObject;
			if (jObject2 == null || jObject4 == null)
			{
				error = "seasonal ambient sidecar has no root fields";
				return false;
			}
			if (jObject3 == null || jObject5 == null)
			{
				error = "seasonal ambient sidecar has no precipitation storage fields";
				return false;
			}
			string text = jObject.Value<string>("clipManifest");
			if (string.IsNullOrWhiteSpace(text))
			{
				error = "seasonal ambient sidecar has no clip manifest reference";
				return false;
			}
			if (!TryReadManifest(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sidecarPath)) ?? ".", text)), out var manifest, out error))
			{
				return false;
			}
			SeasonAmbientDefinition seasonAmbientDefinition = new SeasonAmbientDefinition
			{
				RetailVersion = (jObject.Value<string>("retailVersion") ?? string.Empty),
				RootName = (jObject2.Value<string>("name") ?? "TerminalSeasonAmbientSoundData"),
				PrecipitationName = (jObject3.Value<string>("name") ?? "TerminalPrecipitationSoundStorageSO"),
				RootFields = jObject4,
				PrecipitationFields = jObject5,
				Manifest = manifest,
				RequiredClips = new List<ClipEntry>()
			};
			if (!ValidateAndCollect(seasonAmbientDefinition, out error))
			{
				return false;
			}
			definition = seasonAmbientDefinition;
			return true;
		}
		catch (Exception ex)
		{
			error = "seasonal ambient sidecar parse failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static IEnumerator LoadRoutine(SeasonAmbientDefinition definition, int generation)
	{
		Dictionary<ClipKey, AudioClip> loaded = new Dictionary<ClipKey, AudioClip>();
		Exception loadError = null;
		for (int i = 0; i < definition.RequiredClips.Count; i++)
		{
			if (generation != _generation)
			{
				yield break;
			}
			ClipEntry entry = definition.RequiredClips[i];
			ClipLoadResult result = new ClipLoadResult();
			IEnumerator step = LoadClipRoutine(entry, generation, result);
			while (true)
			{
				bool flag;
				try
				{
					flag = step.MoveNext();
				}
				catch (Exception ex)
				{
					loadError = ex;
					break;
				}
				if (!flag)
				{
					break;
				}
				yield return step.Current;
			}
			if (loadError != null)
			{
				break;
			}
			if (!result.Loaded || !result.Clip)
			{
				loadError = new InvalidOperationException("WAV request returned no clip for '" + entry.Key.ToString() + "'");
				break;
			}
			loaded[entry.Key] = result.Clip;
			OwnedAssets.Add(result.Clip);
		}
		if (generation == _generation)
		{
			if (loadError != null)
			{
				Fail("seasonal ambient loading failed: " + loadError.GetBaseException().Message);
				_loadRoutine = null;
				_runner = null;
				yield break;
			}
			if (!TryProject(definition, (ClipKey key) => (!loaded.TryGetValue(key, out var value)) ? null : value, out var asset, out var error))
			{
				Fail(error);
				_loadRoutine = null;
				_runner = null;
				yield break;
			}
			if (generation != _generation)
			{
				DestroyOwnedAssets();
				yield break;
			}
			_active = asset;
			_phase = Phase.Ready;
			_lastError = null;
			DisposeRequest();
			_loadRoutine = null;
			_runner = null;
			Plugin.Log?.LogInfo("[AmbientSeason] seasonal ambient graph ready: " + definition.RequiredClips.Count + " unique clip(s), 7 wind season(s), 7 day/night season(s), 6 precipitation season(s)");
		}
	}

	private static IEnumerator LoadClipRoutine(ClipEntry entry, int generation, ClipLoadResult result)
	{
		UnityWebRequest request = (_loadingRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(entry.AbsolutePath).AbsoluteUri, AudioType.WAV));
		try
		{
			yield return request.SendWebRequest();
			if (generation == _generation)
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					throw new InvalidOperationException("WAV request failed for '" + entry.Key.ToString() + "': " + request.error);
				}
				AudioClip content = DownloadHandlerAudioClip.GetContent(request);
				if (!content || content.samples <= 0)
				{
					throw new InvalidOperationException("WAV request returned no samples for '" + entry.Key.ToString() + "'");
				}
				content.name = entry.Name;
				result.Clip = content;
				result.Loaded = true;
			}
		}
		finally
		{
			if (_loadingRequest == request)
			{
				_loadingRequest = null;
			}
			request.Dispose();
		}
	}

	private static bool TryProject(SeasonAmbientDefinition definition, Func<ClipKey, AudioClip> resolve, out SeasonAmbientSoundDataSO asset, out string error)
	{
		asset = null;
		error = null;
		if (definition == null || resolve == null)
		{
			error = "seasonal ambient projection input is null";
			return false;
		}
		try
		{
			SeasonAmbientSoundDataSO seasonAmbientSoundDataSO = Create<SeasonAmbientSoundDataSO>(definition.RootName);
			PrecipitationSoundStorageSO precipitationSoundStorageSo = seasonAmbientSoundDataSO._precipitationSoundStorageSo;
			if ((bool)precipitationSoundStorageSo && !OwnedAssets.Contains(precipitationSoundStorageSo))
			{
				OwnedAssets.Add(precipitationSoundStorageSo);
			}
			seasonAmbientSoundDataSO._windSoundStorage = BuildWind(definition.RootFields, resolve);
			seasonAmbientSoundDataSO._dayTimeAmbientSeasonClips = BuildDayNight(definition.RootFields, resolve);
			PrecipitationSoundStorageSO precipitationSoundStorageSO = Create<PrecipitationSoundStorageSO>(definition.PrecipitationName);
			precipitationSoundStorageSO._storage = BuildPrecipitation(definition.PrecipitationFields, resolve);
			seasonAmbientSoundDataSO._precipitationSoundStorageSo = precipitationSoundStorageSO;
			if ((bool)precipitationSoundStorageSo && (object)precipitationSoundStorageSo != precipitationSoundStorageSO)
			{
				UnityEngine.Object.DestroyImmediate(precipitationSoundStorageSo);
			}
			asset = seasonAmbientSoundDataSO;
			return true;
		}
		catch (Exception ex)
		{
			DestroyOwnedAssets();
			error = "seasonal ambient graph construction failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static WindSoundStorage BuildWind(JObject fields, Func<ClipKey, AudioClip> resolve)
	{
		WindSoundStorage windSoundStorage = new WindSoundStorage();
		DictionaryData dictionary = ReadDictionary(fields, "_windSoundStorage", "_seasonPrecipitations", WindSeasons.Length, "wind season");
		SeasonWind seasonWind = new SeasonWind();
		for (int i = 0; i < WindSeasons.Length; i++)
		{
			DictionaryData dictionary2 = ReadDictionary(GetDictionaryValue(dictionary, WindSeasons[i].ToString(), "wind season " + WindSeasons[i]), null, null, WindSpeeds.Length, "wind speed for " + WindSeasons[i]);
			WindBySpeed windBySpeed = new WindBySpeed();
			for (int j = 0; j < WindSpeeds.Length; j++)
			{
				EnvironmentSoundContainer value = ReadEnvironmentContainer(GetDictionaryValue(dictionary2, WindSpeeds[j].ToString(), "wind speed " + WindSeasons[i].ToString() + "/" + WindSpeeds[j]), resolve, "wind " + WindSeasons[i].ToString() + "/" + WindSpeeds[j]);
				windBySpeed[WindSpeeds[j]] = value;
			}
			seasonWind[WindSeasons[i]] = windBySpeed;
		}
		windSoundStorage._seasonPrecipitations = seasonWind;
		return windSoundStorage;
	}

	private static DayTimeAmbientSeasonClips BuildDayNight(JObject fields, Func<ClipKey, AudioClip> resolve)
	{
		DictionaryData dictionary = ReadDictionary(fields, "_dayTimeAmbientSeasonClips", null, WindSeasons.Length, "day/night season");
		DayTimeAmbientSeasonClips dayTimeAmbientSeasonClips = new DayTimeAmbientSeasonClips();
		for (int i = 0; i < WindSeasons.Length; i++)
		{
			if (!(GetDictionaryValue(dictionary, WindSeasons[i].ToString(), "day/night season " + WindSeasons[i]) is JObject jObject) || !jObject.ContainsKey("DayAmbientClip") || !jObject.ContainsKey("NightAmbientClip"))
			{
				throw new InvalidOperationException("day/night row for " + WindSeasons[i].ToString() + " is incomplete");
			}
			dayTimeAmbientSeasonClips[WindSeasons[i]] = new DayTimeAmbientSoundContainer
			{
				DayAmbientClip = ResolveClip(jObject["DayAmbientClip"], resolve, "day " + WindSeasons[i]),
				NightAmbientClip = ResolveClip(jObject["NightAmbientClip"], resolve, "night " + WindSeasons[i])
			};
		}
		return dayTimeAmbientSeasonClips;
	}

	private static PrecipitationSoundStorage BuildPrecipitation(JObject fields, Func<ClipKey, AudioClip> resolve)
	{
		PrecipitationSoundStorage precipitationSoundStorage = new PrecipitationSoundStorage();
		DictionaryData dictionary = ReadDictionary(fields, "_storage", "_seasonPrecipitations", PrecipitationSeasons.Length, "precipitation season");
		SeasonPrecipitations seasonPrecipitations = new SeasonPrecipitations();
		for (int i = 0; i < PrecipitationSeasons.Length; i++)
		{
			DictionaryData dictionary2 = ReadDictionary(GetDictionaryValue(dictionary, PrecipitationSeasons[i].ToString(), "precipitation season " + PrecipitationSeasons[i]), null, null, PrecipitationIntensities.Length, "precipitation intensity for " + PrecipitationSeasons[i]);
			PrecipitationsByIntensity precipitationsByIntensity = new PrecipitationsByIntensity();
			for (int j = 0; j < PrecipitationIntensities.Length; j++)
			{
				JToken dictionaryValue = GetDictionaryValue(dictionary2, PrecipitationIntensities[j].ToString(), "precipitation intensity " + PrecipitationSeasons[i].ToString() + "/" + PrecipitationIntensities[j]);
				precipitationsByIntensity[PrecipitationIntensities[j]] = ReadEnvironmentContainer(dictionaryValue, resolve, "precipitation " + PrecipitationSeasons[i].ToString() + "/" + PrecipitationIntensities[j]);
			}
			seasonPrecipitations[PrecipitationSeasons[i]] = precipitationsByIntensity;
		}
		precipitationSoundStorage._seasonPrecipitations = seasonPrecipitations;
		return precipitationSoundStorage;
	}

	private static EnvironmentSoundContainer ReadEnvironmentContainer(JToken value, Func<ClipKey, AudioClip> resolve, string context)
	{
		if (!(value is JObject jObject) || !jObject.ContainsKey("_indoorClip") || !jObject.ContainsKey("_outdoorClip"))
		{
			throw new InvalidOperationException(context + " is missing an environment clip field");
		}
		return new EnvironmentSoundContainer
		{
			_indoorClip = ResolveClip(jObject["_indoorClip"], resolve, context + "/indoor"),
			_outdoorClip = ResolveClip(jObject["_outdoorClip"], resolve, context + "/outdoor")
		};
	}

	private static AudioClip ResolveClip(JToken value, Func<ClipKey, AudioClip> resolve, string context)
	{
		ClipKey? clipKey = ReadClipKey(value, context, allowNull: true);
		if (!clipKey.HasValue)
		{
			return null;
		}
		AudioClip audioClip = resolve(clipKey.Value);
		if (!audioClip)
		{
			throw new InvalidOperationException("required seasonal ambient clip is missing: " + clipKey.Value.ToString() + " (" + context + ")");
		}
		return audioClip;
	}

	private static bool ValidateAndCollect(SeasonAmbientDefinition definition, out string error)
	{
		error = null;
		try
		{
			DictionaryData dictionaryData = ReadDictionary(definition.RootFields, "_windSoundStorage", "_seasonPrecipitations", WindSeasons.Length, "wind season");
			ValidateKeys(dictionaryData.Keys, WindSeasons, "wind season");
			for (int i = 0; i < WindSeasons.Length; i++)
			{
				DictionaryData dictionaryData2 = ReadDictionary(GetDictionaryValue(dictionaryData, WindSeasons[i].ToString(), "wind season " + WindSeasons[i]), null, null, WindSpeeds.Length, "wind speed for " + WindSeasons[i]);
				ValidateKeys(dictionaryData2.Keys, WindSpeeds, "wind speed for " + WindSeasons[i]);
				for (int j = 0; j < WindSpeeds.Length; j++)
				{
					CollectEnvironmentRefs(GetDictionaryValue(dictionaryData2, WindSpeeds[j].ToString(), "wind speed " + WindSeasons[i].ToString() + "/" + WindSpeeds[j]), definition, "wind " + WindSeasons[i].ToString() + "/" + WindSpeeds[j]);
				}
			}
			DictionaryData dictionaryData3 = ReadDictionary(definition.RootFields, "_dayTimeAmbientSeasonClips", null, WindSeasons.Length, "day/night season");
			ValidateKeys(dictionaryData3.Keys, WindSeasons, "day/night season");
			for (int k = 0; k < WindSeasons.Length; k++)
			{
				if (!(GetDictionaryValue(dictionaryData3, WindSeasons[k].ToString(), "day/night season " + WindSeasons[k]) is JObject jObject) || !jObject.ContainsKey("DayAmbientClip") || !jObject.ContainsKey("NightAmbientClip"))
				{
					throw new InvalidOperationException("day/night row for " + WindSeasons[k].ToString() + " is incomplete");
				}
				CollectRef(jObject["DayAmbientClip"], definition, "day " + WindSeasons[k]);
				CollectRef(jObject["NightAmbientClip"], definition, "night " + WindSeasons[k]);
			}
			DictionaryData dictionaryData4 = ReadDictionary(definition.PrecipitationFields, "_storage", "_seasonPrecipitations", PrecipitationSeasons.Length, "precipitation season");
			ValidateKeys(dictionaryData4.Keys, PrecipitationSeasons, "precipitation season");
			for (int l = 0; l < PrecipitationSeasons.Length; l++)
			{
				DictionaryData dictionaryData5 = ReadDictionary(GetDictionaryValue(dictionaryData4, PrecipitationSeasons[l].ToString(), "precipitation season " + PrecipitationSeasons[l]), null, null, PrecipitationIntensities.Length, "precipitation intensity for " + PrecipitationSeasons[l]);
				ValidateKeys(dictionaryData5.Keys, PrecipitationIntensities, "precipitation intensity for " + PrecipitationSeasons[l]);
				for (int m = 0; m < PrecipitationIntensities.Length; m++)
				{
					CollectEnvironmentRefs(GetDictionaryValue(dictionaryData5, PrecipitationIntensities[m].ToString(), "precipitation intensity " + PrecipitationSeasons[l].ToString() + "/" + PrecipitationIntensities[m]), definition, "precipitation " + PrecipitationSeasons[l].ToString() + "/" + PrecipitationIntensities[m]);
				}
			}
			ClipKey? clipKey = ReadClipKey(definition.RootFields["_precipitationSoundStorageSo"], "root _precipitationSoundStorageSo", allowNull: false);
			if (!clipKey.HasValue || clipKey.Value.PathId != 104)
			{
				throw new InvalidOperationException("root _precipitationSoundStorageSo does not point to path ID 104");
			}
			return true;
		}
		catch (Exception ex)
		{
			error = "seasonal ambient dictionary validation failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static void CollectEnvironmentRefs(JToken value, SeasonAmbientDefinition definition, string context)
	{
		if (!(value is JObject jObject) || !jObject.ContainsKey("_indoorClip") || !jObject.ContainsKey("_outdoorClip"))
		{
			throw new InvalidOperationException(context + " is missing an environment clip field");
		}
		CollectRef(jObject["_indoorClip"], definition, context + "/indoor");
		CollectRef(jObject["_outdoorClip"], definition, context + "/outdoor");
	}

	private static void CollectRef(JToken value, SeasonAmbientDefinition definition, string context)
	{
		ClipKey? clipKey = ReadClipKey(value, context, allowNull: true);
		if (clipKey.HasValue)
		{
			if (!definition.Manifest.TryGetValue(clipKey.Value, out var value2))
			{
				throw new InvalidOperationException("required nonnull clip has no manifest entry: " + clipKey.Value.ToString() + " (" + context + ")");
			}
			if (!definition.RequiredClips.Contains(value2))
			{
				definition.RequiredClips.Add(value2);
			}
		}
	}

	private static ClipKey? ReadClipKey(JToken value, string context, bool allowNull)
	{
		if (value == null || value.Type == JTokenType.Null)
		{
			if (allowNull)
			{
				return null;
			}
			throw new InvalidOperationException(context + " is null");
		}
		if (!(value is JObject jObject))
		{
			throw new InvalidOperationException(context + " is not an asset reference");
		}
		string text = jObject.Value<string>("file");
		if (!jObject.ContainsKey("path_id"))
		{
			throw new InvalidOperationException(context + " has no path ID");
		}
		long num;
		try
		{
			num = jObject.Value<long>("path_id");
		}
		catch
		{
			throw new InvalidOperationException(context + " has an invalid path ID");
		}
		if (num == 0L)
		{
			return null;
		}
		if (num < 0 || string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException(context + " has an invalid asset reference");
		}
		return new ClipKey(text, num);
	}

	private static DictionaryData ReadDictionary(JToken owner, string field, string nestedField, int expectedCount, string context)
	{
		JToken jToken = owner;
		if (!string.IsNullOrEmpty(field))
		{
			jToken = (owner as JObject)?[field];
		}
		if (!string.IsNullOrEmpty(nestedField))
		{
			jToken = (jToken as JObject)?[nestedField];
		}
		JObject obj = jToken as JObject;
		JArray jArray = obj?["_keys"] as JArray;
		JArray jArray2 = obj?["_values"] as JArray;
		if (jArray == null || jArray2 == null || jArray.Count != jArray2.Count || jArray.Count != expectedCount)
		{
			throw new InvalidOperationException(context + " dictionary must contain " + expectedCount + " matching keys and values");
		}
		DictionaryData dictionaryData = new DictionaryData
		{
			Keys = new List<string>(jArray.Count),
			Values = new List<JToken>(jArray2.Count)
		};
		for (int i = 0; i < jArray.Count; i++)
		{
			string text = jArray[i]?.Value<string>();
			if (string.IsNullOrWhiteSpace(text) || dictionaryData.Keys.Contains(text))
			{
				throw new InvalidOperationException(context + " dictionary contains duplicate/empty keys");
			}
			dictionaryData.Keys.Add(text);
			dictionaryData.Values.Add(jArray2[i]);
		}
		return dictionaryData;
	}

	private static JToken GetDictionaryValue(DictionaryData dictionary, string key, string context)
	{
		for (int i = 0; i < dictionary.Keys.Count; i++)
		{
			if (string.Equals(dictionary.Keys[i], key, StringComparison.Ordinal))
			{
				return dictionary.Values[i];
			}
		}
		throw new InvalidOperationException(context + " dictionary omits key '" + key + "'");
	}

	private static void ValidateKeys<T>(List<string> keys, T[] expected, string context) where T : struct
	{
		if (keys.Count != expected.Length)
		{
			throw new InvalidOperationException(context + " dictionary has the wrong key count");
		}
		HashSet<T> hashSet = new HashSet<T>();
		for (int i = 0; i < keys.Count; i++)
		{
			if (!Enum.TryParse<T>(keys[i], ignoreCase: false, out var result) || !hashSet.Add(result))
			{
				throw new InvalidOperationException(context + " dictionary has unsupported key '" + keys[i] + "'");
			}
		}
		for (int j = 0; j < expected.Length; j++)
		{
			if (!hashSet.Contains(expected[j]))
			{
				T val = expected[j];
				throw new InvalidOperationException(context + " dictionary omits key '" + val.ToString() + "'");
			}
		}
	}

	private static bool TryReadManifest(string manifestPath, out Dictionary<ClipKey, ClipEntry> manifest, out string error)
	{
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		manifest = null;
		error = null;
		try
		{
			if (!File.Exists(manifestPath))
			{
				error = "seasonal ambient clip manifest is missing: " + manifestPath;
				return false;
			}
			JObject jObject = JObject.Parse(File.ReadAllText(manifestPath));
			if (jObject.Value<int?>("schema") != 1)
			{
				error = "seasonal ambient clip manifest schema is unsupported";
				return false;
			}
			if (!(jObject["clips"] is JArray { Count: not 0 } jArray))
			{
				error = "seasonal ambient clip manifest contains no clips";
				return false;
			}
			string pluginDataDirectory = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ".")?.FullName ?? ".";
			Dictionary<ClipKey, ClipEntry> dictionary = new Dictionary<ClipKey, ClipEntry>();
			for (int i = 0; i < jArray.Count; i++)
			{
				if (!(jArray[i] is JObject jObject2))
				{
					throw new InvalidOperationException("manifest row " + i + " is not an object");
				}
				string text = jObject2.Value<string>("file");
				string text2 = jObject2.Value<string>("path");
				string text3 = jObject2.Value<string>("name");
				string text4 = jObject2.Value<string>("sha256");
				long num;
				try
				{
					num = jObject2.Value<long>("path_id");
				}
				catch
				{
					throw new InvalidOperationException("manifest row " + i + " has an invalid path ID");
				}
				if (string.IsNullOrWhiteSpace(text) || num <= 0 || string.IsNullOrWhiteSpace(text2) || string.IsNullOrWhiteSpace(text3) || string.IsNullOrWhiteSpace(text4))
				{
					throw new InvalidOperationException("manifest row " + i + " is incomplete");
				}
				string text5 = ResolveManifestPath(pluginDataDirectory, text2);
				if (text5 == null)
				{
					throw new InvalidOperationException("manifest row " + i + " escapes plugin-data: " + text2);
				}
				if (!File.Exists(text5))
				{
					throw new InvalidOperationException("manifest WAV is missing for " + text + ":" + num + ": " + text5);
				}
				ClipKey key = new ClipKey(text, num);
				if (dictionary.ContainsKey(key))
				{
					throw new InvalidOperationException("manifest contains duplicate clip reference: " + key);
				}
				dictionary.Add(key, new ClipEntry(key, text3, text2, text4, text5));
			}
			if (!ValueEnumerableExtensions.Any<FromEnumerable<ClipEntry>, ClipEntry>(ValueEnumerable.AsValueEnumerable<ClipEntry>((IEnumerable<ClipEntry>)dictionary.Values), (Func<ClipEntry, bool>)((ClipEntry entry) => entry != null && entry.Name.Length > 0)))
			{
				throw new InvalidOperationException("seasonal ambient clip manifest has no named clips");
			}
			manifest = dictionary;
			return true;
		}
		catch (Exception ex)
		{
			error = "seasonal ambient clip manifest parse failed: " + ex.GetBaseException().Message;
			return false;
		}
	}

	private static string ResolveManifestPath(string pluginDataDirectory, string relative)
	{
		string fullPath = Path.GetFullPath(Path.Combine(pluginDataDirectory, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
		string value = Path.GetFullPath(pluginDataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return fullPath;
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

	private static void SubscribeAudioReset()
	{
		if (!_audioEventSubscribed)
		{
			AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
			_audioEventSubscribed = true;
		}
	}

	private static void UnsubscribeAudioReset()
	{
		if (_audioEventSubscribed)
		{
			AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
			_audioEventSubscribed = false;
		}
	}

	private static void OnAudioConfigurationChanged(bool deviceWasChanged)
	{
		if ((bool)_active)
		{
			try
			{
				Invalidating?.Invoke();
			}
			catch (Exception ex)
			{
				Plugin.Log?.LogWarning("[AmbientSeason] invalidation callback failed: " + ex.GetBaseException().Message);
			}
		}
		_generation++;
		DisposeRequest();
		DestroyOwnedAssets();
		_active = null;
		_phase = Phase.Idle;
		_lastError = null;
		Plugin.Log?.LogInfo("[AmbientSeason] audio configuration changed; seasonal WAVs will reload before native ambient readiness");
	}

	private static void DisposeRequest()
	{
		if (_loadingRequest != null)
		{
			try
			{
				_loadingRequest.Dispose();
			}
			catch
			{
			}
			_loadingRequest = null;
		}
	}

	private static void Fail(string error)
	{
		DisposeRequest();
		DestroyOwnedAssets();
		_active = null;
		_phase = Phase.Failed;
		_lastError = error ?? "unknown seasonal ambient asset failure";
		Plugin.Log?.LogWarning("[AmbientSeason] " + _lastError);
	}

	private static void DestroyOwnedAssets()
	{
		for (int num = OwnedAssets.Count - 1; num >= 0; num--)
		{
			UnityEngine.Object obj = OwnedAssets[num];
			if ((bool)obj)
			{
				try
				{
					UnityEngine.Object.DestroyImmediate(obj);
				}
				catch
				{
				}
			}
		}
		OwnedAssets.Clear();
	}
}
