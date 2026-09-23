using Arch.Core;

namespace ThreeKingdomsScenarioMod.Gameplay;

internal enum ThreeKingdomsAttachmentLayoutKind : byte
{
    Center = 0,
    FrontLine = 1,
    BackLine = 2,
    Ring = 3,
}

internal enum ThreeKingdomsSiegeActionPhase : byte
{
    None = 0,
    Approaching = 1,
    Channeling = 2,
    Traveling = 3,
    Attached = 4,
}

internal struct ThreeKingdomsAttachmentHost
{
    public byte Capacity;
    public byte SlotCount;
    public ThreeKingdomsAttachmentLayoutKind Layout;
    public float ForwardOffsetCm;
    public float SideSpacingCm;
    public float RadiusCm;
}

internal struct ThreeKingdomsWallFeature
{
    public float OuterOffsetCm;
    public float InnerOffsetCm;
    public float ClimbDurationSeconds;
    public float FastClimbDurationSeconds;
}

internal struct ThreeKingdomsGateFeature
{
    public float OuterOffsetCm;
    public float InnerOffsetCm;
    public float CaptureDurationSeconds;
}

internal struct ThreeKingdomsLadderFeature
{
    public float AttachDurationSeconds;
    public float DeployIntervalSeconds;
}

internal struct ThreeKingdomsTunnelFeature
{
    public float TravelSpeedCmPerSecond;
}

internal struct ThreeKingdomsTrenchFeature
{
    public float HalfWidthCm;
    public float HalfHeightCm;
    public float FillDurationSeconds;
    public byte TrapCapacity;
}

internal struct ThreeKingdomsTrenchRuntimeState
{
    public float FillProgressSeconds;
}

internal struct ThreeKingdomsAttachedSlotRef
{
    public byte SlotIndex;
}

internal struct ThreeKingdomsWallClimbState
{
    public Entity Wall;
    public ThreeKingdomsSiegeActionPhase Phase;
    public float ProgressSeconds;
    public byte FromInnerSide;
    public byte SlotIndex;
}

internal struct ThreeKingdomsGateCaptureState
{
    public Entity Gate;
    public ThreeKingdomsSiegeActionPhase Phase;
    public float ProgressSeconds;
}

internal struct ThreeKingdomsEnterAttachmentState
{
    public Entity Host;
    public ThreeKingdomsSiegeActionPhase Phase;
}

internal struct ThreeKingdomsTrenchFillState
{
    public Entity Trench;
    public ThreeKingdomsSiegeActionPhase Phase;
    public float ProgressSeconds;
    public byte SlotIndex;
}

internal struct ThreeKingdomsLadderAttachState
{
    public Entity Wall;
    public ThreeKingdomsSiegeActionPhase Phase;
    public float ProgressSeconds;
    public byte SlotIndex;
}

internal struct ThreeKingdomsLadderModeState
{
    public byte HoldDeployment;
    public float DeployCooldownSeconds;
}

internal struct ThreeKingdomsTunnelExitState
{
    public Entity Exit;
}

internal struct ThreeKingdomsTunnelTransitState
{
    public Entity Destination;
    public float TravelSpeedCmPerSecond;
}

internal struct ThreeKingdomsProcessedExecState
{
    public int LastOrderId;
}
