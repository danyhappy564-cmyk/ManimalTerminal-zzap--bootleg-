using EFT.CameraControl;
using HarmonyLib;

namespace Manimal.Terminal;

[HarmonyPatch(typeof(CameraManager), "SetCameraFromSettings")]
internal static class Patch_RejectShellCameraPrefab
{
	private static void Prefix(ref CameraManager.ISettings settings)
	{
		if (TerminalGate.On)
		{
			string text = ((settings != null && settings.CameraPrefab != null) ? settings.CameraPrefab.name : "<null>");
			Plugin.Log.LogDebug("[RaidFix] SetCameraFromSettings on terminal: prefab=" + text);
			if (settings != null && !(settings.CameraPrefab == null))
			{
				Plugin.Log.LogDebug("[RaidFix] discarding scene camera prefab — Cam2 chassis + donor graft owns the camera");
				settings = null;
			}
		}
	}
}
