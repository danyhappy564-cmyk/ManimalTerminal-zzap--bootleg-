using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: owns only the visibility flags captured by Ghost, never the player's GO.
internal sealed class TerminalGhostBody
{
	private readonly List<Renderer> _renderers = new List<Renderer>(128);
	private readonly List<bool> _renderOff = new List<bool>(128);
	private readonly List<Light> _lights = new List<Light>(16);
	private readonly List<bool> _lightEnabled = new List<bool>(16);
	private readonly List<Renderer> _scratchRenderers = new List<Renderer>(128);
	private readonly List<Light> _scratchLights = new List<Light>(16);
	private bool _owned;

	internal void Hide(Player player)
	{
		_owned = true; // partial capture is also recoverable if a component throws
		Capture(player.Transform.Original);
		// A weapon can be parented outside PlayerBody; cover the whole hands tree too.
		if (player.HandsController != null)
		{
			Capture(player.HandsController.transform);
			Capture(player.HandsController.WeaponRoot);
		}
		Enforce();
	}

	internal void Capture(Transform root)
	{
		if (!_owned || !root) return;
		_scratchRenderers.Clear();
		root.GetComponentsInChildren(true, _scratchRenderers);
		for (int i = 0; i < _scratchRenderers.Count; i++)
		{
			Renderer renderer = _scratchRenderers[i];
			if (!renderer || _renderers.Contains(renderer)) continue;
			_renderers.Add(renderer);
			_renderOff.Add(renderer.forceRenderingOff);
		}
		_scratchLights.Clear();
		root.GetComponentsInChildren(true, _scratchLights);
		for (int i = 0; i < _scratchLights.Count; i++)
		{
			Light light = _scratchLights[i];
			if (!light || _lights.Contains(light)) continue;
			_lights.Add(light);
			_lightEnabled.Add(light.enabled);
		}
	}

	internal void Enforce()
	{
		if (!_owned) return;
		// Keep GO/colliders/health alive for real damage and death. enabled is also
		// written by Terminal culling; forceRenderingOff leaves that owner intact.
		// StageWait/recycler MUST wake Ghost before capturing/repairing these flags.
		for (int i = 0; i < _renderers.Count; i++)
			if (_renderers[i]) _renderers[i].forceRenderingOff = true;
		for (int i = 0; i < _lights.Count; i++)
			if (_lights[i]) _lights[i].enabled = false;
	}

	internal void ReleaseWeapon(Transform root)
	{
		// Pool return must not carry our flags into another player's weapon.
		for (int i = _renderers.Count - 1; i >= 0; i--)
		{
			Renderer renderer = _renderers[i];
			if (renderer && renderer.transform.IsChildOf(root))
			{
				renderer.forceRenderingOff = _renderOff[i];
				_renderers.RemoveAt(i);
				_renderOff.RemoveAt(i);
			}
		}
		for (int i = _lights.Count - 1; i >= 0; i--)
		{
			Light light = _lights[i];
			if (light && light.transform.IsChildOf(root))
			{
				light.enabled = _lightEnabled[i];
				_lights.RemoveAt(i);
				_lightEnabled.RemoveAt(i);
			}
		}
	}

	internal void Restore()
	{
		if (!_owned) return;
		Exception failure = null;
		for (int i = 0; i < _renderers.Count; i++)
		{
			try { if (_renderers[i]) _renderers[i].forceRenderingOff = _renderOff[i]; }
			catch (Exception ex) { if (failure == null) failure = ex; }
		}
		for (int i = 0; i < _lights.Count; i++)
		{
			try { if (_lights[i]) _lights[i].enabled = _lightEnabled[i]; }
			catch (Exception ex) { if (failure == null) failure = ex; }
		}
		_renderers.Clear();
		_renderOff.Clear();
		_lights.Clear();
		_lightEnabled.Clear();
		_scratchRenderers.Clear();
		_scratchLights.Clear();
		_owned = false;
		if (failure != null) throw failure;
	}
}
