namespace FogMobaTerrainShowcaseMod.Runtime;

public readonly record struct FogMobaTerrainSnapshot(
    int Tick,
    int XCm,
    int YCm,
    int FacingDegrees,
    string Shape,
    int RangeCm,
    bool RulesEnabled,
    bool MemoryEnabled,
    int VisibleCells,
    int ExploredCells,
    int UnseenCells,
    int WallCells,
    int BrushCells,
    string Status);
