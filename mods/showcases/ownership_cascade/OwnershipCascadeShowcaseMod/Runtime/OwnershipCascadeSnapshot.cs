namespace OwnershipCascadeShowcaseMod.Runtime;

public sealed record OwnershipCascadeSnapshot(
    string CityOwner,
    string GarrisonOwner,
    string WarehouseOwner,
    string ProductionOwner,
    string Status,
    int OwnsTypeId,
    int CityIncomingCount,
    int GarrisonIncomingCount,
    int WarehouseIncomingCount,
    int ProductionIncomingCount);
