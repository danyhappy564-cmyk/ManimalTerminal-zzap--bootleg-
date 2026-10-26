using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[DefaultExecutionOrder(10000)]
internal sealed class TerminalWaterReflectionDriver : MonoBehaviour
{
	private static readonly int ProbeId = Shader.PropertyToID("_MyGlobalReflectionProbe");

	private static readonly FieldInfo CubeField = AccessTools.Field(typeof(AmbientLight), "_cubeRT");

	private static readonly FieldInfo FaceField = AccessTools.Field(typeof(AmbientLight), "_faceNum");

	private static readonly FieldInfo NextCaptureField = AccessTools.Field(typeof(AmbientLight), "_nextRenderTime");

	private AmbientLight _source;

	private RenderTexture _cube;

	private Texture _previousProbe;

	private bool _ownsCube;

	private float _diagnosticAt;

	private bool _diagnosticDone;

	private int _captureFailures;

	private float _nextCaptureAttemptAt;

	private AmbientLight _failedSource;

	private bool _captureRetryExhausted;

	private void LateUpdate()
	{
		if (!TerminalGate.On)
		{
			ReleaseCapture();
			enabled = false;
			return;
		}
		if (!TerminalWeather.Staged)
		{
			if ((bool)_source || (bool)_cube)
			{
				ReleaseCapture();
			}
			return;
		}
		AmbientLight ambientLight = WeatherController.Instance?.TimeOfDayController?.AmbientLightScript;
		if (!ambientLight || !ambientLight.IsInitialized)
		{
			if ((bool)_source || (bool)_cube)
			{
				ReleaseCapture();
			}
			return;
		}
		if (ambientLight.isActiveAndEnabled)
		{
			HandoffToNative(ambientLight);
			return;
		}
		if (_failedSource != ambientLight)
		{
			_failedSource = ambientLight;
			_captureFailures = 0;
			_nextCaptureAttemptAt = 0f;
			_captureRetryExhausted = false;
		}
		if (_captureRetryExhausted || !TerminalWaterLifecycle.IsRetryDue(Time.realtimeSinceStartup, _nextCaptureAttemptAt))
		{
			return;
		}
		try
		{
			if (_source != ambientLight)
			{
				ReleaseCapture();
				if (CubeField == null || FaceField == null || NextCaptureField == null)
				{
					throw new MissingFieldException("AmbientLight reflection capture layout changed");
				}
				_source = ambientLight;
				_previousProbe = Shader.GetGlobalTexture(ProbeId);
				_cube = CubeField.GetValue(ambientLight) as RenderTexture;
				_ownsCube = !_cube;
				if (_ownsCube)
				{
					FaceField.SetValue(ambientLight, -1);
					NextCaptureField.SetValue(ambientLight, 0f);
				}
				ambientLight.method_2();
				_cube = CubeField.GetValue(ambientLight) as RenderTexture;
				if (!_cube || !_cube.IsCreated())
				{
					throw new InvalidOperationException("native reflection cubemap was not created");
				}
				Plugin.Log.LogInfo("[Water] reflection capture ready: source='" + ambientLight.name + "' " + $"cube={(_cube ? _cube.width : 0)}px created={(bool)_cube && _cube.IsCreated()} " + $"faceInterval={ambientLight.RenderDelay:F2}s screenAmbientEnabled={ambientLight.enabled} " + "previousProbe='" + (_previousProbe ? _previousProbe.name : "missing") + "'");
				_nextCaptureAttemptAt = 0f;
				_diagnosticAt = Time.realtimeSinceStartup + 10f;
				_diagnosticDone = false;
				return;
			}
			ambientLight.method_2();
			Camera cameraRef = TerminalCullingDriver.CameraRef;
			if (_diagnosticDone || !(Time.realtimeSinceStartup >= _diagnosticAt) || !cameraRef || !cameraRef.isActiveAndEnabled || !cameraRef.CompareTag("MainCamera") || !Singleton<GameWorld>.Instantiated || !(Singleton<GameWorld>.Instance.MainPlayer != null))
			{
				return;
			}
			_diagnosticDone = true;
			try
			{
				TerminalWaterDiagnostics.Capture(cameraRef, ambientLight, _cube);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogInfo("[WaterDiag] diagnostics unavailable: " + ex.Message);
			}
		}
		catch (Exception error)
		{
			HandleCaptureFailure(ambientLight, error);
		}
	}

	private void HandoffToNative(AmbientLight source)
	{
		if (_source != source)
		{
			if ((bool)_source || (bool)_cube)
			{
				ReleaseCapture();
			}
			_failedSource = null;
			_captureFailures = 0;
			_nextCaptureAttemptAt = 0f;
			_captureRetryExhausted = false;
			return;
		}
		if (_ownsCube)
		{
			RenderTexture renderTexture = CubeField?.GetValue(source) as RenderTexture;
			if (renderTexture == _cube && (bool)renderTexture)
			{
				DetachWithoutRelease();
			}
			else
			{
				ReleaseCapture();
			}
		}
		else
		{
			DetachWithoutRelease();
		}
		_failedSource = null;
		_captureFailures = 0;
		_nextCaptureAttemptAt = 0f;
		_captureRetryExhausted = false;
	}

	private void DetachWithoutRelease()
	{
		_source = null;
		_cube = null;
		_previousProbe = null;
		_ownsCube = false;
		_diagnosticDone = true;
	}

	private void HandleCaptureFailure(AmbientLight source, Exception error)
	{
		ReleaseCapture();
		_failedSource = source;
		_captureFailures++;
		if (!TerminalWaterLifecycle.ShouldRetryReflection(_captureFailures))
		{
			_captureRetryExhausted = true;
			Plugin.Log.LogWarning($"[Water] reflection capture stopped after {_captureFailures - 1} retries: {error.Message}");
			enabled = false;
		}
		else
		{
			_nextCaptureAttemptAt = Time.realtimeSinceStartup + TerminalWaterLifecycle.ReflectionRetryDelay(_captureFailures);
			Plugin.Log.LogWarning($"[Water] reflection capture attempt {_captureFailures} failed; " + $"retrying in {TerminalWaterLifecycle.ReflectionRetryDelay(_captureFailures):F1}s: {error.Message}");
		}
	}

	private void OnDisable()
	{
		ReleaseCapture();
	}

	private void OnDestroy()
	{
		ReleaseCapture();
	}

	private void ReleaseCapture()
	{
		if (_ownsCube && (bool)_source && _source.isActiveAndEnabled)
		{
			RenderTexture renderTexture = CubeField?.GetValue(_source) as RenderTexture;
			if (renderTexture == _cube && (bool)renderTexture)
			{
				DetachWithoutRelease();
				return;
			}
		}
		if (!_cube && (bool)_source && _ownsCube)
		{
			_cube = CubeField?.GetValue(_source) as RenderTexture;
		}
		if ((bool)_cube && Shader.GetGlobalTexture(ProbeId) == _cube)
		{
			Shader.SetGlobalTexture(ProbeId, _previousProbe ? _previousProbe : null);
		}
		if (_ownsCube && (bool)_cube)
		{
			if ((bool)_source)
			{
				Camera component = _source.GetComponent<Camera>();
				if ((bool)component && component.targetTexture == _cube)
				{
					component.targetTexture = null;
				}
				if (CubeField?.GetValue(_source) as RenderTexture == _cube)
				{
					CubeField.SetValue(_source, null);
				}
			}
			_cube.Release();
			UnityEngine.Object.Destroy(_cube);
		}
		_source = null;
		_cube = null;
		_previousProbe = null;
		_ownsCube = false;
		_diagnosticDone = false;
	}
}
