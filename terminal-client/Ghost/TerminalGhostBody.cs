using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: owns only the visibility flags captured by Ghost, never the player's GO.
internal sealed class TerminalGhostBody
{
	private const int MaxRenderers = 512, MaxLights = 64;
	private readonly List<Renderer> _renderers = new List<Renderer>(128);
	private readonly List<bool> _renderOff = new List<bool>(128);
	private readonly List<Light> _lights = new List<Light>(16);
	private readonly List<bool> _lightEnabled = new List<bool>(16);
	private readonly List<Renderer> _scratchRenderers = new List<Renderer>(128);
	private readonly List<Light> _scratchLights = new List<Light>(16);
	private bool _owned;
	private Player _player;

	// zzap 10/28: EFT's own body culler (OfflinePlayerCulling, LocalPlayer.botPlayerCulling)
	// writes PlayerBody forceRenderingOff = !IsVisible, but ONLY when its distance state
	// changes (and clears it on death). A far bot is already culled when Ghost captures,
	// so Restore put back "off" after the culler had flipped to visible: RUAF invisible
	// at 3m until death. And a culler flip while dormant showed a frozen ghost body.
	// The culler owns the body; after Restore it re-applies, while dormant Ghost re-hides.
	private static readonly AccessTools.FieldRef<LocalPlayer, OfflinePlayerCulling> BotCulling =
		AccessTools.FieldRefAccess<LocalPlayer, OfflinePlayerCulling>("botPlayerCulling");

	internal static void Reapply(Player player)
	{
		if (!(player is LocalPlayer local) || !local) return;
		OfflinePlayerCulling culling = BotCulling(local);
		if (culling != null && culling._player != null) culling.ApplyVisibleState();
	}

	internal void Hide(Player player)
	{
		_owned = true; // partial capture is also recoverable if a component throws
		_player = player;
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
		if (_scratchRenderers.Count > MaxRenderers) throw new InvalidOperationException("Ghost renderer capture limit");
		for (int i = 0; i < _scratchRenderers.Count && i < MaxRenderers; i++)
		{
			Renderer renderer = _scratchRenderers[i];
			if (!renderer || _renderers.Contains(renderer)) continue;
			if (_renderers.Count >= MaxRenderers) throw new InvalidOperationException("Ghost renderer ownership limit");
			_renderers.Add(renderer);
			_renderOff.Add(renderer.forceRenderingOff);
		}
		_scratchLights.Clear();
		root.GetComponentsInChildren(true, _scratchLights);
		if (_scratchLights.Count > MaxLights) throw new InvalidOperationException("Ghost light capture limit");
		for (int i = 0; i < _scratchLights.Count && i < MaxLights; i++)
		{
			Light light = _scratchLights[i];
			if (!light || _lights.Contains(light)) continue;
			if (_lights.Count >= MaxLights) throw new InvalidOperationException("Ghost light ownership limit");
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
		for (int i = 0; i < _renderers.Count && i < MaxRenderers; i++)
			if (_renderers[i]) _renderers[i].forceRenderingOff = true;
		for (int i = 0; i < _lights.Count && i < MaxLights; i++)
			if (_lights[i]) _lights[i].enabled = false;
	}

	internal void ReleaseWeapon(Transform root)
	{
		// Pool return must not carry our flags into another player's weapon.
		for (int i = Math.Min(_renderers.Count, MaxRenderers) - 1; i >= 0; i--)
		{
			Renderer renderer = _renderers[i];
			if (renderer && renderer.transform.IsChildOf(root))
			{
				renderer.forceRenderingOff = _renderOff[i];
				_renderers.RemoveAt(i);
				_renderOff.RemoveAt(i);
			}
		}
		for (int i = Math.Min(_lights.Count, MaxLights) - 1; i >= 0; i--)
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
		for (int i = 0; i < _renderers.Count && i < MaxRenderers; i++)
		{
			try { if (_renderers[i]) _renderers[i].forceRenderingOff = _renderOff[i]; }
			catch (Exception ex) { if (failure == null) failure = ex; }
		}
		for (int i = 0; i < _lights.Count && i < MaxLights; i++)
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
		Player player = _player;
		_player = null;
		try { Reapply(player); }
		catch (Exception ex) { if (failure == null) failure = ex; }
		if (failure != null) throw failure;
	}
}
