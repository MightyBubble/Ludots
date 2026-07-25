using System.Numerics;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal enum Physics3DShowcaseScene : byte
{
    ScannerRange = 1,
    MaterialHill = 2,
    PlatformStation = 3,
    WindTunnel = 4,
    TraversalCourse = 5,
    WheelLab = 6,
    RagdollLab = 7,
    ConstraintForge = 8,
    ReplayTheater = 9,
    ScaleCity = 10
}

internal enum Physics3DShowcaseCommandKind : byte
{
    SelectScene = 1,
    Reset = 2,
    TogglePause = 3,
    SingleStep = 4,
    Impact = 5,
    SetBenchmarkBodies = 6,
    StartReplayComparison = 7,
    SetWheelMode = 8,
    LaunchRagdollPendulum = 9,
    ToggleRagdollActivePose = 10,
    RecoverRagdoll = 11,
    SetScannerQueryKind = 12,
    SetScannerDistancePreset = 13,
    SetScannerLayerFilter = 14,
    RunScannerQuery = 15,
    SetWindZone = 16,
    ReverseWindDirection = 17,
    RelaunchWindPair = 18,
    ToggleConstraintDrive = 19,
    ReverseConstraintDrive = 20,
    StartReplayDifferenceComparison = 21,
    SetScannerResultMode = 22,
    ToggleScannerSensors = 23,
    ToggleScannerIgnoreSelf = 24,
    ToggleScannerIgnoreAssembly = 25
}

internal readonly struct Physics3DShowcaseCommand
{
    public Physics3DShowcaseCommand(Physics3DShowcaseCommandKind kind, int value = 0)
    {
        Kind = kind;
        Value = value;
    }

    public Physics3DShowcaseCommandKind Kind { get; }
    public int Value { get; }
}

internal enum Physics3DShowcaseReplayStatus : byte
{
    NotRunning = 0,
    Recording = 1,
    ReadyToReplay = 2,
    Replaying = 3,
    Passed = 4,
    Failed = 5
}

internal enum Physics3DShowcaseChallengeStatus : byte
{
    Ready = 0,
    Running = 1,
    Complete = 2,
    Failed = 3
}

internal enum Physics3DShowcaseQueryKind : byte
{
    Ray = 1,
    BoxCast = 2,
    SphereCast = 3,
    CapsuleCast = 4,
    BoxOverlap = 5,
    SphereOverlap = 6,
    CapsuleOverlap = 7
}

internal enum Physics3DShowcaseQueryResultMode : byte
{
    All = 0,
    Closest = 1,
    Any = 2
}

internal enum Physics3DShowcaseWindZone : byte
{
    Steady = 0,
    Gust = 1,
    Vortex = 2
}

internal enum Physics3DScannerPlaybackStatus : byte
{
    Waiting = 0,
    Playing = 1,
    Pulsing = 2,
    Complete = 3,
    Failed = 4
}

internal enum Physics3DShowcaseDriveDirection : sbyte
{
    Reverse = -1,
    Forward = 1
}

internal readonly struct Physics3DShowcaseQueryVisual
{
    public Physics3DShowcaseQueryVisual(
        Physics3DShowcaseQueryKind kind,
        Vector3 originCm,
        Vector3 direction,
        float distanceCm,
        Vector3 sizeCm,
        int hitCount,
        int visibleHitCount,
        int playbackTick,
        float playbackDistanceCm,
        float pulseScale,
        bool hasFirstHit,
        Vector3 firstHitPositionCm)
    {
        Kind = kind;
        OriginCm = originCm;
        Direction = direction;
        DistanceCm = distanceCm;
        SizeCm = sizeCm;
        HitCount = hitCount;
        VisibleHitCount = visibleHitCount;
        PlaybackTick = playbackTick;
        PlaybackDistanceCm = playbackDistanceCm;
        PulseScale = pulseScale;
        HasFirstHit = hasFirstHit;
        FirstHitPositionCm = firstHitPositionCm;
    }

    public Physics3DShowcaseQueryKind Kind { get; }
    public Vector3 OriginCm { get; }
    public Vector3 Direction { get; }
    public float DistanceCm { get; }
    public Vector3 SizeCm { get; }
    public int HitCount { get; }
    public int VisibleHitCount { get; }
    public int PlaybackTick { get; }
    public float PlaybackDistanceCm { get; }
    public float PulseScale { get; }
    public bool HasFirstHit { get; }
    public Vector3 FirstHitPositionCm { get; }
    public bool IsOverlap => Kind is Physics3DShowcaseQueryKind.BoxOverlap or
        Physics3DShowcaseQueryKind.SphereOverlap or
        Physics3DShowcaseQueryKind.CapsuleOverlap;
}

internal readonly struct Physics3DShowcaseQueryHitVisual
{
    public Physics3DShowcaseQueryHitVisual(
        Vector3 positionCm,
        Vector3 normal,
        float distanceCm,
        bool startedOverlapping)
    {
        PositionCm = positionCm;
        Normal = normal;
        DistanceCm = distanceCm;
        StartedOverlapping = startedOverlapping;
    }

    public Vector3 PositionCm { get; }
    public Vector3 Normal { get; }
    public float DistanceCm { get; }
    public bool StartedOverlapping { get; }
}
