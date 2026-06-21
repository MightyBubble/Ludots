namespace FogVisionDecayShowcaseMod.Runtime;

public readonly record struct FogVisionDecaySnapshot(
    int Tick,
    bool PatrolEnabled,
    int TargetCount,
    int LiveCount,
    int KnownCount,
    int ExpiredCount,
    int SeenEverCount,
    int ActiveRecordCount,
    int PhysicalRecordCount,
    int RecordCapacity,
    int ConfiguredCapacityCeiling,
    int LastExpiredCount,
    int LastCompactedCount,
    long LastFrameAllocatedBytes,
    string PatrolLabel,
    string Status);
