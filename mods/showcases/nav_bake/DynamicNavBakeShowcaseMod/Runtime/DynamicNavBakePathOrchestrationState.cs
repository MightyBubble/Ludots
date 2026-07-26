namespace DynamicNavBakeShowcaseMod.Runtime;

/// <summary>
/// Honest open-world / RTS path orchestration state for the Dynamic NavMesh bake showcase.
/// </summary>
public enum DynamicNavBakePathOrchestrationState : byte
{
    Idle = 0,
    GlobalCorridorReady = 1,
    WindowRebuilding = 2,
    LocalSegmentReady = 3,
    Arrived = 4,
    Unreachable = 5,
}
