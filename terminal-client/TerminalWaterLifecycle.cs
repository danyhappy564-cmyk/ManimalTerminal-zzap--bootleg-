using System;

namespace Manimal.Terminal;

internal static class TerminalWaterLifecycle
{
	internal const int MaxStageAttempts = 8;

	internal const int MaxReflectionCaptureRetries = 3;

	internal const float ReflectionRetryBaseDelaySeconds = 0.5f;

	internal const float ReflectionRetryMaxDelaySeconds = 4f;

	internal static bool CanAttemptStage(int failures)
	{
		return failures < 8;
	}

	internal static bool ShouldRetryReflection(int retryCount)
	{
		if (retryCount > 0)
		{
			return retryCount <= 3;
		}
		return false;
	}

	internal static float ReflectionRetryDelay(int retryCount)
	{
		if (retryCount <= 0)
		{
			return 0f;
		}
		int num = Math.Min(retryCount - 1, 3);
		return Math.Min(0.5f * (float)(1 << num), 4f);
	}

	internal static bool IsRetryDue(float now, float nextAttemptAt)
	{
		return now >= nextAttemptAt;
	}
}
