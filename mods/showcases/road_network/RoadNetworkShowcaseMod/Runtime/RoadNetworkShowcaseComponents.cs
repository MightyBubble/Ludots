using Ludots.Core.Mathematics.FixedPoint;

namespace RoadNetworkShowcaseMod.Runtime
{
    public struct RoadColumnTag
    {
    }

    public struct RoadFortTag
    {
    }

    public struct RoadAiControlledTag
    {
    }

    public struct RoadMoveProfileRef
    {
        public byte PlannerPresetId { get; set; }
        public byte ExecutionPresetId { get; set; }
        public byte PreviewPaletteId { get; set; }
    }

    public struct RoadFortControlState
    {
        public int ControllerTeamId;
        public int CapturingTeamId;
        public float CaptureProgressSeconds;
        public float CaptureDurationSeconds;
        public int CaptureRadiusCm;
    }

    public struct RoadRouteRuntimeState
    {
        public Fix64Vec2 LastProgressPosition;
        public int LastResolvedWaypointIndex;
        public float StallSeconds;
        public short TimeoutCount;
        public byte Initialized;
    }
}
