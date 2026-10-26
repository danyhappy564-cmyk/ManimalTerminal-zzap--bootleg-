using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using Manimal.Terminal.Civilian.Behavior;
using SPT.Reflection.Patching;

namespace Manimal.Terminal.Civilian.Patches;

internal sealed class CivilianBrainInitPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(TarkovApplication).GetMethod("Init", BindingFlags.Instance | BindingFlags.Public);
	}

	[PatchPostfix]
	private static void PatchPostfix()
	{
		List<string> brainNames = new List<string> { "Assault", "AssaultGroup", "CursedAssault", "ExUsec", "PMC", "PmcBEAR", "PmcUSEC", "PmcBear", "PmcUsec" };
		List<WildSpawnType> roles = new List<WildSpawnType> { (WildSpawnType)776701 };
		BrainManager.AddCustomLayer(typeof(CivilianPassiveLayer), brainNames, 90, roles);
		BrainManager.AddCustomLayer(typeof(CivilianFleeLayer), brainNames, 95, roles);
		BepInEx.Logging.Logger.CreateLogSource("Terminal.Civ").LogInfo("Civilian BigBrain layers registered (passive 90, flee 95)");
	}
}
