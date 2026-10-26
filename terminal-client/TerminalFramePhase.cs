using System;
using System.Diagnostics;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalFramePhase
{
	[DefaultExecutionOrder(-32000)]
	internal class EarlyProbe : MonoBehaviour
	{
		private void Update()
		{
			if (TerminalGate.On)
			{
				MarkUpdateStart();
			}
		}
	}

	[DefaultExecutionOrder(32000)]
	internal class LateProbe : MonoBehaviour
	{
		private void Update()
		{
			if (TerminalGate.On)
			{
				MarkUpdateEnd();
			}
		}

		private void LateUpdate()
		{
			if (TerminalGate.On)
			{
				MarkLateEnd();
			}
		}
	}

	private static readonly double TickMs = 1000.0 / (double)Stopwatch.Frequency;

	private static long _tUpdStart;

	private static long _tUpdEnd;

	private static long _tLateEnd;

	private static long _tPreCull;

	private static long _tPreRender;

	private static long _tPostRender;

	private static bool _havePrevPost;

	private static int _updateFrame = -1;

	private static int _updateEndFrame = -1;

	private static int _lateFrame = -1;

	private static int _renderFrame = -1;

	private static int _completedFrame = -1;

	private static double _completedUpd;

	private static double _completedLate;

	private static double _completedPostLate;

	private static double _completedCull;

	private static double _completedRend;

	private static double _completedTail;

	internal static double UpdMs;

	internal static double LateMs;

	internal static double PostLateMs;

	internal static double CullMs;

	internal static double RendMs;

	internal static double TailMs;

	internal static double AvgUpd;

	internal static double AvgLate;

	internal static double AvgPostLate;

	internal static double AvgCull;

	internal static double AvgRend;

	internal static double AvgTail;

	internal static double PeakUpd;

	internal static double PeakLate;

	internal static double PeakPostLate;

	internal static double PeakCull;

	internal static double PeakRend;

	internal static double PeakTail;

	private static bool _spawned;

	private static GameObject _host;

	internal static long PostRenderStamp => _tPostRender;

	internal static void EnsureSpawned()
	{
		if (!_spawned)
		{
			_spawned = true;
			_host = new GameObject("Manimal_TerminalFramePhase");
			UnityEngine.Object.DontDestroyOnLoad(_host);
			_host.AddComponent<EarlyProbe>();
			_host.AddComponent<LateProbe>();
			Camera.onPreCull = (Camera.CameraCallback)Delegate.Combine(Camera.onPreCull, new Camera.CameraCallback(OnPreCull));
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPreRender, new Camera.CameraCallback(OnPreRender));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			Plugin.Log.LogInfo("[Phase] frame-phase profiler armed (upd/late/cull/rend/tail breakdown)");
		}
	}

	internal static void Disable()
	{
		if (_spawned)
		{
			Camera.onPreCull = (Camera.CameraCallback)Delegate.Remove(Camera.onPreCull, new Camera.CameraCallback(OnPreCull));
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPreRender, new Camera.CameraCallback(OnPreRender));
			Camera.onPostRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPostRender, new Camera.CameraCallback(OnPostRender));
			if (_host != null)
			{
				UnityEngine.Object.Destroy(_host);
			}
			_host = null;
			_spawned = false;
			_havePrevPost = false;
			_completedFrame = -1;
			Plugin.Log.LogDebug("[Phase] frame-phase profiler disarmed off Terminal");
		}
	}

	internal static void ResetForRaid()
	{
		UpdMs = (LateMs = (PostLateMs = (CullMs = (RendMs = (TailMs = 0.0)))));
		AvgUpd = (AvgLate = (AvgPostLate = (AvgCull = (AvgRend = (AvgTail = 0.0)))));
		PeakUpd = (PeakLate = (PeakPostLate = (PeakCull = (PeakRend = (PeakTail = 0.0)))));
		_havePrevPost = false;
		_updateFrame = (_updateEndFrame = (_lateFrame = (_renderFrame = (_completedFrame = -1))));
	}

	private static double Ms(long a, long b)
	{
		return (double)(b - a) * TickMs;
	}

	internal static void MarkUpdateStart()
	{
		long timestamp = Stopwatch.GetTimestamp();
		int num = Time.frameCount - 1;
		if (_havePrevPost && _renderFrame == num)
		{
			TailMs = Ms(_tPostRender, timestamp);
			AvgTail = AvgTail * 0.95 + TailMs * 0.05;
			if (TailMs > PeakTail)
			{
				PeakTail = TailMs;
			}
		}
		_completedFrame = -1;
		if (_havePrevPost && _updateFrame == num && _updateEndFrame == num && _lateFrame == num && _renderFrame == num)
		{
			_completedFrame = num;
			_completedUpd = UpdMs;
			_completedLate = LateMs;
			_completedPostLate = PostLateMs;
			_completedCull = CullMs;
			_completedRend = RendMs;
			_completedTail = TailMs;
		}
		_updateFrame = Time.frameCount;
		_tUpdStart = timestamp;
	}

	internal static void MarkUpdateEnd()
	{
		_tUpdEnd = Stopwatch.GetTimestamp();
		_updateEndFrame = Time.frameCount;
		UpdMs = Ms(_tUpdStart, _tUpdEnd);
		AvgUpd = AvgUpd * 0.95 + UpdMs * 0.05;
		if (UpdMs > PeakUpd)
		{
			PeakUpd = UpdMs;
		}
	}

	internal static void MarkLateEnd()
	{
		_tLateEnd = Stopwatch.GetTimestamp();
		_lateFrame = Time.frameCount;
		LateMs = Ms(_tUpdEnd, _tLateEnd);
		AvgLate = AvgLate * 0.95 + LateMs * 0.05;
		if (LateMs > PeakLate)
		{
			PeakLate = LateMs;
		}
	}

	private static void OnPreCull(Camera cam)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (!(cameraRef == null) && !(cam != cameraRef))
		{
			_tPreCull = Stopwatch.GetTimestamp();
			PostLateMs = Ms(_tLateEnd, _tPreCull);
			AvgPostLate = AvgPostLate * 0.95 + PostLateMs * 0.05;
			if (PostLateMs > PeakPostLate)
			{
				PeakPostLate = PostLateMs;
			}
		}
	}

	private static void OnPreRender(Camera cam)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (!(cameraRef == null) && !(cam != cameraRef))
		{
			_tPreRender = Stopwatch.GetTimestamp();
			CullMs = Ms(_tPreCull, _tPreRender);
			AvgCull = AvgCull * 0.95 + CullMs * 0.05;
			if (CullMs > PeakCull)
			{
				PeakCull = CullMs;
			}
		}
	}

	private static void OnPostRender(Camera cam)
	{
		if (!TerminalGate.On)
		{
			return;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (!(cameraRef == null) && !(cam != cameraRef))
		{
			_tPostRender = Stopwatch.GetTimestamp();
			_renderFrame = Time.frameCount;
			_havePrevPost = true;
			RendMs = Ms(_tPreRender, _tPostRender);
			AvgRend = AvgRend * 0.95 + RendMs * 0.05;
			if (RendMs > PeakRend)
			{
				PeakRend = RendMs;
			}
		}
	}

	internal static string Summary()
	{
		return $"upd={AvgUpd:F1}/{PeakUpd:F0}p late={AvgLate:F1}/{PeakLate:F0}p " + $"postLate={AvgPostLate:F1}/{PeakPostLate:F0}p cull={AvgCull:F1}/{PeakCull:F0}p " + $"rend={AvgRend:F1}/{PeakRend:F0}p tail={AvgTail:F1}/{PeakTail:F0}p";
	}

	internal static string Instant()
	{
		if (_completedFrame < 0 || _completedFrame != Time.frameCount - 1)
		{
			return $"frame={Time.frameCount - 1} incomplete";
		}
		return $"frame={_completedFrame} upd={_completedUpd:F1} late={_completedLate:F1} postLate={_completedPostLate:F1} cull={_completedCull:F1} " + $"rend={_completedRend:F1} tail={_completedTail:F1}";
	}
}
