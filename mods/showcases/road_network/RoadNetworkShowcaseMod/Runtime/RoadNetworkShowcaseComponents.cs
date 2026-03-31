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

    public enum RoadMoveLifecycleState : byte
    {
        None = 0,
        Active = 1,
        NeedsReplan = 2,
        Arrived = 3,
        Failed = 4,
    }

    public enum RoadMoveFailureReason : byte
    {
        None = 0,
        MissingPlan = 1,
        ExecutionUnavailable = 2,
        RefreshRejected = 3,
        TimeoutAbandoned = 4,
        FinalTargetMissing = 5,
        RouteEndedEarly = 6,
    }

    public struct RoadMoveOrderRuntime
    {
        public int ActiveOrderId;
        public short TimeoutCount;
        public short ExecutionGeneration;
        public RoadMoveLifecycleState LifecycleState;
        public RoadMoveFailureReason FailureReason;
    }

    public struct RoadNavPlanRuntime
    {
        public int BoundOrderId;
        public short PlanGeneration;
        public int PointCount;
        public int FinalGoalXcm;
        public int FinalGoalYcm;
        public int CurrentWaypointIndex;
        public Fix64Vec2 LastProgressPosition;
        public int LastResolvedWaypointIndex;
        public float StallSeconds;
        public byte Initialized;
    }

    public struct RoadMoveExecutionIntent
    {
        public Fix64Vec2 Target;
        public float SpeedCmPerSec;
        public float StopRadiusCm;
        public byte HasTarget;
    }
}
