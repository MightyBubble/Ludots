using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Shared selection runtime for single-click and screen-space box selection.
    /// Formal selection writes only to the selector's live primary selection set.
    /// </summary>
    public sealed class CurrentSelectionApplySystem : ISystem<float>
    {
        private static readonly QueryDescription SelectableQuery = new QueryDescription().WithAll<VisualTransform, CullState, SelectionSelectableTag>();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private readonly EntityCollectionStore _entityCollections;
        private Entity[] _boxSelectionScratch = new Entity[16];
        private Entity[] _selectionScratch = new Entity[16];
        private bool _suppressConfirmRelease;

        public Action<WorldCmInt2, Entity>? OnEntitySelected { get; set; }

        public CurrentSelectionApplySystem(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            EntityCollectionStore entityCollections)
        {
            _world = world;
            _globals = globals;
            _selection = selection;
            _entityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
        }

        public CurrentSelectionApplySystem(World world, Dictionary<string, object> globals, SelectionRuntime selection)
            : this(world, globals, selection, ResolveEntityCollectionStore(globals))
        {
        }

        public CurrentSelectionApplySystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = ResolveSelectionRuntime(world, globals);
            _entityCollections = ResolveEntityCollectionStore(globals);
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

            bool hasOwner = TryGetSelectionOwner(out var owner);
            Entity hovered = hasOwner
                ? FindNearestEntity(owner, pointer.Pointer, _selection.Config.ClickPickRadiusPixels)
                : Entity.Null;
            UpdateHoveredEntity(hovered);

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

            if (pointer.Confirm.PressedThisFrame && pointer.Confirm.ReleasedThisFrame)
            {
                drag.Begin(pointer.Confirm.ResolvePressPointerOrCurrent(), ResolveAcquisitionMode());
                drag.CurrentScreen = pointer.Confirm.ResolveReleasePointerOrCurrent();
                ApplyCompletedSelectionGesture(owner, in drag, hovered, pointer);
                drag.Clear();
            }
            else if (pointer.Confirm.PressedThisFrame)
            {
                drag.Begin(pointer.Confirm.ResolvePressPointerOrCurrent(), ResolveAcquisitionMode());
            }
            else if (drag.Active && pointer.Confirm.IsDown)
            {
                drag.CurrentScreen = pointer.Confirm.ResolveDownPointerOrCurrent();
            }

            if (pointer.Confirm.ReleasedThisFrame && drag.Active)
            {
                drag.CurrentScreen = pointer.Confirm.ResolveReleasePointerOrCurrent();
                ApplyCompletedSelectionGesture(owner, in drag, hovered, pointer);
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

        private void ApplyCompletedSelectionGesture(Entity owner, in SelectionDragState drag, Entity hovered, in PointerInteractionSnapshot pointer)
        {
            SelectionAcquisitionMode acquisitionMode = drag.AcquisitionMode;

            if (drag.ExceedsThreshold(_selection.Config.DragThresholdPixels))
            {
                ApplyBoxSelection(owner, in drag, acquisitionMode);
                return;
            }

            Entity acquired = ResolveClickAcquisition(owner, hovered);
            ApplyClickSelection(owner, acquired, acquisitionMode);
            if (pointer.HasGroundPoint)
            {
                OnEntitySelected?.Invoke(pointer.GroundWorldCm, acquired);
            }
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

            _selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
            EnsureLivePrimarySelectionView(owner);
        }

        private void EnsureLivePrimarySelectionView(Entity owner)
        {
            if (_globals.TryGetValue(CoreServiceKeys.EntityViewViewerEntity.Name, out var viewerObj) &&
                viewerObj is Entity viewer &&
                _world.IsAlive(viewer) &&
                _globals.TryGetValue(CoreServiceKeys.EntityViewKey.Name, out var viewKeyObj) &&
                viewKeyObj is string viewKey &&
                !string.IsNullOrWhiteSpace(viewKey) &&
                EntityViewRuntime.TryResolveCurrentProfile(_globals, RequireEntityViewConfig(), out _))
            {
                return;
            }

            EntityViewRuntimeConfig entityViewConfig = RequireEntityViewConfig();
            if (!EntityViewRuntime.TrySetCurrentView(
                    _world,
                    _globals,
                    entityViewConfig,
                    _selection,
                    owner,
                    entityViewConfig.DefaultViewKey,
                    owner,
                    SelectionSetKeys.LivePrimary))
            {
                throw new InvalidOperationException("CurrentSelectionApplySystem failed to bind EntityView primary profile.");
            }
        }

        private void UpdateHoveredEntity(Entity hovered)
        {
            SelectionAcquisitionConfig acquisition = _selection.Config.Acquisition
                ?? throw new InvalidOperationException("selection.acquisition must be explicitly configured.");
            Entity owner = _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var ownerObj) && ownerObj is Entity local && _world.IsAlive(local)
                ? local
                : Entity.Null;
            if (owner == Entity.Null)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.HoveredEntity,
                EntityCollectionSourceKind.UiHover,
                EntityCollectionRoleKind.Display,
                owner,
                _world.IsAlive(hovered) ? hovered : Entity.Null,
                string.IsNullOrWhiteSpace(acquisition.Title) ? "Hover target" : $"{acquisition.Title} hover",
                _world.IsAlive(hovered) ? "hover" : "hover-empty");

            if (_world.IsAlive(hovered))
            {
                Span<Entity> single = stackalloc Entity[1];
                single[0] = hovered;
                _entityCollections.Replace(owner, descriptor, single);
                return;
            }

            _entityCollections.Replace(owner, descriptor, ReadOnlySpan<Entity>.Empty);
        }

        private void ApplyClickSelection(Entity owner, Entity clicked, SelectionAcquisitionMode acquisitionMode)
        {
            if (_world.IsAlive(clicked))
            {
                Span<Entity> hit = stackalloc Entity[1];
                hit[0] = clicked;
                ApplyAcquisition(owner, hit, acquisitionMode);
                return;
            }

            if (acquisitionMode == SelectionAcquisitionMode.Replace)
            {
                ApplyAcquisition(owner, ReadOnlySpan<Entity>.Empty, acquisitionMode);
            }
        }

        private void ApplyBoxSelection(Entity owner, in SelectionDragState drag, SelectionAcquisitionMode acquisitionMode)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return;
            }

            ScreenRect marquee = ScreenRect.FromPoints(drag.StartScreen, drag.CurrentScreen);
            var targetRelationFilter = _selection.TargetRelationFilter;

            int nextCount = 0;
            _world.Query(in SelectableQuery, (Entity entity, ref VisualTransform transform, ref CullState cull, ref SelectionSelectableTag selectable) =>
            {
                if (!cull.IsVisible ||
                    !SelectionEligibility.CanAcquire(_world, _globals, owner, entity, targetRelationFilter))
                {
                    return;
                }

                if (!SpatialBoundsUtility.EntityIntersectsScreenRect(_world, entity, projector, in marquee))
                {
                    return;
                }

                EnsureScratchCapacity(nextCount + 1);
                _boxSelectionScratch[nextCount++] = entity;
            });

            SortByEntityId(_boxSelectionScratch, nextCount);
            ApplyAcquisition(owner, _boxSelectionScratch.AsSpan(0, nextCount), acquisitionMode);
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

        private Entity FindNearestEntity(Entity owner, Vector2 pointer, float radiusPixels)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return default;
            }

            Entity best = default;
            ScreenRect bestBounds = default;
            bool hasBestBounds = false;

            _world.Query(in SelectableQuery, (Entity entity, ref VisualTransform transform, ref CullState cull, ref SelectionSelectableTag selectable) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                if (!SelectionEligibility.CanInspectLive(_world, _globals, owner, entity))
                {
                    return;
                }

                if (!SpatialBoundsUtility.PointerHitsEntity(_world, entity, projector, pointer, radiusPixels))
                {
                    return;
                }

                if (!SpatialBoundsUtility.TryProjectScreenBounds(_world, entity, projector, out ScreenRect candidateBounds))
                {
                    return;
                }

                if (!hasBestBounds ||
                    CompareProjectedBounds(candidateBounds, bestBounds, pointer) < 0 ||
                    (CompareProjectedBounds(candidateBounds, bestBounds, pointer) == 0 && (best == Entity.Null || Compare(entity, best) < 0)))
                {
                    best = entity;
                    bestBounds = candidateBounds;
                    hasBestBounds = true;
                }
            });

            return best;
        }

        private Entity ResolveClickAcquisition(Entity owner, Entity hovered)
        {
            return _world.IsAlive(hovered) && SelectionEligibility.CanAcquire(_world, _globals, owner, hovered, _selection.TargetRelationFilter)
                ? hovered
                : Entity.Null;
        }

        private void ApplyAcquisition(Entity owner, ReadOnlySpan<Entity> hits, SelectionAcquisitionMode mode)
        {
            SelectionAcquisitionConfig acquisition = _selection.Config.Acquisition
                ?? throw new InvalidOperationException("selection.acquisition must be explicitly configured.");
            string collectionKey = RequireConfiguredKey(acquisition.CollectionKey, "selection.acquisition.collectionKey");
            string formalSetKey = RequireConfiguredKey(acquisition.FormalSelectionSetKey, "selection.acquisition.formalSelectionSetKey");
            var descriptor = EntityCollectionDescriptor.Create(
                collectionKey,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.AcquisitionPreview,
                owner,
                hits.Length > 0 ? hits[0] : Entity.Null,
                string.IsNullOrWhiteSpace(acquisition.Title) ? "UI acquisition" : acquisition.Title,
                $"{mode} | {hits.Length} entities");
            _entityCollections.Replace(owner, descriptor, hits);

            if (EntityViewRuntime.TryResolveCurrentProfile(_globals, RequireEntityViewConfig(), out EntityViewProfileEntry profile))
            {
                EntityViewRuntime.PromoteCommandSource(
                    _entityCollections,
                    owner,
                    in profile,
                    hits,
                    $"{mode} | {hits.Length} entities");
            }

            if (!acquisition.CommitToFormalSelection)
            {
                return;
            }

            switch (mode)
            {
                case SelectionAcquisitionMode.Replace:
                    _selection.ReplaceSelection(owner, formalSetKey, hits);
                    return;

                case SelectionAcquisitionMode.Additive:
                    for (int i = 0; i < hits.Length; i++)
                    {
                        _selection.AddToSelection(owner, formalSetKey, hits[i]);
                    }
                    return;

                case SelectionAcquisitionMode.Toggle:
                    for (int i = 0; i < hits.Length; i++)
                    {
                        Entity target = hits[i];
                        if (SelectionContains(owner, target))
                        {
                            _selection.RemoveFromSelection(owner, formalSetKey, target);
                        }
                        else
                        {
                            _selection.AddToSelection(owner, formalSetKey, target);
                        }
                    }
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported selection acquisition mode '{mode}'.");
            }
        }

        private bool SelectionContains(Entity owner, Entity target)
        {
            string formalSetKey = RequireConfiguredKey(
                _selection.Config.Acquisition?.FormalSelectionSetKey,
                "selection.acquisition.formalSelectionSetKey");
            int count = _selection.GetSelectionCount(owner, formalSetKey);
            if (count <= 0)
            {
                return false;
            }

            EnsureSelectionScratchCapacity(count);
            int written = _selection.CopySelection(owner, formalSetKey, _selectionScratch);
            for (int i = 0; i < written; i++)
            {
                if (_selectionScratch[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private SelectionAcquisitionMode ResolveAcquisitionMode()
        {
            if (_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                inputObj is Ludots.Core.Input.Runtime.IInputActionReader input)
            {
                bool additive = input.IsDown(SelectionModifierActionIds.Additive);
                bool toggle = input.IsDown(SelectionModifierActionIds.Toggle);
                if (toggle)
                {
                    return SelectionAcquisitionMode.Toggle;
                }

                if (additive)
                {
                    return SelectionAcquisitionMode.Additive;
                }
            }

            return SelectionAcquisitionMode.Replace;
        }

        private void EnsureSelectionScratchCapacity(int required)
        {
            if (required <= _selectionScratch.Length)
            {
                return;
            }

            int nextSize = _selectionScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _selectionScratch, nextSize);
        }

        private static int CompareProjectedBounds(in ScreenRect candidate, in ScreenRect best, Vector2 pointer)
        {
            float candidateArea = MathF.Max(0f, candidate.MaxX - candidate.MinX) * MathF.Max(0f, candidate.MaxY - candidate.MinY);
            float bestArea = MathF.Max(0f, best.MaxX - best.MinX) * MathF.Max(0f, best.MaxY - best.MinY);
            int areaComparison = candidateArea.CompareTo(bestArea);
            if (areaComparison != 0)
            {
                return areaComparison;
            }

            Vector2 candidateCenter = new((candidate.MinX + candidate.MaxX) * 0.5f, (candidate.MinY + candidate.MaxY) * 0.5f);
            Vector2 bestCenter = new((best.MinX + best.MaxX) * 0.5f, (best.MinY + best.MaxY) * 0.5f);
            float candidateD2 = Vector2.DistanceSquared(candidateCenter, pointer);
            float bestD2 = Vector2.DistanceSquared(bestCenter, pointer);
            return candidateD2.CompareTo(bestD2);
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

        private static EntityCollectionStore ResolveEntityCollectionStore(Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var storeObj) &&
                storeObj is EntityCollectionStore store)
            {
                return store;
            }

            throw new InvalidOperationException(
                $"{nameof(CurrentSelectionApplySystem)} requires {CoreServiceKeys.EntityCollectionStore.Name} to be registered before construction.");
        }

        private EntityViewRuntimeConfig RequireEntityViewConfig()
        {
            if (_globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) &&
                configObj is EntityViewRuntimeConfig config)
            {
                return config;
            }

            throw new InvalidOperationException(
                $"{nameof(CurrentSelectionApplySystem)} requires {CoreServiceKeys.EntityViewConfig.Name} to be registered.");
        }

        private static string RequireConfiguredKey(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be explicitly configured.");
            }

            return value.Trim();
        }
    }

    public enum SelectionAcquisitionMode : byte
    {
        Replace = 0,
        Additive = 1,
        Toggle = 2,
    }

    public static class SelectionModifierActionIds
    {
        public const string Additive = "QueueModifier";
        public const string Toggle = "PrecisionModifier";
    }
}
