using System;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalLoaded
{
	internal static bool Check()
	{
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene sceneAt = SceneManager.GetSceneAt(i);
			if (sceneAt.isLoaded && sceneAt.name != null && sceneAt.name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
