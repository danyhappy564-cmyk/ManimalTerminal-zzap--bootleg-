using System;
using EFT.CameraControl;
using UnityEngine;

namespace Manimal.Terminal;

[DisallowMultipleComponent]
internal sealed class RainRehookHost : MonoBehaviour
{
	private RainController _rain;

	private float _deadline;

	private float _nextProbe;

	private bool _invoking;

	private bool _observed;

	private bool _finished;

	private string _reason;

	private Exception _failure;

	internal static void Observe(RainController rain, Exception error)
	{
		if (!rain)
		{
			return;
		}
		RainRehookHost component = rain.GetComponent<RainRehookHost>();
		if ((bool)component && component._invoking)
		{
			component._observed = true;
			component._failure = error;
		}
		else if (error == null)
		{
			if ((bool)component)
			{
				component.Finish(null);
			}
		}
		else if (!component)
		{
			component = rain.gameObject.AddComponent<RainRehookHost>();
			component._rain = rain;
			component._deadline = Time.realtimeSinceStartup + 30f;
			component._reason = error.Message;
			Plugin.Log.LogWarning("[RainCamera] hookup failed; waiting for complete screen-effect dependencies: " + error.Message);
		}
	}

	private void Update()
	{
		if (_finished)
		{
			return;
		}
		if (!TerminalGate.On || !_rain)
		{
			Finish(null);
			return;
		}
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (realtimeSinceStartup >= _deadline)
		{
			Finish(_reason);
		}
		else
		{
			if (realtimeSinceStartup < _nextProbe)
			{
				return;
			}
			_nextProbe = realtimeSinceStartup + 1f;
			CameraManager instance = CameraManager.Instance;
			if (!DependenciesReady(instance?.Camera, instance?.EffectsController, out _reason))
			{
				return;
			}
			_invoking = true;
			_observed = false;
			_failure = null;
			try
			{
				_rain.CameraChanged();
			}
			catch (Exception failure)
			{
				_failure = failure;
			}
			finally
			{
				_invoking = false;
			}
			if (!_observed || _failure != null)
			{
				Finish(_failure?.Message ?? "native completion was not observed");
				return;
			}
			Plugin.Log.LogInfo("[RainCamera] native camera hookup completed with screen-rain dependencies present");
			Finish(null);
		}
	}

	internal static bool DependenciesReady(Camera camera, EffectsController effects, out string reason)
	{
		if (!camera || !effects)
		{
			reason = "camera or EffectsController missing";
			return false;
		}
		RainScreenDrops rainScreenDrops = effects.RainScreenDrops;
		ScreenWater screenWater = effects.ScreenWater;
		if (!rainScreenDrops || !screenWater)
		{
			reason = "RainScreenDrops or ScreenWater missing";
			return false;
		}
		if (rainScreenDrops.GetComponent<Camera>() != camera || screenWater.GetComponent<Camera>() != camera)
		{
			reason = "screen effects belong to a different camera";
			return false;
		}
		if (!rainScreenDrops._blitShader || !rainScreenDrops._blitShader.isSupported || !rainScreenDrops._dropMaterial || !rainScreenDrops._dropMaterial.shader || !rainScreenDrops._dropMaterial.shader.isSupported)
		{
			reason = "rain shader or drop material unavailable";
			return false;
		}
		if (!rainScreenDrops.DuDvMap || rainScreenDrops.DuDvMap.width <= 0 || rainScreenDrops.DuDvMap.height <= 0 || rainScreenDrops._dropScaleCurve == null || rainScreenDrops._dropScaleCurve.length == 0)
		{
			reason = "rain render texture or scale curve unavailable";
			return false;
		}
		if (!screenWater.shaderRGB || !screenWater.shaderRGB.isSupported || !screenWater.WaterFlows || !screenWater.WaterMask || !screenWater.WetScreen)
		{
			reason = "screen-water shader or textures unavailable";
			return false;
		}
		reason = null;
		return true;
	}

	private void Finish(string failure)
	{
		if (!_finished)
		{
			_finished = true;
			if (failure != null)
			{
				Plugin.Log.LogWarning("[RainCamera] screen-effect recovery unavailable: " + failure + ". Outdoor rain readiness is tracked separately.");
			}
			UnityEngine.Object.Destroy(this);
		}
	}
}
