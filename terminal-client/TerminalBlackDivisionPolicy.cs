namespace Manimal.Terminal;

internal static class TerminalBlackDivisionPolicy
{
	internal static bool CanUse(int currentOwnerId, int placeConnectionGroupId, int ownerId, int ownerConnectionGroupId, bool checkZones, bool hasMatchingZone)
	{
		if (currentOwnerId > 0)
		{
			return false;
		}
		if (placeConnectionGroupId != ownerConnectionGroupId)
		{
			return false;
		}
		if (!checkZones)
		{
			return true;
		}
		return hasMatchingZone;
	}

	internal static bool TryClaim(ref int currentOwnerId, int placeConnectionGroupId, int ownerId, int ownerConnectionGroupId, bool checkZones, bool hasMatchingZone)
	{
		if (!CanUse(currentOwnerId, placeConnectionGroupId, ownerId, ownerConnectionGroupId, checkZones, hasMatchingZone))
		{
			return false;
		}
		currentOwnerId = ownerId;
		return true;
	}

	internal static bool HaveOwner(int ownerId)
	{
		return ownerId > 0;
	}

	internal static int CoverAreaId(int areaId)
	{
		return areaId;
	}
}
