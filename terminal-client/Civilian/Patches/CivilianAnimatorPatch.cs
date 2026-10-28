using System;
using System.IO;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianAnimatorPatch : ModulePatch
{
	private const string BundleFile = "terminal_civilian_anims.bundle";

	private const string ControllerName = "civilian_anim_controller";

	private static RuntimeAnimatorController _controller;

	private static bool _tried;

	private static bool _unsupportedAnimatorLogged;

	private static bool _appliedLogged;

	internal static bool UsesAuthoredController(Player player)
	{
		if ((bool)_controller && player?._animators != null && player._animators.Length != 0 && player._animators[0] != null)
		{
			return player._animators[0].runtimeAnimatorController == _controller;
		}
		return false;
	}

	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(Player), "CreatePlayerAnimator", Type.EmptyTypes);
	}

	[PatchPrefix]
	private static void PatchPrefix(Player __instance)
	{
		try
		{
			WildSpawnType? wildSpawnType = __instance.Profile?.Info?.Settings?.Role;
			if (!wildSpawnType.HasValue || !CivilianConstants.IsCivilian(wildSpawnType.Value) || !Plugin.CivilianRetailAnimator.Value)
			{
				return;
			}
			if (AppEnvironment.Config.UseBodyFastAnimator || AppEnvironment.Config.UseSpiritPlayer || __instance.UsedSimplifiedSkeleton)
			{
				if (!_unsupportedAnimatorLogged)
				{
					_unsupportedAnimatorLogged = true;
					Plugin.Log.LogWarning("[Civ] civilian animator override requires the standard body animator " + $"(fast={AppEnvironment.Config.UseBodyFastAnimator}, spirit={AppEnvironment.Config.UseSpiritPlayer}, " + $"simplified={__instance.UsedSimplifiedSkeleton})");
				}
				return;
			}
			RuntimeAnimatorController runtimeAnimatorController = Controller();
			if (!runtimeAnimatorController)
			{
				return;
			}
			if (__instance._animators == null || __instance._animators.Length == 0 || __instance._animators[0] == null)
			{
				Plugin.Log.LogWarning("[Civ] civilian body animator is missing during CreatePlayerAnimator");
				return;
			}
			__instance._animators[0].runtimeAnimatorController = runtimeAnimatorController;
			if (!_appliedLogged)
			{
				_appliedLogged = true;
				Plugin.Log.LogInfo("[Civ] retail civilian animation controller assigned to body animator");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Civ] animator override failed: " + ex.Message);
		}
	}

	private static RuntimeAnimatorController Controller()
	{
		if (_tried)
		{
			return _controller;
		}
		_tried = true;
		string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "terminal_civilian_anims.bundle");
		if (!File.Exists(path))
		{
			Plugin.Log.LogWarning("[Civ] terminal_civilian_anims.bundle missing — civilians keep the armed scav animations");
			return null;
		}
		AssetBundle assetBundle = AssetBundle.LoadFromFile(path);
		if (!assetBundle)
		{
			Plugin.Log.LogWarning("[Civ] terminal_civilian_anims.bundle failed to open");
			return null;
		}
		_controller = assetBundle.LoadAsset<RuntimeAnimatorController>("civilian_anim_controller");
		if (!_controller)
		{
			Plugin.Log.LogWarning("[Civ] 'civilian_anim_controller' not found in terminal_civilian_anims.bundle");
		}
		else if (_controller is AnimatorOverrideController animatorOverrideController && (!animatorOverrideController.runtimeAnimatorController || animatorOverrideController.animationClips.Length == 0))
		{
			Plugin.Log.LogWarning("[Civ] civilian animator loaded but its player-controller dependencies are unresolved");
			_controller = null;
		}
		else
		{
			Plugin.Log.LogInfo("[Civ] retail civilian animator loaded at 'civilian_anim_controller' " + $"({_controller.GetType().Name}, {_controller.animationClips.Length} clips)");
		}
		return _controller;
	}
}
