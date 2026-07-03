namespace AssociationStressShowcaseMod.Runtime;

public readonly record struct AssociationStressSnapshot(
    string ScaleLabel,
    int AssociationCount,
    int ActiveKnowledgeCount,
    int PhysicalKnowledgeCount,
    int KnowledgeCapacity,
    int CollectionCount,
    int CollectionRowCapacity,
    long LastFrameAllocatedBytes,
    int LastExpiredCount,
    int LastCompactedCount,
    bool PulseEnabled,
    int Tick,
    string Status);
