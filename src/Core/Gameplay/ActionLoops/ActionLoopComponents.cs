using Arch.Core;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Gameplay.ActionLoops;

public interface IGameplayActionLoopGate
{
    bool CanAdvanceGameplay { get; }
}

public struct ResourceTransportProfile
{
    public int GatherOrderTypeId;
    public int MoveOrderTypeId;
    public int ResourceAttributeId;
    public float CargoAmount;
    public int LoadDurationTicks;
    public int ArrivalRadiusCm;
}

public struct ResourceSourceProfile
{
    public int ResourceAttributeId;
}

public struct ResourceSinkProfile
{
    public int ResourceAttributeId;
    public int DockOffsetXCm;
    public int DockOffsetYCm;
}

public enum ResourceTransportPhase : byte
{
    Idle = 0,
    TravellingToSource = 1,
    Loading = 2,
    ReturningToSink = 3,
}

public struct ResourceTransportState
{
    public ResourceTransportPhase Phase;
    public int RemainingTicks;
    public int ExpectedMoveOrderId;
    public int TargetXCm;
    public int TargetYCm;
    public byte ExpectedMoveObserved;
}

public struct DirectAttackProfile
{
    public const int PursuitArrivalSlackCm = 50;

    public int AttackOrderTypeId;
    public int MoveOrderTypeId;
    public int EffectTemplateId;
    public RelationshipFilter TargetRelation;
    public int RangeCm;
    public int CooldownTicks;

    /// <summary>
    /// Radius from the attack target at which attackers hold while engaging. Zero engages from
    /// wherever the attacker entered range. A positive radius routes every attacker to a per-actor
    /// standoff slot on that ring before engaging: slot direction follows the attack order's
    /// explicit engagement point (the group layout offset), falling back to the attacker's own
    /// bearing from the target. Must stay at most <see cref="RangeCm"/> minus
    /// <see cref="PursuitArrivalSlackCm"/> so a slot arrival is always inside attack range.
    /// </summary>
    public int EngagementStandoffRadiusCm;
}

public enum DirectAttackPhase : byte
{
    Idle = 0,
    Pursuing = 1,
    Engaging = 2,
}

public struct DirectAttackState
{
    public DirectAttackPhase Phase;
    public Entity Target;
    public int ExpectedMoveOrderId;
    public int EngagementPointXCm;
    public int EngagementPointYCm;
    public int CooldownTicks;
    public byte ExpectedMoveObserved;
    public byte HasExplicitEngagementPoint;
}
