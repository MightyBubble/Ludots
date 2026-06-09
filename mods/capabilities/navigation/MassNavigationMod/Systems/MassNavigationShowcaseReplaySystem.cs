using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Text.Json;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationShowcaseReplaySystem : ISystem<float>
{
    private const string ReplayUseCaseEnv = "LUDOTS_MASS_NAV_REPLAY_USECASE";
    private const string ReplayTracePathEnv = "LUDOTS_MASS_NAV_REPLAY_TRACE_PATH";
    private const string ReplayStartFrameEnv = "LUDOTS_MASS_NAV_REPLAY_FRAME_START";
    private const int DefaultStartFrame = 30;

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly MassNavigationShowcaseGuideRuntime _guide;
    private readonly string _useCase;
    private readonly string _tracePath;
    private readonly int _startFrame;
    private readonly FrozenInputActionReader _replayInput = new();
    private readonly AuthoritativePointerButtonSnapshot _replayPointerButtons = new();
    private readonly InteractionActionBindings _bindings;
    private readonly MassNavigationPathPreviewInputSystem _pathPreview;
    private readonly MassNavigationRuntimeBakeAuthoringInputSystem _runtimeBakeAuthoring;
    private readonly MassNavigationCommandBridgeSystem _commandBridge;
    private readonly MassNavigationOrderBridgeSystem _orderBridge;
    private readonly List<string> _traceLines = new(64);
    private bool _traceFlushed;
    private bool _inputInstalled;
    private bool _completed;
    private bool _failed;
    private int _phase;
    private bool _targetRefreshReplayStarted;
    private int _targetRefreshReplayFrames;
    private string _orderBridgeReplayOperation = string.Empty;
    private int _orderBridgeReplayFrames;

    private MassNavigationShowcaseReplaySystem(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide,
        string useCase,
        string tracePath,
        int startFrame)
    {
        _engine = engine;
        _simulation = simulation;
        _guide = guide;
        _useCase = useCase;
        _tracePath = tracePath;
        _startFrame = Math.Max(0, startFrame);
        _bindings = InteractionActionBindingsResolver.Require(
            engine.GlobalContext,
            nameof(MassNavigationShowcaseReplaySystem));
        _pathPreview = new MassNavigationPathPreviewInputSystem(engine, simulation, guide);
        _runtimeBakeAuthoring = new MassNavigationRuntimeBakeAuthoringInputSystem(engine, simulation, guide);
        _commandBridge = new MassNavigationCommandBridgeSystem(engine, simulation);
        _orderBridge = new MassNavigationOrderBridgeSystem(engine, simulation);
    }

    public static bool TryCreate(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide,
        out MassNavigationShowcaseReplaySystem system)
    {
        system = null!;
        string? rawUseCase = Environment.GetEnvironmentVariable(ReplayUseCaseEnv);
        if (string.IsNullOrWhiteSpace(rawUseCase))
        {
            return false;
        }

        string useCase = rawUseCase.Trim().ToUpperInvariant();
        string? rawTracePath = Environment.GetEnvironmentVariable(ReplayTracePathEnv);
        string tracePath = string.IsNullOrWhiteSpace(rawTracePath)
            ? Path.Combine(AppContext.BaseDirectory, $"mass-navigation-{useCase.ToLowerInvariant()}-operation-trace.jsonl")
            : Path.GetFullPath(rawTracePath);
        int startFrame = int.TryParse(
            Environment.GetEnvironmentVariable(ReplayStartFrameEnv),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsedFrame)
                ? parsedFrame
                : DefaultStartFrame;

        system = new MassNavigationShowcaseReplaySystem(engine, simulation, guide, useCase, tracePath, startFrame);
        return true;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose()
    {
        FlushTrace();
    }

    public void Update(in float dt)
    {
        if (_completed || _failed || !MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        try
        {
            if (_simulation.FrameIndex < _startFrame || _simulation.AgentState.ControllableCount <= 0)
            {
                return;
            }

            EnsureReplayInputInstalled();
            bool advanced = _useCase switch
            {
                "U04" => ReplayPathOnly(),
                "U05" => ReplayPathDriven(MassNavigationShowcaseStepId.WorldHpa, "world_hpa_route"),
                "U06" => ReplayPathDriven(MassNavigationShowcaseStepId.StrategySwitch, "strategy_switch_route"),
                "U07" => ReplayOrderReuse(),
                "U08" => ReplayLargeArmyOrder(MassNavigationShowcaseStepId.TargetAllocation, "target_allocation", requireFlow: false),
                "U10" => ReplayWaypointAuthoring(),
                "U11" => ReplayStreamingWindow(),
                "U12" => ReplayLargeArmyOrder(MassNavigationShowcaseStepId.TenKFlow, "ten_k_flow", requireFlow: true),
                "U13" => ReplayStaticObstacleWorld(),
                "U14" => ReplayDiagnosticsOnly(MassNavigationShowcaseStepId.PerformanceDebug, "performance_debug"),
                "U15" => ReplayDiagnosticsOnly(MassNavigationShowcaseStepId.DebugVisualBudget, "debug_visual_budget"),
                "U16" => ReplayRuntimeNavDataUpdate(),
                _ => Fail($"Unsupported playable replay use case '{_useCase}'.")
            };

            ClearReplayInput();
            if (advanced)
            {
                _phase++;
            }
        }
        finally
        {
            ClearReplayInput();
        }
    }

    private bool ReplayPathOnly()
    {
        return _phase switch
        {
            0 => ArmPathOperation(MassNavigationShowcaseStepId.PathOnly, "path_only_start_goal"),
            1 => SubmitPathClick("left_click_start", ResolveRouteStart(), _bindings.ConfirmActionId),
            2 => SubmitPathClick("right_click_goal", ResolveRouteGoal(), _bindings.CommandActionId),
            3 => CompletePathTrace("path_only_query"),
            _ => Complete()
        };
    }

    private bool ReplayPathDriven(MassNavigationShowcaseStepId stepId, string operation)
    {
        return _phase switch
        {
            0 => ArmPathOperation(stepId, operation),
            1 => SubmitPathClick("left_click_start", ResolveRouteStart(), _bindings.ConfirmActionId),
            2 => SubmitPathClick("right_click_goal", ResolveRouteGoal(), _bindings.CommandActionId),
            3 => CompletePathTrace(operation),
            _ => Complete()
        };
    }

    private bool ReplayOrderReuse()
    {
        Vector2 target = ResolveOrderDestination();
        Vector2 nearTarget = target + new Vector2(100f, 80f);
        return _phase switch
        {
            0 => SelectArmy(64, MassNavigationShowcaseStepId.OrderReuse, "select_reuse_squad"),
            1 => SubmitMoveOrder("right_click_same_destination_first", target),
            2 => RunFormalOrderBridge("order_bridge_after_first_reuse_order"),
            3 => SubmitMoveOrder("right_click_same_destination_second", target),
            4 => RunFormalOrderBridge("order_bridge_after_second_reuse_order"),
            5 => SubmitMoveOrder("right_click_near_destination", nearTarget),
            6 => RunFormalOrderBridge("order_bridge_after_near_reuse_order"),
            7 => CompleteOrderTrace("order_reuse", requireFlow: false),
            _ => Complete()
        };
    }

    private bool ReplayLargeArmyOrder(MassNavigationShowcaseStepId stepId, string operation, bool requireFlow)
    {
        return _phase switch
        {
            0 => SelectArmy(10_000, stepId, "select_10k_army"),
            1 => SubmitMoveOrder("right_click_destination", ResolveOrderDestination()),
            2 => RunFormalOrderBridge("order_bridge_after_large_selection_order"),
            3 => RunTargetRefreshAndFlow("target_refresh_and_flow_smoke"),
            4 => CompleteOrderTrace(operation, requireFlow),
            _ => Complete()
        };
    }

    private bool ReplayWaypointAuthoring()
    {
        return _phase switch
        {
            0 => ArmPathOperation(MassNavigationShowcaseStepId.WaypointAuthoring, "waypoint_start_goal"),
            1 => SubmitPathClick("left_click_start", ResolveRouteStart(), _bindings.ConfirmActionId),
            2 => SubmitPathClick("right_click_goal", ResolveRouteGoal(), _bindings.CommandActionId),
            3 => SubmitPathClick("left_click_authored_midpoint", ResolveWaypointMidpoint(), _bindings.ConfirmActionId),
            4 => CompleteWaypointTrace(),
            _ => Complete()
        };
    }

    private bool ReplayStreamingWindow()
    {
        switch (_phase)
        {
            case 0:
                _guide.SetStep(MassNavigationShowcaseStepId.LargeWorldStreaming);
                MassNavigationRuntime.RequestStrategicCameraReset(_engine);
                MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);
                Trace("input", "open_large_world_streaming_view", new
                {
                    mode = "playable_world_view",
                    input = "open U11 and inspect active window"
                });
                return true;
            case 1:
                Vector2 target = ResolveRouteGoal();
                MassNavigationRuntime.RequestCameraJump(_engine, target, 58_000f);
                _simulation.ObserveCameraFocus(target);
                Trace("result", "large_world_streaming", new
                {
                    worldWidthCm = _simulation.WorldWidthCm,
                    worldHeightCm = _simulation.WorldHeightCm,
                    macroChunks = _simulation.AcceptanceDiagnostics.HpaMacro.MacroChunkCount,
                    loadedChunks = _simulation.LoadedChunkCount,
                    activeWindowDriver = _simulation.SolverWindowDriver
                });
                return true;
            default:
                return Complete();
        }
    }

    private bool ReplayRuntimeNavDataUpdate()
    {
        return _phase switch
        {
            0 => ArmPathOperation(MassNavigationShowcaseStepId.BakeToolQuery, "runtime_navdata_route_query"),
            1 => SubmitPathClick("left_click_route_start", ResolveRuntimeBakeRouteStart(), _bindings.ConfirmActionId),
            2 => SubmitPathClick("right_click_route_goal", ResolveRuntimeBakeRouteGoal(), _bindings.CommandActionId),
            3 => ArmRuntimeObstaclePolygonReplay(),
            4 => SubmitRuntimeObstacleClick("left_click_obstacle_vertex_a", ResolveRuntimeObstaclePoint(0), _bindings.ConfirmActionId),
            5 => SubmitRuntimeObstacleClick("left_click_obstacle_vertex_b", ResolveRuntimeObstaclePoint(1), _bindings.ConfirmActionId),
            6 => SubmitRuntimeObstacleClick("right_click_obstacle_vertex_c_close", ResolveRuntimeObstaclePoint(2), _bindings.CommandActionId),
            7 => RequestRuntimeNavDataUpdateReplay(),
            8 => CompleteRuntimeNavDataUpdateTrace(),
            _ => Complete()
        };
    }

    private bool ReplayStaticObstacleWorld()
    {
        switch (_phase)
        {
            case 0:
                _guide.SetStep(MassNavigationShowcaseStepId.StaticObstacleWorld);
                _simulation.AcceptanceDiagnostics.RecordObstacleRuntime(
                    _simulation.AcceptanceDiagnostics.StaticObstacleWorld.ActiveWindowLoadedCount,
                    _simulation.MassFlow.UnitCount);
                Trace("input", "inspect_static_obstacle_world", new
                {
                    input = "open U13 obstacle world and inspect baked vs active-window counts"
                });
                return true;
            case 1:
                MassNavigationStaticObstacleWorldDiagnostics obstacles = _simulation.AcceptanceDiagnostics.StaticObstacleWorld;
                Trace("result", "static_obstacle_world", new
                {
                    planned = obstacles.PlannedWorldObstacleCount,
                    coverage = obstacles.MacroChunkCoverageCount,
                    solverActive = obstacles.SolverActiveStaticObstacleCount,
                    capacity = obstacles.SolverStaticObstacleCapacity,
                    dataSource = obstacles.DataSource,
                    activation = obstacles.RuntimeActivationStrategy
                });
                return true;
            default:
                return Complete();
        }
    }

    private bool ReplayDiagnosticsOnly(MassNavigationShowcaseStepId stepId, string operation)
    {
        switch (_phase)
        {
            case 0:
                _guide.SetStep(stepId);
                Trace("input", operation, new
                {
                    input = "open diagnostics mode after runtime data is live",
                    overlay = _guide.CurrentStep.DebugLegend
                });
                return true;
            case 1:
                Trace("result", operation, new
                {
                    selected = _simulation.SelectedCount,
                    fpsScope = "raylib_timing_log",
                    debugNavMesh = _guide.DebugNavMeshEnabled,
                    debugHpa = _guide.DebugHpaEnabled,
                    debugPath = _guide.DebugPathEnabled,
                    debugLayerCost = _guide.DebugLayerCostEnabled,
                    debugSlots = _guide.DebugSlotsEnabled
                });
                return true;
            default:
                return Complete();
        }
    }

    private bool ArmPathOperation(MassNavigationShowcaseStepId stepId, string operation)
    {
        _guide.ArmPathDrivenOperation(stepId);
        Trace("operation", operation, new
        {
            input = _guide.CurrentStep.PlayerInput,
            chain = "AuthoritativeInput -> MassNavigationPathPreviewInputSystem -> PathService/PathStore -> AcceptanceDiagnostics"
        });
        return true;
    }

    private bool SubmitPathClick(string operation, Vector2 worldCm, string actionId)
    {
        InjectPointerClick(worldCm, actionId);
        _pathPreview.Update(1f / 60f);
        Trace("input", operation, new
        {
            actionId,
            worldCm = ToPoint(worldCm),
            consumedBy = nameof(MassNavigationPathPreviewInputSystem),
            pathpoints = _simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount,
            orderDelta = _guide.LastActionOrderDelta
        });
        return true;
    }

    private bool ArmRuntimeObstaclePolygonReplay()
    {
        _guide.ArmRuntimeObstacleAuthoring();
        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
        Trace("operation", "draw_runtime_obstacle_polygon", new
        {
            input = "Draw Poly, left-click two vertices, then right-click the final vertex to close.",
            chain = "AuthoritativeInput -> MassNavigationRuntimeBakeAuthoringInputSystem -> MassNavigationRuntimeBakeAuthoringRuntime",
            pathQueryBeforeAuthoring = _simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount
        });
        return true;
    }

    private bool SubmitRuntimeObstacleClick(string operation, Vector2 worldCm, string actionId)
    {
        InjectPointerClick(worldCm, actionId);
        _runtimeBakeAuthoring.Update(1f / 60f);
        MassNavigationRuntimeBakeAuthoringRuntime authoring = _guide.RuntimeBakeAuthoring;
        Trace("input", operation, new
        {
            actionId,
            worldCm = ToPoint(worldCm),
            consumedBy = nameof(MassNavigationRuntimeBakeAuthoringInputSystem),
            draftPoints = authoring.DraftPointCount,
            authoredPolygons = authoring.AuthoredPolygonCount,
            dirtyChunks = authoring.DirtyChunkCount,
            status = authoring.LastStatus
        });

        if (string.Equals(actionId, _bindings.CommandActionId, StringComparison.Ordinal) &&
            authoring.AuthoredPolygonCount <= 0)
        {
            return Fail("Runtime obstacle polygon did not close during U16 replay.");
        }

        return true;
    }

    private bool RequestRuntimeNavDataUpdateReplay()
    {
        MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics = _guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
            _simulation,
            _engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
            _engine.GetService(CoreServiceKeys.NavQueryServices),
            _engine.GetService(CoreServiceKeys.NavMeshProfiles),
            _engine.GetService(CoreServiceKeys.PathService),
            _engine.GetService(CoreServiceKeys.PathStore));
        _guide.RecordRuntimeNavDataUpdateResult(diagnostics);
        Trace("system", "update_navdata", new
        {
            diagnostics.Status,
            diagnostics.UpdateSource,
            diagnostics.NavDataRevision,
            diagnostics.AuthoredPolygonCount,
            diagnostics.DirtyChunkCount,
            diagnostics.ReloadedTileCount,
            diagnostics.BakedTileCount,
            diagnostics.ChangedTileCount,
            diagnostics.BeforeTriangleCount,
            diagnostics.AfterTriangleCount,
            diagnostics.BeforeChecksumXor,
            diagnostics.AfterChecksumXor,
            diagnostics.BeforeGeometryHashXor,
            diagnostics.AfterGeometryHashXor,
            diagnostics.QueryStatusAfterUpdate,
            diagnostics.QueryPathPointCount,
            diagnostics.QueryTouchedTileCount,
            diagnostics.FlowObstacleRefreshQueued,
            diagnostics.ProductionGap
        });

        if (diagnostics.NavDataRevision <= 0 ||
            diagnostics.AuthoredPolygonCount <= 0 ||
            diagnostics.DirtyChunkCount <= 0 ||
            diagnostics.BakedTileCount <= 0 ||
            diagnostics.ChangedTileCount <= 0 ||
            diagnostics.QueryPathPointCount <= 0)
        {
            return Fail("Runtime NavData update replay did not produce polygon, dirty chunks, baked/changed tiles, revision, and refreshed query evidence.");
        }

        return true;
    }

    private bool SelectArmy(int requestedCount, MassNavigationShowcaseStepId stepId, string operation)
    {
        int selected = TrySelectArmy(requestedCount);
        if (selected <= 0)
        {
            return Fail($"SelectionRuntime could not select army for {operation}.");
        }

        if (stepId == MassNavigationShowcaseStepId.OrderReuse)
        {
            _guide.RecordOrderReuseSelectionPrepared(selected);
        }
        else
        {
            _guide.RecordLargeSelectionPrepared(stepId, selected);
        }

        Trace("input", operation, new
        {
            requested = requestedCount,
            selected,
            chain = "SelectionRuntime.LivePrimary -> MassNavigationSelectionSync"
        });
        return true;
    }

    private bool SubmitMoveOrder(string operation, Vector2 destination)
    {
        InjectPointerClick(destination, _bindings.CommandActionId);
        _commandBridge.Update(1f / 60f);
        Trace("input", operation, new
        {
            actionId = _bindings.CommandActionId,
            destination = ToPoint(destination),
            selected = _simulation.SelectedCount,
            pendingCommands = _simulation.PendingCommandCount,
            orderReuse = ToOrderReuse()
        });
        return true;
    }

    private bool RunFormalOrderBridge(string operation)
    {
        if (!string.Equals(_orderBridgeReplayOperation, operation, StringComparison.Ordinal))
        {
            _orderBridgeReplayOperation = operation;
            _orderBridgeReplayFrames = 0;
        }

        _orderBridgeReplayFrames++;
        _orderBridge.Update(1f / 60f);
        bool activeGroupAvailable = _simulation.NavGroupRuntime.ActiveOrderGroupCount > 0;
        bool allocationComplete = _simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount >= _simulation.SelectedCount;
        Trace("system", operation, new
        {
            activeOrderGroups = _simulation.NavGroupRuntime.ActiveOrderGroupCount,
            appliedTargetRefresh = _simulation.NavGroupRuntime.AppliedTargetRefreshCountFrame,
            pendingTargetRefresh = _simulation.NavGroupRuntime.PendingTargetRefreshCount,
            targetRefreshBudget = _simulation.NavGroupRuntime.TargetRefreshBudget,
            replayFrames = _orderBridgeReplayFrames,
            maxSeparationNeighborsPerUnit = _simulation.MassFlow.Semantics.Steering.MaxSeparationNeighborsPerUnit,
            orderReuse = ToOrderReuse(),
            allocation = ToTargetAllocation()
        });
        if (!activeGroupAvailable || !allocationComplete)
        {
            return false;
        }

        _orderBridgeReplayOperation = string.Empty;
        _orderBridgeReplayFrames = 0;
        return true;
    }

    private bool RunTargetRefreshAndFlow(string operation)
    {
        Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(_engine.World, _engine.GlobalContext);
        if (!_targetRefreshReplayStarted)
        {
            _targetRefreshReplayStarted = true;
            _targetRefreshReplayFrames = 0;
            Trace("system", $"{operation}_started", new
            {
                activeOrderGroups = _simulation.NavGroupRuntime.ActiveOrderGroupCount,
                pendingTargetRefresh = _simulation.NavGroupRuntime.PendingTargetRefreshCount,
                targetRefreshBudget = _simulation.NavGroupRuntime.TargetRefreshBudget,
                selected = selected.Length,
                chain = "NavGroupRuntime target refresh is replayed over multiple frames, matching the runtime budget."
            });
        }

        _targetRefreshReplayFrames++;
        if (_simulation.NavGroupRuntime.PendingTargetRefreshCount > 0)
        {
            _simulation.NavGroupRuntime.UpdateTargets(
                _simulation.MassFlow,
                _simulation.AgentState,
                selected,
                _simulation.FrameIndex);
        }

        bool advanced = _simulation.MassFlow.AdvanceFlowPipeline(
            _simulation.FlowTuning,
            _simulation.FrameIndex,
            _simulation.ObserveFlowFieldRebuild);

            if (_simulation.NavGroupRuntime.PendingTargetRefreshCount > 0)
            {
                return false;
            }

            _simulation.AcceptanceDiagnostics.RecordTargetSamples(
                _simulation.MassFlow,
                ResolveSelectedControllableIndices(selected),
                maxSamples: 96);

            Trace("system", operation, new
            {
            activeOrderGroups = _simulation.NavGroupRuntime.ActiveOrderGroupCount,
            appliedTargetRefresh = _simulation.NavGroupRuntime.AppliedTargetRefreshCountFrame,
            pendingTargetRefresh = _simulation.NavGroupRuntime.PendingTargetRefreshCount,
            targetRefreshBudget = _simulation.NavGroupRuntime.TargetRefreshBudget,
            replayFrames = _targetRefreshReplayFrames,
            maxSeparationNeighborsPerUnit = _simulation.MassFlow.Semantics.Steering.MaxSeparationNeighborsPerUnit,
            unitsWithTargets = _simulation.MassFlow.CountUnitsWithTargets(),
                moving = _simulation.MassFlow.CountMovingUnits(0.0001f),
                settled = _simulation.MassFlow.SettledUnitCount,
                flowEnabled = _simulation.FlowTuning.Enabled,
                flowAdvanced = advanced,
                allocation = ToTargetAllocation()
            });
        _targetRefreshReplayStarted = false;
        _targetRefreshReplayFrames = 0;
        return true;
    }

    private bool CompletePathTrace(string operation)
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        Trace("result", operation, new
        {
            available = path.Available,
            status = path.Status,
            noOrderSubmitted = path.NoOrderSubmitted,
            start = ToPoint(path.StartWorldCm),
            goal = ToPoint(path.GoalWorldCm),
            pathpoints = path.PathPointCount,
            routeChunks = path.MacroRouteChunkCount,
            expandedChunks = path.MacroExpandedChunkCount,
            hpa = ToHpa(),
            orderDelta = _guide.LastActionOrderDelta
        });
        return true;
    }

    private bool CompleteWaypointTrace()
    {
        MassNavigationWaypointPathDiagnostics waypoint = _simulation.AcceptanceDiagnostics.WaypointPath;
        Trace("result", "waypoint_authoring", new
        {
            waypoint.WaypointCount,
            waypoint.PathPointCount,
            waypoint.WaypointsEditable,
            waypoint.PathPointsImmutable,
            waypoint.HasAuthoredPlan,
            waypoint.EditRevision,
            waypoint.InvalidatedPathPointCount,
            authoredMidpoint = ToPoint(waypoint.AuthoredMidpointWorldCm),
            orderDelta = _guide.LastActionOrderDelta
        });
        return true;
    }

    private bool CompleteOrderTrace(string operation, bool requireFlow)
    {
        int targets = _simulation.MassFlow.CountUnitsWithTargets();
        Trace("result", operation, new
        {
            selected = _simulation.SelectedCount,
            lastCommandSelection = _simulation.LastCommandSelectionCount,
            activeOrderGroups = _simulation.NavGroupRuntime.ActiveOrderGroupCount,
            unitsWithTargets = targets,
            pendingTargetRefresh = _simulation.NavGroupRuntime.PendingTargetRefreshCount,
            targetRefreshBudget = _simulation.NavGroupRuntime.TargetRefreshBudget,
            moving = _simulation.MassFlow.CountMovingUnits(0.0001f),
            settled = _simulation.MassFlow.SettledUnitCount,
            flowEnabled = _simulation.FlowTuning.Enabled,
            requireFlow,
            orderReuse = ToOrderReuse(),
            allocation = ToTargetAllocation()
        });
        return true;
    }

    private bool CompleteRuntimeNavDataUpdateTrace()
    {
        MassNavigationRuntimeNavDataUpdateDiagnostics update = _simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        Trace("result", "runtime_navdata_authoring_update", new
        {
            update.Available,
            update.Status,
            update.UpdateSource,
            update.AuthoredPolygonCount,
            update.DirtyChunkCount,
            update.ReloadedTileCount,
            update.BakedTileCount,
            update.ChangedTileCount,
            update.BeforeTriangleCount,
            update.AfterTriangleCount,
            update.BeforeChecksumXor,
            update.AfterChecksumXor,
            update.BeforeGeometryHashXor,
            update.AfterGeometryHashXor,
            update.NavDataRevision,
            update.QueryStatusAfterUpdate,
            update.QueryPathPointCount,
            update.QueryTouchedTileCount,
            update.FlowObstacleRefreshQueued,
            update.ProductionGap,
            path = new
            {
                path.Available,
                path.Status,
                start = ToPoint(path.StartWorldCm),
                goal = ToPoint(path.GoalWorldCm),
                path.PathPointCount,
                path.NoOrderSubmitted
            }
        });
        return true;
    }

    private bool Complete()
    {
        _completed = true;
        Trace("complete", _useCase, new
        {
            frame = _simulation.FrameIndex,
            step = _guide.CurrentStepId.ToString(),
            action = _guide.LastActionText
        });
        FlushTrace();
        return false;
    }

    private bool Fail(string reason)
    {
        _failed = true;
        Trace("failed", _useCase, new
        {
            reason,
            frame = _simulation.FrameIndex,
            step = _guide.CurrentStepId.ToString()
        });
        FlushTrace();
        return false;
    }

    private void EnsureReplayInputInstalled()
    {
        if (_inputInstalled)
        {
            return;
        }

        _engine.SetService(CoreServiceKeys.AuthoritativeInput, _replayInput);
        _engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, _replayPointerButtons);
        _inputInstalled = true;
        Trace("setup", "replay_input_installed", new
        {
            useCase = _useCase,
            startFrame = _startFrame,
            targetRefreshBudget = _simulation.NavGroupRuntime.TargetRefreshBudget,
            maxSeparationNeighborsPerUnit = _simulation.MassFlow.Semantics.Steering.MaxSeparationNeighborsPerUnit,
            chain = "authoritative replay input replaces live input only while LUDOTS_MASS_NAV_REPLAY_USECASE is set"
        });
    }

    private void InjectPointerClick(Vector2 worldCm, string actionId)
    {
        _replayInput.Clear();
        _replayInput.SetActionState(
            AuthoritativeGroundPointerHelper.ActionId,
            new Vector3(worldCm.X, 0f, worldCm.Y),
            isDown: true,
            pressedThisFrame: false,
            releasedThisFrame: false);
        _replayInput.SetActionState(
            actionId,
            Vector3.One,
            isDown: false,
            pressedThisFrame: true,
            releasedThisFrame: false);
        _replayInput.SetActionState(
            _bindings.PointerPositionActionId,
            new Vector3(worldCm.X, worldCm.Y, 0f),
            isDown: true,
            pressedThisFrame: false,
            releasedThisFrame: false);

        _replayPointerButtons.Clear();
        _replayPointerButtons.SetState(
            _bindings.ConfirmActionId,
            new PointerButtonState(
                pointer: Vector2.Zero,
                pressPointer: Vector2.Zero,
                releasePointer: Vector2.Zero,
                lastDownPointer: Vector2.Zero,
                isDown: false,
                pressedThisFrame: string.Equals(actionId, _bindings.ConfirmActionId, StringComparison.Ordinal),
                releasedThisFrame: false,
                hasPressPointer: string.Equals(actionId, _bindings.ConfirmActionId, StringComparison.Ordinal),
                hasReleasePointer: false,
                hasLastDownPointer: false));
        if (!string.Equals(actionId, _bindings.ConfirmActionId, StringComparison.Ordinal))
        {
            _replayPointerButtons.SetState(
                actionId,
                new PointerButtonState(
                    pointer: Vector2.Zero,
                    pressPointer: Vector2.Zero,
                    releasePointer: Vector2.Zero,
                    lastDownPointer: Vector2.Zero,
                    isDown: false,
                    pressedThisFrame: true,
                    releasedThisFrame: false,
                    hasPressPointer: true,
                    hasReleasePointer: false,
                    hasLastDownPointer: false));
        }

        _engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
    }

    private void ClearReplayInput()
    {
        _replayInput.Clear();
        _replayPointerButtons.Clear();
        _engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
    }

    private int TrySelectArmy(int requestedCount)
    {
        SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime);
        if (selection == null ||
            !_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity owner ||
            !_engine.World.IsAlive(owner))
        {
            return 0;
        }

        int count = Math.Min(Math.Max(1, requestedCount), _simulation.AgentState.ControllableCount);
        if (count <= 0)
        {
            return 0;
        }

        Entity[] entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = _simulation.AgentState.ControllableAgents[i];
        }

        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, entities))
        {
            return 0;
        }

        MassNavigationSelectionSync.SyncIfChanged(_engine.World, _engine.GlobalContext, selection, _simulation);
        return _simulation.SelectedCount;
    }

    private Vector2 ResolveRouteStart()
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake != null && bake.MacroChunkSizeXCm > 0 && bake.MacroChunkSizeYCm > 0)
        {
            return new Vector2(
                bake.WorldMinXCm + Math.Max(1_000f, bake.MacroChunkSizeXCm * 8.5f),
                bake.WorldMinYCm + Math.Max(1_000f, bake.MacroChunkSizeYCm * 9.5f));
        }

        return new Vector2(
            _simulation.SolverWindowCenterXCm - Math.Max(2_000f, _simulation.SolverWindowWidthCm * 0.25f),
            _simulation.SolverWindowCenterYCm - Math.Max(2_000f, _simulation.SolverWindowHeightCm * 0.20f));
    }

    private Vector2 ResolveRouteGoal()
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake != null && bake.MacroChunkSizeXCm > 0 && bake.MacroChunkSizeYCm > 0)
        {
            return new Vector2(
                bake.WorldMinXCm + bake.WorldWidthCm - Math.Max(1_000f, bake.MacroChunkSizeXCm * 7.5f),
                bake.WorldMinYCm + bake.WorldHeightCm - Math.Max(1_000f, bake.MacroChunkSizeYCm * 8.5f));
        }

        return new Vector2(
            _simulation.SolverWindowCenterXCm + Math.Max(2_000f, _simulation.SolverWindowWidthCm * 0.35f),
            _simulation.SolverWindowCenterYCm + Math.Max(2_000f, _simulation.SolverWindowHeightCm * 0.25f));
    }

    private Vector2 ResolveOrderDestination()
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (path.GoalWorldCm != Vector2.Zero)
        {
            return path.GoalWorldCm;
        }

        return new Vector2(
            _simulation.SolverWindowCenterXCm + Math.Max(2_500f, _simulation.SolverWindowWidthCm * 0.30f),
            _simulation.SolverWindowCenterYCm + Math.Max(2_000f, _simulation.SolverWindowHeightCm * 0.22f));
    }

    private Vector2 ResolveWaypointMidpoint()
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (path.StartWorldCm != Vector2.Zero && path.GoalWorldCm != Vector2.Zero)
        {
            Vector2 center = (path.StartWorldCm + path.GoalWorldCm) * 0.5f;
            return center + new Vector2(1_800f, -1_200f);
        }

        return (ResolveRouteStart() + ResolveRouteGoal()) * 0.5f;
    }

    private Vector2 ResolveRuntimeBakeRouteStart()
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (path.Available && path.StartWorldCm != Vector2.Zero)
        {
            return path.StartWorldCm;
        }

        ResolveRuntimeBakeSampleChunk(out int chunkX, out int chunkY);
        return ResolveRuntimePointInChunk(chunkX, chunkY, 0.22f, 0.22f);
    }

    private Vector2 ResolveRuntimeBakeRouteGoal()
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (path.Available && path.GoalWorldCm != Vector2.Zero)
        {
            return path.GoalWorldCm;
        }

        ResolveRuntimeBakeSampleChunk(out int chunkX, out int chunkY);
        return ResolveRuntimePointInChunk(chunkX, chunkY, 0.76f, 0.70f);
    }

    private Vector2 ResolveRuntimeObstaclePoint(int index)
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (path.Available && path.StartWorldCm != Vector2.Zero && path.GoalWorldCm != Vector2.Zero)
        {
            Vector2 route = path.GoalWorldCm - path.StartWorldCm;
            if (route.LengthSquared() > 1f)
            {
                Vector2 center = path.StartWorldCm + (route * 0.50f);
                Vector2 normal = Vector2.Normalize(new Vector2(-route.Y, route.X));
                Vector2 tangent = Vector2.Normalize(route);
                float width = MathF.Max(700f, MathF.Min(1_600f, route.Length() * 0.08f));
                float height = MathF.Max(600f, MathF.Min(1_400f, route.Length() * 0.06f));
                ReadOnlySpan<Vector2> offsets = stackalloc Vector2[]
                {
                    (normal * height) - (tangent * width),
                    (normal * -height) + (tangent * width),
                    (normal * height) + (tangent * width)
                };

                return center + offsets[Math.Clamp(index, 0, offsets.Length - 1)];
            }
        }

        ReadOnlySpan<Vector2> local = stackalloc Vector2[]
        {
            new(0.38f, 0.50f),
            new(0.58f, 0.42f),
            new(0.58f, 0.62f)
        };
        ResolveRuntimeBakeSampleChunk(out int chunkX, out int chunkY);
        Vector2 point = local[Math.Clamp(index, 0, local.Length - 1)];
        Vector2 resolved = ResolveRuntimePointInChunk(chunkX, chunkY, point.X, point.Y);
        if (resolved != new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm))
        {
            return resolved;
        }

        ReadOnlySpan<Vector2> fallbackOffsets = stackalloc Vector2[]
        {
            new(-900f, -1_600f),
            new(1_700f, -200f),
            new(-400f, 1_700f)
        };
        return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm) +
            fallbackOffsets[Math.Clamp(index, 0, fallbackOffsets.Length - 1)];
    }

    private void ResolveRuntimeBakeSampleChunk(out int chunkX, out int chunkY)
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (_guide.NavMeshSample.Available && bake != null)
        {
            chunkX = Math.Clamp(_guide.NavMeshSample.ChunkX, 0, Math.Max(0, bake.MacroChunkColumns - 1));
            chunkY = Math.Clamp(_guide.NavMeshSample.ChunkY, 0, Math.Max(0, bake.MacroChunkRows - 1));
            return;
        }

        if (bake != null)
        {
            chunkX = Math.Clamp(bake.MacroChunkColumns / 2, 0, Math.Max(0, bake.MacroChunkColumns - 1));
            chunkY = Math.Clamp(bake.MacroChunkRows / 2, 0, Math.Max(0, bake.MacroChunkRows - 1));
            return;
        }

        chunkX = 0;
        chunkY = 0;
    }

    private Vector2 ResolveRuntimePointInChunk(int chunkX, int chunkY, float localX01, float localY01)
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake == null ||
            bake.MacroChunkSizeXCm <= 0 ||
            bake.MacroChunkSizeYCm <= 0 ||
            bake.MacroChunkColumns <= 0 ||
            bake.MacroChunkRows <= 0)
        {
            return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        }

        int x = Math.Clamp(chunkX, 0, bake.MacroChunkColumns - 1);
        int y = Math.Clamp(chunkY, 0, bake.MacroChunkRows - 1);
        return new Vector2(
            bake.WorldMinXCm + (x * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * Math.Clamp(localX01, 0.05f, 0.95f)),
            bake.WorldMinYCm + (y * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * Math.Clamp(localY01, 0.05f, 0.95f)));
    }

    private object ToOrderReuse()
    {
        MassNavigationOrderReuseDiagnostics reuse = _simulation.AcceptanceDiagnostics.OrderReuse;
        return new
        {
            reuse.HasOrder,
            reuse.LastOrderId,
            reuse.CacheHit,
            reuse.ReusedRouteId,
            reuse.RouteCacheSize,
            reuse.FanoutCount,
            reuse.SamePointReuseCount,
            reuse.NearPointReuseCount,
            reuse.ReuseScope,
            reuse.NormalizedKey,
            reuse.PathRouteSignature,
            reuse.MeshRouteSignature
        };
    }

    private object ToTargetAllocation()
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        return new
        {
            allocation.HasAllocation,
            allocation.SelectedCount,
            allocation.SlotCount,
            allocation.ReachableSlotCount,
            allocation.BlockedSlotCount,
            allocation.FallbackSlotCount,
            allocation.AllocationRouteId,
            allocation.ReachabilityProbeStatus,
            allocation.ReachabilitySource,
            allocation.MeshReachabilityStatus,
            allocation.ActualTargetSampleCount,
            allocation.ActualTargetSampleSource
        };
    }

    private int[] ResolveSelectedControllableIndices(Entity[] selected)
    {
        if (selected.Length == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new int[selected.Length];
        int count = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            if (_simulation.AgentState.TryGetControllableIndex(selected[i], out int unitIndex))
            {
                indices[count++] = unitIndex;
            }
        }

        if (count == indices.Length)
        {
            return indices;
        }

        Array.Resize(ref indices, count);
        return indices;
    }

    private object ToHpa()
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        return new
        {
            hpa.Available,
            hpa.MacroChunkColumns,
            hpa.MacroChunkRows,
            hpa.SampleRouteChunkCount,
            hpa.SamplePortalCount,
            hpa.StartMacroChunkX,
            hpa.StartMacroChunkY,
            hpa.GoalMacroChunkX,
            hpa.GoalMacroChunkY,
            hpa.RouteSource,
            hpa.UsesSyntheticMacroGridTarget
        };
    }

    private static object ToPoint(Vector2 value)
    {
        return new { x = MathF.Round(value.X, 1), y = MathF.Round(value.Y, 1) };
    }

    private void Trace(string kind, string operation, object payload)
    {
        string line = JsonSerializer.Serialize(new
        {
            schema = "mass-navigation.operation-trace.v1",
            timeUtc = DateTime.UtcNow,
            frame = _simulation.FrameIndex,
            useCase = _useCase,
            phase = _phase,
            kind,
            operation,
            payload
        });
        _traceLines.Add(line);
    }

    private void FlushTrace()
    {
        if (_traceFlushed || _traceLines.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_tracePath) ?? ".");
        File.WriteAllLines(_tracePath, _traceLines);
        _traceFlushed = true;
    }
}
