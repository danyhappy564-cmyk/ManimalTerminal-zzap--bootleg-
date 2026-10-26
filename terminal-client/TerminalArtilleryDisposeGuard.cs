using System;
using CommonAssets.Scripts.ArtilleryShelling.Client.Audio;
using HarmonyLib;

namespace Manimal.Terminal;

// zzap: raid end stuck on the loading screen (10/26 third raid). With Terminal's restored
// artillery still active when the raid ended, the game's
// ArtilleryShellingSoundControllerClient.Dispose enumerates its sound dictionary while
// the disposals it triggers remove entries from it -> InvalidOperationException
// ("Collection was modified"). That exception aborted GameWorld.Dispose, so the game
// never reached the results/epilogue screen. On Terminal, swallow exactly that
// exception so the rest of the world teardown runs; the leftover shell sounds die
// with the scene anyway.
internal static class TerminalArtilleryDisposeGuard
{
	[HarmonyPatch(typeof(ArtilleryShellingSoundControllerClient), "Dispose")]
	internal static class Patch_SoundDisposeArmor
	{
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception)
		{
			if (__exception is InvalidOperationException && TerminalGate.On)
			{
				Plugin.Log.LogWarning("[Artillery] shelling sound teardown hit a modified collection — swallowed so the raid can end: " + __exception.Message);
				return null;
			}
			return __exception;
		}
	}
}
