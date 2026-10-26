using EFT;

namespace Manimal.Terminal.Civilian;

internal static class CivilianConstants
{
	public const int WildSpawnTypeValue = 776701;

	public const float ThreatDetectionRadius = 25f;

	public const float ThreatMemorySeconds = 8f;

	public const float HomeZoneRadius = 30f;

	public const float CowerDurationSeconds = 5f;

	public const float GunshotHearingRadius = 45f;

	public const float GunshotMemorySeconds = 6f;

	public static bool IsCivilian(WildSpawnType type)
	{
		return type == (WildSpawnType)776701;
	}
}
