using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

internal static class TerminalShoreAudio
{
	private static TerminalShoreAudioLoader _instance;

	private static bool _assetsReady;

	internal static bool AssetsReady => _assetsReady;

	internal static void ResetForRaid()
	{
		if ((bool)_instance)
		{
			Object.Destroy(_instance.gameObject);
		}
		_instance = null;
		_assetsReady = false;
		TerminalAcoustics.ResetRecoveredAudioForRaid();
	}

	internal static void TryStage()
	{
		if (!_instance && TerminalGate.On)
		{
			Scene sceneByName = SceneManager.GetSceneByName("Terminal_Sound");
			if (sceneByName.IsValid() && sceneByName.isLoaded)
			{
				GameObject gameObject = new GameObject("Terminal_ShoreAudioAssets");
				SceneManager.MoveGameObjectToScene(gameObject, sceneByName);
				_instance = gameObject.AddComponent<TerminalShoreAudioLoader>();
			}
		}
	}

	internal static void MarkReady()
	{
		_assetsReady = true;
	}

	internal static void MarkReloading()
	{
		_assetsReady = false;
	}
}
