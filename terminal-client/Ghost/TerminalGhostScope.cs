using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

// Cached human aim cones. No component discovery/reflection in the scan.
internal sealed class TerminalGhostScope
{
	private struct Cone { internal Vector3 Origin, Forward; internal float RangeSquared, Cosine; }
	private readonly Cone[] _cones = new Cone[TerminalGhostDirector.MaxHumans];
	private int _count;
	private float _nextRead;
	private bool _failed;

	internal void Capture(List<Player> humans, float wakeDistance)
	{
		if (!Plugin.GhostScopeWake.Value || _failed) { _count = 0; return; }
		if (Time.time < _nextRead) return;
		_nextRead = Time.time + 0.2f;
		_count = 0;
		try
		{
			for (int i = 0; i < humans.Count && i < _cones.Length; i++)
				if (ReadCone(humans[i], wakeDistance, out Cone cone)) _cones[_count++] = cone;
		}
		catch (Exception ex)
		{
			_count = 0;
			_failed = true;
			Plugin.Log.LogWarning("[GhostScope] disabled for this raid; zoom access failed: " + ex.Message);
		}
	}

	private static bool ReadCone(Player player, float wakeDistance, out Cone cone)
	{
		cone = default;
		var animation = player.ProceduralWeaponAnimation;
		if (animation == null || !animation.IsAiming || animation.ScopeAimTransforms.Count == 0) return false;
		var sight = animation.CurrentScope;
		if (sight == null || !sight.IsOptic || sight.Mod == null) return false;
		float zoom = sight.Mod.GetCurrentOpticZoom();
		var adjustable = sight.Mod.AdjustableOpticData;
		if (adjustable != null && adjustable.IsAdjustableOptic)
		{
			// ScopeZoomValue is a field of view in degrees, NOT magnification (EFT
			// ScopeZoomHandler.Init/Update). Anchor its tangent ratio to the low zoom.
			float wideFov = adjustable.MinMaxFov.x;
			float currentFov = sight.Mod.ScopeZoomValue;
			if (currentFov <= 0f) currentFov = wideFov;
			float lowZoom = sight.Mod.GetMinOpticZoom(), highZoom = sight.Mod.GetMaxOpticZoom();
			if (wideFov <= 0f || wideFov >= 179f || currentFov <= 0f || currentFov >= 179f || lowZoom <= 0f || highZoom < lowZoom)
				return false;
			zoom = Mathf.Clamp(lowZoom * Mathf.Tan(wideFov * 0.5f * Mathf.Deg2Rad)
				/ Mathf.Tan(currentFov * 0.5f * Mathf.Deg2Rad), lowZoom, highZoom);
		}
		if (float.IsNaN(zoom) || float.IsInfinity(zoom) || zoom <= 1f) return false;
		float range = Mathf.Min(wakeDistance * zoom, Plugin.GhostScopeMaxDistance.Value);
		cone.Origin = player.CameraPosition ? player.CameraPosition.position : player.Transform.Original.position + Vector3.up * 1.5f;
		cone.Forward = player.LookDirection.normalized;
		cone.RangeSquared = range * range;
		cone.Cosine = Mathf.Cos(Mathf.Clamp(5f / Mathf.Sqrt(zoom), 0.5f, 5f) * Mathf.Deg2Rad);
		return true;
	}

	internal bool Contains(Vector3 position, float bodyHeight = 1f)
	{
		if (!Plugin.GhostScopeWake.Value || _failed) return false;
		for (int i = 0; i < _count && i < _cones.Length; i++)
		{
			Cone cone = _cones[i];
			Vector3 offset = position + Vector3.up * bodyHeight - cone.Origin;
			float distanceSq = offset.sqrMagnitude;
			if (distanceSq <= cone.RangeSquared && distanceSq > 0.0001f
				&& Vector3.Dot(offset, cone.Forward) >= Mathf.Sqrt(distanceSq) * cone.Cosine) return true;
		}
		return false;
	}
}
