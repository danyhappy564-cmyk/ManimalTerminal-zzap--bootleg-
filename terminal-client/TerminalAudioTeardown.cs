using System;
using Audio.AmbientSubsystem;
using HarmonyLib;

namespace Manimal.Terminal;

internal static class TerminalAudioTeardown
{
	[HarmonyPatch(typeof(AmbientAudioSystem), "Dispose")]
	internal static class Patch_AmbientDisposeArmor
	{
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception != null && TerminalGate.On)
			{
				Plugin.Log.LogWarning("[AudioTeardown] AmbientAudioSystem.Dispose threw (swallowed, session end continues): " + __exception.Message);
				return null;
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(EnvironmentSoundBlendSystem), "Dispose")]
	internal static class Patch_BlendDisposeArmor
	{
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception != null && TerminalGate.On)
			{
				Plugin.Log.LogWarning("[AudioTeardown] EnvironmentSoundBlendSystem.Dispose threw (swallowed): " + __exception.Message);
				return null;
			}
			return __exception;
		}
	}
}
