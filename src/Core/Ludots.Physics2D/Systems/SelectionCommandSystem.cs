using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionCommandSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISelectionCandidatePolicy _defaultPolicy = new DefaultSelectionCandidatePolicy();
        private readonly QueryDescription _candidateQuery = new QueryDescription().WithAll<WorldPositionCm>();
        private readonly List<Entity> _scratch = new(64);

        public SelectionCommandSystem(World world, Dictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!TryResolveController(out var controller) || !TryResolveHandler(out var handler))
            {
                return;
            }

            EnsureControllerBuffers(controller);
            var selection = _world.Get<SelectionBuffer>(controller);
            var groups = _world.Get<SelectionGroupBuffer>(controller);
            bool changed = false;

            handler.Update(dt);

            while (handler.Poll(out var command))
            {
                changed |= ApplyCommand(controller, ref selection, ref groups, in command);
            }

            if (!changed)
            {
                return;
            }

            _world.Set(controller, selection);
            _world.Set(controller, groups);

            if (selection.Count > 0 && _world.IsAlive(selection.Primary))
            {
                _globals[CoreServiceKeys.SelectedEntity.Name] = selection.Primary;
            }
            else
            {
                _globals[CoreServiceKeys.SelectedEntity.Name] = controller;
            }
        }

        private bool ApplyCommand(Entity controller, ref SelectionBuffer selection, ref SelectionGroupBuffer groups, in SelectionInputCommand command)
        {
            switch (command.Kind)
            {
                case SelectionCommandKind.Clear:
                    ClearSelection(ref selection);
                    return true;

                case SelectionCommandKind.SaveGroup:
                    groups.SaveGroup(command.GroupIndex, in selection);
                    return true;

                case SelectionCommandKind.RecallGroup:
                    ClearSelection(ref selection);
                    groups.RecallGroup(command.GroupIndex, ref selection);
                    ReapplyTags(in selection);
                    return true;

                case SelectionCommandKind.SelectAtPoint:
                    return ApplyPointSelection(controller, ref selection, in command);

                case SelectionCommandKind.SelectInRectangle:
                {
                    var min = command.RectangleMinScreen;
                    var max = command.RectangleMaxScreen;
                    return ApplyAreaSelection(controller, ref selection, command.ApplyMode, candidate => IsInsideRectangle(candidate, min, max));
                }

                case SelectionCommandKind.SelectInPolygon:
                {
                    var polygon = command.PolygonScreen;
                    return ApplyAreaSelection(controller, ref selection, command.ApplyMode, candidate => IsInsidePolygon(candidate, polygon));
                }

                default:
                    return false;
            }
        }

        private bool ApplyPointSelection(Entity controller, ref SelectionBuffer selection, in SelectionInputCommand command)
        {
            var policy = ResolveCandidatePolicy();
            var projector = ResolveProjector();
            if (projector == null)
            {
                return false;
            }

            Entity resolved = FindNearestCandidate(controller, projector, policy, command.PointScreen, command.PickRadiusPx);
            _scratch.Clear();
            if (_world.IsAlive(resolved))
            {
                _scratch.Add(resolved);
                if (command.ExpandSameClassFromResolvedCandidate)
                {
                    CollectSameClassVisibleCandidates(controller, projector, policy, resolved, _scratch);
                }
            }

            return ApplyCandidates(ref selection, command.ApplyMode, _scratch);
        }

        private bool ApplyAreaSelection(Entity controller, ref SelectionBuffer selection, SelectionApplyMode applyMode, Func<Vector2, bool> contains)
        {
            var policy = ResolveCandidatePolicy();
            var projector = ResolveProjector();
            if (projector == null)
            {
                return false;
            }

            _scratch.Clear();
            VisitSelectableCandidates(controller, projector, policy, candidate =>
            {
                if (contains(candidate.Screen))
                {
                    _scratch.Add(candidate.Entity);
                }
            });

            return ApplyCandidates(ref selection, applyMode, _scratch);
        }

        private bool ApplyCandidates(ref SelectionBuffer selection, SelectionApplyMode applyMode, List<Entity> candidates)
        {
            bool changed = false;
            if (applyMode == SelectionApplyMode.Replace)
            {
                ClearSelection(ref selection);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!_world.IsAlive(candidate))
                {
                    continue;
                }

                switch (applyMode)
                {
                    case SelectionApplyMode.Replace:
                    case SelectionApplyMode.Add:
                        if (selection.Add(candidate))
                        {
                            AddSelectedTag(candidate);
                            changed = true;
                        }
                        break;

                    case SelectionApplyMode.Toggle:
                        if (selection.Contains(candidate))
                        {
                            if (selection.Remove(candidate))
                            {
                                RemoveSelectedTag(candidate);
                                changed = true;
                            }
                        }
                        else if (selection.Add(candidate))
                        {
                            AddSelectedTag(candidate);
                            changed = true;
                        }
                        break;
                }
            }

            return changed || applyMode == SelectionApplyMode.Replace;
        }

        private void ClearSelection(ref SelectionBuffer selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                var entity = selection.Get(i);
                if (_world.IsAlive(entity))
                {
                    RemoveSelectedTag(entity);
                }
            }

            selection.Clear();
        }

        private void ReapplyTags(in SelectionBuffer selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                var entity = selection.Get(i);
                if (_world.IsAlive(entity))
                {
                    AddSelectedTag(entity);
                }
            }
        }

        private Entity FindNearestCandidate(Entity controller, IScreenProjector projector, ISelectionCandidatePolicy policy, Vector2 pointScreen, float radiusPx)
        {
            float radius = Math.Max(1f, radiusPx);
            float radiusSq = radius * radius;
            Entity best = default;
            float bestDistanceSq = float.MaxValue;

            VisitSelectableCandidates(controller, projector, policy, candidate =>
            {
                float distanceSq = Vector2.DistanceSquared(candidate.Screen, pointScreen);
                if (distanceSq <= radiusSq && distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best = candidate.Entity;
                }
            });

            return best;
        }

        private void CollectSameClassVisibleCandidates(Entity controller, IScreenProjector projector, ISelectionCandidatePolicy policy, Entity reference, List<Entity> results)
        {
            for (int i = results.Count - 1; i >= 0; i--)
            {
                if (results[i] != reference)
                {
                    results.RemoveAt(i);
                }
            }

            VisitSelectableCandidates(controller, projector, policy, candidate =>
            {
                if (candidate.Entity == reference || policy.IsSameSelectionClass(_world, reference, candidate.Entity))
                {
                    if (!results.Contains(candidate.Entity))
                    {
                        results.Add(candidate.Entity);
                    }
                }
            });
        }

        private void VisitSelectableCandidates(Entity controller, IScreenProjector projector, ISelectionCandidatePolicy policy, Action<ScreenCandidate> visitor)
        {
            foreach (ref var chunk in _world.Query(in _candidateQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    if (!policy.IsSelectable(_world, controller, entity) || !IsVisible(entity))
                    {
                        continue;
                    }

                    visitor(new ScreenCandidate(entity, projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(positions[i].Value))));
                }
            }
        }

        private bool TryResolveController(out Entity controller)
        {
            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj) && obj is Entity entity && _world.IsAlive(entity))
            {
                controller = entity;
                return true;
            }

            controller = default;
            return false;
        }

        private bool TryResolveHandler(out ISelectionInputHandler handler)
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectionInputHandler.Name, out var obj) && obj is ISelectionInputHandler selectionInputHandler)
            {
                handler = selectionInputHandler;
                return true;
            }

            handler = default!;
            return false;
        }

        private void EnsureControllerBuffers(Entity controller)
        {
            if (!_world.Has<SelectionBuffer>(controller))
            {
                _world.Add(controller, new SelectionBuffer());
            }

            if (!_world.Has<SelectionGroupBuffer>(controller))
            {
                _world.Add(controller, new SelectionGroupBuffer());
            }
        }

        private ISelectionCandidatePolicy ResolveCandidatePolicy()
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectionCandidatePolicy.Name, out var obj) && obj is ISelectionCandidatePolicy policy)
            {
                return policy;
            }

            return _defaultPolicy;
        }

        private IScreenProjector? ResolveProjector()
        {
            if (_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var obj) && obj is IScreenProjector projector)
            {
                return projector;
            }

            return null;
        }

        private bool IsVisible(Entity entity)
        {
            return !_world.TryGet(entity, out CullState cull) || cull.IsVisible;
        }

        private void AddSelectedTag(Entity entity)
        {
            if (!_world.Has<SelectedTag>(entity))
            {
                _world.Add(entity, new SelectedTag());
            }
        }

        private void RemoveSelectedTag(Entity entity)
        {
            if (_world.Has<SelectedTag>(entity))
            {
                _world.Remove<SelectedTag>(entity);
            }
        }

        private static bool IsInsideRectangle(Vector2 point, Vector2 min, Vector2 max)
        {
            return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
        }

        private static bool IsInsidePolygon(Vector2 point, Vector2[]? polygon)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
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

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private readonly struct ScreenCandidate
        {
            public ScreenCandidate(Entity entity, Vector2 screen)
            {
                Entity = entity;
                Screen = screen;
            }

            public Entity Entity { get; }
            public Vector2 Screen { get; }
        }
    }
}
