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

public struct FrontlineMatchStateEntity
{
}

public struct FrontlineMatchStateProjection
{
    public int CommittedTick;
    public FrontlineMatchPhase Phase;
    public int CountdownRemainingTicks;
    public FrontlineMatchOutcome Outcome;
    public int WinningSideIndex;
    public byte SideOneReady;
    public byte SideTwoReady;
    public byte SideOneConnected;
    public byte SideTwoConnected;

    public readonly FrontlineMatchSnapshot ToSnapshot() => new(
        CommittedTick,
        Phase,
        CountdownRemainingTicks,
        Outcome,
        WinningSideIndex,
        SideOneReady != 0,
        SideTwoReady != 0,
        SideOneConnected != 0,
        SideTwoConnected != 0);

    public static FrontlineMatchStateProjection FromSnapshot(in FrontlineMatchSnapshot snapshot) => new()
    {
        CommittedTick = snapshot.CommittedTick,
        Phase = snapshot.Phase,
        CountdownRemainingTicks = snapshot.CountdownRemainingTicks,
        Outcome = snapshot.Outcome,
        WinningSideIndex = snapshot.WinningSideIndex,
        SideOneReady = snapshot.SideOneReady ? (byte)1 : (byte)0,
        SideTwoReady = snapshot.SideTwoReady ? (byte)1 : (byte)0,
        SideOneConnected = snapshot.SideOneConnected ? (byte)1 : (byte)0,
        SideTwoConnected = snapshot.SideTwoConnected ? (byte)1 : (byte)0,
    };
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
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineMatchStateEntity>(nameof(FrontlineMatchStateEntity), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineMatchStateProjection>(nameof(FrontlineMatchStateProjection), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineHarvestState>(nameof(FrontlineHarvestState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineAttackState>(nameof(FrontlineAttackState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineCoreState>(nameof(FrontlineCoreState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineDeathState>(nameof(FrontlineDeathState), modId);
        Ludots.Core.Config.ComponentRegistry.Register<FrontlineTagBindingState>(nameof(FrontlineTagBindingState), modId);
    }
}
