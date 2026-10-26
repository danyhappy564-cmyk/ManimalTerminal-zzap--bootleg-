using System.Collections.Generic;
using System.Reflection;
using Audio.SpatialSystem;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch]
internal static class Patch_OcclusionWhenUninitialized
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodInfo[] methods = typeof(SpatialAudioSystem).GetMethods(BindingFlags.Instance | BindingFlags.Public);
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.Name == "ProcessSourceOcclusion")
			{
				yield return methodInfo;
			}
		}
	}

	private static bool Prefix(ref int __result)
	{
		if (SpatialAudioSystem.Initialized)
		{
			return true;
		}
		__result = -1;
		return false;
	}
}
