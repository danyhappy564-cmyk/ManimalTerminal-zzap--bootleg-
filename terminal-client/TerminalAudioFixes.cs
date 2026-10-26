using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Audio.SpatialSystem;
using EFT;
using EFT.Interactive;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalAudioFixes
{
	[HarmonyPatch(typeof(SpatialAudioSystem), "Initialize")]
	internal static class Patch_SpatialAudioInitSkip
	{
		[HarmonyPrefix]
		private static bool Prefix(SpatialAudioSystem __instance, ref Task __result)
		{
			if (!TerminalGate.On && !TerminalLoaded.Check())
			{
				return true;
			}
			if (Plugin.SpatialAudio.Value && TerminalAcoustics.TryPrepareSpatialAudio(__instance))
			{
				return true;
			}
			Plugin.Log.LogInfo("[AudioFix] SpatialAudioSystem.Initialize skipped (staging not ready yet) — occlusion airbags active until we drive it in-raid");
			__result = Task.CompletedTask;
			return false;
		}
	}

	[HarmonyPatch(typeof(SpatialAudioSystem), "ProcessInteractiveObjectOcclusion")]
	internal static class Patch_InteractiveOcclusionUninit
	{
		[HarmonyPrefix]
		private static bool Prefix()
		{
			return SpatialAudioSystem.Initialized;
		}
	}

	[HarmonyPatch(typeof(WorldInteractiveObject), "PlaySound")]
	internal static class Patch_DoorFoleyVolume
	{
		[HarmonyPrefix]
		private static void Prefix(ref float volume)
		{
			if (TerminalGate.On)
			{
				volume *= Plugin.DoorFoleyVolume.Value;
			}
		}
	}

	[HarmonyPatch(typeof(BarbedWire), "PlaySound")]
	internal static class Patch_NullBarbedWireSound
	{
		[HarmonyPrefix]
		private static bool Prefix(SoundBank ____soundBank)
		{
			if (TerminalGate.On)
			{
				return ____soundBank;
			}
			return true;
		}
	}

	[HarmonyPatch]
	internal static class Patch_SourceOcclusionUninit
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

		[HarmonyPrefix]
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
}
