using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalPerfWatch
{
	private static float _lastLog;

	private static int _spikes;

	private static List<Renderer> _water;

	private static readonly Stopwatch _sw = new Stopwatch();

	private static double _oursMs;

	private static readonly float[] _ring = new float[8];

	private static int _ringIdx;

	private static int _gc0Prev;

	private static float _avg = 1f / 60f;

	private static float _nextCensusAt;

	private static int _censusNum;

	private static float _nextHeartAt;

	private static float _nextTrace;

	private static float _traceMaxDt;

	private static int _traceFrames;

	private static ParticleSystem[] _psCache;

	private static float _nextPsRefresh;

	internal static void OursBegin()
	{
		_sw.Restart();
	}

	internal static void OursEnd()
	{
		_oursMs = _sw.Elapsed.TotalMilliseconds;
	}

	internal static void ResetForRaid()
	{
		_spikes = 0;
		_lastLog = 0f;
		_water = null;
		_nextCensusAt = 0f;
		_censusNum = 0;
		_nextHeartAt = 0f;
		_avg = 1f / 60f;
		_ringIdx = 0;
		Array.Clear(_ring, 0, _ring.Length);
		_gc0Prev = GC.CollectionCount(0);
		_nextTrace = (_traceMaxDt = 0f);
		_traceFrames = 0;
		_psCache = null;
		_nextPsRefresh = 0f;
		TerminalTickProfiler.Reset();
		TerminalPathDiagnostics.ResetForRaid();
		TerminalRenderProfiler.ResetForRaid();
		TerminalFramePhase.ResetForRaid();
		TerminalLoopProbe.ResetForRaid();
		TerminalVramProbe.ResetForRaid();
		TerminalWorldDiff.ResetForRaid();
	}

	internal static void WatchWater(List<Renderer> planes)
	{
		_water = planes;
	}

	private static int LiveParticles(out int emitting)
	{
		emitting = 0;
		try
		{
			if (_psCache == null || Time.realtimeSinceStartup >= _nextPsRefresh)
			{
				_nextPsRefresh = Time.realtimeSinceStartup + 20f;
				_psCache = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
			}
			int num = 0;
			for (int i = 0; i < _psCache.Length; i++)
			{
				ParticleSystem particleSystem = _psCache[i];
				if (particleSystem == null)
				{
					continue;
				}
				int particleCount = particleSystem.particleCount;
				if (particleCount > 0)
				{
					num += particleCount;
					if (particleSystem.isEmitting)
					{
						emitting++;
					}
				}
			}
			return num;
		}
		catch
		{
			return -1;
		}
	}

	private static void TickTrace(GameWorld world, Player p)
	{
		try
		{
			if (Plugin.TraceFrameCycle == null || !Plugin.TraceFrameCycle.Value)
			{
				return;
			}
			float num = Time.unscaledDeltaTime * 1000f;
			if (num > _traceMaxDt)
			{
				_traceMaxDt = num;
			}
			_traceFrames++;
			if (Time.realtimeSinceStartup < _nextTrace)
			{
				return;
			}
			_nextTrace = Time.realtimeSinceStartup + 0.25f;
			Vector3 vector = ((p != null) ? p.Position : Vector3.zero);
			int num2 = LiveParticles(out var emitting);
			int num3 = -1;
			try
			{
				Camera cameraRef = TerminalCullingDriver.CameraRef;
				if (cameraRef != null)
				{
					num3 = cameraRef.commandBufferCount;
				}
			}
			catch
			{
			}
			long num4 = GC.GetTotalMemory(forceFullCollection: false) / 1048576;
			int num5 = GC.CollectionCount(0);
			TerminalVramProbe.Read(out var totalMb, out var budgetMb, out var usedMb);
			int num6 = 0;
			int num7 = 0;
			List<IPlayer> list = ((world != null) ? world.RegisteredPlayers : null);
			if (list != null)
			{
				foreach (IPlayer item in list)
				{
					if (item != null && item.IsAI && item.HealthController != null)
					{
						if (item.HealthController.IsAlive)
						{
							num6++;
						}
						else
						{
							num7++;
						}
					}
				}
			}
			double num8 = TerminalLoopProbe.MsBetween(TerminalFramePhase.PostRenderStamp, TerminalLoopProbe.EndStampOf("FinishFrameRendering"));
			float num9 = 1f;
			float num10 = 1f;
			int num11 = 0;
			int num12 = 0;
			try
			{
				num9 = ScalableBufferManager.widthScaleFactor;
				num10 = ScalableBufferManager.heightScaleFactor;
				Camera cameraRef2 = TerminalCullingDriver.CameraRef;
				if (cameraRef2 != null)
				{
					num11 = cameraRef2.pixelWidth;
					num12 = cameraRef2.pixelHeight;
				}
			}
			catch
			{
			}
			Plugin.Log.LogWarning($"[Trace] t={Time.realtimeSinceStartup:F2} frame={num:F1} worst={_traceMaxDt:F1}" + $" n={_traceFrames}" + string.Format(" PHASE[{0}] finishLast={1:F1}", TerminalFramePhase.Instant(), TerminalLoopProbe.InstantOf("FinishFrameRendering")) + $" parts={num2} emit={emitting}" + $" postFx={num8:F1} resScale={num9:F3}x{num10:F3} camPx={num11}x{num12}" + $" cmdBufs={num3} memMB={num4} gc0={num5}" + $" livingAi={num6} deadAi={num7}" + $" vramMB={usedMb}/{budgetMb}/{totalMb}" + $" pos={vector.x:F0},{vector.y:F0},{vector.z:F0}");
			_traceMaxDt = 0f;
			_traceFrames = 0;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Trace] failed: " + ex.Message);
		}
	}

	private static string LoopBit()
	{
		string text = TerminalLoopProbe.TopN(6);
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		return $" POSTLATE[sum={TerminalLoopProbe.MeasuredSum():F1} vs {TerminalFramePhase.AvgPostLate:F1}] {text} ||";
	}

	internal static void Tick()
	{
		if (!TerminalGate.On)
		{
			return;
		}
		float unscaledDeltaTime = Time.unscaledDeltaTime;
		int num = GC.CollectionCount(0);
		TerminalRenderProfiler.TickInventory();
		TerminalRenderProfiler.TickFrameTimings();
		TerminalLoopProbe.Tick();
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		Player player = ((gameWorld != null) ? gameWorld.MainPlayer : null);
		TickTrace(gameWorld, player);
		if (player != null)
		{
			TerminalWorldDiff.Tick(_avg * 1000f, player);
		}
		if (player != null && Plugin.SceneCensus != null && Plugin.SceneCensus.Value && Time.realtimeSinceStartup >= _nextCensusAt)
		{
			_nextCensusAt = Time.realtimeSinceStartup + 60f;
			_censusNum++;
			try
			{
				int num2 = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
				int num3 = UnityEngine.Object.FindObjectsOfType<AudioSource>().Length;
				int num4 = UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Length;
				int num5 = 0;
				try
				{
					num5 = gameWorld.AllAlivePlayersList?.Count ?? 0;
				}
				catch
				{
				}
				int num6 = 0;
				int num7 = 0;
				int num8 = 0;
				try
				{
					Vector3 vector;
					if (TerminalCullingDriver.CameraRef != null)
					{
						vector = TerminalCullingDriver.CameraRef.transform.position;
					}
					else
					{
						vector = ((player != null) ? player.Position : Vector3.zero);
					}
					Light[] array = UnityEngine.Object.FindObjectsOfType<Light>();
					foreach (Light light in array)
					{
						if (!(light == null) && light.enabled && light.gameObject.activeInHierarchy && !(light.intensity <= 0.01f))
						{
							num6++;
							if (light.shadows != LightShadows.None)
							{
								num8++;
							}
							if ((light.transform.position - vector).sqrMagnitude < 3600f)
							{
								num7++;
							}
						}
					}
				}
				catch
				{
				}
				StringBuilder stringBuilder = new StringBuilder();
				int num9 = 0;
				try
				{
					Camera[] array2 = UnityEngine.Object.FindObjectsOfType<Camera>();
					foreach (Camera camera in array2)
					{
						if (!(camera == null))
						{
							bool isActiveAndEnabled = camera.isActiveAndEnabled;
							if (isActiveAndEnabled)
							{
								num9++;
							}
							stringBuilder.Append($"\n    '{camera.name}' on={isActiveAndEnabled} depth={camera.depth} " + "rt=" + ((camera.targetTexture != null) ? (camera.targetTexture.width + "x" + camera.targetTexture.height) : "screen") + " " + $"px={camera.pixelWidth}x{camera.pixelHeight} mask=0x{camera.cullingMask:X} path={camera.renderingPath}");
						}
					}
				}
				catch
				{
				}
				Plugin.Log.LogWarning($"[Perf][cams #{_censusNum}] {num9} camera(s) ENABLED:{stringBuilder}");
				Plugin.Log.LogWarning($"[Perf][census #{_censusNum}] avgFrame={_avg * 1000f:F1}ms renderers={num2}" + $" audioSources={num3} particles={num4} alive={num5}" + $" | LIGHTS lit={num6} within60m={num7} shadowCasting={num8}" + $" | cull={TerminalFramePhase.AvgCull:F1}ms");
			}
			catch
			{
			}
		}
		if (player != null && Time.realtimeSinceStartup >= _nextHeartAt)
		{
			_nextHeartAt = Time.realtimeSinceStartup + 30f;
			string text = TerminalTickProfiler.TopN(6);
			if (!string.IsNullOrEmpty(text))
			{
				Plugin.Log.LogWarning($"[Perf][heartbeat] t={Time.realtimeSinceStartup:F0}s avg={_avg * 1000f:F1}ms" + " || PHASE " + TerminalFramePhase.Summary() + " ||" + LoopBit() + $" fpsCam={TerminalRenderProfiler.AvgRenderMs:F1}avg" + $" allCams={TerminalRenderProfiler.LastCamCount}(peak {TerminalRenderProfiler.PeakCamCount})" + $" allCamMs={TerminalRenderProfiler.LastTotalMs:F1}/{TerminalRenderProfiler.AvgTotalMs:F1}avg/{TerminalRenderProfiler.PeakTotalMs:F1}peak" + $" worstCam='{TerminalRenderProfiler.WorstCamName}'@{TerminalRenderProfiler.WorstCamMs:F1}ms" + " | top: " + text);
			}
		}
		if (unscaledDeltaTime >= 0.05f)
		{
			_spikes++;
			if (Time.realtimeSinceStartup - _lastLog >= 2f)
			{
				_lastLog = Time.realtimeSinceStartup;
				int num10 = -1;
				try
				{
					num10 = ((gameWorld != null && gameWorld.AllAlivePlayersList != null) ? gameWorld.AllAlivePlayersList.Count : (-1));
				}
				catch
				{
				}
				int num11 = 0;
				if (_water != null)
				{
					foreach (Renderer item in _water)
					{
						if ((bool)item && item.isVisible)
						{
							num11++;
						}
					}
				}
				int num12 = -1;
				try
				{
					num12 = TerminalArtillery.ActiveZonesDict()?.Count ?? (-1);
				}
				catch
				{
				}
				StringBuilder stringBuilder2 = new StringBuilder();
				for (int j = 1; j <= _ring.Length; j++)
				{
					stringBuilder2.Append((_ring[(_ringIdx + j) % _ring.Length] * 1000f).ToString("F0")).Append(' ');
				}
				string text2 = TerminalTickProfiler.TopN(4);
				Plugin.Log.LogWarning($"[Perf] {unscaledDeltaTime * 1000f:F0}ms frame (spike #{_spikes})" + string.Format(" at {0}, {1} alive", (player != null) ? player.Position.ToString() : "?", num10) + string.Format(", water visible: {0}, artyZones={1} pump={2}", num11, num12, TerminalArtillery.Pumping ? "on" : "off") + " | PHASE " + TerminalFramePhase.Instant() + $" pluginUpdateCurrent(frame={Time.frameCount})={_oursMs:F1}ms fpsCamLast={TerminalRenderProfiler.LastRenderMs:F1}ms" + $" allCams={TerminalRenderProfiler.LastCamCount}@{TerminalRenderProfiler.LastTotalMs:F1}ms" + $" gc0Δ={num - _gc0Prev} avg={_avg * 1000f:F1}ms prev8: {stringBuilder2.ToString().TrimEnd()}" + " | scopesPrevious(inclusive,not-additive): " + TerminalTickProfiler.PreviousFrameSummary() + (string.IsNullOrEmpty(text2) ? "" : (" | recentScopes: " + text2)));
			}
		}
		_ring[_ringIdx] = unscaledDeltaTime;
		_ringIdx = (_ringIdx + 1) % _ring.Length;
		_gc0Prev = num;
		_avg = Mathf.Lerp(_avg, unscaledDeltaTime, 0.05f);
	}
}
