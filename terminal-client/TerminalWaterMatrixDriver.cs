using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

[DefaultExecutionOrder(10000)]
internal sealed class TerminalWaterMatrixDriver : MonoBehaviour
{
	internal WaterForSSR Water;

	private Material _ownedMaterial;

	private static readonly FieldInfo MaterialField = AccessTools.Field(typeof(WaterForSSR), "_waterMaterial");

	internal bool MaterialReady
	{
		get
		{
			if ((bool)_ownedMaterial && (bool)_ownedMaterial.shader)
			{
				return _ownedMaterial.shader.isSupported;
			}
			return false;
		}
	}

	internal void CaptureMaterial()
	{
		if ((bool)Water)
		{
			_ownedMaterial = MaterialField?.GetValue(Water) as Material;
		}
	}

	internal void ReleaseMaterial()
	{
		if (!_ownedMaterial)
		{
			CaptureMaterial();
		}
		if ((bool)_ownedMaterial)
		{
			if ((bool)Water && MaterialField?.GetValue(Water) == _ownedMaterial)
			{
				MaterialField.SetValue(Water, null);
			}
			UnityEngine.Object.Destroy(_ownedMaterial);
			_ownedMaterial = null;
		}
	}

	private void LateUpdate()
	{
		try
		{
			if ((bool)Water)
			{
				Water.InitWaterMatrices();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Water] per-plane matrix refresh stopped: " + ex.Message);
			enabled = false;
		}
	}

	private void OnDestroy()
	{
		ReleaseMaterial();
	}
}
