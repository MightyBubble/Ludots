using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.MovePlanning;

public enum MovePlanLifecycleState : byte
{
    None = 0,
    Active = 1,
    NeedsReplan = 2,
    Arrived = 3,
    Failed = 4,
}

public enum MovePlanFailureReason : byte
{
    None = 0,
    MissingPlan = 1,
    ExecutionUnavailable = 2,
    RefreshRejected = 3,
    TimeoutAbandoned = 4,
    FinalTargetMissing = 5,
    RouteEndedEarly = 6,
}

public struct MovePlanOrderRuntime
{
    public int ActiveOrderId;
    public short TimeoutCount;
    public short ExecutionGeneration;
    public MovePlanLifecycleState LifecycleState;
    public MovePlanFailureReason FailureReason;
}

public struct MovePlanRuntime
{
    public int BoundOrderId;
    public short PlanGeneration;
    public int PointCount;
    public int FinalGoalXcm;
    public int FinalGoalYcm;
    public int CurrentWaypointIndex;
    public Vector2 LastProgressPositionCm;
    public int LastResolvedWaypointIndex;
    public float StallSeconds;
    public byte Initialized;
}

public enum MovePlanExecutionMode : byte
{
    None = 0,
    Individual = 1,
    CommandGroup = 2,
}

public struct MovePlanExecutionIntent
{
    public int CommandGroupToken;
    public Vector2 TargetWorldCm;
    public Vector2 ProjectionHintWorldCm;
    public float SpeedCmPerSec;
    public float StopRadiusCm;
    public float MinimumClearanceCm;
    public byte HasTarget;
    public byte ResolveNavigableTarget;
    public MovePlanExecutionMode Mode;
}

public enum MovePlanExecutionResultKind : byte
{
    None = 0,
    Arrived = 1,
    Failed = 2,
}

public struct MovePlanExecutionResult
{
    public int CommandGroupToken;
    public MovePlanExecutionResultKind Kind;
    public MovePlanFailureReason FailureReason;
}

/// <summary>Scheduling marker for a command-group MovePlan execution adapter.</summary>
public interface IMovePlanCommandGroupExecutionSystem
{
}

public readonly ref struct MovePlanView
{
    public readonly ReadOnlySpan<int> PathXcm;
    public readonly ReadOnlySpan<int> PathYcm;
    public readonly int Count;
    public readonly Vector2 FinalGoalWorldCm;
    public readonly short PlanGeneration;

    public MovePlanView(
        ReadOnlySpan<int> pathXcm,
        ReadOnlySpan<int> pathYcm,
        int count,
        Vector2 finalGoalWorldCm,
        short planGeneration)
    {
        PathXcm = pathXcm;
        PathYcm = pathYcm;
        Count = count;
        FinalGoalWorldCm = finalGoalWorldCm;
        PlanGeneration = planGeneration;
    }

    public bool TryGetWaypoint(int waypointIndex, out Vector2 waypoint)
    {
        waypoint = default;
        if ((uint)waypointIndex >= (uint)Count)
        {
            return false;
        }

        waypoint = new Vector2(PathXcm[waypointIndex], PathYcm[waypointIndex]);
        return true;
    }
}

public interface IMovePlanStore
{
    bool TryBindFromOrder(Entity entity, in Order order, out short planGeneration, out Vector2 finalGoalWorldCm);
    bool TryBindFromOrder(Entity entity, in Order order, Vector2 bindPositionWorldCm, out short planGeneration, out Vector2 finalGoalWorldCm);
    bool TryGetPlan(Entity entity, int orderId, out MovePlanView plan);
    void Clear(Entity entity);
}

public interface IMovePlanFinalTargetResolver
{
    bool TryResolveFinalTarget(World world, in Order order, out Vector2 finalGoalWorldCm);
}

public interface IMovePlanExecutionSink
{
    bool TryApply(World world, Entity entity, in MovePlanExecutionIntent intent);
    void Clear(World world, Entity entity);
}
