using System;
using System.Diagnostics;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.Rendering;

namespace Manimal.Terminal;

internal static class TerminalRenderProfiler
{
	private static readonly Stopwatch _sw = new Stopwatch();

	private static bool _subscribed;

	private static int _frame = -1;

	private static int _camsThisFrame;

	private static double _msThisFrame;

	private static string _worstNameThisFrame;

	private static double _worstMsThisFrame;

	internal static double LastRenderMs;

	internal static double AvgRenderMs;

	internal static double PeakRenderMs;

	internal static int LastCamCount;

	internal static int PeakCamCount;

	internal static double LastTotalMs;

	internal static double AvgTotalMs;

	internal static double PeakTotalMs;

	internal static string WorstCamName = "";

	internal static double WorstCamMs;

	internal static double GpuMs;

	internal static double CpuMs;

	internal static double MainThreadMs;

	internal static double RenderThreadMs;

	internal static double AvgGpuMs;

	internal static double AvgMainMs;

	internal static double AvgRenderThreadMs;

	internal static double PeakGpuMs;

	private static FrameTiming[] _timings = new FrameTiming[1];

	private static bool _timingWarned;

	private static int _timingFrames;

	private static bool _inventoryDone;

	private static float _inventoryAt = -1f;

	internal static void EnsureSubscribed()
	{
		// zzap perf: per-camera pre/post-render callbacks on every camera every frame
		// are diagnostic only — developer mode
		if (!_subscribed && Plugin.DevMode.Value)
		{
			_subscribed = true;
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPreRender, new Camera.CameraCallback(OnPreRender));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			Plugin.Log.LogInfo("[Render] all-camera profiler armed (per-frame camera count + total submit time)");
		}
	}

	internal static void Disable()
	{
		if (_subscribed)
		{
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPreRender, new Camera.CameraCallback(OnPreRender));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			_subscribed = false;
			_sw.Reset();
			Plugin.Log.LogDebug("[Render] all-camera profiler disarmed off Terminal");
		}
	}

	internal static void ResetForRaid()
	{
		LastRenderMs = (AvgRenderMs = (PeakRenderMs = 0.0));
		LastCamCount = (PeakCamCount = 0);
		LastTotalMs = (AvgTotalMs = (PeakTotalMs = 0.0));
		WorstCamName = "";
		WorstCamMs = 0.0;
		_inventoryDone = false;
		_inventoryAt = -1f;
		GpuMs = (CpuMs = (MainThreadMs = (RenderThreadMs = 0.0)));
		AvgGpuMs = (AvgMainMs = (AvgRenderThreadMs = 0.0));
		PeakGpuMs = 0.0;
		_timingWarned = false;
	}

	internal static void TickFrameTimings()
	{
		// zzap perf: CaptureFrameTimings every frame reads zeros on most rigs (see
		// heartbeat) — developer mode only
		if (!TerminalGate.On || !Plugin.DevMode.Value)
		{
			return;
		}
		try
		{
			FrameTimingManager.CaptureFrameTimings();
			if (FrameTimingManager.GetLatestTimings(1u, _timings) != 0)
			{
				FrameTiming frameTiming = _timings[0];
				CpuMs = frameTiming.cpuFrameTime;
				GpuMs = frameTiming.gpuFrameTime;
				MainThreadMs = frameTiming.cpuMainThreadFrameTime;
				RenderThreadMs = frameTiming.cpuRenderThreadFrameTime;
				AvgGpuMs = AvgGpuMs * 0.95 + GpuMs * 0.05;
				AvgMainMs = AvgMainMs * 0.95 + MainThreadMs * 0.05;
				AvgRenderThreadMs = AvgRenderThreadMs * 0.95 + RenderThreadMs * 0.05;
				if (GpuMs > PeakGpuMs)
				{
					PeakGpuMs = GpuMs;
				}
				if (!_timingWarned && ++_timingFrames > 300 && AvgGpuMs <= 0.01)
				{
					_timingWarned = true;
					Plugin.Log.LogWarning("[Render] FrameTimingManager reports gpuFrameTime=0 — this GPU/driver isn't returning GPU timestamps, so the gpu= field below is meaningless. main/renderThread are still valid.");
				}
			}
		}
		catch (Exception ex)
		{
			if (!_timingWarned)
			{
				_timingWarned = true;
				Plugin.Log.LogWarning("[Render] FrameTimingManager unavailable: " + ex.Message);
			}
		}
	}

	private static void OnPreRender(Camera cam)
	{
		if (!TerminalGate.On || cam == null)
		{
			return;
		}
		if (Time.frameCount != _frame)
		{
			if (_frame >= 0)
			{
				LastCamCount = _camsThisFrame;
				LastTotalMs = _msThisFrame;
				AvgTotalMs = AvgTotalMs * 0.95 + _msThisFrame * 0.05;
				if (_camsThisFrame > PeakCamCount)
				{
					PeakCamCount = _camsThisFrame;
				}
				if (_msThisFrame > PeakTotalMs)
				{
					PeakTotalMs = _msThisFrame;
				}
				if (_worstMsThisFrame > WorstCamMs)
				{
					WorstCamMs = _worstMsThisFrame;
					WorstCamName = _worstNameThisFrame ?? "";
				}
			}
			_frame = Time.frameCount;
			_camsThisFrame = 0;
			_msThisFrame = 0.0;
			_worstMsThisFrame = 0.0;
			_worstNameThisFrame = null;
		}
		_sw.Restart();
	}

	private static void OnPostRender(Camera cam)
	{
		if (!TerminalGate.On || cam == null || !_sw.IsRunning)
		{
			return;
		}
		_sw.Stop();
		double totalMilliseconds = _sw.Elapsed.TotalMilliseconds;
		_camsThisFrame++;
		_msThisFrame += totalMilliseconds;
		if (totalMilliseconds > _worstMsThisFrame)
		{
			_worstMsThisFrame = totalMilliseconds;
			_worstNameThisFrame = cam.name;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (cameraRef != null && cam == cameraRef)
		{
			LastRenderMs = totalMilliseconds;
			AvgRenderMs = AvgRenderMs * 0.95 + totalMilliseconds * 0.05;
			if (totalMilliseconds > PeakRenderMs)
			{
				PeakRenderMs = totalMilliseconds;
			}
		}
	}

	internal static void TickInventory()
	{
		if (_inventoryDone || !TerminalGate.On)
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.MainPlayer == null)
		{
			return;
		}
		if (_inventoryAt < 0f)
		{
			_inventoryAt = Time.realtimeSinceStartup + 20f;
		}
		else if (!(Time.realtimeSinceStartup < _inventoryAt))
		{
			_inventoryDone = true;
			try
			{
				DumpInventory();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Render] inventory dump failed: " + ex.Message);
			}
		}
	}

	private static void DumpInventory()
	{
		ReflectionProbe[] array = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		StringBuilder stringBuilder = new StringBuilder();
		int num8 = 0;
		ReflectionProbe[] array2 = array;
		foreach (ReflectionProbe reflectionProbe in array2)
		{
			if (reflectionProbe == null)
			{
				continue;
			}
			if (reflectionProbe.mode == ReflectionProbeMode.Realtime)
			{
				num++;
			}
			else if (reflectionProbe.mode == ReflectionProbeMode.Baked)
			{
				num5++;
			}
			else
			{
				num6++;
			}
			if (reflectionProbe.mode == ReflectionProbeMode.Realtime)
			{
				switch (reflectionProbe.refreshMode)
				{
				case ReflectionProbeRefreshMode.EveryFrame:
					num2++;
					break;
				case ReflectionProbeRefreshMode.ViaScripting:
					num3++;
					break;
				default:
					num4++;
					break;
				}
				if (reflectionProbe.refreshMode == ReflectionProbeRefreshMode.EveryFrame && num8 < 12)
				{
					num8++;
					stringBuilder.Append($"\n    '{reflectionProbe.name}' res={reflectionProbe.resolution} timeSlice={reflectionProbe.timeSlicingMode} " + $"importance={reflectionProbe.importance} box={reflectionProbe.size} pos={reflectionProbe.transform.position} enabled={reflectionProbe.enabled}");
				}
			}
			if (reflectionProbe.mode == ReflectionProbeMode.Baked && reflectionProbe.bakedTexture == null)
			{
				num7++;
			}
		}
		Plugin.Log.LogWarning($"[Render][probes] {array.Length} reflection probe(s): " + $"{num} realtime ({num2} EVERY-FRAME, {num3} via-scripting, {num4} on-awake), " + $"{num5} baked ({num7} with NO baked cubemap — rip casualties), {num6} custom. " + $"QualitySettings.realtimeReflectionProbes={QualitySettings.realtimeReflectionProbes} " + $"defaultReflectionMode={RenderSettings.defaultReflectionMode}" + ((stringBuilder.Length > 0) ? $"\n  EVERY-FRAME probes:{stringBuilder}" : ""));
		Camera[] array3 = UnityEngine.Object.FindObjectsOfType<Camera>();
		StringBuilder stringBuilder2 = new StringBuilder();
		Camera[] array4 = array3;
		foreach (Camera camera in array4)
		{
			if (!(camera == null))
			{
				stringBuilder2.Append($"\n    '{camera.name}' enabled={camera.isActiveAndEnabled} depth={camera.depth} " + "rt=" + ((camera.targetTexture != null) ? (camera.targetTexture.width + "x" + camera.targetTexture.height) : "screen") + " " + $"mask=0x{camera.cullingMask:X} path={camera.renderingPath} hdr={camera.allowHDR}");
			}
		}
		Plugin.Log.LogWarning($"[Render][cams] {array3.Length} camera(s) in scene:{stringBuilder2}");
	}
}
