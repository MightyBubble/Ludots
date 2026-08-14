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
    public int AttackOrderTypeId;
    public int MoveOrderTypeId;
    public int EffectTemplateId;
    public RelationshipFilter TargetRelation;
    public int RangeCm;
    public int CooldownTicks;
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
