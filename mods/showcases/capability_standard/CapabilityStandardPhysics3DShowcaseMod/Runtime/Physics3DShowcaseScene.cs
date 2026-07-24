using System.Numerics;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal enum Physics3DShowcaseScene : byte
{
    Bodies = 1,
    Shapes = 2,
    Stacking = 3,
    Continuous = 4,
    Queries = 5,
    ContactEvents = 6,
    Joints = 7,
    Determinism = 8,
    Benchmark = 9
}

internal enum Physics3DShowcaseCommandKind : byte
{
    SelectScene = 1,
    Reset = 2,
    TogglePause = 3,
    SingleStep = 4,
    Impact = 5,
    SetBenchmarkBodies = 6,
    StartReplayComparison = 7
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

internal readonly struct Physics3DShowcaseQueryVisual
{
    public Physics3DShowcaseQueryVisual(
        Physics3DShowcaseQueryKind kind,
        Vector3 originCm,
        Vector3 direction,
        float distanceCm,
        Vector3 sizeCm,
        int hitCount,
        bool hasFirstHit,
        Vector3 firstHitPositionCm)
    {
        Kind = kind;
        OriginCm = originCm;
        Direction = direction;
        DistanceCm = distanceCm;
        SizeCm = sizeCm;
        HitCount = hitCount;
        HasFirstHit = hasFirstHit;
        FirstHitPositionCm = firstHitPositionCm;
    }

    public Physics3DShowcaseQueryKind Kind { get; }
    public Vector3 OriginCm { get; }
    public Vector3 Direction { get; }
    public float DistanceCm { get; }
    public Vector3 SizeCm { get; }
    public int HitCount { get; }
    public bool HasFirstHit { get; }
    public Vector3 FirstHitPositionCm { get; }
    public bool IsOverlap => Kind is Physics3DShowcaseQueryKind.BoxOverlap or
        Physics3DShowcaseQueryKind.SphereOverlap or
        Physics3DShowcaseQueryKind.CapsuleOverlap;
}
