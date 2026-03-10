using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionCommandSystem : ISystem<float>
    {
        private static readonly SelectionProfile DefaultProfile = new() { Id = "Default" };
        private static readonly InteractionActionBindings DefaultBindings = new();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISelectionCandidatePolicy _defaultPolicy = new DefaultSelectionCandidatePolicy();
        private readonly QueryDescription _candidateQuery = new QueryDescription().WithAll<WorldPositionCm>();
        private readonly List<Entity> _scratch = new(64);

        public Action<WorldCmInt2, Entity>? OnPointSelectionApplied { get; set; }

        public SelectionCommandSystem(World world, Dictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!SelectionRuntime.TryGetController(_world, _globals, out var controller))
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
                return;
            }

            SelectionRuntime.EnsureControllerBuffers(_world, controller);
            var profile = ResolveProfile();
            var projector = ResolveProjector();
            var policy = ResolveCandidatePolicy();

            UpdateHoveredEntity(controller, projector, policy, profile.PickRadiusPx);

            if (!TryResolveHandler(out var handler))
            {
                return;
            }

            var selection = _world.Get<SelectionBuffer>(controller);
            var groups = _world.Get<SelectionGroupBuffer>(controller);
            bool changed = false;

            handler.Update(dt);
            while (handler.Poll(out var command))
            {
                changed |= ApplyCommand(controller, projector, policy, ref selection, ref groups, command);
            }

            if (!changed)
            {
                return;
            }

            _world.Set(controller, selection);
            _world.Set(controller, groups);
            SelectionRuntime.SyncSelectedEntity(_world, _globals, in selection);
        }

        private bool ApplyCommand(
            Entity controller,
            IScreenProjector? projector,
            ISelectionCandidatePolicy policy,
            ref SelectionBuffer selection,
            ref SelectionGroupBuffer groups,
            SelectionInputCommand command)
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
                {
                    bool changed = ApplyPointSelection(controller, projector, policy, ref selection, command, out var resolved);
                    NotifyPointSelection(command, resolved);
                    return changed;
                }

                case SelectionCommandKind.SelectInRectangle:
                {
                    var min = command.RectangleMinScreen;
                    var max = command.RectangleMaxScreen;
                    return ApplyAreaSelection(controller, projector, policy, ref selection, command.ApplyMode, point => IsInsideRectangle(point, min, max));
                }

                case SelectionCommandKind.SelectInPolygon:
                {
                    var polygon = command.PolygonScreen;
                    return ApplyAreaSelection(controller, projector, policy, ref selection, command.ApplyMode, point => IsInsidePolygon(point, polygon));
                }

                default:
                    return false;
            }
        }

        private bool ApplyPointSelection(
            Entity controller,
            IScreenProjector? projector,
            ISelectionCandidatePolicy policy,
            ref SelectionBuffer selection,
            SelectionInputCommand command,
            out Entity resolved)
        {
            resolved = default;
            if (projector == null)
            {
                return false;
            }

            resolved = FindNearestCandidate(controller, projector, policy, command.PointScreen, command.PickRadiusPx);
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

        private bool ApplyAreaSelection(
            Entity controller,
            IScreenProjector? projector,
            ISelectionCandidatePolicy policy,
            ref SelectionBuffer selection,
            SelectionApplyMode applyMode,
            Func<Vector2, bool> contains)
        {
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

        private void UpdateHoveredEntity(Entity controller, IScreenProjector? projector, ISelectionCandidatePolicy policy, float pickRadiusPx)
        {
            if (projector == null || !TryResolvePointer(out var pointer))
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
                return;
            }

            var hovered = FindNearestCandidate(controller, projector, policy, pointer, Math.Max(1f, pickRadiusPx));
            if (_world.IsAlive(hovered))
            {
                _globals[CoreServiceKeys.HoveredEntity.Name] = hovered;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
            }
        }

        private void NotifyPointSelection(SelectionInputCommand command, Entity resolved)
        {
            if (OnPointSelectionApplied == null)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) ||
                rayObj is not IScreenRayProvider rayProvider)
            {
                return;
            }

            var ray = rayProvider.GetRay(command.PointScreen);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm))
            {
                return;
            }

            OnPointSelectionApplied(worldCm, resolved);
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

        private void CollectSameClassVisibleCandidates(
            Entity controller,
            IScreenProjector projector,
            ISelectionCandidatePolicy policy,
            Entity reference,
            List<Entity> results)
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
                if ((candidate.Entity == reference || policy.IsSameSelectionClass(_world, reference, candidate.Entity)) &&
                    !results.Contains(candidate.Entity))
                {
                    results.Add(candidate.Entity);
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

        private void ClearSelection(ref SelectionBuffer selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                RemoveSelectedTag(selection.Get(i));
            }

            selection.Clear();
        }

        private void ReapplyTags(in SelectionBuffer selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                AddSelectedTag(selection.Get(i));
            }
        }

        private bool TryResolveHandler(out ISelectionInputHandler handler)
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectionInputHandler.Name, out var obj) &&
                obj is ISelectionInputHandler selectionInputHandler)
            {
                handler = selectionInputHandler;
                return true;
            }

            handler = default!;
            return false;
        }

        private SelectionProfile ResolveProfile()
        {
            if (_globals.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var idObj) &&
                idObj is string profileId &&
                !string.IsNullOrWhiteSpace(profileId) &&
                _globals.TryGetValue(CoreServiceKeys.SelectionProfileRegistry.Name, out var registryObj) &&
                registryObj is SelectionProfileRegistry registry)
            {
                var profile = registry.Get(profileId);
                if (profile != null)
                {
                    return profile;
                }
            }

            return DefaultProfile;
        }

        private ISelectionCandidatePolicy ResolveCandidatePolicy()
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectionCandidatePolicy.Name, out var obj) &&
                obj is ISelectionCandidatePolicy policy)
            {
                return policy;
            }

            return _defaultPolicy;
        }

        private IScreenProjector? ResolveProjector()
        {
            if (_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var obj) &&
                obj is IScreenProjector projector)
            {
                return projector;
            }

            return null;
        }

        private bool TryResolvePointer(out Vector2 pointer)
        {
            pointer = default;
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not Ludots.Core.Input.Runtime.IInputActionReader input)
            {
                return false;
            }

            var bindings = ResolveBindings();
            pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            return true;
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) &&
                obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        private bool IsVisible(Entity entity)
        {
            return !_world.TryGet(entity, out CullState cull) || cull.IsVisible;
        }

        private void AddSelectedTag(Entity entity)
        {
            if (_world.IsAlive(entity) && !_world.Has<SelectedTag>(entity))
            {
                _world.Add(entity, new SelectedTag());
            }
        }

        private void RemoveSelectedTag(Entity entity)
        {
            if (_world.IsAlive(entity) && _world.Has<SelectedTag>(entity))
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
