using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianMotionMatchingPatch : ModulePatch
{
	internal const string PluginGuid = "com.manimal.motionmagic";

	internal const int NativeFullBody = 1;

	internal const int NativeUpperBody = 2;

	protected override MethodBase GetTargetMethod()
	{
		Type type = AccessTools.TypeByName("Manimal.MotionMatching.MotionOwnership");
		MethodInfo methodInfo = ((type == null) ? null : AccessTools.Method(type, "Resolve", new Type[1] { typeof(Player) }));
		if (methodInfo == null || !methodInfo.IsStatic || methodInfo.ReturnType != typeof(int))
		{
			throw new MissingMethodException("MotionMagic 0.1.3 or newer with MotionOwnership.Resolve(Player) is required for civilian animation compatibility");
		}
		return methodInfo;
	}

	[PatchPostfix]
	private static void PatchPostfix(Player player, ref int __result)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		WildSpawnType? wildSpawnType = player?.Profile?.Info?.Settings?.Role;
		if (wildSpawnType.HasValue && CivilianConstants.IsCivilian(wildSpawnType.Value))
		{
			__result |= 2;
			if (!CivilianFleeState.TryGet(player.AIData?.BotOwner, out var state) || !state.MotionRequested || state.InCover || player.IsInPronePose || player.PoseLevel < 0.85f || !IsLocomotion(player.MovementContext?.CurrentState?.Name) || (player.CurrentManagedState != null && !IsLocomotion(player.CurrentManagedState.Name)))
			{
				__result |= 1;
			}
		}
	}

	private static bool IsLocomotion(EPlayerState? state)
	{
		if (state != EPlayerState.Idle && state != EPlayerState.Run && state != EPlayerState.Sprint)
		{
			return state == EPlayerState.Transition;
		}
		return true;
	}
}
