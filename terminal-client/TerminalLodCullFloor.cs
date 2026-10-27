using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using EFT.EnvironmentEffect;
using EFT.Interactive;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalLodCullFloor
{
	private class Cell
	{
		public readonly List<int> Members = new List<int>();

		public float Applied = float.NaN;
	}

	private static LODGroup[] _g;

	private static float[] _orig;

	private static bool[] _neverCull;

	private static Dictionary<Vector3Int, Cell> _cells;

	private static readonly Queue<Cell> _dirty = new Queue<Cell>();

	private static readonly Queue<float> _dirtyWant = new Queue<float>();

	private static int _n;

	private static bool _built;

	private static float _cellSize = 30f;

	private static Vector3Int _camCell = new Vector3Int(int.MinValue, 0, 0);

	private static float _lastNear = float.NaN;

	private static float _lastFar = float.NaN;

	private static float _lastRadius = float.NaN;

	private static bool _wasIndoor;

	private static Cell _draining;

	private static float _drainWant;

	private static int _drainIdx;

	private static readonly Stopwatch _sw = new Stopwatch();

	internal static void ReleaseAfterRaid()
	{
		_g = null;
		_orig = null;
		_neverCull = null;
		_cells = null;
		_dirty.Clear();
		_dirtyWant.Clear();
		_draining = null;
		_n = 0;
		_built = false;
	}

	private static Vector3Int Key(Vector3 p)
	{
		return new Vector3Int(Mathf.RoundToInt(p.x / _cellSize), Mathf.RoundToInt(p.y / _cellSize), Mathf.RoundToInt(p.z / _cellSize));
	}

	internal static IEnumerator Apply()
	{
		_built = false;
		_n = 0;
		_cellSize = Mathf.Max(5f, Plugin.LodCellSize.Value);
		_cells = new Dictionary<Vector3Int, Cell>(8192);
		_dirty.Clear();
		_dirtyWant.Clear();
		_draining = null;
		_camCell = new Vector3Int(int.MinValue, 0, 0);
		_lastNear = (_lastFar = (_lastRadius = float.NaN));
		LODGroup[] array = UnityEngine.Object.FindObjectsOfType<LODGroup>();
		_g = new LODGroup[array.Length];
		_orig = new float[array.Length];
		_neverCull = new bool[array.Length];
		int[] histo = new int[5];
		int skippedLoot = 0;
		Stopwatch sw = Stopwatch.StartNew();
		LODGroup[] array2 = array;
		foreach (LODGroup lODGroup in array2)
		{
			if (lODGroup == null)
			{
				continue;
			}
			string name = lODGroup.gameObject.scene.name;
			if (name == null || !name.StartsWith("Terminal"))
			{
				continue;
			}
			LOD[] lODs = lODGroup.GetLODs();
			if (lODs == null || lODs.Length == 0)
			{
				continue;
			}
			if (lODGroup.GetComponentInParent<LootItem>() != null || lODGroup.GetComponentInParent<LootableContainer>() != null)
			{
				skippedLoot++;
				continue;
			}
			histo[Math.Min(lODs.Length, histo.Length - 1)]++;
			_g[_n] = lODGroup;
			_orig[_n] = lODs[^1].screenRelativeTransitionHeight;
			_neverCull[_n] = IsMarineStationOpenShell(lODGroup);
			Vector3 p = lODGroup.transform.position;
			Renderer[] renderers = lODs[0].renderers;
			if (renderers != null)
			{
				for (int j = 0; j < renderers.Length; j++)
				{
					if (renderers[j] != null)
					{
						p = renderers[j].bounds.center;
						break;
					}
				}
			}
			Vector3Int key = Key(p);
			if (!_cells.TryGetValue(key, out var value))
			{
				value = (_cells[key] = new Cell());
			}
			value.Members.Add(_n);
			_n++;
			if (sw.ElapsedMilliseconds > 4)
			{
				yield return null;
				sw.Restart();
			}
		}
		_built = true;
		int num = 0;
		for (int k = 0; k < _n; k++)
		{
			if (_neverCull[k])
			{
				num++;
			}
		}
		Plugin.Log.LogInfo($"[LodCullFloor] {_n} map LODGroups in {_cells.Count} cells of {_cellSize:0}m (cell-tiered culling live) | " + $"lod counts: 1-lod={histo[1]} 2-lod={histo[2]} 3-lod={histo[3]} 4+lod={histo[4]}" + ((skippedLoot > 0) ? $" | {skippedLoot} loot group(s) left alone" : "") + ((num > 0) ? $" | {num} Marine Station shell group(s) protected" : ""));
	}

	private static bool IsMarineStationOpenShell(LODGroup g)
	{
		string name = g.gameObject.scene.name;
		string name2 = g.name;
		if (name == "Terminal_Area_04_MarineStation")
		{
			return name2 == "Marine_Station_01";
		}
		if (name == "Terminal_Area_04_MarineStation_Indoor")
		{
			if (!(name2 == "Marine_Station_01_INDOOR"))
			{
				return name2 == "Marine_Station_01_FAKEINDOOR";
			}
			return true;
		}
		return false;
	}

	internal static void Tick(Vector3 cam)
	{
		if (!_built)
		{
			return;
		}
		float value = Plugin.LodCullNearFloor.Value;
		float value2 = Plugin.LodCullFloor.Value;
		bool flag = false;
		try
		{
			EnvironmentManager instance = EnvironmentManager.Instance;
			if (instance != null)
			{
				flag = instance.Environment == EnvironmentType.Indoor;
			}
		}
		catch
		{
		}
		float num = (flag ? Plugin.LodCullNearRadiusIndoor.Value : Plugin.LodCullNearRadius.Value);
		if (flag != _wasIndoor)
		{
			_wasIndoor = flag;
			Plugin.Log.LogInfo(string.Format("[LodCullFloor] {0} — active near radius {1:0}m ", flag ? "INDOOR" : "OUTDOOR", num) + "(" + (flag ? "LodCullNearRadiusIndoor" : "LodCullNearRadius") + ")");
		}
		Vector3Int vector3Int = Key(cam);
		if (vector3Int != _camCell || value != _lastNear || value2 != _lastFar || num != _lastRadius)
		{
			_camCell = vector3Int;
			_lastNear = value;
			_lastFar = value2;
			_lastRadius = num;
			float num2 = num * num;
			float num3 = _cellSize * 0.5f;
			foreach (KeyValuePair<Vector3Int, Cell> cell in _cells)
			{
				float num4 = Mathf.Max(0f, Mathf.Abs((float)cell.Key.x * _cellSize - cam.x) - num3);
				float num5 = Mathf.Max(0f, Mathf.Abs((float)cell.Key.y * _cellSize - cam.y) - num3);
				float num6 = Mathf.Max(0f, Mathf.Abs((float)cell.Key.z * _cellSize - cam.z) - num3);
				float num7 = ((num4 * num4 + num5 * num5 + num6 * num6 <= num2) ? value : value2);
				if (!Mathf.Approximately(num7, cell.Value.Applied))
				{
					cell.Value.Applied = num7;
					_dirty.Enqueue(cell.Value);
					_dirtyWant.Enqueue(num7);
				}
			}
		}
		_sw.Restart();
		while (_sw.Elapsed.TotalMilliseconds < 1.0)
		{
			if (_draining == null)
			{
				if (_dirty.Count == 0)
				{
					break;
				}
				_draining = _dirty.Dequeue();
				_drainWant = _dirtyWant.Dequeue();
				_drainIdx = 0;
				if (!Mathf.Approximately(_drainWant, _draining.Applied))
				{
					_draining = null;
					continue;
				}
			}
			List<int> members = _draining.Members;
			while (_drainIdx < members.Count && _sw.Elapsed.TotalMilliseconds < 1.0)
			{
				int num8 = members[_drainIdx++];
				LODGroup lODGroup = _g[num8];
				if (lODGroup == null)
				{
					continue;
				}
				float num9;
				if (_neverCull[num8])
				{
					num9 = 0f;
				}
				else
				{
					num9 = ((_drainWant < 0f) ? _orig[num8] : Mathf.Min(_orig[num8], _drainWant));
				}
				LOD[] lODs = lODGroup.GetLODs();
				if (lODs != null && lODs.Length != 0)
				{
					int num10 = lODs.Length - 1;
					float num11 = ((num10 > 0) ? (lODs[num10 - 1].screenRelativeTransitionHeight - 0.001f) : 1f);
					if (num9 > num11)
					{
						num9 = Mathf.Max(0f, num11);
					}
					if (Mathf.Abs(lODs[num10].screenRelativeTransitionHeight - num9) > 0.0005f)
					{
						lODs[num10].screenRelativeTransitionHeight = num9;
						lODGroup.SetLODs(lODs);
					}
				}
			}
			if (_drainIdx >= members.Count)
			{
				_draining = null;
			}
		}
	}
}
