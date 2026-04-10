using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Shared selection runtime for single-click and screen-space box selection.
    /// Formal selection writes only to the selector's ambient selection set.
    /// </summary>
    public sealed class CurrentSelectionApplySystem : ISystem<float>
    {
        private static readonly QueryDescription SelectableQuery = new QueryDescription().WithAll<VisualTransform, CullState, SelectionSelectableTag>();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private Entity[] _boxSelectionScratch = new Entity[16];
        private bool _suppressConfirmRelease;
        private Entity _cachedHoveredEntity;
        private Vector2 _cachedHoveredPointer;
        private int _hoverRefreshCooldown;

        private const float HoverPointerRefreshDistanceSq = 0.25f;
        private const int HoverIdleRefreshTicks = 6;

        public Action<WorldCmInt2, Entity>? OnEntitySelected { get; set; }

        public CurrentSelectionApplySystem(World world, Dictionary<string, object> globals, SelectionRuntime selection)
        {
            _world = world;
            _globals = globals;
            _selection = selection;
        }

        public CurrentSelectionApplySystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = ResolveSelectionRuntime(world, globals);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!PointerInteractionSnapshotReader.TryRead(_globals, out PointerInteractionSnapshot pointer))
            {
                return;
            }

            bool selectionSuppressed = IsSelectionSuppressed();

            if (selectionSuppressed && pointer.Confirm.PressedThisFrame)
            {
                _suppressConfirmRelease = true;
            }

            Entity hovered = ResolveHoveredEntity(pointer);
            UpdateHoveredEntity(hovered);

            bool hasOwner = TryGetSelectionOwner(out var owner);
            if (!hasOwner)
            {
                if (_suppressConfirmRelease && pointer.Confirm.ReleasedThisFrame)
                {
                    _suppressConfirmRelease = false;
                    return;
                }

                return;
            }

            EnsureSelectionComponents(owner);
            ref var drag = ref _world.Get<SelectionDragState>(owner);

            if (selectionSuppressed || _suppressConfirmRelease)
            {
                if (drag.Active)
                {
                    drag.Clear();
                }

                if (_suppressConfirmRelease && pointer.Confirm.ReleasedThisFrame)
                {
                    _suppressConfirmRelease = false;
                }
                return;
            }

            if (pointer.Confirm.PressedThisFrame)
            {
                drag.Begin(pointer.Confirm.ResolvePressPointerOrCurrent());
            }
            else if (drag.Active && pointer.Confirm.IsDown)
            {
                drag.CurrentScreen = pointer.Confirm.ResolveDownPointerOrCurrent();
            }

            if (pointer.Confirm.ReleasedThisFrame && drag.Active)
            {
                drag.CurrentScreen = pointer.Confirm.ResolveReleasePointerOrCurrent();

                if (drag.ExceedsThreshold(_selection.Config.DragThresholdPixels))
                {
                    ApplyBoxSelection(owner, in drag);
                }
                else if (pointer.HasGroundPoint)
                {
                    ApplyClickSelection(owner, hovered);
                    OnEntitySelected?.Invoke(pointer.GroundWorldCm, hovered);
                }

                drag.Clear();
            }
            else if (!pointer.Confirm.IsDown && drag.Active)
            {
                drag.Clear();
            }
        }

        private bool TryGetSelectionOwner(out Entity owner)
        {
            owner = default;
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private bool IsSelectionSuppressed()
        {
            return _globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) &&
                   mappingObj is Ludots.Core.Input.Orders.InputOrderMappingSystem mapping &&
                   mapping.IsAiming;
        }

        private void EnsureSelectionComponents(Entity owner)
        {
            if (!_world.Has<SelectionDragState>(owner))
            {
                _world.Add(owner, default(SelectionDragState));
            }

            _selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.Ambient, out _);
        }

        private void UpdateHoveredEntity(Entity hovered)
        {
            _cachedHoveredEntity = hovered;
            if (_world.IsAlive(hovered))
            {
                _globals[CoreServiceKeys.HoveredEntity.Name] = hovered;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
            }
        }

        private void ApplyClickSelection(Entity owner, Entity clicked)
        {
            if (_world.IsAlive(clicked))
            {
                Span<Entity> next = stackalloc Entity[1];
                next[0] = clicked;
                _selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, next);
                return;
            }

            _selection.ClearSelection(owner, SelectionSetKeys.Ambient);
        }

        private void ApplyBoxSelection(Entity owner, in SelectionDragState drag)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return;
            }

            var min = Vector2.Min(drag.StartScreen, drag.CurrentScreen);
            var max = Vector2.Max(drag.StartScreen, drag.CurrentScreen);

            int nextCount = 0;
            foreach (ref var chunk in _world.Query(in SelectableQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var transforms = chunk.GetSpan<VisualTransform>();
                var culls = chunk.GetSpan<CullState>();
                bool hasSelectableState = chunk.Has<SelectionSelectableState>();
                var selectableStates = hasSelectableState ? chunk.GetSpan<SelectionSelectableState>() : default;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible || (hasSelectableState && !selectableStates[i].Enabled))
                    {
                        continue;
                    }

                    Vector2 screen = projector.WorldToScreen(transforms[i].Position);
                    if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                    {
                        continue;
                    }

                    if (screen.X < min.X || screen.X > max.X || screen.Y < min.Y || screen.Y > max.Y)
                    {
                        continue;
                    }

                    EnsureScratchCapacity(nextCount + 1);
                    _boxSelectionScratch[nextCount++] = chunk.Entity(i);
                }
            }

            SortByEntityId(_boxSelectionScratch, nextCount);
            _selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, _boxSelectionScratch.AsSpan(0, nextCount));
        }

        private Entity ResolveHoveredEntity(in PointerInteractionSnapshot pointer)
        {
            bool pointerMoved = Vector2.DistanceSquared(pointer.Pointer, _cachedHoveredPointer) > HoverPointerRefreshDistanceSq;
            bool pointerActive = pointer.Confirm.PressedThisFrame || pointer.Confirm.IsDown || pointer.Confirm.ReleasedThisFrame;
            bool cachedAlive = _world.IsAlive(_cachedHoveredEntity);

            if (!pointerMoved &&
                !pointerActive &&
                cachedAlive &&
                _hoverRefreshCooldown > 0)
            {
                _hoverRefreshCooldown--;
                return _cachedHoveredEntity;
            }

            _cachedHoveredPointer = pointer.Pointer;
            _hoverRefreshCooldown = HoverIdleRefreshTicks;
            return FindNearestEntity(pointer.Pointer, _selection.Config.ClickPickRadiusPixels);
        }

        private void EnsureScratchCapacity(int required)
        {
            if (required <= _boxSelectionScratch.Length)
            {
                return;
            }

            int nextSize = _boxSelectionScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _boxSelectionScratch, nextSize);
        }

        private Entity FindNearestEntity(Vector2 pointer, float radiusPixels)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return default;
            }

            Entity best = default;
            float bestD2 = float.MaxValue;
            float maxD2 = radiusPixels * radiusPixels;

            foreach (ref var chunk in _world.Query(in SelectableQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var transforms = chunk.GetSpan<VisualTransform>();
                var culls = chunk.GetSpan<CullState>();
                bool hasSelectableState = chunk.Has<SelectionSelectableState>();
                var selectableStates = hasSelectableState ? chunk.GetSpan<SelectionSelectableState>() : default;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible || (hasSelectableState && !selectableStates[i].Enabled))
                    {
                        continue;
                    }

                    Vector2 screen = projector.WorldToScreen(transforms[i].Position);
                    if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                    {
                        continue;
                    }

                    float dx = screen.X - pointer.X;
                    float dy = screen.Y - pointer.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > maxD2)
                    {
                        continue;
                    }

                    Entity entity = chunk.Entity(i);
                    if (d2 < bestD2 || (d2 == bestD2 && (best == Entity.Null || Compare(entity, best) < 0)))
                    {
                        bestD2 = d2;
                        best = entity;
                    }
                }
            }

            return best;
        }

        private static void SortByEntityId(Span<Entity> entities, int count)
        {
            for (int i = 1; i < count; i++)
            {
                Entity value = entities[i];
                int j = i - 1;
                while (j >= 0 && Compare(entities[j], value) > 0)
                {
                    entities[j + 1] = entities[j];
                    j--;
                }

                entities[j + 1] = value;
            }
        }

        private static int Compare(Entity a, Entity b)
        {
            int worldCmp = a.WorldId.CompareTo(b.WorldId);
            return worldCmp != 0 ? worldCmp : a.Id.CompareTo(b.Id);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private static SelectionRuntime ResolveSelectionRuntime(World world, Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var runtimeObj) &&
                runtimeObj is SelectionRuntime runtime)
            {
                return runtime;
            }

            throw new InvalidOperationException(
                $"{nameof(CurrentSelectionApplySystem)} requires {CoreServiceKeys.SelectionRuntime.Name} to be registered before construction.");
        }
    }
}
