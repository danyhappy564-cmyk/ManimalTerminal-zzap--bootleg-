using EFT;

namespace Manimal.Terminal.Civilian;

internal static class CivilianDoorHelper
{
	public static void CheckAndOpenNearbyDoor(BotOwner bot)
	{
		(bot?.DoorOpener)?.UpdateDoorInteractionStatus();
	}
}
