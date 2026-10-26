using System.Collections.Generic;
using UnityEngine;

namespace Manimal.Terminal;

internal sealed class TerminalWaterAssets : MonoBehaviour
{
	internal readonly List<Texture2D> Textures = new List<Texture2D>();

	internal void ReleaseTextures()
	{
		foreach (Texture2D texture in Textures)
		{
			if ((bool)texture)
			{
				Object.Destroy(texture);
			}
		}
		Textures.Clear();
	}

	private void OnDestroy()
	{
		ReleaseTextures();
	}
}
