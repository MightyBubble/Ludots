using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

internal struct MassNavigationSolverWindowTransition
{
    internal float CenterXCm;
    internal float CenterYCm;
    internal int CommandFocusTicksRemaining;
    internal bool HasCommandFocus;
    internal float LastCommandFocusXCm;
    internal float LastCommandFocusYCm;
    internal int LastCommandActorCount;
    internal float WorkAreaCenterXCm;
    internal float WorkAreaCenterYCm;
    internal float WorkAreaWidthCm;
    internal float WorkAreaHeightCm;
    internal int WorkAreaRevision;
    internal string WorkAreaReason;
    internal string Driver;
    internal bool SolverMoved;

    public Vector2 StreamingFocus => new(WorkAreaCenterXCm, WorkAreaCenterYCm);
}

internal sealed class MassNavigationSolverWindowCoordinator
{
    private readonly int _commandFocusHoldTicks;
    private readonly int _workAreaPaddingCm;
    private readonly int _workAreaMaxWidthCm;
    private readonly int _workAreaMaxHeightCm;
    private WorldSizeSpec _boardWorldSize;
    private bool _boardWorldBound;
    private float _centerXCm;
    private float _centerYCm;
    private readonly float _widthCm;
    private readonly float _heightCm;
    private int _commandFocusTicksRemaining;
    private bool _hasCommandFocus;
    private float _lastCommandFocusXCm;
    private float _lastCommandFocusYCm;
    private int _lastCommandActorCount;
    private float _workAreaCenterXCm;
    private float _workAreaCenterYCm;
    private float _workAreaWidthCm;
    private float _workAreaHeightCm;
    private int _workAreaRevision;
    private string _workAreaReason = "initial contact";
    private string _driver = "initial nav area";

    public MassNavigationSolverWindowCoordinator(
        in MassNavigationHotZonePlan initialHotZone,
        MassNavigationWorldPlan worldPlan,
        int fieldWidthCm,
        int fieldHeightCm)
    {
        _commandFocusHoldTicks = worldPlan.CommandFocusHoldTicks;
        _workAreaPaddingCm = worldPlan.WorkAreaPaddingCm;
        _workAreaMaxWidthCm = worldPlan.WorkAreaMaxWidthCm;
        _workAreaMaxHeightCm = worldPlan.WorkAreaMaxHeightCm;
        _widthCm = fieldWidthCm;
        _heightCm = fieldHeightCm;
        _centerXCm = initialHotZone.CenterXCm;
        _centerYCm = initialHotZone.CenterYCm;
        _workAreaCenterXCm = _centerXCm;
        _workAreaCenterYCm = _centerYCm;
        _workAreaWidthCm = _widthCm;
        _workAreaHeightCm = _heightCm;
    }

    public float CenterXCm => _centerXCm;
    public float CenterYCm => _centerYCm;
    public float WidthCm => _widthCm;
    public float HeightCm => _heightCm;
    public float MinXCm => _centerXCm - (_widthCm * 0.5f);
    public float MinYCm => _centerYCm - (_heightCm * 0.5f);
    public float MaxXCm => _centerXCm + (_widthCm * 0.5f);
    public float MaxYCm => _centerYCm + (_heightCm * 0.5f);
    public float WorkAreaCenterXCm => _workAreaCenterXCm;
    public float WorkAreaCenterYCm => _workAreaCenterYCm;
    public float WorkAreaWidthCm => _workAreaWidthCm;
    public float WorkAreaHeightCm => _workAreaHeightCm;
    public float WorkAreaMinXCm => _workAreaCenterXCm - (_workAreaWidthCm * 0.5f);
    public float WorkAreaMinYCm => _workAreaCenterYCm - (_workAreaHeightCm * 0.5f);
    public float WorkAreaMaxXCm => _workAreaCenterXCm + (_workAreaWidthCm * 0.5f);
    public float WorkAreaMaxYCm => _workAreaCenterYCm + (_workAreaHeightCm * 0.5f);
    public int WorkAreaRevision => _workAreaRevision;
    public string WorkAreaReason => _workAreaReason;
    public int CommandFocusTicksRemaining => _commandFocusTicksRemaining;
    public bool HasCommandFocus => _hasCommandFocus && _commandFocusTicksRemaining > 0;
    public float CommandFocusXCm => _lastCommandFocusXCm;
    public float CommandFocusYCm => _lastCommandFocusYCm;
    public int LastCommandActorCount => _lastCommandActorCount;
    public string Driver => _driver;
    public WorldSizeSpec BoardWorldSize => RequireBoardWorldSize();
    public Vector2 SolverFocus => HasCommandFocus
        ? new Vector2(_lastCommandFocusXCm, _lastCommandFocusYCm)
        : new Vector2(_workAreaCenterXCm, _workAreaCenterYCm);
    public Vector2 StreamingFocus => new(_workAreaCenterXCm, _workAreaCenterYCm);

    public void BindBoardWorld(WorldSizeSpec boardWorldSize, string initialHotZoneId)
    {
        WorldAabbCm bounds = boardWorldSize.Bounds;
        EnsureWindowFitsBoard(_widthCm, bounds.Width, "solver window width");
        EnsureWindowFitsBoard(_heightCm, bounds.Height, "solver window height");
        EnsurePointInsideWindowCenterBounds(_centerXCm, bounds.Left, bounds.Right, _widthCm, initialHotZoneId, "x");
        EnsurePointInsideWindowCenterBounds(_centerYCm, bounds.Top, bounds.Bottom, _heightCm, initialHotZoneId, "y");

        _boardWorldSize = boardWorldSize;
        _boardWorldBound = true;
        _workAreaCenterXCm = _centerXCm;
        _workAreaCenterYCm = _centerYCm;
    }

    public MassNavigationSolverWindowTransition PlanAdvanceCommandFocus(out bool streamingUpdateRequired)
    {
        MassNavigationSolverWindowTransition transition = CaptureTransition();
        if (transition.CommandFocusTicksRemaining <= 0)
        {
            transition.HasCommandFocus = false;
            streamingUpdateRequired = false;
            return transition;
        }

        transition.CommandFocusTicksRemaining--;
        if (transition.CommandFocusTicksRemaining > 0)
        {
            streamingUpdateRequired = false;
            return transition;
        }

        transition.HasCommandFocus = false;
        streamingUpdateRequired = true;
        return transition;
    }

    public MassNavigationSolverWindowTransition PlanManualFocus(
        Vector2 worldCenterCm,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> commandActors,
        MassNavigationAgentState agentState,
        MassNavigationFlowSolverState flow,
        string workAreaReason,
        string solverReason)
    {
        MassNavigationSolverWindowTransition transition = CaptureTransition();
        PlanWorkArea(
            ref transition,
            worldCenterCm,
            focusWidthCm,
            focusHeightCm,
            commandActors,
            agentState,
            flow,
            workAreaReason);
        PlanMove(ref transition, worldCenterCm, solverReason);
        return transition;
    }

    public MassNavigationSolverWindowTransition PlanCommandFocus(
        Vector2 worldCenterCm,
        int commandActorCount,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> commandActors,
        MassNavigationAgentState agentState,
        MassNavigationFlowSolverState flow,
        string workAreaReason,
        string solverReason)
    {
        MassNavigationSolverWindowTransition transition = CaptureTransition();
        transition.HasCommandFocus = true;
        transition.LastCommandFocusXCm = worldCenterCm.X;
        transition.LastCommandFocusYCm = worldCenterCm.Y;
        transition.LastCommandActorCount = commandActorCount;
        transition.CommandFocusTicksRemaining = _commandFocusHoldTicks;
        PlanWorkArea(
            ref transition,
            worldCenterCm,
            focusWidthCm,
            focusHeightCm,
            commandActors,
            agentState,
            flow,
            workAreaReason);
        PlanMove(ref transition, worldCenterCm, solverReason);
        return transition;
    }

    public MassNavigationSolverWindowTransition PlanRuntimeFocus(
        Vector2 focusCm,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> commandActors,
        MassNavigationAgentState agentState,
        MassNavigationFlowSolverState flow,
        string reason)
    {
        MassNavigationSolverWindowTransition transition = CaptureTransition();
        PlanWorkArea(
            ref transition,
            focusCm,
            focusWidthCm,
            focusHeightCm,
            commandActors,
            agentState,
            flow,
            reason);
        return transition;
    }

    public void Commit(in MassNavigationSolverWindowTransition transition)
    {
        _centerXCm = transition.CenterXCm;
        _centerYCm = transition.CenterYCm;
        _commandFocusTicksRemaining = transition.CommandFocusTicksRemaining;
        _hasCommandFocus = transition.HasCommandFocus;
        _lastCommandFocusXCm = transition.LastCommandFocusXCm;
        _lastCommandFocusYCm = transition.LastCommandFocusYCm;
        _lastCommandActorCount = transition.LastCommandActorCount;
        _workAreaCenterXCm = transition.WorkAreaCenterXCm;
        _workAreaCenterYCm = transition.WorkAreaCenterYCm;
        _workAreaWidthCm = transition.WorkAreaWidthCm;
        _workAreaHeightCm = transition.WorkAreaHeightCm;
        _workAreaRevision = transition.WorkAreaRevision;
        _workAreaReason = transition.WorkAreaReason;
        _driver = transition.Driver;
    }

    private void PlanWorkArea(
        ref MassNavigationSolverWindowTransition transition,
        Vector2 focusCm,
        float focusWidthCm,
        float focusHeightCm,
        ReadOnlySpan<Entity> commandActors,
        MassNavigationAgentState agentState,
        MassNavigationFlowSolverState flow,
        string reason)
    {
        float clampedWidth = MathF.Max(1f, focusWidthCm);
        float clampedHeight = MathF.Max(1f, focusHeightCm);
        float minX = focusCm.X - (clampedWidth * 0.5f);
        float maxX = focusCm.X + (clampedWidth * 0.5f);
        float minY = focusCm.Y - (clampedHeight * 0.5f);
        float maxY = focusCm.Y + (clampedHeight * 0.5f);

        if (TransitionHasCommandFocus(in transition))
        {
            IncludePoint(
                ref minX,
                ref maxX,
                ref minY,
                ref maxY,
                transition.LastCommandFocusXCm,
                transition.LastCommandFocusYCm);
        }

        IncludeCommandActorBounds(
            ref minX,
            ref maxX,
            ref minY,
            ref maxY,
            commandActors,
            agentState,
            flow,
            transition.CenterXCm,
            transition.CenterYCm);
        minX -= _workAreaPaddingCm;
        maxX += _workAreaPaddingCm;
        minY -= _workAreaPaddingCm;
        maxY += _workAreaPaddingCm;
        ClampWorkArea(ref minX, ref maxX, ref minY, ref maxY);

        float width = MathF.Min(MathF.Max(_widthCm, maxX - minX), _workAreaMaxWidthCm);
        float height = MathF.Min(MathF.Max(_heightCm, maxY - minY), _workAreaMaxHeightCm);
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        ClampWorkAreaCenter(ref centerX, ref centerY, width, height);
        if (MathF.Abs(centerX - transition.WorkAreaCenterXCm) < 0.5f &&
            MathF.Abs(centerY - transition.WorkAreaCenterYCm) < 0.5f &&
            MathF.Abs(width - transition.WorkAreaWidthCm) < 0.5f &&
            MathF.Abs(height - transition.WorkAreaHeightCm) < 0.5f &&
            string.Equals(transition.WorkAreaReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        transition.WorkAreaCenterXCm = centerX;
        transition.WorkAreaCenterYCm = centerY;
        transition.WorkAreaWidthCm = width;
        transition.WorkAreaHeightCm = height;
        transition.WorkAreaReason = reason;
        transition.WorkAreaRevision++;
    }

    private MassNavigationSolverWindowTransition CaptureTransition()
    {
        return new MassNavigationSolverWindowTransition
        {
            CenterXCm = _centerXCm,
            CenterYCm = _centerYCm,
            CommandFocusTicksRemaining = _commandFocusTicksRemaining,
            HasCommandFocus = _hasCommandFocus,
            LastCommandFocusXCm = _lastCommandFocusXCm,
            LastCommandFocusYCm = _lastCommandFocusYCm,
            LastCommandActorCount = _lastCommandActorCount,
            WorkAreaCenterXCm = _workAreaCenterXCm,
            WorkAreaCenterYCm = _workAreaCenterYCm,
            WorkAreaWidthCm = _workAreaWidthCm,
            WorkAreaHeightCm = _workAreaHeightCm,
            WorkAreaRevision = _workAreaRevision,
            WorkAreaReason = _workAreaReason,
            Driver = _driver,
        };
    }

    private void PlanMove(
        ref MassNavigationSolverWindowTransition transition,
        Vector2 requestedCenterCm,
        string reason)
    {
        float nextCenterX = requestedCenterCm.X;
        float nextCenterY = requestedCenterCm.Y;
        ClampWindowCenter(ref nextCenterX, ref nextCenterY);
        if (MathF.Abs(nextCenterX - transition.CenterXCm) < 0.5f &&
            MathF.Abs(nextCenterY - transition.CenterYCm) < 0.5f)
        {
            return;
        }

        transition.CenterXCm = nextCenterX;
        transition.CenterYCm = nextCenterY;
        transition.Driver = reason;
        transition.SolverMoved = true;
    }

    private static bool TransitionHasCommandFocus(in MassNavigationSolverWindowTransition transition)
    {
        return transition.HasCommandFocus && transition.CommandFocusTicksRemaining > 0;
    }

    public bool ContainsWorldPoint(float worldXCm, float worldYCm)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        return worldXCm >= bounds.Left &&
            worldXCm <= bounds.Right &&
            worldYCm >= bounds.Top &&
            worldYCm <= bounds.Bottom;
    }

    public Vector2 ToLocalCm(Vector2 worldCm) => new(worldCm.X - MinXCm, worldCm.Y - MinYCm);
    public Vector2 ToWorldCm(Vector2 localCm) => new(localCm.X + MinXCm, localCm.Y + MinYCm);
    public float ToWorldXCm(float localXCm) => localXCm + MinXCm;
    public float ToWorldYCm(float localYCm) => localYCm + MinYCm;
    public float ToLocalXCm(float worldXCm) => worldXCm - MinXCm;
    public float ToLocalYCm(float worldYCm) => worldYCm - MinYCm;

    private void IncludeCommandActorBounds(
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY,
        ReadOnlySpan<Entity> commandActors,
        MassNavigationAgentState agentState,
        MassNavigationFlowSolverState flow,
        float solverCenterXCm,
        float solverCenterYCm)
    {
        float solverMinXCm = solverCenterXCm - (_widthCm * 0.5f);
        float solverMinYCm = solverCenterYCm - (_heightCm * 0.5f);
        for (int i = 0; i < commandActors.Length; i++)
        {
            if (!agentState.TryGetControllableIndex(commandActors[i], out int unitIndex) ||
                (uint)unitIndex >= (uint)flow.UnitCount)
            {
                continue;
            }

            IncludePoint(
                ref minX,
                ref maxX,
                ref minY,
                ref maxY,
                flow.GetPositionX(unitIndex) + solverMinXCm,
                flow.GetPositionY(unitIndex) + solverMinYCm);
        }
    }

    private void ClampWindowCenter(ref float centerX, ref float centerY)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        centerX = ClampWindowCenterToBounds(centerX, bounds.Left, bounds.Right, _widthCm);
        centerY = ClampWindowCenterToBounds(centerY, bounds.Top, bounds.Bottom, _heightCm);
    }

    private void ClampWorkArea(ref float minX, ref float maxX, ref float minY, ref float maxY)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        minX = Math.Clamp(minX, bounds.Left, bounds.Right);
        maxX = Math.Clamp(maxX, bounds.Left, bounds.Right);
        minY = Math.Clamp(minY, bounds.Top, bounds.Bottom);
        maxY = Math.Clamp(maxY, bounds.Top, bounds.Bottom);
        if (minX > maxX)
        {
            (minX, maxX) = (maxX, minX);
        }

        if (minY > maxY)
        {
            (minY, maxY) = (maxY, minY);
        }
    }

    private void ClampWorkAreaCenter(ref float centerX, ref float centerY, float width, float height)
    {
        WorldAabbCm bounds = RequireBoardWorldSize().Bounds;
        centerX = ClampWindowCenterToBounds(centerX, bounds.Left, bounds.Right, width);
        centerY = ClampWindowCenterToBounds(centerY, bounds.Top, bounds.Bottom, height);
    }

    private WorldSizeSpec RequireBoardWorldSize()
    {
        if (!_boardWorldBound)
        {
            throw new InvalidOperationException("MassNavigationSimulationRuntime requires PrimaryBoard.WorldSize to be bound before world operations.");
        }

        return _boardWorldSize;
    }

    private static float ClampWindowCenterToBounds(float worldCm, int minCm, int maxCm, float windowSizeCm)
    {
        float halfSize = windowSizeCm * 0.5f;
        float min = minCm + halfSize;
        float max = maxCm - halfSize;
        if (min > max)
        {
            throw new InvalidOperationException(
                $"MassNavigation solver/work area window {windowSizeCm:0.###} cm exceeds board span {maxCm - minCm} cm.");
        }

        return Math.Clamp(worldCm, min, max);
    }

    private static void EnsureWindowFitsBoard(float windowSizeCm, int boardSpanCm, string windowName)
    {
        if (windowSizeCm > boardSpanCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation initial {windowName} {windowSizeCm:0.###} cm exceeds board span {boardSpanCm} cm.");
        }
    }

    private static void EnsurePointInsideWindowCenterBounds(
        float centerCm,
        int minCm,
        int maxCm,
        float windowSizeCm,
        string hotZoneId,
        string axisName)
    {
        float halfSize = windowSizeCm * 0.5f;
        float minCenter = minCm + halfSize;
        float maxCenter = maxCm - halfSize;
        if (centerCm < minCenter || centerCm > maxCenter)
        {
            throw new InvalidOperationException(
                $"MassNavigation active hot zone '{hotZoneId}' center {axisName}={centerCm:0.###} cannot host solver window {windowSizeCm:0.###} cm inside board center range [{minCenter:0.###}, {maxCenter:0.###}].");
        }
    }

    private static void IncludePoint(ref float minX, ref float maxX, ref float minY, ref float maxY, float x, float y)
    {
        minX = MathF.Min(minX, x);
        maxX = MathF.Max(maxX, x);
        minY = MathF.Min(minY, y);
        maxY = MathF.Max(maxY, y);
    }
}
