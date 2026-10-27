using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Manimal.Terminal;

internal class TerminalTriggerViz : MonoBehaviour
{
	private struct Box
	{
		public Transform T;

		public Vector3 Center;

		public Vector3 Size;

		public Color C;

		public string Label;
	}

	internal static TerminalTriggerViz Instance;

	private bool _wasVisible;

	private readonly List<Box> _boxes = new List<Box>();

	private static Material _mat;

	internal static void ReleaseAfterRaid()
	{
		TerminalTriggerViz instance = Instance;
		Instance = null;
		if (instance) TerminalRaidCleanup.DestroyOwned(instance.gameObject);
		TerminalRaidCleanup.DestroyOwned(_mat);
		_mat = null;
	}

	private static bool Visible
	{
		get
		{
			if (Plugin.ShowSpawnTriggers.Value)
			{
				return TerminalGate.On;
			}
			return false;
		}
	}

	internal static void Ensure()
	{
		if (!(Instance != null))
		{
			GameObject gameObject = new GameObject("Manimal_TerminalTriggerViz");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			Instance = gameObject.AddComponent<TerminalTriggerViz>();
		}
	}

	private void Update()
	{
		bool visible = Visible;
		if (visible != _wasVisible)
		{
			_wasVisible = visible;
			if (visible)
			{
				Rebuild();
			}
			Plugin.Log.LogWarning("[TriggerViz] spawn-trigger overlay " + (visible ? "ON" : "OFF") + (visible ? $" — {_boxes.Count} trigger box(es)" : ""));
		}
	}

	private static Material Mat()
	{
		if (_mat != null)
		{
			return _mat;
		}
		Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
		if (shader == null)
		{
			Plugin.Log.LogWarning("[TriggerViz] no usable shader — overlay unavailable");
			return null;
		}
		_mat = new Material(shader)
		{
			hideFlags = HideFlags.HideAndDontSave
		};
		try
		{
			_mat.SetInt("_SrcBlend", 5);
			_mat.SetInt("_DstBlend", 10);
			_mat.SetInt("_Cull", 0);
			_mat.SetInt("_ZWrite", 0);
			_mat.SetInt("_ZTest", 8);
		}
		catch
		{
		}
		return _mat;
	}

	private static Color ColorFor(string ev)
	{
		if (string.IsNullOrEmpty(ev))
		{
			return new Color(0.5f, 0.5f, 0.5f, 0.7f);
		}
		if (ev.Equals("T4", StringComparison.OrdinalIgnoreCase))
		{
			return new Color(1f, 0.1f, 0.1f, 1f);
		}
		if (ev.StartsWith("TB", StringComparison.OrdinalIgnoreCase))
		{
			return new Color(0.2f, 0.9f, 1f, 0.9f);
		}
		if (ev.StartsWith("T", StringComparison.OrdinalIgnoreCase))
		{
			return new Color(1f, 0.65f, 0.1f, 0.9f);
		}
		return new Color(0.6f, 1f, 0.4f, 0.9f);
	}

	internal void Rebuild()
	{
		_boxes.Clear();
		GameObject gameObject = GameObject.Find("AIPlaceInfoHolder");
		if (gameObject == null)
		{
			Plugin.Log.LogWarning("[TriggerViz] AIPlaceInfoHolder not in scene");
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		BoxCollider[] componentsInChildren = gameObject.GetComponentsInChildren<BoxCollider>(includeInactive: true);
		foreach (BoxCollider boxCollider in componentsInChildren)
		{
			if (boxCollider == null)
			{
				continue;
			}
			TerminalTierEventLogic component = boxCollider.GetComponent<TerminalTierEventLogic>();
			string text = ((component != null) ? component.EventName : null);
			if (text == null)
			{
				TerminalTierEventLogic componentInChildren = boxCollider.GetComponentInChildren<TerminalTierEventLogic>(includeInactive: true);
				if (componentInChildren != null)
				{
					text = componentInChildren.EventName;
				}
			}
			_boxes.Add(new Box
			{
				T = boxCollider.transform,
				Center = boxCollider.center,
				Size = boxCollider.size,
				C = ColorFor(text),
				Label = (string.IsNullOrEmpty(text) ? boxCollider.gameObject.name : (text + "  (" + boxCollider.gameObject.name + ")"))
			});
			Vector3 vector = boxCollider.transform.TransformPoint(boxCollider.center);
			Vector3 vector2 = Vector3.Scale(boxCollider.size, boxCollider.transform.lossyScale);
			stringBuilder.Append(string.Format("\n    {0,-10} '{1}' ", string.IsNullOrEmpty(text) ? "(no event)" : text, boxCollider.gameObject.name) + $"center={vector} size={vector2} isTrigger={boxCollider.isTrigger} enabled={boxCollider.enabled} " + $"active={boxCollider.gameObject.activeInHierarchy}");
		}
		Plugin.Log.LogWarning($"[TriggerViz] {_boxes.Count} trigger box(es):{stringBuilder}");
	}

	private void OnRenderObject()
	{
		if (!Visible || _boxes.Count == 0)
		{
			return;
		}
		Camera cameraRef = TerminalCullingDriver.CameraRef;
		if (cameraRef != null && Camera.current != cameraRef)
		{
			return;
		}
		Material material = Mat();
		if (material == null)
		{
			return;
		}
		material.SetPass(0);
		for (int i = 0; i < _boxes.Count; i++)
		{
			Box box = _boxes[i];
			if (!(box.T == null))
			{
				GL.PushMatrix();
				GL.MultMatrix(box.T.localToWorldMatrix);
				GL.Begin(1);
				GL.Color(box.C);
				Vector3 vector = box.Size * 0.5f;
				Vector3 center = box.Center;
				Vector3 vector2 = center + new Vector3(0f - vector.x, 0f - vector.y, 0f - vector.z);
				Vector3 vector3 = center + new Vector3(vector.x, 0f - vector.y, 0f - vector.z);
				Vector3 vector4 = center + new Vector3(0f - vector.x, vector.y, 0f - vector.z);
				Vector3 vector5 = center + new Vector3(vector.x, vector.y, 0f - vector.z);
				Vector3 vector6 = center + new Vector3(0f - vector.x, 0f - vector.y, vector.z);
				Vector3 vector7 = center + new Vector3(vector.x, 0f - vector.y, vector.z);
				Vector3 vector8 = center + new Vector3(0f - vector.x, vector.y, vector.z);
				Vector3 vector9 = center + new Vector3(vector.x, vector.y, vector.z);
				Line(vector2, vector3);
				Line(vector3, vector7);
				Line(vector7, vector6);
				Line(vector6, vector2);
				Line(vector4, vector5);
				Line(vector5, vector9);
				Line(vector9, vector8);
				Line(vector8, vector4);
				Line(vector2, vector4);
				Line(vector3, vector5);
				Line(vector7, vector9);
				Line(vector6, vector8);
				GL.End();
				GL.PopMatrix();
			}
		}
		static void Line(Vector3 a, Vector3 bb)
		{
			GL.Vertex(a);
			GL.Vertex(bb);
		}
	}

	private void OnGUI()
	{
		if (!Visible || _boxes.Count == 0)
		{
			return;
		}
		Camera camera = ((TerminalCullingDriver.CameraRef != null) ? TerminalCullingDriver.CameraRef : Camera.main);
		if (camera == null)
		{
			return;
		}
		GUIStyle gUIStyle = new GUIStyle(GUI.skin.label)
		{
			fontSize = 13,
			fontStyle = FontStyle.Bold
		};
		for (int i = 0; i < _boxes.Count; i++)
		{
			Box box = _boxes[i];
			if (!(box.T == null))
			{
				Vector3 position = box.T.TransformPoint(box.Center);
				Vector3 vector = camera.WorldToScreenPoint(position);
				if (!(vector.z <= 0f) && !(vector.z > 250f))
				{
					gUIStyle.normal.textColor = box.C;
					GUI.Label(new Rect(vector.x - 90f, (float)Screen.height - vector.y - 9f, 180f, 18f), $"{box.Label}  [{vector.z:F0}m]", gUIStyle);
				}
			}
		}
	}
}
