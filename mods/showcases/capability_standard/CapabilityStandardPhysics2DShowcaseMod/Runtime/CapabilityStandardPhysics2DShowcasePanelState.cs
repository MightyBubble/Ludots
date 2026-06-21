namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal readonly record struct CapabilityStandardPhysics2DShowcasePanelState(
    string Title,
    string LastAction,
    int PhysicsHz,
    int PhysicsMaxSteps,
    string BroadphaseStrategy,
    int BroadphaseCellSizeCm,
    double PhysicsUpdateMs,
    int PotentialPairs,
    int ContactPairs,
    int DynamicBodies,
    int StaticBodies,
    int DirtyStaticBodies,
    int SpawnBatchDynamic,
    int SpawnBatchStatic,
    bool PolygonDrawMode,
    int DrawnPolygonVertices,
    string MaterialSummary,
    string ScaleSummary)
{
    public static CapabilityStandardPhysics2DShowcasePanelState Empty { get; } = new(
        Title: "Physics2D",
        LastAction: "No active Physics2D showcase map.",
        PhysicsHz: 0,
        PhysicsMaxSteps: 0,
        BroadphaseStrategy: "Unavailable",
        BroadphaseCellSizeCm: 0,
        PhysicsUpdateMs: 0d,
        PotentialPairs: 0,
        ContactPairs: 0,
        DynamicBodies: 0,
        StaticBodies: 0,
        DirtyStaticBodies: 0,
        SpawnBatchDynamic: 0,
        SpawnBatchStatic: 0,
        PolygonDrawMode: false,
        DrawnPolygonVertices: 0,
        MaterialSummary: string.Empty,
        ScaleSummary: string.Empty);
}
