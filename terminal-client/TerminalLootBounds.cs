using System;
using System.Collections.Generic;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalLootBounds
{
	private sealed class Tracked
	{
		public LootItem Item;

		public Renderer[] Rends;

		public bool Hidden;
	}

	private const float SweepEvery = 4f;

	private const float LootCullHeight = 0.0004f;

	private const int ChecksPerFrame = 64;

	private const int DiscoveriesPerFrame = 32;

	private static readonly long DiscoveryBudgetTicks = Math.Max(1L, Stopwatch.Frequency / 2000);

	private const float ShowHysteresis = 1.06f;

	private static readonly HashSet<int> _done = new HashSet<int>();

	private static readonly List<Tracked> _tracked = new List<Tracked>();

	private static float _next;

	private static int _cursor;

	private static int _fixedRenderers;

	private static int _lodFixed;

	private static int _discoveryCursor;

	private static int _discoveryLimit;

	private static bool _discovering;

	private static bool _radiusMode;

	private static bool _summarised;

	internal static void ReleaseAfterRaid()
	{
		_tracked.Clear();
		_done.Clear();
		_discovering = false;
	}

	internal static void ResetForRaid()
	{
		UnhideAll();
		_done.Clear();
		_next = 0f;
		_cursor = 0;
		_fixedRenderers = 0;
		_lodFixed = 0;
		_radiusMode = false;
		_summarised = false;
		_discovering = false;
		_discoveryCursor = (_discoveryLimit = 0);
	}

	internal static void Tick()
	{
		TickRadiusCull();
		if (!_discovering && Time.time < _next)
		{
			return;
		}
		bool flag = Plugin.LootCullRadius.Value > 0f;
		if (flag != _radiusMode)
		{
			_radiusMode = flag;
			_done.Clear();
			_discovering = false;
			if (!flag)
			{
				UnhideAll();
			}
			Plugin.Log.LogInfo("[LootVis] loot cull radius " + (flag ? (Plugin.LootCullRadius.Value.ToString("0") + "m") : "OFF (LOD bias governs loot again)"));
		}
		DictionaryListHydra<int, LootItem> dictionaryListHydra = Singleton<GameWorld>.Instance?.LootItems;
		if (dictionaryListHydra == null)
		{
			_discovering = false;
			return;
		}
		if (!_discovering)
		{
			_discoveryCursor = 0;
			_discoveryLimit = dictionaryListHydra.Count;
			_discovering = true;
		}
		long timestamp = Stopwatch.GetTimestamp();
		int num = 0;
		while (_discoveryCursor < Math.Min(_discoveryLimit, dictionaryListHydra.Count) && num++ < 32 && Stopwatch.GetTimestamp() - timestamp < DiscoveryBudgetTicks)
		{
			int index = _discoveryCursor++;
			LootItem byIndex;
			try
			{
				byIndex = dictionaryListHydra.GetByIndex(index);
			}
			catch
			{
				continue;
			}
			if (!(byIndex == null) && _done.Add(byIndex.GetInstanceID()))
			{
				try
				{
					Fix(byIndex);
				}
				catch (Exception ex)
				{
					Plugin.Log.LogDebug("[LootVis] '" + byIndex.name + "' failed: " + ex.Message);
				}
			}
		}
		if (_discoveryCursor >= Math.Min(_discoveryLimit, dictionaryListHydra.Count))
		{
			_discovering = false;
			_next = Time.time + 4f;
			if (!_summarised && _done.Count > 0 && Time.time > 30f)
			{
				_summarised = true;
				Plugin.Log.LogInfo($"[LootVis] {_done.Count} loot item(s) checked — {_lodFixed} LODGroup cull height(s) lowered " + $"(compensating lodBias {QualitySettings.lodBias:0.00}), {_fixedRenderers} skinned renderer(s) set to update offscreen" + (_radiusMode ? $", culling past {Plugin.LootCullRadius.Value:0}m" : ""));
			}
		}
	}

	private static void Fix(LootItem item)
	{
		if (item is Corpse)
		{
			return;
		}
		Renderer[] componentsInChildren = item.GetComponentsInChildren<Renderer>(includeInactive: true);
		if (_radiusMode)
		{
			LODGroup[] componentsInChildren2 = item.GetComponentsInChildren<LODGroup>(includeInactive: true);
			foreach (LODGroup lODGroup in componentsInChildren2)
			{
				if (lODGroup == null)
				{
					continue;
				}
				LOD[] lODs = lODGroup.GetLODs();
				if (lODs != null && lODs.Length != 0)
				{
					int num = lODs.Length - 1;
					float b = ((num > 0) ? (lODs[num - 1].screenRelativeTransitionHeight - 0.001f) : 1f);
					float num2 = Mathf.Clamp(0.0004f, 0f, Mathf.Max(0f, b));
					if (!(lODs[num].screenRelativeTransitionHeight <= num2))
					{
						lODs[num].screenRelativeTransitionHeight = num2;
						lODGroup.SetLODs(lODs);
						_lodFixed++;
					}
				}
			}
			if (componentsInChildren != null && componentsInChildren.Length != 0)
			{
				_tracked.Add(new Tracked
				{
					Item = item,
					Rends = componentsInChildren,
					Hidden = false
				});
			}
		}
		Renderer[] array = componentsInChildren;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] is SkinnedMeshRenderer { updateWhenOffscreen: false } skinnedMeshRenderer)
			{
				skinnedMeshRenderer.updateWhenOffscreen = true;
				_fixedRenderers++;
			}
		}
	}

	private static void TickRadiusCull()
	{
		if (!_radiusMode || _tracked.Count == 0)
		{
			return;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (cameraRef == null)
		{
			return;
		}
		float value = Plugin.LootCullRadius.Value;
		float num = value * value;
		float num2 = value / 1.06f * (value / 1.06f);
		Vector3 position = cameraRef.transform.position;
		int num3 = Mathf.Min(64, _tracked.Count);
		for (int i = 0; i < num3; i++)
		{
			if (_tracked.Count <= 0)
			{
				break;
			}
			if (_cursor >= _tracked.Count)
			{
				_cursor = 0;
			}
			Tracked tracked = _tracked[_cursor];
			if (tracked == null || tracked.Item == null)
			{
				_tracked.RemoveAt(_cursor);
				continue;
			}
			_cursor++;
			float sqrMagnitude = (tracked.Item.transform.position - position).sqrMagnitude;
			bool flag = (tracked.Hidden ? (sqrMagnitude > num2) : (sqrMagnitude > num));
			if (flag == tracked.Hidden)
			{
				continue;
			}
			tracked.Hidden = flag;
			for (int j = 0; j < tracked.Rends.Length; j++)
			{
				if (tracked.Rends[j] != null)
				{
					tracked.Rends[j].forceRenderingOff = flag;
				}
			}
		}
	}

	private static void UnhideAll()
	{
		foreach (Tracked item in _tracked)
		{
			if (item?.Rends == null)
			{
				continue;
			}
			for (int i = 0; i < item.Rends.Length; i++)
			{
				if (item.Rends[i] != null)
				{
					item.Rends[i].forceRenderingOff = false;
				}
			}
			item.Hidden = false;
		}
		_tracked.Clear();
		_cursor = 0;
	}
}
