namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Per-source-span walkability outcome from slope and vertical-clearance classification.
    /// </summary>
    public enum LayeredSpanWalkabilityStatus : byte
    {
        SolidOnly = 0,
        DegenerateNormal = 1,
        SlopeRejected = 2,
        ClearanceRejected = 3,
        Walkable = 4,
        ObstacleBlocked = 5
    }
}
