using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    public sealed class SelectedMovePathPresentationSystem : ISystem<float>
    {
        public const string DebugSummaryKey = "CoreInputMod.SelectedMovePath.DebugSummary";

        private const string DebugEnvironmentVariable = "LUDOTS_DEBUG_SELECTED_MOVE_PATH";
        private static readonly Vector4 DebugTextColor = new(0.92f, 0.97f, 1.0f, 1.0f);
        private static readonly Vector4 DebugPanelFill = new(0.04f, 0.08f, 0.12f, 0.92f);
        private static readonly Vector4 DebugPanelBorder = new(0.30f, 0.58f, 0.78f, 0.96f);

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private Entity[] _selected = new Entity[16];
        private readonly bool _debugEnabled;
        private SelectedMovePathOverlayBridge? _bridge;
        private GroundOverlayBuffer? _groundOverlays;
        private string _lastBridgeFailureReason = "bridge=uninitialized";

        public SelectedMovePathPresentationSystem(World world, Dictionary<string, object> globals, SelectionRuntime selection)
        {
            _world = world;
            _globals = globals;
            _selection = selection;
            _debugEnabled = IsDebugEnabled();
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (_bridge == null && !TryCreateBridge(out _bridge))
            {
                PublishDebugState(
                    selectionCount: 0,
                    overlayLineDelta: 0,
                    overlayCircleDelta: 0,
                    selectionViewer: Entity.Null,
                    selectionViewKey: SelectionViewKeys.Primary,
                    viewedContainer: Entity.Null,
                    primaryViewed: Entity.Null);
                return;
            }

            Entity selectionViewer = Entity.Null;
            string selectionViewKey = SelectionViewKeys.Primary;
            Entity viewedContainer = Entity.Null;
            SelectionViewRuntime.TryResolveViewedSelection(_world, _globals, _selection, out selectionViewer, out selectionViewKey, out viewedContainer);
            Entity primaryViewed = SelectionViewRuntime.TryGetViewedPrimary(_world, _globals, _selection, out var primary)
                ? primary
                : Entity.Null;

            int lineCountBefore = CountOverlayShape(GroundOverlayShape.Line);
            int circleCountBefore = CountOverlayShape(GroundOverlayShape.Circle);
            int viewedCount = SelectionViewRuntime.GetViewedSelectionCount(_world, _globals, _selection);
            EnsureSelectedCapacity(viewedCount);
            int count = SelectionViewRuntime.CopyViewedSelection(_world, _globals, _selection, _selected);
            if (count <= 0)
            {
                PublishDebugState(
                    selectionCount: 0,
                    overlayLineDelta: 0,
                    overlayCircleDelta: 0,
                    selectionViewer: selectionViewer,
                    selectionViewKey: selectionViewKey,
                    viewedContainer: viewedContainer,
                    primaryViewed: primaryViewed);
                return;
            }

            _bridge.UpdateViewedSelection(new ReadOnlySpan<Entity>(_selected, 0, count));
            PublishDebugState(
                selectionCount: count,
                overlayLineDelta: CountOverlayShape(GroundOverlayShape.Line) - lineCountBefore,
                overlayCircleDelta: CountOverlayShape(GroundOverlayShape.Circle) - circleCountBefore,
                selectionViewer: selectionViewer,
                selectionViewKey: selectionViewKey,
                viewedContainer: viewedContainer,
                primaryViewed: primaryViewed);
        }

        private bool TryCreateBridge(out SelectedMovePathOverlayBridge bridge)
        {
            bridge = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) ||
                configObj is not GameConfig config)
            {
                _lastBridgeFailureReason = "bridge=missing:GameConfig";
                return false;
            }

            if (!config.Constants.OrderTypeIds.TryGetValue("moveTo", out int moveToOrderTypeId) ||
                moveToOrderTypeId <= 0)
            {
                _lastBridgeFailureReason = "bridge=missing:moveToOrderType";
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.PathService.Name, out var pathServiceObj) ||
                pathServiceObj is not IPathService pathService)
            {
                _lastBridgeFailureReason = "bridge=missing:PathService";
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.PathStore.Name, out var pathStoreObj) ||
                pathStoreObj is not PathStore pathStore)
            {
                _lastBridgeFailureReason = "bridge=missing:PathStore";
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.GroundOverlayBuffer.Name, out var overlaysObj) ||
                overlaysObj is not GroundOverlayBuffer overlays)
            {
                _lastBridgeFailureReason = "bridge=missing:GroundOverlayBuffer";
                return false;
            }

            _groundOverlays = overlays;
            _lastBridgeFailureReason = $"bridge=ready:path={pathService.GetType().Name}";
            bridge = new SelectedMovePathOverlayBridge(_world, pathService, pathStore, overlays, moveToOrderTypeId);
            return true;
        }

        private void PublishDebugState(
            int selectionCount,
            int overlayLineDelta,
            int overlayCircleDelta,
            Entity selectionViewer,
            string selectionViewKey,
            Entity viewedContainer,
            Entity primaryViewed)
        {
            string summary = BuildDebugSummary(
                selectionCount,
                overlayLineDelta,
                overlayCircleDelta,
                selectionViewer,
                selectionViewKey,
                viewedContainer,
                primaryViewed);
            _globals[DebugSummaryKey] = summary;
            if (!_debugEnabled)
            {
                return;
            }

            if (_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) &&
                overlayObj is ScreenOverlayBuffer overlay)
            {
                overlay.AddRect(8, 8, 980, 118, DebugPanelFill, DebugPanelBorder, stableId: 31001, dirtySerial: 1);
                string[] lines = BuildDebugLines(summary);
                for (int i = 0; i < lines.Length; i++)
                {
                    overlay.AddText(18, 16 + (i * 18), lines[i], 14, DebugTextColor, stableId: 31010 + i, dirtySerial: 1);
                }
            }
        }

        private string BuildDebugSummary(
            int selectionCount,
            int overlayLineDelta,
            int overlayCircleDelta,
            Entity selectionViewer,
            string selectionViewKey,
            Entity viewedContainer,
            Entity primaryViewed)
        {
            Entity inspected = primaryViewed;

            bool hasOrderBuffer = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<OrderBuffer>(inspected);
            bool hasNavAgent = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<NavAgent2D>(inspected);
            bool hasPosition2D = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<Position2D>(inspected);
            bool hasVelocity2D = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<Velocity2D>(inspected);
            bool hasNavGoal = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<NavGoal2D>(inspected);
            int activeMoveCount = 0;
            int queuedMoveCount = 0;
            string navGoalSummary = "goal=(none)";
            string positionSummary = "pos2D=(none)";
            string worldSummary = "world=(none)";
            if (hasOrderBuffer && TryResolveMoveToOrderTypeId(out int moveToOrderTypeId))
            {
                ref var buffer = ref _world.Get<OrderBuffer>(inspected);
                if (buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == moveToOrderTypeId)
                {
                    activeMoveCount = 1;
                }

                for (int i = 0; i < buffer.QueuedCount; i++)
                {
                    if (buffer.GetQueued(i).Order.OrderTypeId == moveToOrderTypeId)
                    {
                        queuedMoveCount++;
                    }
                }
            }

            if (inspected != Entity.Null && _world.IsAlive(inspected))
            {
                if (_world.TryGet(inspected, out Position2D position2D))
                {
                    positionSummary = $"pos2D=({position2D.Value.X.ToFloat():0.#},{position2D.Value.Y.ToFloat():0.#})";
                }

                if (_world.TryGet(inspected, out WorldPositionCm worldPosition))
                {
                    Vector2 world = worldPosition.Value.ToVector2();
                    worldSummary = $"world=({world.X:0.#},{world.Y:0.#})";
                }

                if (_world.TryGet(inspected, out NavGoal2D navGoal))
                {
                    navGoalSummary = $"goal={navGoal.Kind}@({navGoal.TargetCm.X.ToFloat():0.#},{navGoal.TargetCm.Y.ToFloat():0.#}) r={navGoal.RadiusCm.ToFloat():0.#}";
                }
            }

            return
                $"{_lastBridgeFailureReason} " +
                $"viewer={DescribeEntity(selectionViewer)} view={selectionViewKey} container={DescribeEntity(viewedContainer)} " +
                $"selCount={selectionCount} primary={DescribeEntity(primaryViewed)} " +
                $"inspect={DescribeEntity(inspected)} orderBuf={hasOrderBuffer} activeMove={activeMoveCount} queuedMove={queuedMoveCount} " +
                $"navAgent={hasNavAgent} pos2D={hasPosition2D} vel2D={hasVelocity2D} navGoal={hasNavGoal} " +
                $"{positionSummary} {worldSummary} {navGoalSummary} " +
                $"overlay+line={overlayLineDelta} overlay+circle={overlayCircleDelta}";
        }

        private string[] BuildDebugLines(string summary)
        {
            return new[]
            {
                "MovePath Debug",
                summary,
                $"selectionView={DescribeSelectionViewContext()}",
                $"overlayTotals=line={CountOverlayShape(GroundOverlayShape.Line)} circle={CountOverlayShape(GroundOverlayShape.Circle)} ring={CountOverlayShape(GroundOverlayShape.Ring)}",
                $"env:{DebugEnvironmentVariable}=1",
            };
        }

        private string DescribeSelectionViewContext()
        {
            bool hasView = SelectionViewRuntime.TryResolveViewedSelection(_world, _globals, _selection, out var viewer, out var viewKey, out var container);
            return hasView
                ? $"viewer={DescribeEntity(viewer)} view={viewKey} container={DescribeEntity(container)}"
                : "viewer=(none)";
        }

        private bool TryResolveMoveToOrderTypeId(out int moveToOrderTypeId)
        {
            moveToOrderTypeId = 0;
            return _globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) &&
                   configObj is GameConfig config &&
                   config.Constants.OrderTypeIds.TryGetValue("moveTo", out moveToOrderTypeId) &&
                   moveToOrderTypeId > 0;
        }

        private int CountOverlayShape(GroundOverlayShape shape)
        {
            if (_groundOverlays == null)
            {
                if (_globals.TryGetValue(CoreServiceKeys.GroundOverlayBuffer.Name, out var overlaysObj) &&
                    overlaysObj is GroundOverlayBuffer overlays)
                {
                    _groundOverlays = overlays;
                }
                else
                {
                    return 0;
                }
            }

            int count = 0;
            ReadOnlySpan<GroundOverlayItem> span = _groundOverlays.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private string DescribeEntity(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return "(none)";
            }

            if (!_world.IsAlive(entity))
            {
                return $"#{entity.Id}(dead)";
            }

            if (_world.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value))
            {
                return $"{name.Value}#{entity.Id}";
            }

            return $"#{entity.Id}";
        }

        private static bool IsDebugEnabled()
        {
            string? raw = Environment.GetEnvironmentVariable(DebugEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw == "1" ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureSelectedCapacity(int required)
        {
            if (required <= _selected.Length)
            {
                return;
            }

            int next = _selected.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selected, next);
        }
    }
}
