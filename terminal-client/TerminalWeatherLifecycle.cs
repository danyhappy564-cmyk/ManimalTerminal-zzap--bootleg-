using System;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Weather;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.Terminal;

internal static class TerminalWeatherLifecycle
{
	internal sealed class Patch_LocalGameWeatherStage : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(LocalGame), "vmethod_5", new Type[1] { typeof(Action) });
		}

		[PatchPrefix]
		private static void Prefix()
		{
			StageBeforeNativeWeatherRequest();
		}
	}

	internal sealed class Patch_WeatherNodes : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(WeatherController), "SetWeatherNodes", new Type[1] { typeof(WeatherNode[]) });
		}

		[PatchPostfix]
		private static void Postfix(WeatherController __instance, WeatherNode[] nodes)
		{
			MarkNativeWeather(__instance, nodes);
		}
	}

	internal sealed class Patch_IncompleteSeasonVisuals : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(SeasonsController), "CreateState", new Type[2]
			{
				typeof(ESeason),
				typeof(SeasonsSettings)
			});
		}

		[PatchPrefix]
		private static bool Prefix(SeasonsController __instance, ref Task __result)
		{
			WeatherController instance = WeatherController.Instance;
			if (!Enabled() || !IsReconstructed(instance))
			{
				return true;
			}
			RainController rainController = instance.RainController;
			if ((bool)rainController && (bool)rainController._snowFlakes && (bool)rainController._snowWetRenderer && (bool)rainController._snowRippleController)
			{
				return true;
			}
			__instance._state = new SeasonsController.StateUnknown(__instance);
			__result = Task.CompletedTask;
			Plugin.Log.LogInfo("[WeatherLifecycle] preserving Terminal's no-snow seasonal state; native rain/weather timeline remains active");
			return false;
		}
	}

	private static WeatherController _nativeWeatherController;

	private static WeatherController _reconstructedController;

	private static bool _nativeWeatherReady;

	internal static void ResetForRaid()
	{
		_nativeWeatherController = null;
		_reconstructedController = null;
		_nativeWeatherReady = false;
	}

	internal static bool HasNativeWeather(WeatherController controller)
	{
		if (controller != null && _nativeWeatherReady && (object)_nativeWeatherController == controller)
		{
			return (object)WeatherController.Instance == controller;
		}
		return false;
	}

	internal static bool IsReconstructed(WeatherController controller)
	{
		if (controller != null)
		{
			return (object)_reconstructedController == controller;
		}
		return false;
	}

	private static bool Enabled()
	{
		if (TerminalGate.On && Plugin.WeatherStack != null)
		{
			return Plugin.WeatherStack.Value;
		}
		return false;
	}

	private static void StageBeforeNativeWeatherRequest()
	{
		if (!Enabled() || HasNativeWeather(WeatherController.Instance))
		{
			return;
		}
		try
		{
			GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
			if (gameWorld != null && gameWorld.GameDateTime != null && TODSkyProvider.IsAvailable && TODSkyProvider.Instance.CurrentTime != null)
			{
				TODSkyProvider.Instance.CurrentTime.GameDateTime = gameWorld.GameDateTime;
			}
			if (!TerminalWeather.TryStage() || !WeatherController.Instance)
			{
				Plugin.Log.LogWarning("[WeatherLifecycle] weather stack unavailable before the native weather request; late staging cannot supply the skipped timeline");
				return;
			}
			_reconstructedController = WeatherController.Instance;
			_reconstructedController.OnCameraChanged();
			TerminalWeather.TryEnsureRainVisualReady();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[WeatherLifecycle] pre-request stage failed: " + ex.Message);
		}
	}

	private static void MarkNativeWeather(WeatherController controller, WeatherNode[] nodes)
	{
		if (!(controller == null) && nodes != null && nodes.Length != 0 && (object)WeatherController.Instance == controller)
		{
			_nativeWeatherController = controller;
			_nativeWeatherReady = true;
			if (TerminalGate.On)
			{
				Plugin.Log.LogInfo($"[WeatherLifecycle] controller={controller.GetInstanceID()} received {nodes.Length} native weather nodes; timeline={nodes[0].Time}..{nodes[^1].Time}");
			}
		}
	}
}
