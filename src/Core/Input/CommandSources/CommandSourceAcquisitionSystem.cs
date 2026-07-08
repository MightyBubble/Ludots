using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.CommandSources
{
    /// <summary>
    /// Shared command-source acquisition for single-click and screen-space box gestures.
    /// The authoritative output is an owner-keyed entity collection.
    /// </summary>
    public sealed class CommandSourceAcquisitionSystem : ISystem<float>
    {
        private static readonly QueryDescription SelectableQuery = new QueryDescription().WithAll<VisualTransform, CullState, CommandSourceSelectableTag>();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly CommandSourceAcquisitionConfig _config;
        private readonly Ludots.Core.Gameplay.Teams.RelationshipFilter _targetRelationFilter;
        private readonly EntityCollectionStore _entityCollections;
        private Entity[] _boxSelectionScratch = new Entity[16];
        private Entity[] _commandSourceScratch = new Entity[16];
        private bool _suppressConfirmRelease;

        public Action<WorldCmInt2, Entity>? OnEntitySelected { get; set; }

        public CommandSourceAcquisitionSystem(
            World world,
            Dictionary<string, object> globals,
            CommandSourceAcquisitionConfig config,
            EntityCollectionStore entityCollections)
        {
            _world = world;
            _globals = globals;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _targetRelationFilter = (_config.TargetFilter ?? throw new InvalidOperationException(
                "commandSource.targetFilter must be explicitly configured.")).ParseRelationFilter();
            _entityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
        }

        public CommandSourceAcquisitionSystem(World world, Dictionary<string, object> globals, CommandSourceAcquisitionConfig config)
            : this(world, globals, config, ResolveEntityCollectionStore(globals))
        {
        }

        public CommandSourceAcquisitionSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _config = ResolveCommandSourceAcquisitionConfig(globals);
            _targetRelationFilter = (_config.TargetFilter ?? throw new InvalidOperationException(
                "commandSource.targetFilter must be explicitly configured.")).ParseRelationFilter();
            _entityCollections = ResolveEntityCollectionStore(globals);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!PointerInteractionSnapshotReader.TryRead(_globals, out PointerInteractionSnapshot pointer))
            {
                return;
            }

            bool acquisitionSuppressed = IsAcquisitionSuppressed();

            if (acquisitionSuppressed && pointer.Confirm.PressedThisFrame)
            {
                _suppressConfirmRelease = true;
            }

            bool hasOwner = TryGetCommandSourceOwner(out var owner);
            Entity hovered = hasOwner
                ? FindNearestEntity(owner, pointer.Pointer, _config.ClickPickRadiusPixels)
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

            EnsureCommandSourceComponents(owner);
            ref var drag = ref _world.Get<CommandSourceDragState>(owner);

            if (acquisitionSuppressed || _suppressConfirmRelease)
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

        private bool TryGetCommandSourceOwner(out Entity owner)
        {
            owner = default;
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private void ApplyCompletedSelectionGesture(Entity owner, in CommandSourceDragState drag, Entity hovered, in PointerInteractionSnapshot pointer)
        {
            CommandSourceAcquisitionMode acquisitionMode = drag.AcquisitionMode;

            if (drag.ExceedsThreshold(_config.DragThresholdPixels))
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

        private bool IsAcquisitionSuppressed()
        {
            return _globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) &&
                   mappingObj is Ludots.Core.Input.Orders.InputOrderMappingSystem mapping &&
                   mapping.IsAiming;
        }

        private void EnsureCommandSourceComponents(Entity owner)
        {
            if (!_world.Has<CommandSourceDragState>(owner))
            {
                _world.Add(owner, default(CommandSourceDragState));
            }
        }

        private void UpdateHoveredEntity(Entity hovered)
        {
            CommandSourceAcquisitionCollectionConfig acquisition = _config.Acquisition
                ?? throw new InvalidOperationException("commandSource.acquisition must be explicitly configured.");
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

        private void ApplyClickSelection(Entity owner, Entity clicked, CommandSourceAcquisitionMode acquisitionMode)
        {
            if (_world.IsAlive(clicked))
            {
                Span<Entity> hit = stackalloc Entity[1];
                hit[0] = clicked;
                ApplyAcquisition(owner, hit, acquisitionMode);
                return;
            }

            if (acquisitionMode == CommandSourceAcquisitionMode.Replace)
            {
                ApplyAcquisition(owner, ReadOnlySpan<Entity>.Empty, acquisitionMode);
            }
        }

        private void ApplyBoxSelection(Entity owner, in CommandSourceDragState drag, CommandSourceAcquisitionMode acquisitionMode)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) || projectorObj is not IScreenProjector projector)
            {
                return;
            }

            ScreenRect marquee = ScreenRect.FromPoints(drag.StartScreen, drag.CurrentScreen);
            int nextCount = 0;
            _world.Query(in SelectableQuery, (Entity entity, ref VisualTransform transform, ref CullState cull, ref CommandSourceSelectableTag selectable) =>
            {
                if (!cull.IsVisible ||
                    !CommandSourceEligibility.CanAcquire(_world, _globals, owner, entity, _targetRelationFilter))
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

            _world.Query(in SelectableQuery, (Entity entity, ref VisualTransform transform, ref CullState cull, ref CommandSourceSelectableTag selectable) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                if (!CommandSourceEligibility.CanInspectLive(_world, _globals, owner, entity))
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
            return _world.IsAlive(hovered) && CommandSourceEligibility.CanAcquire(_world, _globals, owner, hovered, _targetRelationFilter)
                ? hovered
                : Entity.Null;
        }

        private void ApplyAcquisition(Entity owner, ReadOnlySpan<Entity> hits, CommandSourceAcquisitionMode mode)
        {
            CommandSourceAcquisitionCollectionConfig acquisition = _config.Acquisition
                ?? throw new InvalidOperationException("commandSource.acquisition must be explicitly configured.");
            string collectionKey = RequireConfiguredKey(acquisition.CollectionKey, "commandSource.acquisition.collectionKey");
            var descriptor = EntityCollectionDescriptor.Create(
                collectionKey,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.AcquisitionPreview,
                owner,
                hits.Length > 0 ? hits[0] : Entity.Null,
                string.IsNullOrWhiteSpace(acquisition.Title) ? "Command acquisition" : acquisition.Title,
                $"{mode} | {hits.Length} entities");
            _entityCollections.Replace(owner, descriptor, hits);

            switch (mode)
            {
                case CommandSourceAcquisitionMode.Replace:
                    PublishCommandSource(owner, hits, mode);
                    return;

                case CommandSourceAcquisitionMode.Additive:
                    PublishMergedCommandSource(owner, hits, mode);
                    return;

                case CommandSourceAcquisitionMode.Toggle:
                    PublishMergedCommandSource(owner, hits, mode);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported selection acquisition mode '{mode}'.");
            }
        }

        private void PublishMergedCommandSource(Entity owner, ReadOnlySpan<Entity> hits, CommandSourceAcquisitionMode mode)
        {
            int count = CopyCurrentCommandSource(owner);
            for (int i = 0; i < hits.Length; i++)
            {
                Entity hit = hits[i];
                if (!_world.IsAlive(hit))
                {
                    continue;
                }

                int existingIndex = IndexOf(_commandSourceScratch.AsSpan(0, count), hit);
                if (mode == CommandSourceAcquisitionMode.Toggle && existingIndex >= 0)
                {
                    count = RemoveAt(_commandSourceScratch, count, existingIndex);
                    continue;
                }

                if (existingIndex < 0)
                {
                    EnsureCommandSourceScratchCapacity(count + 1);
                    _commandSourceScratch[count++] = hit;
                }
            }

            PublishCommandSource(owner, _commandSourceScratch.AsSpan(0, count), mode);
        }

        private void PublishCommandSource(Entity owner, ReadOnlySpan<Entity> members, CommandSourceAcquisitionMode mode)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                $"{mode} | {members.Length} actor(s)");
            _entityCollections.Replace(owner, descriptor, members, owner);
        }

        private int CopyCurrentCommandSource(Entity owner)
        {
            if (!_entityCollections.TryGet(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle) ||
                !_entityCollections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return 0;
            }

            EnsureCommandSourceScratchCapacity(view.Count);
            return _entityCollections.CopyEntities(handle, 0, _commandSourceScratch.AsSpan(0, view.Count));
        }

        private CommandSourceAcquisitionMode ResolveAcquisitionMode()
        {
            if (_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                inputObj is Ludots.Core.Input.Runtime.IInputActionReader input)
            {
                bool additive = input.IsDown(CommandSourceModifierActionIds.Additive);
                bool toggle = input.IsDown(CommandSourceModifierActionIds.Toggle);
                if (toggle)
                {
                    return CommandSourceAcquisitionMode.Toggle;
                }

                if (additive)
                {
                    return CommandSourceAcquisitionMode.Additive;
                }
            }

            return CommandSourceAcquisitionMode.Replace;
        }

        private void EnsureCommandSourceScratchCapacity(int required)
        {
            if (required <= _commandSourceScratch.Length)
            {
                return;
            }

            int nextSize = _commandSourceScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _commandSourceScratch, nextSize);
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

        private static int IndexOf(ReadOnlySpan<Entity> entities, Entity value)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int RemoveAt(Entity[] entities, int count, int index)
        {
            if ((uint)index >= (uint)count)
            {
                return count;
            }

            int tail = count - index - 1;
            if (tail > 0)
            {
                Array.Copy(entities, index + 1, entities, index, tail);
            }

            entities[count - 1] = Entity.Null;
            return count - 1;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private static CommandSourceAcquisitionConfig ResolveCommandSourceAcquisitionConfig(Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.CommandSourceAcquisitionConfig.Name, out var configObj) &&
                configObj is CommandSourceAcquisitionConfig config)
            {
                return config;
            }

            throw new InvalidOperationException(
                $"{nameof(CommandSourceAcquisitionSystem)} requires {CoreServiceKeys.CommandSourceAcquisitionConfig.Name} to be registered before construction.");
        }

        private static EntityCollectionStore ResolveEntityCollectionStore(Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var storeObj) &&
                storeObj is EntityCollectionStore store)
            {
                return store;
            }

            throw new InvalidOperationException(
                $"{nameof(CommandSourceAcquisitionSystem)} requires {CoreServiceKeys.EntityCollectionStore.Name} to be registered before construction.");
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
}
