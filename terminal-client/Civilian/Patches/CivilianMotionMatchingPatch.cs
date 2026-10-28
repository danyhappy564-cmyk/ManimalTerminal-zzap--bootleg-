using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianMotionMatchingPatch : ModulePatch
{
	internal const string PluginGuid = "com.manimal.motionmagic";

	internal const int NativeFullBody = 1;

	internal const int NativeUpperBody = 2;

	// zzap 10/28: civilians still "tremble" with zero position steps ([CivJumpNear]
	// events 0). The full-body owner below is re-decided every Resolve from soft
	// inputs (MotionRequested, PoseLevel, Transition states) that can blink for a
	// frame, handing the body between MotionMagic and the native animator. Debounce:
	// a new owner must hold for HoldSeconds. Prone/dead stay immediate.
	private const float HoldSeconds = 0.25f;

	private struct Hold
	{
		internal bool Full, Raw, Known;

		internal float PendingSince;
	}

	private static readonly Dictionary<int, Hold> Holds = new Dictionary<int, Hold>();

	internal static int RawFlips, AppliedFlips;

	internal static void ClearAll()
	{
		Holds.Clear();
		RawFlips = AppliedFlips = 0;
	}

	protected override MethodBase GetTargetMethod()
	{
		Type type = TerminalTypes.ByName("Manimal.MotionMatching.MotionOwnership");
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
			bool raw = !CivilianFleeState.TryGet(player.AIData?.BotOwner, out var state) || !state.MotionRequested || state.InCover || player.IsInPronePose || player.PoseLevel < 0.85f || !IsLocomotion(player.MovementContext?.CurrentState?.Name) || (player.CurrentManagedState != null && !IsLocomotion(player.CurrentManagedState.Name));
			if (Debounced(player, raw, hard: player.IsInPronePose))
			{
				__result |= 1;
			}
		}
	}

	private static bool Debounced(Player player, bool raw, bool hard)
	{
		int id = player.GetInstanceID();
		float now = Time.time;
		if (!Holds.TryGetValue(id, out Hold hold) || !hold.Known)
		{
			if (Holds.Count < 512) Holds[id] = new Hold { Full = raw, Raw = raw, Known = true, PendingSince = -1f };
			return raw;
		}
		if (raw != hold.Raw) RawFlips++;
		hold.Raw = raw;
		if (raw == hold.Full) hold.PendingSince = -1f;
		else if (hard) { hold.Full = raw; hold.PendingSince = -1f; AppliedFlips++; }
		else if (hold.PendingSince < 0f) hold.PendingSince = now;
		else if (now - hold.PendingSince >= HoldSeconds) { hold.Full = raw; hold.PendingSince = -1f; AppliedFlips++; }
		Holds[id] = hold;
		return hold.Full;
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
