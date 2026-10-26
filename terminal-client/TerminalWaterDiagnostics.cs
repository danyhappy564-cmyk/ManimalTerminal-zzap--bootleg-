using System;
using UnityEngine;
using UnityEngine.Rendering;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalWaterDiagnostics
{
	internal static void Capture(Camera camera, AmbientLight source, RenderTexture cube)
	{
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		if (!camera || !source || !cube)
		{
			return;
		}
		CommandBuffer[] commandBuffers = camera.GetCommandBuffers(CameraEvent.BeforeReflections);
		string arg = string.Join(",", ValueEnumerableExtensions.ToArray<CommandBuffer, string>(ValueEnumerableExtensions.Select<CommandBuffer, string>(ValueEnumerable.AsValueEnumerable<CommandBuffer>(commandBuffers), (Func<CommandBuffer, string>)((CommandBuffer buffer) => buffer.name))));
		Vector4 globalVector = Shader.GetGlobalVector("_EFT_Ambient");
		Camera component = source.GetComponent<Camera>();
		Plugin.Log.LogInfo("[WaterDiag] liveCamera='" + camera.name + "' tag=" + camera.tag + " " + $"path={camera.actualRenderingPath} HDR={camera.allowHDR} size={camera.pixelWidth}x{camera.pixelHeight} " + $"BeforeReflections=[{arg}] ambient={globalVector} " + string.Format("bottomShade={0:F3} ", Shader.GetGlobalFloat("_ReflectionBottomShade")) + string.Format("probeBound={0}", Shader.GetGlobalTexture("_MyGlobalReflectionProbe") == cube));
		if ((bool)component)
		{
			Plugin.Log.LogInfo($"[WaterDiag] captureCamera clear={component.clearFlags} " + $"background={component.backgroundColor} near={component.nearClipPlane:F3} " + $"far={component.farClipPlane:F3} mask=0x{component.cullingMask:X} " + $"cube={cube.width}px/{cube.format} mips={cube.mipmapCount}");
		}
		if (!SystemInfo.supportsAsyncGPUReadback)
		{
			Plugin.Log.LogInfo("[WaterDiag] asynchronous probe readback unavailable on this device");
			return;
		}
		int mip = Math.Min(3, cube.mipmapCount - 1);
		try
		{
			AsyncGPUReadback.Request(cube, mip, TextureFormat.RGBAFloat, (AsyncGPUReadbackRequest request) =>
			{
				//IL_005d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0081: Unknown result type (might be due to invalid IL or missing references)
				//IL_0091: Unknown result type (might be due to invalid IL or missing references)
				//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
				try
				{
					if (TerminalGate.On && (bool)source && (bool)cube)
					{
						if (request.hasError)
						{
							Plugin.Log.LogInfo("[WaterDiag] asynchronous probe readback failed; no pixel conclusion");
						}
						else
						{
							for (int i = 0; i < request.layerCount; i++)
							{
								Color[] array = request.GetData<Color>(i).ToArray();
								Color[] array2 = ValueEnumerableExtensions.ToArray<Color>(ValueEnumerableExtensions.Where<Color>(ValueEnumerable.AsValueEnumerable<Color>(array), (Func<Color, bool>)((Color color) => IsFinite(color.r) && IsFinite(color.g) && IsFinite(color.b))));
								double num = ((array2.Length == 0) ? 0.0 : ValueEnumerableExtensions.Average<FromArray<Color>, Color, double>(ValueEnumerable.AsValueEnumerable<Color>(array2), (Func<Color, double>)((Color color) => (double)(color.r + color.g + color.b) / 3.0)));
								float num2 = ((array2.Length == 0) ? 0f : ValueEnumerableExtensions.Max<FromArray<Color>, Color, float>(ValueEnumerable.AsValueEnumerable<Color>(array2), (Func<Color, float>)((Color color) => Mathf.Max(color.r, Mathf.Max(color.g, color.b)))));
								Plugin.Log.LogInfo($"[WaterDiag] probe mip={mip} layer={i}/{request.layerCount} " + $"pixels={array.Length} meanRGB={num:F5} " + $"peakRGB={num2:F5} nonFinite={array.Length - array2.Length}");
							}
						}
					}
				}
				catch (Exception ex2)
				{
					Plugin.Log.LogInfo("[WaterDiag] probe samples unavailable: " + ex2.Message);
				}
			});
		}
		catch (Exception ex)
		{
			Plugin.Log.LogInfo("[WaterDiag] probe readback unavailable: " + ex.Message);
		}
	}

	private static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}
}
