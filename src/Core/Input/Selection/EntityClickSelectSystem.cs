using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Shared selection runtime for single-click and screen-space box selection.
    /// Uses the authoritative input snapshot and keeps selection state on the
    /// local player entity, while preserving SelectedEntity / HoveredEntity
    /// globals as compatibility bridges for downstream systems.
    /// </summary>
    public sealed class EntityClickSelectSystem : ISystem<float>
    {
        private static readonly InteractionActionBindings DefaultBindings = new();
        private static readonly QueryDescription ClickSelectableQuery = new QueryDescription().WithAll<WorldPositionCm>();
        private static readonly QueryDescription BoxSelectableQuery = new QueryDescription().WithAll<WorldPositionCm, CullState>();

        public const float DragThresholdPixels = 8f;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly Entity[] _boxSelectionScratch = new Entity[SelectionBuffer.CAPACITY];

        public int PickRadiusCm { get; set; } = 120;
        public Action<WorldCmInt2, Entity>? OnEntitySelected { get; set; }

        public EntityClickSelectSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public EntityClickSelectSystem(World world, Dictionary<string, object> globals, ISpatialQueryService spatial)
            : this(world, globals)
        {
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!TryGetInput(out var input))
            {
                return;
            }

            var bindings = ResolveBindings();
            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            bool confirmDown = input.IsDown(bindings.ConfirmActionId);
            bool confirmPressed = input.PressedThisFrame(bindings.ConfirmActionId);
            bool confirmReleased = input.ReleasedThisFrame(bindings.ConfirmActionId);

            bool hasGroundPoint = TryResolveGroundPointer(pointer, out var groundWorldCm, out var hovered);
            UpdateHoveredEntity(hovered);

            bool hasOwner = TryGetSelectionOwner(out var owner);
            if (!hasOwner)
            {
                HandleLegacyClickWithoutSelectionOwner(confirmReleased, hasGroundPoint, groundWorldCm, hovered);
                return;
            }

            EnsureSelectionComponents(owner);

            ref var drag = ref _world.Get<SelectionDragState>(owner);
            ref var selection = ref _world.Get<SelectionBuffer>(owner);

            PruneInvalidSelection(owner, ref selection);

            if (confirmPressed)
            {
                drag.Begin(pointer);
            }
            else if (drag.Active && confirmDown)
            {
                drag.CurrentScreen = pointer;
            }

            if (confirmReleased && drag.Active)
            {
                drag.CurrentScreen = pointer;

                if (drag.ExceedsThreshold(DragThresholdPixels))
                {
                    ApplyBoxSelection(owner, ref selection, in drag);
                }
                else if (hasGroundPoint)
                {
                    ApplyClickSelection(owner, ref selection, hovered);
                    OnEntitySelected?.Invoke(groundWorldCm, hovered);
                }

                drag.Clear();
            }
            else if (!confirmDown && drag.Active)
            {
                drag.Clear();
            }

            SyncPrimarySelectedEntity(in selection);
        }

        private bool TryGetInput(out IInputActionReader input)
        {
            input = default!;
            return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                   inputObj is IInputActionReader reader &&
                   (input = reader) != null;
        }

        private bool TryResolveGroundPointer(Vector2 pointer, out WorldCmInt2 groundWorldCm, out Entity hovered)
        {
            groundWorldCm = default;
            hovered = default;

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) || rayObj is not IScreenRayProvider rayProvider)
            {
                return false;
            }

            var ray = rayProvider.GetRay(pointer);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out groundWorldCm))
            {
                return false;
            }

            hovered = FindNearestEntity(groundWorldCm, PickRadiusCm);
            return true;
        }

        private bool TryGetSelectionOwner(out Entity owner)
        {
            owner = default;
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private void EnsureSelectionComponents(Entity owner)
        {
            if (!_world.Has<SelectionBuffer>(owner))
            {
                _world.Add(owner, default(SelectionBuffer));
            }

            if (!_world.Has<SelectionGroupBuffer>(owner))
            {
                _world.Add(owner, default(SelectionGroupBuffer));
            }

            if (!_world.Has<SelectionDragState>(owner))
            {
                _world.Add(owner, default(SelectionDragState));
            }
        }

        private void UpdateHoveredEntity(Entity hovered)
        {
            if (_world.IsAlive(hovered))
            {
                _globals[CoreServiceKeys.HoveredEntity.Name] = hovered;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.HoveredEntity.Name);
            }
        }

        private void HandleLegacyClickWithoutSelectionOwner(bool confirmReleased, bool hasGroundPoint, in WorldCmInt2 groundWorldCm, Entity hovered)
        {
            if (!confirmReleased || !hasGroundPoint)
            {
                return;
            }

            if (_world.IsAlive(hovered))
            {
                _globals[CoreServiceKeys.SelectedEntity.Name] = hovered;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.SelectedEntity.Name);
            }

            OnEntitySelected?.Invoke(groundWorldCm, hovered);
        }

        private void ApplyClickSelection(Entity owner, ref SelectionBuffer selection, Entity clicked)
        {
            Span<Entity> next = stackalloc Entity[SelectionBuffer.CAPACITY];
            int nextCount = 0;
            if (_world.IsAlive(clicked))
            {
                next[nextCount++] = clicked;
            }

            ApplySelection(owner, ref selection, next.Slice(0, nextCount));
        }

        private void ApplyBoxSelection(Entity owner, ref SelectionBuffer selection, in SelectionDragState drag)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return;
            }

            var min = Vector2.Min(drag.StartScreen, drag.CurrentScreen);
            var max = Vector2.Max(drag.StartScreen, drag.CurrentScreen);

            int nextCount = 0;
            _world.Query(in BoxSelectableQuery, (Entity entity, ref WorldPositionCm position, ref CullState cull) =>
            {
                if (nextCount >= _boxSelectionScratch.Length || !cull.IsVisible || !_world.IsAlive(entity))
                {
                    return;
                }

                Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    return;
                }

                if (screen.X < min.X || screen.X > max.X || screen.Y < min.Y || screen.Y > max.Y)
                {
                    return;
                }

                _boxSelectionScratch[nextCount++] = entity;
            });

            SortByEntityId(_boxSelectionScratch, nextCount);
            ApplySelection(owner, ref selection, _boxSelectionScratch.AsSpan(0, nextCount));
        }

        private void ApplySelection(Entity owner, ref SelectionBuffer selection, ReadOnlySpan<Entity> next)
        {
            Span<Entity> previous = stackalloc Entity[SelectionBuffer.CAPACITY];
            int previousCount = CopySelection(selection, previous);

            for (int i = 0; i < previousCount; i++)
            {
                Entity entity = previous[i];
                if (!_world.IsAlive(entity) || Contains(next, entity))
                {
                    continue;
                }

                if (_world.Has<SelectedTag>(entity))
                {
                    _world.Remove<SelectedTag>(entity);
                }
            }

            selection.Clear();
            for (int i = 0; i < next.Length; i++)
            {
                Entity entity = next[i];
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                if (!selection.Add(entity))
                {
                    break;
                }

                if (!_world.Has<SelectedTag>(entity))
                {
                    _world.Add<SelectedTag>(entity);
                }
            }

            _world.Set(owner, selection);
        }

        private void PruneInvalidSelection(Entity owner, ref SelectionBuffer selection)
        {
            if (selection.Count <= 0)
            {
                return;
            }

            Span<Entity> alive = stackalloc Entity[SelectionBuffer.CAPACITY];
            int aliveCount = 0;
            bool changed = false;

            for (int i = 0; i < selection.Count; i++)
            {
                Entity entity = selection.Get(i);
                if (_world.IsAlive(entity))
                {
                    alive[aliveCount++] = entity;
                }
                else
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            ApplySelection(owner, ref selection, alive.Slice(0, aliveCount));
        }

        private void SyncPrimarySelectedEntity(in SelectionBuffer selection)
        {
            Entity primary = selection.Primary;
            if (_world.IsAlive(primary))
            {
                _globals[CoreServiceKeys.SelectedEntity.Name] = primary;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.SelectedEntity.Name);
            }
        }

        private Entity FindNearestEntity(in WorldCmInt2 worldCm, int radiusCm)
        {
            int targetX = worldCm.X;
            int targetY = worldCm.Y;
            Entity best = default;
            long bestD2 = long.MaxValue;
            long maxD2 = (long)radiusCm * radiusCm;

            _world.Query(in ClickSelectableQuery, (Entity entity, ref WorldPositionCm pos) =>
            {
                if (!_world.IsAlive(entity))
                {
                    return;
                }

                WorldCmInt2 cmPos = pos.Value.ToWorldCmInt2();
                long dx = cmPos.X - targetX;
                long dy = cmPos.Y - targetY;
                long d2 = dx * dx + dy * dy;
                if (d2 > maxD2)
                {
                    return;
                }

                if (d2 < bestD2 || (d2 == bestD2 && (best == Entity.Null || Compare(entity, best) < 0)))
                {
                    bestD2 = d2;
                    best = entity;
                }
            });

            return best;
        }

        private static int CopySelection(in SelectionBuffer selection, Span<Entity> destination)
        {
            int count = selection.Count;
            if (count > destination.Length)
            {
                count = destination.Length;
            }

            for (int i = 0; i < count; i++)
            {
                destination[i] = selection.Get(i);
            }

            return count;
        }

        private static bool Contains(ReadOnlySpan<Entity> entities, Entity candidate)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == candidate)
                {
                    return true;
                }
            }

            return false;
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

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) && obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
