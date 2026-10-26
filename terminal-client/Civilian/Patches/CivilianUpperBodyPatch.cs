using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianUpperBodyPatch : ModulePatch
{
	internal struct SavedMask
	{
		internal bool Changed;

		internal Player.EAnimatorMask Value;
	}

	private static bool _logged;

	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(Player), "VisualPass");
	}

	[PatchPrefix]
	internal static void Before(Player __instance, out SavedMask __state)
	{
		__state = default;
		if (!TerminalGate.On || __instance == null || __instance.FirstPersonPointOfView)
		{
			return;
		}
		IHealthController healthController = __instance.HealthController;
		if (healthController == null || !healthController.IsAlive)
		{
			return;
		}
		WildSpawnType? wildSpawnType = __instance.Profile?.Info?.Settings?.Role;
		if (wildSpawnType.HasValue && CivilianConstants.IsCivilian(wildSpawnType.Value) && CivilianAnimatorPatch.UsesAuthoredController(__instance) && (__instance.HandsController is Player.KnifeController || __instance.HandsController is Player.EmptyHandsController))
		{
			__state = new SavedMask
			{
				Changed = true,
				Value = __instance.EnabledAnimators
			};
			__instance.EnabledAnimators &= ~(Player.EAnimatorMask.Procedural | Player.EAnimatorMask.FBBIK | Player.EAnimatorMask.IK);
			if (!_logged)
			{
				_logged = true;
				Plugin.Log.LogInfo("[Civ] authored upper body active: native weapon/hand/full-body IK suppressed; body and leg animation retained");
			}
		}
	}

	[PatchPostfix]
	internal static void After(Player __instance, SavedMask __state)
	{
		if (__state.Changed)
		{
			__instance.EnabledAnimators = __state.Value;
		}
	}

	[PatchFinalizer]
	internal static void OnFinally(Player __instance, SavedMask __state, Exception __exception)
	{
		if (__exception != null && __state.Changed)
		{
			__instance.EnabledAnimators = __state.Value;
		}
	}
}
