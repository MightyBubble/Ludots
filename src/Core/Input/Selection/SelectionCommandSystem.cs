using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionCommandSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly QueryDescription _query = new QueryDescription().WithAll<WorldPositionCm>();
        private readonly List<Entity> _scratchSelection = new(SelectionBuffer.CAPACITY);

        public SelectionCommandSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!TryResolveDependencies(out var handler, out var policy, out var projector))
            {
                return;
            }

            if (!SelectionRuntime.TryGetController(_world, _globals, out var controller))
            {
                return;
            }

            SelectionRuntime.PruneSelection(_world, _globals, controller, policy);
            handler.Update(dt);
            while (handler.Poll(out var command))
            {
                ProcessCommand(controller, policy, projector, in command);
            }
        }

        private void ProcessCommand(Entity controller, ISelectionCandidatePolicy policy, IScreenProjector projector, in SelectionInputCommand command)
        {
            switch (command.Kind)
            {
                case SelectionCommandKind.Clear:
                    SelectionRuntime.ClearSelection(_world, _globals, controller);
                    return;
                case SelectionCommandKind.SaveGroup:
                    SelectionRuntime.SaveGroup(_world, controller, command.GroupIndex);
                    return;
                case SelectionCommandKind.RecallGroup:
                    SelectionRuntime.RecallGroup(_world, _globals, controller, command.GroupIndex);
                    return;
                case SelectionCommandKind.SelectAtPoint:
                    ProcessPointSelection(controller, policy, projector, in command);
                    return;
                case SelectionCommandKind.SelectInRectangle:
                    ProcessRectangleSelection(controller, policy, projector, in command);
                    return;
                case SelectionCommandKind.SelectInPolygon:
                    ProcessPolygonSelection(controller, policy, projector, in command);
                    return;
            }
        }

        private void ProcessPointSelection(Entity controller, ISelectionCandidatePolicy policy, IScreenProjector projector, in SelectionInputCommand command)
        {
            _scratchSelection.Clear();
            Entity best = default;
            float bestDistanceSq = command.PickRadiusPx * command.PickRadiusPx;

            foreach (ref var chunk in _world.Query(in _query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!IsSelectableCandidate(policy, controller, entity))
                    {
                        continue;
                    }

                    Vector2 screen = ProjectToScreen(projector, positions[i]);
                    if (!IsValidScreen(screen))
                    {
                        continue;
                    }

                    float distanceSq = Vector2.DistanceSquared(screen, command.PointScreen);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        best = entity;
                    }
                }
            }

            if (_world.IsAlive(best))
            {
                if (command.ExpandSameClassFromResolvedCandidate)
                {
                    CollectSameClassVisible(policy, controller, projector, best, _scratchSelection);
                }
                else
                {
                    _scratchSelection.Add(best);
                }
            }

            SelectionRuntime.ApplySelection(_world, _globals, controller, _scratchSelection, command.ApplyMode);
        }

        private void ProcessRectangleSelection(Entity controller, ISelectionCandidatePolicy policy, IScreenProjector projector, in SelectionInputCommand command)
        {
            _scratchSelection.Clear();
            Vector2 min = Vector2.Min(command.RectangleMinScreen, command.RectangleMaxScreen);
            Vector2 max = Vector2.Max(command.RectangleMinScreen, command.RectangleMaxScreen);

            foreach (ref var chunk in _world.Query(in _query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count && _scratchSelection.Count < SelectionBuffer.CAPACITY; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!IsSelectableCandidate(policy, controller, entity))
                    {
                        continue;
                    }

                    Vector2 screen = ProjectToScreen(projector, positions[i]);
                    if (!IsValidScreen(screen))
                    {
                        continue;
                    }

                    if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                    {
                        _scratchSelection.Add(entity);
                    }
                }
            }

            SelectionRuntime.ApplySelection(_world, _globals, controller, _scratchSelection, command.ApplyMode);
        }

        private void ProcessPolygonSelection(Entity controller, ISelectionCandidatePolicy policy, IScreenProjector projector, in SelectionInputCommand command)
        {
            _scratchSelection.Clear();
            var polygon = command.PolygonScreen;
            if (polygon == null || polygon.Length < 3)
            {
                SelectionRuntime.ApplySelection(_world, _globals, controller, _scratchSelection, command.ApplyMode);
                return;
            }

            foreach (ref var chunk in _world.Query(in _query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count && _scratchSelection.Count < SelectionBuffer.CAPACITY; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!IsSelectableCandidate(policy, controller, entity))
                    {
                        continue;
                    }

                    Vector2 screen = ProjectToScreen(projector, positions[i]);
                    if (!IsValidScreen(screen))
                    {
                        continue;
                    }

                    if (IsPointInPolygon(screen, polygon))
                    {
                        _scratchSelection.Add(entity);
                    }
                }
            }

            SelectionRuntime.ApplySelection(_world, _globals, controller, _scratchSelection, command.ApplyMode);
        }

        private void CollectSameClassVisible(
            ISelectionCandidatePolicy policy,
            Entity controller,
            IScreenProjector projector,
            Entity reference,
            List<Entity> destination)
        {
            destination.Clear();
            foreach (ref var chunk in _world.Query(in _query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count && destination.Count < SelectionBuffer.CAPACITY; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!IsSelectableCandidate(policy, controller, entity) || !policy.IsSameSelectionClass(_world, reference, entity))
                    {
                        continue;
                    }

                    Vector2 screen = ProjectToScreen(projector, positions[i]);
                    if (!IsValidScreen(screen))
                    {
                        continue;
                    }

                    destination.Add(entity);
                }
            }
        }

        private bool IsSelectableCandidate(ISelectionCandidatePolicy policy, Entity controller, Entity entity)
        {
            if (!_world.IsAlive(entity) || !policy.IsSelectable(_world, controller, entity))
            {
                return false;
            }

            if (_world.TryGet(entity, out CullState cull) && !cull.IsVisible)
            {
                return false;
            }

            return true;
        }

        private static bool IsPointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool intersects = ((a.Y > point.Y) != (b.Y > point.Y))
                    && (point.X < (b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) + float.Epsilon) + a.X);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool IsValidScreen(Vector2 screen)
        {
            return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
        }

        private static Vector2 ProjectToScreen(IScreenProjector projector, in WorldPositionCm position)
        {
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value));
        }

        private bool TryResolveDependencies(
            out ISelectionInputHandler handler,
            out ISelectionCandidatePolicy policy,
            out IScreenProjector projector)
        {
            handler = default!;
            policy = default!;
            projector = default!;

            if (!_globals.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var profileIdObj)
                || profileIdObj is not string profileId
                || string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.SelectionInputHandler.Name, out var handlerObj)
                || handlerObj is not ISelectionInputHandler selectionHandler)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.SelectionCandidatePolicy.Name, out var policyObj)
                || policyObj is not ISelectionCandidatePolicy selectionPolicy)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj)
                || projectorObj is not IScreenProjector screenProjector)
            {
                return false;
            }

            handler = selectionHandler;
            policy = selectionPolicy;
            projector = screenProjector;
            return true;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
