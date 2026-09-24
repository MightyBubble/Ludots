using Arch.Core;
using Ludots.Core.Config;

namespace RtsMultiplayerFrontlineMod.Runtime;

public enum FrontlineHarvestPhase : byte
{
    Idle = 0,
    TravellingToNode = 1,
    Loading = 2,
    ReturningToCore = 3,
}

public enum FrontlineAttackPhase : byte
{
    Idle = 0,
    Pursuing = 1,
    Engaging = 2,
}

public enum FrontlineTrainResult : byte
{
    None = 0,
    Accepted = 1,
    InsufficientCrystals = 2,
}

public enum FrontlineMatchOutcome : byte
{
    InProgress = 0,
    SideOneVictory = 1,
    SideTwoVictory = 2,
    Draw = 3,
}

public enum FrontlineMatchPhase : byte
{
    WaitingForPlayers = 0,
    Countdown = 1,
    InProgress = 2,
    Completed = 3,
}

public struct FrontlineParticipant
{
    public int SideIndex;
}

public struct FrontlineCore
{
}

public struct FrontlineHarvester
{
}

public struct FrontlineInfantry
{
}

public struct FrontlineCrystalNode
{
}

public struct FrontlineHarvestState
{
    public FrontlineHarvestPhase Phase;
    public int RemainingTicks;
    public int ExpectedMoveOrderId;
    public int TargetXCm;
    public int TargetYCm;
    public byte ExpectedMoveObserved;
}

public struct FrontlineAttackState
{
    public FrontlineAttackPhase Phase;
    public Entity Target;
    public int ExpectedMoveOrderId;
    public int CooldownTicks;
    public byte ExpectedMoveObserved;
}

public struct FrontlineCoreState
{
    public int LastHandledTrainOrderId;
    public FrontlineTrainResult LastTrainResult;
}

public struct FrontlineDeathState
{
    public byte DestroyQueued;
}

public struct FrontlineTagBindingState
{
    public byte IsBound;
}

internal static class FrontlineComponentAuthoring
{
    public static void Register(string modId)
    {
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineParticipant>(nameof(FrontlineParticipant), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineCore>(nameof(FrontlineCore), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineHarvester>(nameof(FrontlineHarvester), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineInfantry>(nameof(FrontlineInfantry), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineCrystalNode>(nameof(FrontlineCrystalNode), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineHarvestState>(nameof(FrontlineHarvestState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineAttackState>(nameof(FrontlineAttackState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineCoreState>(nameof(FrontlineCoreState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineDeathState>(nameof(FrontlineDeathState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineTagBindingState>(nameof(FrontlineTagBindingState), modId);
    }
}
