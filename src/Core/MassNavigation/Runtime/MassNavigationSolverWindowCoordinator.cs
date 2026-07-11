using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

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

    public bool AdvanceCommandFocus()
    {
        if (_commandFocusTicksRemaining <= 0)
        {
            _hasCommandFocus = false;
            return false;
        }

        _commandFocusTicksRemaining--;
        if (_commandFocusTicksRemaining > 0)
        {
            return false;
        }

        _hasCommandFocus = false;
        return true;
    }

    public void BeginCommandFocus(Vector2 worldCenterCm, int commandActorCount)
    {
        _hasCommandFocus = true;
        _lastCommandFocusXCm = worldCenterCm.X;
        _lastCommandFocusYCm = worldCenterCm.Y;
        _lastCommandActorCount = commandActorCount;
        _commandFocusTicksRemaining = _commandFocusHoldTicks;
    }

    public bool Move(Vector2 requestedCenterCm, string reason)
    {
        float nextCenterX = requestedCenterCm.X;
        float nextCenterY = requestedCenterCm.Y;
        ClampWindowCenter(ref nextCenterX, ref nextCenterY);
        if (MathF.Abs(nextCenterX - _centerXCm) < 0.5f &&
            MathF.Abs(nextCenterY - _centerYCm) < 0.5f)
        {
            return false;
        }

        _centerXCm = nextCenterX;
        _centerYCm = nextCenterY;
        _driver = reason;
        return true;
    }

    public void ObserveWorkArea(
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

        if (HasCommandFocus)
        {
            IncludePoint(ref minX, ref maxX, ref minY, ref maxY, _lastCommandFocusXCm, _lastCommandFocusYCm);
        }

        IncludeCommandActorBounds(ref minX, ref maxX, ref minY, ref maxY, commandActors, agentState, flow);
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
        if (MathF.Abs(centerX - _workAreaCenterXCm) < 0.5f &&
            MathF.Abs(centerY - _workAreaCenterYCm) < 0.5f &&
            MathF.Abs(width - _workAreaWidthCm) < 0.5f &&
            MathF.Abs(height - _workAreaHeightCm) < 0.5f &&
            string.Equals(_workAreaReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _workAreaCenterXCm = centerX;
        _workAreaCenterYCm = centerY;
        _workAreaWidthCm = width;
        _workAreaHeightCm = height;
        _workAreaReason = reason;
        _workAreaRevision++;
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
        MassNavigationFlowSolverState flow)
    {
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
                ToWorldXCm(flow.GetPositionX(unitIndex)),
                ToWorldYCm(flow.GetPositionY(unitIndex)));
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
