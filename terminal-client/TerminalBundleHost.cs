using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Comfort.Common;
using EFT.AssetsManager;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalBundleHost
{
	private sealed class FailedBundleOperation : Operation<AssetBundle>
	{
		internal FailedBundleOperation(string error)
		{
			Error = error;
		}
	}

	internal sealed class LoadBundlePatch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(BundlesManager), "LoadBundleAsync");
		}

		[PatchPrefix]
		private static bool Prefix(BundlesManager __instance, string bundleName, ref IOperation<AssetBundle> __result)
		{
			return BeforeLoadBundle(__instance, bundleName, ref __result);
		}
	}

	private const string PayloadDir = "streamingassets";

	private const string Platform = "Windows";

	private static Dictionary<string, string> _ours;

	private static readonly Dictionary<string, AssetBundle> _loaded = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

	private static Type _entryType;

	private static FieldInfo _loadedDict;

	internal static void Init()
	{
		try
		{
			_ours = ScanPayload();
			if (_ours.Count == 0)
			{
				Plugin.Log.LogDebug("[Bundles] no payload under streamingassets/Windows — map bundles unavailable");
				return;
			}
			_entryType = AccessTools.Inner(typeof(BundlesManager), "AssetBundleReference");
			_loadedDict = AccessTools.Field(typeof(BundlesManager), "_bundles");
			if (_entryType == null || _loadedDict == null)
			{
				Plugin.Log.LogError("[Bundles] EFT.AssetsManager.BundlesManager layout changed — cannot host bundles from the plugin folder");
				return;
			}
			new LoadBundlePatch().Enable();
			foreach (string value in _ours.Values)
			{
				TerminalNativeAssets.Prepare(value, Application.dataPath);
			}
			Plugin.Log.LogInfo($"[Bundles] hosting {_ours.Count} bundle(s) from the plugin folder: " + string.Join(", ", new List<string>(_ours.Keys).ToArray()));
		}
		catch (Exception arg)
		{
			Plugin.Log.LogError($"[Bundles] host init failed: {arg}");
		}
	}

	private static Dictionary<string, string> ScanPayload()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string text = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "streamingassets", "Windows");
		if (!Directory.Exists(text))
		{
			return dictionary;
		}
		string[] files = Directory.GetFiles(text, "*", SearchOption.AllDirectories);
		foreach (string text2 in files)
		{
			if (text2.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) && !TerminalNativeAssets.IsNativeBundle(text2))
			{
				string key = text2.Substring(text.Length + 1).Replace('\\', '/').ToLowerInvariant();
				dictionary[key] = text2;
			}
		}
		files = Directory.GetFiles(text, "*", SearchOption.AllDirectories);
		foreach (string text3 in files)
		{
			if (TerminalNativeAssets.IsNativeBundle(text3))
			{
				string text4 = text3.Substring(text.Length + 1).Replace('\\', '/').ToLowerInvariant();
				text4 = text4.Substring(0, text4.Length - ".native.bundle".Length) + ".bundle";
				dictionary[text4] = text3;
			}
		}
		return dictionary;
	}

	private static object CreateEntry(AssetBundle bundle)
	{
		try
		{
			ConstructorInfo constructorInfo = AccessTools.Constructor(_entryType, new Type[1] { typeof(AssetBundle) });
			if (constructorInfo != null)
			{
				return constructorInfo.Invoke(new object[1] { bundle });
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Bundles] entry ctor failed (" + ex.Message + ") — falling back to field init");
		}
		try
		{
			object uninitializedObject = FormatterServices.GetUninitializedObject(_entryType);
			FieldInfo[] fields = _entryType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (fieldInfo.FieldType == typeof(AssetBundle))
				{
					fieldInfo.SetValue(uninitializedObject, bundle);
				}
				else if (fieldInfo.FieldType == typeof(int))
				{
					fieldInfo.SetValue(uninitializedObject, 1);
				}
			}
			return uninitializedObject;
		}
		catch (Exception ex2)
		{
			Plugin.Log.LogError("[Bundles] could not build a cache entry: " + ex2.Message);
			return null;
		}
	}

	private static bool BeforeLoadBundle(BundlesManager __instance, string bundleName, ref IOperation<AssetBundle> result)
	{
		try
		{
			if (string.IsNullOrEmpty(bundleName))
			{
				return true;
			}
			string text = bundleName.Replace('\\', '/').ToLowerInvariant();
			if (!_ours.TryGetValue(text, out var value))
			{
				return true;
			}
			TerminalNativeAssets.RequireVerified(value, Application.dataPath);
			if (!(_loadedDict.GetValue(__instance) is IDictionary dictionary))
			{
				throw new InvalidOperationException("Terminal bundle cache is unavailable");
			}
			if (dictionary.Contains(bundleName))
			{
				return true;
			}
			if (!_loaded.TryGetValue(text, out var value2) || value2 == null)
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				value2 = AssetBundle.LoadFromFile(value);
				if (value2 == null)
				{
					throw new InvalidDataException("[Bundles] '" + text + "' failed to load from " + value);
				}
				_loaded[text] = value2;
				Plugin.Log.LogDebug($"[Bundles] loaded '{text}' from the plugin folder in {stopwatch.ElapsedMilliseconds}ms");
			}
			object obj = CreateEntry(value2);
			if (obj == null)
			{
				throw new InvalidOperationException("Terminal bundle cache entry could not be created");
			}
			dictionary[bundleName] = obj;
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("[Bundles] serve failed for '" + bundleName + "': " + ex.Message);
			result = new FailedBundleOperation(ex.Message);
			return false;
		}
	}
}
