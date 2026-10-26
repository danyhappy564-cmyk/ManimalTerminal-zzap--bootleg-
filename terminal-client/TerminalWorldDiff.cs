using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalWorldDiff
{
	private sealed class Snapshot
	{
		internal readonly Dictionary<string, int> Total = new Dictionary<string, int>(768);

		internal readonly Dictionary<string, int> Active = new Dictionary<string, int>(768);

		internal readonly Dictionary<string, string> State = new Dictionary<string, string>(4096);

		internal long AllocatedMb;

		internal long ReservedMb;

		internal long UnusedMb;

		internal long MonoUsedMb;

		internal long MonoHeapMb;

		internal int Alive;

		internal int LiveParticles;

		internal int EmittingParticles;

		internal int RenderTextures;

		internal int CreatedRenderTextures;

		internal long RenderTexturePixels;

		internal long VramUsedMb;

		internal long VramBudgetMb;

		internal long VramTotalMb;
	}

	private struct Sample
	{
		internal float At;

		internal float Frame;

		internal float AvgFrame;

		internal double Post;

		internal double AvgPost;

		internal double Finish;

		internal double Upd;

		internal double Late;

		internal double Cull;

		internal double Rend;

		internal double Tail;

		internal long ManagedMb;

		internal long VramUsedMb;

		internal long VramBudgetMb;

		internal int Alive;

		internal Vector3 Pos;
	}

	private struct EventMark
	{
		internal float At;

		internal string Text;
	}

	private static Snapshot _baseline;

	private static float _baseAt = -1f;

	private static float _nextSnap;

	private static float _chopSince = -1f;

	private static float _nextSample;

	private static float _gameplaySince = -1f;

	private static float _ignoreUntil = -1f;

	private static bool _armed;

	private static bool _fired;

	private const float GameplayWarmupSeconds = 30f;

	private const float LoadingHitchIgnoreSeconds = 10f;

	private const int HistorySize = 180;

	private static readonly Sample[] History = new Sample[180];

	private static int _historyNext;

	private static int _historyCount;

	private static readonly Queue<EventMark> Events = new Queue<EventMark>(64);

	private static readonly List<Component> ComponentBuffer = new List<Component>(24);

	internal static void ResetForRaid()
	{
		_baseline = null;
		_baseAt = -1f;
		_nextSnap = (_nextSample = 0f);
		_chopSince = -1f;
		_gameplaySince = (_ignoreUntil = -1f);
		_armed = false;
		_fired = false;
		_historyNext = (_historyCount = 0);
		Events.Clear();
	}

	internal static void RecordEvent(string category, string detail)
	{
		try
		{
			while (Events.Count >= 64)
			{
				Events.Dequeue();
			}
			Events.Enqueue(new EventMark
			{
				At = Time.realtimeSinceStartup,
				Text = category + ": " + detail
			});
		}
		catch
		{
		}
	}

	internal static void Tick(float avgFrameMs, Player player)
	{
		try
		{
			if (!TerminalGate.On || Plugin.WorldDiff == null || !Plugin.WorldDiff.Value)
			{
				return;
			}
			CaptureTimingSample(avgFrameMs, player);
			if (_fired)
			{
				return;
			}
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			if (!GameplayCameraReady())
			{
				_gameplaySince = -1f;
				_chopSince = -1f;
				return;
			}
			if (Time.unscaledDeltaTime >= 0.25f)
			{
				_ignoreUntil = realtimeSinceStartup + 10f;
				if (!_armed)
				{
					_gameplaySince = realtimeSinceStartup;
				}
				_chopSince = -1f;
				return;
			}
			if (realtimeSinceStartup < _ignoreUntil)
			{
				_chopSince = -1f;
				return;
			}
			if (_gameplaySince < 0f)
			{
				_gameplaySince = realtimeSinceStartup;
			}
			if (!_armed)
			{
				if (realtimeSinceStartup - _gameplaySince < 30f)
				{
					return;
				}
				_armed = true;
				_nextSnap = 0f;
				Plugin.Log.LogWarning($"[Onset] detector armed after {30f:F0}s of hitch-free FPS-camera gameplay");
			}
			float num = (float)TerminalFramePhase.AvgPostLate;
			if (num < 4f)
			{
				_chopSince = -1f;
				if (!(realtimeSinceStartup < _nextSnap))
				{
					_nextSnap = realtimeSinceStartup + 20f;
					_baseline = TakeSnapshot();
					_baseAt = realtimeSinceStartup;
					Plugin.Log.LogWarning($"[Onset] smooth baseline t={_baseAt:F0}s frame={avgFrameMs:F1}ms " + $"postLate={num:F1}ms activeTypes={_baseline.Active.Count} stateful={_baseline.State.Count} " + Metrics(_baseline));
				}
			}
			else if (num <= 10f || _baseline == null)
			{
				_chopSince = -1f;
			}
			else if (_chopSince < 0f)
			{
				_chopSince = realtimeSinceStartup;
				RecordEvent("detector", $"postLate EMA crossed 10ms ({num:F1}ms)");
			}
			else if (!(realtimeSinceStartup - _chopSince < 3f))
			{
				_fired = true;
				Report(TakeSnapshot(), avgFrameMs, num);
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Onset] recorder failed: {arg}");
		}
	}

	private static bool GameplayCameraReady()
	{
		try
		{
			Camera cameraRef = TerminalCullingDriver.CameraRef;
			return cameraRef != null && cameraRef.enabled && cameraRef.gameObject.activeInHierarchy && string.Equals(cameraRef.name, "FPS Camera", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static void CaptureTimingSample(float avgFrameMs, Player player)
	{
		if (!(Time.realtimeSinceStartup < _nextSample))
		{
			_nextSample = Time.realtimeSinceStartup + 0.25f;
			int alive = -1;
			try
			{
				GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
				alive = ((gameWorld != null && gameWorld.AllAlivePlayersList != null) ? gameWorld.AllAlivePlayersList.Count : (-1));
			}
			catch
			{
			}
			TerminalVramProbe.Read(out var _, out var budgetMb, out var usedMb);
			History[_historyNext] = new Sample
			{
				At = Time.realtimeSinceStartup,
				Frame = Time.unscaledDeltaTime * 1000f,
				AvgFrame = avgFrameMs,
				Post = TerminalFramePhase.PostLateMs,
				AvgPost = TerminalFramePhase.AvgPostLate,
				Finish = TerminalLoopProbe.InstantOf("FinishFrameRendering"),
				Upd = TerminalFramePhase.UpdMs,
				Late = TerminalFramePhase.LateMs,
				Cull = TerminalFramePhase.CullMs,
				Rend = TerminalFramePhase.RendMs,
				Tail = TerminalFramePhase.TailMs,
				ManagedMb = GC.GetTotalMemory(forceFullCollection: false) / 1048576,
				VramUsedMb = usedMb,
				VramBudgetMb = budgetMb,
				Alive = alive,
				Pos = ((player != null) ? player.Position : Vector3.zero)
			};
			_historyNext = (_historyNext + 1) % 180;
			if (_historyCount < 180)
			{
				_historyCount++;
			}
		}
	}

	private static void Report(Snapshot now, float avgFrameMs, float post)
	{
		StringBuilder stringBuilder = new StringBuilder(32768);
		stringBuilder.Append($"[Onset] SAWTOOTH ONSET t={Time.realtimeSinceStartup:F2}s frame={avgFrameMs:F1}ms postLate={post:F1}ms\n").Append($"  baseline t={_baseAt:F2}s ({Time.realtimeSinceStartup - _baseAt:F1}s earlier): {Metrics(_baseline)}\n").Append("  onset: " + Metrics(now) + "\n");
		AppendTypeDiff(stringBuilder, now);
		AppendStateDiff(stringBuilder, now);
		AppendEvents(stringBuilder);
		AppendHistory(stringBuilder);
		Plugin.Log.LogWarning(stringBuilder.ToString());
	}

	private static void AppendTypeDiff(StringBuilder sb, Snapshot now)
	{
		List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
		foreach (KeyValuePair<string, int> item in now.Active)
		{
			_baseline.Active.TryGetValue(item.Key, out var value);
			int num = item.Value - value;
			if (num != 0)
			{
				list.Add(new KeyValuePair<string, int>(item.Key, num));
			}
		}
		foreach (KeyValuePair<string, int> item2 in _baseline.Active)
		{
			if (!now.Active.ContainsKey(item2.Key))
			{
				list.Add(new KeyValuePair<string, int>(item2.Key, -item2.Value));
			}
		}
		list.Sort((KeyValuePair<string, int> a, KeyValuePair<string, int> b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
		sb.Append("  ACTIVE COMPONENT TYPE DELTAS (top 50):\n");
		if (list.Count == 0)
		{
			sb.Append("    none\n");
		}
		for (int num2 = 0; num2 < list.Count && num2 < 50; num2++)
		{
			KeyValuePair<string, int> keyValuePair = list[num2];
			_baseline.Active.TryGetValue(keyValuePair.Key, out var value2);
			now.Total.TryGetValue(keyValuePair.Key, out var value3);
			sb.Append(string.Format("    {0}{1,-7} {2,-42} active {3} -> {4} (total {5})\n", (keyValuePair.Value > 0) ? "+" : "", keyValuePair.Value, keyValuePair.Key, value2, value2 + keyValuePair.Value, value3));
		}
	}

	private static void AppendStateDiff(StringBuilder sb, Snapshot now)
	{
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, string> item in now.State)
		{
			if (!_baseline.State.TryGetValue(item.Key, out var value))
			{
				list.Add("ADDED   " + item.Value);
			}
			else if (!string.Equals(value, item.Value, StringComparison.Ordinal))
			{
				list.Add("CHANGED " + value + "  =>  " + item.Value);
			}
		}
		foreach (KeyValuePair<string, string> item2 in _baseline.State)
		{
			if (!now.State.ContainsKey(item2.Key))
			{
				list.Add("REMOVED " + item2.Value);
			}
		}
		sb.Append($"  STATEFUL INSTANCE DELTAS ({list.Count} total, first 100):\n");
		if (list.Count == 0)
		{
			sb.Append("    none\n");
		}
		for (int i = 0; i < list.Count && i < 100; i++)
		{
			sb.Append("    ").Append(list[i]).Append('\n');
		}
	}

	private static void AppendEvents(StringBuilder sb)
	{
		sb.Append("  RECENT MAP EVENTS:\n");
		bool flag = false;
		foreach (EventMark @event in Events)
		{
			if (!(Time.realtimeSinceStartup - @event.At > 120f))
			{
				flag = true;
				sb.Append($"    t={@event.At:F2} {@event.Text}\n");
			}
		}
		if (!flag)
		{
			sb.Append("    none recorded in the preceding 120s\n");
		}
	}

	private static void AppendHistory(StringBuilder sb)
	{
		sb.Append($"  ROLLING FRAME HISTORY ({_historyCount} samples, oldest -> onset):\n").Append("    t frame avg post postAvg finish upd late cull rend tail mem vram/budget alive pos\n");
		int num = (_historyNext - _historyCount + 180) % 180;
		for (int i = 0; i < _historyCount; i++)
		{
			Sample sample = History[(num + i) % 180];
			sb.Append($"    {sample.At:F2} {sample.Frame:F1} {sample.AvgFrame:F1} {sample.Post:F1} {sample.AvgPost:F1} {sample.Finish:F1} " + $"{sample.Upd:F1} {sample.Late:F1} {sample.Cull:F1} {sample.Rend:F1} {sample.Tail:F1} {sample.ManagedMb} " + $"{sample.VramUsedMb}/{sample.VramBudgetMb} {sample.Alive} " + $"{sample.Pos.x:F0},{sample.Pos.y:F0},{sample.Pos.z:F0}\n");
		}
	}

	private static string Metrics(Snapshot s)
	{
		return $"alloc={s.AllocatedMb}MB reserved={s.ReservedMb}MB unused={s.UnusedMb}MB " + $"mono={s.MonoUsedMb}/{s.MonoHeapMb}MB alive={s.Alive} " + $"particles={s.LiveParticles} emitting={s.EmittingParticles} " + $"RT={s.CreatedRenderTextures}/{s.RenderTextures} ({(float)s.RenderTexturePixels / 1000000f:F1}MP) " + $"vram={s.VramUsedMb}/{s.VramBudgetMb}/{s.VramTotalMb}MB";
	}

	private static Snapshot TakeSnapshot()
	{
		Snapshot snapshot = new Snapshot
		{
			AllocatedMb = Profiler.GetTotalAllocatedMemoryLong() / 1048576,
			ReservedMb = Profiler.GetTotalReservedMemoryLong() / 1048576,
			UnusedMb = Profiler.GetTotalUnusedReservedMemoryLong() / 1048576,
			MonoUsedMb = Profiler.GetMonoUsedSizeLong() / 1048576,
			MonoHeapMb = Profiler.GetMonoHeapSizeLong() / 1048576
		};
		try
		{
			GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
			snapshot.Alive = ((gameWorld != null && gameWorld.AllAlivePlayersList != null) ? gameWorld.AllAlivePlayersList.Count : (-1));
		}
		catch
		{
			snapshot.Alive = -1;
		}
		TerminalVramProbe.Read(out snapshot.VramTotalMb, out snapshot.VramBudgetMb, out snapshot.VramUsedMb);
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (sceneAt.isLoaded)
			{
				GameObject[] rootGameObjects = sceneAt.GetRootGameObjects();
				for (int j = 0; j < rootGameObjects.Length; j++)
				{
					Walk(rootGameObjects[j].transform, snapshot);
				}
			}
		}
		GameObject gameObject = null;
		try
		{
			gameObject = new GameObject("Manimal_DdolProbe");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			GameObject[] rootGameObjects = gameObject.scene.GetRootGameObjects();
			for (int j = 0; j < rootGameObjects.Length; j++)
			{
				Walk(rootGameObjects[j].transform, snapshot);
			}
		}
		catch
		{
		}
		finally
		{
			if (gameObject != null)
			{
				UnityEngine.Object.Destroy(gameObject);
			}
		}
		try
		{
			RenderTexture[] array = Resources.FindObjectsOfTypeAll<RenderTexture>();
			foreach (RenderTexture renderTexture in array)
			{
				if (!(renderTexture == null))
				{
					snapshot.RenderTextures++;
					if (renderTexture.IsCreated())
					{
						snapshot.CreatedRenderTextures++;
						snapshot.RenderTexturePixels += (long)renderTexture.width * (long)renderTexture.height;
					}
				}
			}
		}
		catch
		{
		}
		return snapshot;
	}

	private static void Walk(Transform t, Snapshot snap)
	{
		t.GetComponents(ComponentBuffer);
		bool activeInHierarchy = t.gameObject.activeInHierarchy;
		for (int i = 0; i < ComponentBuffer.Count; i++)
		{
			Component component = ComponentBuffer[i];
			if (!(component == null))
			{
				string name = component.GetType().Name;
				Bump(snap.Total, name);
				Behaviour behaviour = component as Behaviour;
				if (activeInHierarchy && (behaviour == null || behaviour.enabled))
				{
					Bump(snap.Active, name);
				}
				string text = StatefulSignature(component, activeInHierarchy, snap);
				if (text != null)
				{
					snap.State[name + "#" + component.GetInstanceID()] = name + " '" + ObjectPath(t) + "' " + text;
				}
			}
		}
		for (int j = 0; j < t.childCount; j++)
		{
			Walk(t.GetChild(j), snap);
		}
	}

	private static string StatefulSignature(Component c, bool active, Snapshot snap)
	{
		try
		{
			if (c is ParticleSystem particleSystem)
			{
				snap.LiveParticles += particleSystem.particleCount;
				if (particleSystem.isEmitting)
				{
					snap.EmittingParticles++;
				}
				ParticleSystem.MainModule main = particleSystem.main;
				return $"active={active} play={particleSystem.isPlaying} emit={particleSystem.isEmitting} pause={particleSystem.isPaused} loop={main.loop}";
			}
			if (c is AudioSource audioSource)
			{
				return string.Format("active={0} enabled={1} playing={2} loop={3} mute={4} clip='{5}'", active, audioSource.enabled, audioSource.isPlaying, audioSource.loop, audioSource.mute, (audioSource.clip != null) ? audioSource.clip.name : "null");
			}
			if (c is Animator animator)
			{
				int num = ((animator.isInitialized && animator.layerCount > 0) ? animator.GetCurrentAnimatorStateInfo(0).fullPathHash : 0);
				return $"active={active} enabled={animator.enabled} init={animator.isInitialized} speed={animator.speed:F2} cull={animator.cullingMode} state0={num}";
			}
			if (c is PlayableDirector playableDirector)
			{
				return string.Format("active={0} enabled={1} state={2} wrap={3} asset='{4}'", active, playableDirector.enabled, playableDirector.state, playableDirector.extrapolationMode, (playableDirector.playableAsset != null) ? playableDirector.playableAsset.name : "null");
			}
			if (c is ReflectionProbe reflectionProbe)
			{
				return $"active={active} enabled={reflectionProbe.enabled} mode={reflectionProbe.mode} refresh={reflectionProbe.refreshMode} slice={reflectionProbe.timeSlicingMode} res={reflectionProbe.resolution}";
			}
			if (c is Camera camera)
			{
				return string.Format("active={0} enabled={1} depth={2:F1} target='{3}' px={4}x{5}", active, camera.enabled, camera.depth, (camera.targetTexture != null) ? camera.targetTexture.name : "screen", camera.pixelWidth, camera.pixelHeight);
			}
			if (c is Light light)
			{
				return $"active={active} enabled={light.enabled} type={light.type} shadows={light.shadows} range={light.range:F1} intensity={light.intensity:F2}";
			}
		}
		catch
		{
		}
		return null;
	}

	private static string ObjectPath(Transform t)
	{
		Stack<string> stack = new Stack<string>();
		Transform transform = t;
		while (transform != null)
		{
			stack.Push(transform.name);
			transform = transform.parent;
		}
		return string.Join("/", stack.ToArray());
	}

	private static void Bump(Dictionary<string, int> d, string k)
	{
		d.TryGetValue(k, out var value);
		d[k] = value + 1;
	}
}
