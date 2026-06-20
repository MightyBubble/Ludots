using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Spatial;

namespace Ludots.Core.Config
{
    [Flags]
    internal enum TemplateBatchSpawnFeatures : byte
    {
        None = 0,
        MapEntity = 1 << 0,
        PresentationStableId = 1 << 1,
        PresentationLifecycleState = 1 << 2,
        SpatialCellRef = 1 << 3,
        PerformerRootBootstrapHandled = 1 << 4,
        PresentationOwnerHasPerformerPayload = 1 << 5,
    }

    internal sealed class TemplateEntityBatchSpawner
    {
        private readonly World _world;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PresentationStableIdAllocator? _stableIds;
        private readonly ISpatialPartitionWorld? _partition;
        private readonly WorldSizeSpec _worldSizeSpec;
        private readonly Entity[] _scratchEntities;
        private readonly Dictionary<string, TemplateSpawnDescriptor> _descriptors = new(StringComparer.Ordinal);

        public TemplateEntityBatchSpawner(
            World world,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator? stableIds = null,
            ISpatialPartitionWorld? partition = null,
            WorldSizeSpec worldSizeSpec = default,
            int scratchCapacity = 4096)
        {
            if (scratchCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(scratchCapacity));
            }

            _world = world ?? throw new ArgumentNullException(nameof(world));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds;
            _partition = partition;
            _worldSizeSpec = worldSizeSpec;
            _scratchEntities = new Entity[scratchCapacity];
        }

        public int ScratchCapacity => _scratchEntities.Length;

        public double LastWorldCreateMs { get; private set; }

        public double LastFillCreatedBatchMs { get; private set; }

        public bool IsBatchCompatible(string templateId, EntityTemplate template)
        {
            return TryGetDescriptor(templateId, template, out TemplateSpawnDescriptor descriptor) && descriptor.IsCompatible;
        }

        public int GetOnSpawnEffectTemplateId(string templateId, EntityTemplate template)
        {
            TryGetDescriptor(templateId, template, out TemplateSpawnDescriptor descriptor);
            return descriptor.OnSpawnEffectTemplateId;
        }

        public bool TryCreateBatch(
            string templateId,
            EntityTemplate template,
            ReadOnlySpan<TemplateBatchSpawnRequest> requests,
            TemplateBatchSpawnFeatures features,
            out ReadOnlySpan<Entity> createdEntities,
            Span<int> createdStableIds = default,
            Span<VisualTransform> createdVisuals = default,
            Span<CullState> createdCulls = default)
        {
            createdEntities = default;
            if (requests.Length == 0)
            {
                return false;
            }

            if (requests.Length > _scratchEntities.Length)
            {
                return false;
            }

            if (!TryGetDescriptor(templateId, template, out TemplateSpawnDescriptor descriptor))
            {
                return false;
            }

            if (!createdStableIds.IsEmpty && createdStableIds.Length < requests.Length)
            {
                return false;
            }

            if (!createdVisuals.IsEmpty && createdVisuals.Length < requests.Length)
            {
                return false;
            }

            if (!createdCulls.IsEmpty && createdCulls.Length < requests.Length)
            {
                return false;
            }

            if ((features & TemplateBatchSpawnFeatures.PresentationStableId) != 0 && _stableIds == null)
            {
                return false;
            }

            if ((features & TemplateBatchSpawnFeatures.MapEntity) != 0)
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    if (!requests[i].HasMapEntity)
                    {
                        return false;
                    }
                }
            }

            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly TemplateBatchSpawnRequest request = ref requests[i];
                if (request.HasMassCrowdFormationAnchorOverride &&
                    !descriptor.HasMassCrowdFormationAnchor)
                {
                    throw new InvalidOperationException(
                        $"Runtime template batch spawn request for '{templateId}' supplied MassCrowdFormationAnchor override but the template does not author MassCrowdFormationAnchor.");
                }

                if (request.HasMassCrowdFormationFollowerOverride &&
                    !descriptor.HasMassCrowdFormationFollower)
                {
                    throw new InvalidOperationException(
                        $"Runtime template batch spawn request for '{templateId}' supplied MassCrowdFormationFollower override but the template does not author MassCrowdFormationFollower.");
                }
            }

            Signature signature = descriptor.BaseSignature;
            if ((features & TemplateBatchSpawnFeatures.MapEntity) != 0)
            {
                signature += Component<MapEntity>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.PresentationStableId) != 0)
            {
                signature += Component<PresentationStableId>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.PresentationLifecycleState) != 0)
            {
                signature += Component<PresentationLifecycleState>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.SpatialCellRef) != 0)
            {
                if (_partition == null || _worldSizeSpec.GridCellSizeCm <= 0)
                {
                    return false;
                }

                signature += Component<SpatialCellRef>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.PerformerRootBootstrapHandled) != 0)
            {
                signature += Component<PerformerRootBootstrapHandled>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.PresentationOwnerHasPerformerPayload) != 0)
            {
                signature += Component<PresentationOwnerHasPerformerPayload>.Signature;
            }

            long createStart = Stopwatch.GetTimestamp();
            _world.Create(_scratchEntities.AsSpan(0, requests.Length), signature, requests.Length);
            LastWorldCreateMs = ElapsedMs(createStart);

            long fillStart = Stopwatch.GetTimestamp();
            FillCreatedBatch(
                descriptor,
                requests,
                features,
                _scratchEntities.AsSpan(0, requests.Length),
                createdStableIds,
                createdVisuals,
                createdCulls);
            LastFillCreatedBatchMs = ElapsedMs(fillStart);

            createdEntities = _scratchEntities.AsSpan(0, requests.Length);
            return true;
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private void FillCreatedBatch(
            in TemplateSpawnDescriptor descriptor,
            ReadOnlySpan<TemplateBatchSpawnRequest> requests,
            TemplateBatchSpawnFeatures features,
            ReadOnlySpan<Entity> created,
            Span<int> createdStableIds,
            Span<VisualTransform> createdVisuals,
            Span<CullState> createdCulls)
        {
            Entity first = created[0];
            Archetype archetype = _world.GetEntityDataArray()[first.Id].Archetype;
            Slot slot = _world.GetSlot(first);
            bool includeMapEntity = (features & TemplateBatchSpawnFeatures.MapEntity) != 0;
            bool includeStableId = (features & TemplateBatchSpawnFeatures.PresentationStableId) != 0;
            bool includeLifecycleState = (features & TemplateBatchSpawnFeatures.PresentationLifecycleState) != 0;
            bool includeSpatialCellRef = (features & TemplateBatchSpawnFeatures.SpatialCellRef) != 0;
            bool includeBootstrapHandled = (features & TemplateBatchSpawnFeatures.PerformerRootBootstrapHandled) != 0;
            bool includeOwnerPayload = (features & TemplateBatchSpawnFeatures.PresentationOwnerHasPerformerPayload) != 0;
            bool includeDynamicHeightSampling = descriptor.HasDynamicHeightSampling;
            int batchIndex = 0;
            int chunkIndex = slot.ChunkIndex;
            int row = slot.Index;

            while (batchIndex < created.Length)
            {
                ref Chunk chunk = ref archetype.GetChunk(chunkIndex);
                int run = Math.Min(created.Length - batchIndex, chunk.Count - row);

                Span<Name> names = chunk.GetSpan<Name>();
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<PreviousWorldPositionCm> previousPositions = chunk.GetSpan<PreviousWorldPositionCm>();
                Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                Span<VisualHeightmapSampleState> heightSamples = includeDynamicHeightSampling
                    ? chunk.GetSpan<VisualHeightmapSampleState>()
                    : default;
                Span<CullState> culls = chunk.GetSpan<CullState>();
                Span<AttributeBuffer> attributes = descriptor.HasAttributeBuffer ? chunk.GetSpan<AttributeBuffer>() : default;
                Span<AttributeLastSnapshot> attributeSnapshots = descriptor.HasAttributeBuffer ? chunk.GetSpan<AttributeLastSnapshot>() : default;
                Span<GameplayTagContainer> gameplayTags = descriptor.HasGameplayTagContainer ? chunk.GetSpan<GameplayTagContainer>() : default;
                Span<TagCountContainer> tagCounts = descriptor.HasTagCountContainer ? chunk.GetSpan<TagCountContainer>() : default;
                Span<EntityTemplateKeyRef> templateKeys = chunk.GetSpan<EntityTemplateKeyRef>();
                Span<OrderBuffer> orderBuffers = descriptor.HasOrderBuffer ? chunk.GetSpan<OrderBuffer>() : default;
                Span<SelectionSelectableState> selectionStates = descriptor.HasSelectionSelectableState ? chunk.GetSpan<SelectionSelectableState>() : default;
                Span<Ludots.Core.Gameplay.Components.EntityLayer> entityLayers = descriptor.HasEntityLayer ? chunk.GetSpan<Ludots.Core.Gameplay.Components.EntityLayer>() : default;
                Span<Team> teams = descriptor.HasTeam ? chunk.GetSpan<Team>() : default;
                Span<PlayerOwner> playerOwners = descriptor.HasPlayerOwner ? chunk.GetSpan<PlayerOwner>() : default;
                Span<MassCrowdAgent> massCrowdAgents = descriptor.HasMassCrowdAgent ? chunk.GetSpan<MassCrowdAgent>() : default;
                Span<MassCrowdBlocker> massCrowdBlockers = descriptor.HasMassCrowdBlocker ? chunk.GetSpan<MassCrowdBlocker>() : default;
                Span<MassCrowdFormationAnchor> massCrowdFormationAnchors = descriptor.HasMassCrowdFormationAnchor ? chunk.GetSpan<MassCrowdFormationAnchor>() : default;
                Span<MassCrowdFormationFollower> massCrowdFormationFollowers = descriptor.HasMassCrowdFormationFollower ? chunk.GetSpan<MassCrowdFormationFollower>() : default;
                Span<MassCrowdFollowerLocomotion> massCrowdFollowerLocomotions = descriptor.HasMassCrowdFollowerLocomotion ? chunk.GetSpan<MassCrowdFollowerLocomotion>() : default;
                Span<MapEntity> mapEntities = includeMapEntity ? chunk.GetSpan<MapEntity>() : default;
                Span<PresentationStableId> stableIds = includeStableId ? chunk.GetSpan<PresentationStableId>() : default;
                Span<PresentationLifecycleState> lifecycleStates = includeLifecycleState ? chunk.GetSpan<PresentationLifecycleState>() : default;
                Span<SpatialCellRef> spatialRefs = includeSpatialCellRef ? chunk.GetSpan<SpatialCellRef>() : default;
                Span<PerformerRootBootstrapHandled> bootstrapHandled = includeBootstrapHandled ? chunk.GetSpan<PerformerRootBootstrapHandled>() : default;
                Span<PresentationOwnerHasPerformerPayload> ownerPayloads = includeOwnerPayload ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>() : default;

                for (int offset = 0; offset < run; offset++)
                {
                    int requestIndex = batchIndex + offset;
                    int componentIndex = row + offset;
                    ref readonly TemplateBatchSpawnRequest request = ref requests[requestIndex];
                    Entity entity = created[requestIndex];
                    var worldPosition = request.HasWorldPosition ? request.WorldPositionCm : descriptor.DefaultWorldPosition;
                    float facingAngle = request.HasFacing ? request.FacingAngleRad : descriptor.Facing.AngleRad;

                    names[componentIndex] = descriptor.Name;
                    positions[componentIndex] = new WorldPositionCm { Value = worldPosition };
                    previousPositions[componentIndex] = new PreviousWorldPositionCm { Value = worldPosition };
                    facings[componentIndex] = new FacingDirection
                    {
                        AngleRad = facingAngle,
                    };
                    VisualTransform visual = descriptor.HasStaticTransform
                        ? CreateStaticVisualTransform(in worldPosition, facingAngle)
                        : descriptor.VisualTransform;
                    CullState cull = descriptor.CullState;
                    visuals[componentIndex] = visual;
                    if (includeDynamicHeightSampling)
                    {
                        heightSamples[componentIndex] = default;
                    }

                    culls[componentIndex] = cull;
                    if (descriptor.HasAttributeBuffer)
                    {
                        AttributeBuffer attributeBuffer = descriptor.CreateAttributeBuffer();
                        attributes[componentIndex] = attributeBuffer;
                        attributeSnapshots[componentIndex] = descriptor.CreateAttributeLastSnapshot(ref attributeBuffer);
                    }

                    if (descriptor.HasGameplayTagContainer)
                    {
                        gameplayTags[componentIndex] = descriptor.GameplayTags;
                    }

                    if (descriptor.HasTagCountContainer)
                    {
                        tagCounts[componentIndex] = descriptor.TagCounts;
                    }
                    templateKeys[componentIndex] = descriptor.TemplateKey;
                    if (descriptor.HasOrderBuffer)
                    {
                        orderBuffers[componentIndex] = OrderBuffer.CreateEmpty();
                    }

                    if (descriptor.HasSelectionSelectableState)
                    {
                        selectionStates[componentIndex] = descriptor.SelectionSelectableState;
                    }

                    if (descriptor.HasEntityLayer)
                    {
                        entityLayers[componentIndex] = descriptor.EntityLayer;
                    }

                    if (descriptor.HasTeam)
                    {
                        teams[componentIndex] = descriptor.Team;
                    }

                    if (descriptor.HasPlayerOwner)
                    {
                        playerOwners[componentIndex] = descriptor.PlayerOwner;
                    }

                    if (descriptor.HasMassCrowdAgent)
                    {
                        massCrowdAgents[componentIndex] = descriptor.MassCrowdAgent;
                    }

                    if (descriptor.HasMassCrowdBlocker)
                    {
                        massCrowdBlockers[componentIndex] = descriptor.MassCrowdBlocker;
                    }

                    if (descriptor.HasMassCrowdFormationAnchor)
                    {
                        massCrowdFormationAnchors[componentIndex] = request.HasMassCrowdFormationAnchorOverride
                            ? request.MassCrowdFormationAnchorOverride
                            : descriptor.MassCrowdFormationAnchor;
                    }

                    if (descriptor.HasMassCrowdFormationFollower)
                    {
                        massCrowdFormationFollowers[componentIndex] = request.HasMassCrowdFormationFollowerOverride
                            ? request.MassCrowdFormationFollowerOverride
                            : descriptor.MassCrowdFormationFollower;
                    }

                    if (descriptor.HasMassCrowdFollowerLocomotion)
                    {
                        massCrowdFollowerLocomotions[componentIndex] = descriptor.MassCrowdFollowerLocomotion;
                    }

                    if (includeMapEntity)
                    {
                        mapEntities[componentIndex] = request.MapEntity;
                    }

                    if (includeStableId)
                    {
                        int stableIdValue = request.HasPresentationStableId
                            ? request.PresentationStableId
                            : _stableIds!.Allocate();
                        stableIds[componentIndex] = new PresentationStableId
                        {
                            Value = stableIdValue,
                        };

                        if (!createdStableIds.IsEmpty)
                        {
                            createdStableIds[requestIndex] = stableIdValue;
                        }
                    }

                    if (!createdVisuals.IsEmpty)
                    {
                        createdVisuals[requestIndex] = visual;
                    }

                    if (!createdCulls.IsEmpty)
                    {
                        createdCulls[requestIndex] = cull;
                    }

                    if (includeLifecycleState)
                    {
                        lifecycleStates[componentIndex] = new PresentationLifecycleState
                        {
                            Spawned = true,
                        };
                    }

                    if (includeSpatialCellRef)
                    {
                        var worldCm = worldPosition.ToWorldCmInt2();
                        if (!_worldSizeSpec.Contains(worldCm))
                        {
                            throw new InvalidOperationException(
                                $"SPATIAL.ERR.WorldPositionOutOfBounds entity={entity.Id}:{entity.WorldId} pos=({worldCm.X},{worldCm.Y}) bounds={_worldSizeSpec.Bounds} cell={_worldSizeSpec.GridCellSizeCm}");
                        }

                        int cellX = FloorDiv(worldCm.X, _worldSizeSpec.GridCellSizeCm);
                        int cellY = FloorDiv(worldCm.Y, _worldSizeSpec.GridCellSizeCm);
                        spatialRefs[componentIndex] = new SpatialCellRef
                        {
                            CellX = cellX,
                            CellY = cellY,
                            Initialized = 1,
                        };
                        _partition!.Add(entity, cellX, cellY);
                    }

                    if (includeBootstrapHandled)
                    {
                        bootstrapHandled[componentIndex] = default;
                    }

                    if (includeOwnerPayload)
                    {
                        ownerPayloads[componentIndex] = new PresentationOwnerHasPerformerPayload
                        {
                            Count = 0,
                            RootCount = 0,
                            SingleRootPerformer = Entity.Null,
                            SingleRootTransformSync = 0,
                        };
                    }
                }

                batchIndex += run;
                chunkIndex++;
                row = 0;
            }
        }

        private bool TryGetDescriptor(string templateId, EntityTemplate template, out TemplateSpawnDescriptor descriptor)
        {
            if (_descriptors.TryGetValue(templateId, out descriptor))
            {
                return descriptor.IsCompatible;
            }

            descriptor = TemplateSpawnDescriptor.Create(templateId, template, _templateKeys);
            _descriptors[templateId] = descriptor;
            return descriptor.IsCompatible;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = Math.DivRem(value, divisor, out int remainder);
            return value < 0 && remainder != 0 ? quotient - 1 : quotient;
        }

        internal readonly struct TemplateBatchSpawnRequest
        {
            public TemplateBatchSpawnRequest(
                in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPositionCm,
                bool hasWorldPosition,
                float facingAngleRad = 0f,
                bool hasFacing = false,
                in MapEntity mapEntity = default,
                bool hasMapEntity = false,
                int presentationStableId = 0,
                bool hasPresentationStableId = false,
                MassCrowdFormationAnchor massCrowdFormationAnchorOverride = default,
                bool hasMassCrowdFormationAnchorOverride = false,
                MassCrowdFormationFollower massCrowdFormationFollowerOverride = default,
                bool hasMassCrowdFormationFollowerOverride = false,
                ParamDefault[]? performerParamOverrides = null)
            {
                WorldPositionCm = worldPositionCm;
                HasWorldPosition = hasWorldPosition;
                FacingAngleRad = facingAngleRad;
                HasFacing = hasFacing;
                MapEntity = mapEntity;
                HasMapEntity = hasMapEntity;
                PresentationStableId = presentationStableId;
                HasPresentationStableId = hasPresentationStableId;
                MassCrowdFormationAnchorOverride = massCrowdFormationAnchorOverride;
                HasMassCrowdFormationAnchorOverride = hasMassCrowdFormationAnchorOverride;
                MassCrowdFormationFollowerOverride = massCrowdFormationFollowerOverride;
                HasMassCrowdFormationFollowerOverride = hasMassCrowdFormationFollowerOverride;
                PerformerParamOverrides = performerParamOverrides ?? Array.Empty<ParamDefault>();
            }

            public Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 WorldPositionCm { get; }

            public bool HasWorldPosition { get; }

            public float FacingAngleRad { get; }

            public bool HasFacing { get; }

            public MapEntity MapEntity { get; }

            public bool HasMapEntity { get; }

            public int PresentationStableId { get; }

            public bool HasPresentationStableId { get; }

            public MassCrowdFormationAnchor MassCrowdFormationAnchorOverride { get; }

            public bool HasMassCrowdFormationAnchorOverride { get; }

            public MassCrowdFormationFollower MassCrowdFormationFollowerOverride { get; }

            public bool HasMassCrowdFormationFollowerOverride { get; }

            public ParamDefault[] PerformerParamOverrides { get; }
        }

        private readonly struct TemplateSpawnDescriptor
        {
            public readonly bool IsCompatible;
            public readonly Signature BaseSignature;
            public readonly bool HasStaticTransform;
            public readonly bool HasDynamicHeightSampling;
            public readonly Name Name;
            public readonly Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 DefaultWorldPosition;
            public readonly FacingDirection Facing;
            public readonly VisualTransform VisualTransform;
            public readonly CullState CullState;
            public readonly bool HasAttributeBuffer;
            public readonly bool HasGameplayTagContainer;
            public readonly bool HasTagCountContainer;
            public readonly GameplayTagContainer GameplayTags;
            public readonly TagCountContainer TagCounts;
            public readonly EntityTemplateKeyRef TemplateKey;
            public readonly bool HasOrderBuffer;
            public readonly bool HasSelectionSelectableState;
            public readonly SelectionSelectableState SelectionSelectableState;
            public readonly bool HasEntityLayer;
            public readonly Ludots.Core.Gameplay.Components.EntityLayer EntityLayer;
            public readonly bool HasTeam;
            public readonly Team Team;
            public readonly bool HasPlayerOwner;
            public readonly PlayerOwner PlayerOwner;
            public readonly bool HasMassCrowdAgent;
            public readonly MassCrowdAgent MassCrowdAgent;
            public readonly bool HasMassCrowdBlocker;
            public readonly MassCrowdBlocker MassCrowdBlocker;
            public readonly bool HasMassCrowdFormationAnchor;
            public readonly MassCrowdFormationAnchor MassCrowdFormationAnchor;
            public readonly bool HasMassCrowdFormationFollower;
            public readonly MassCrowdFormationFollower MassCrowdFormationFollower;
            public readonly bool HasMassCrowdFollowerLocomotion;
            public readonly MassCrowdFollowerLocomotion MassCrowdFollowerLocomotion;
            public readonly int OnSpawnEffectTemplateId;
            public readonly ComponentType[] TagComponentTypes;
            private readonly AttributeSeed[] _attributeSeeds;

            private TemplateSpawnDescriptor(
                bool isCompatible,
                Signature baseSignature,
                bool hasStaticTransform,
                bool hasDynamicHeightSampling,
                Name name,
                in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 defaultWorldPosition,
                FacingDirection facing,
                VisualTransform visualTransform,
                CullState cullState,
                bool hasAttributeBuffer,
                bool hasGameplayTagContainer,
                bool hasTagCountContainer,
                GameplayTagContainer gameplayTags,
                TagCountContainer tagCounts,
                EntityTemplateKeyRef templateKey,
                bool hasOrderBuffer,
                SelectionSelectableState selectionSelectableState,
                bool hasSelectionSelectableState,
                Ludots.Core.Gameplay.Components.EntityLayer entityLayer,
                bool hasEntityLayer,
                Team team,
                bool hasTeam,
                PlayerOwner playerOwner,
                bool hasPlayerOwner,
                MassCrowdAgent massCrowdAgent,
                bool hasMassCrowdAgent,
                MassCrowdBlocker massCrowdBlocker,
                bool hasMassCrowdBlocker,
                MassCrowdFormationAnchor massCrowdFormationAnchor,
                bool hasMassCrowdFormationAnchor,
                MassCrowdFormationFollower massCrowdFormationFollower,
                bool hasMassCrowdFormationFollower,
                MassCrowdFollowerLocomotion massCrowdFollowerLocomotion,
                bool hasMassCrowdFollowerLocomotion,
                int onSpawnEffectTemplateId,
                ComponentType[] tagComponentTypes,
                AttributeSeed[] attributeSeeds)
            {
                IsCompatible = isCompatible;
                BaseSignature = baseSignature;
                HasStaticTransform = hasStaticTransform;
                HasDynamicHeightSampling = hasDynamicHeightSampling;
                Name = name;
                DefaultWorldPosition = defaultWorldPosition;
                Facing = facing;
                VisualTransform = visualTransform;
                CullState = cullState;
                HasAttributeBuffer = hasAttributeBuffer;
                HasGameplayTagContainer = hasGameplayTagContainer;
                HasTagCountContainer = hasTagCountContainer;
                GameplayTags = gameplayTags;
                TagCounts = tagCounts;
                TemplateKey = templateKey;
                HasOrderBuffer = hasOrderBuffer;
                SelectionSelectableState = selectionSelectableState;
                HasSelectionSelectableState = hasSelectionSelectableState;
                EntityLayer = entityLayer;
                HasEntityLayer = hasEntityLayer;
                Team = team;
                HasTeam = hasTeam;
                PlayerOwner = playerOwner;
                HasPlayerOwner = hasPlayerOwner;
                MassCrowdAgent = massCrowdAgent;
                HasMassCrowdAgent = hasMassCrowdAgent;
                MassCrowdBlocker = massCrowdBlocker;
                HasMassCrowdBlocker = hasMassCrowdBlocker;
                MassCrowdFormationAnchor = massCrowdFormationAnchor;
                HasMassCrowdFormationAnchor = hasMassCrowdFormationAnchor;
                MassCrowdFormationFollower = massCrowdFormationFollower;
                HasMassCrowdFormationFollower = hasMassCrowdFormationFollower;
                MassCrowdFollowerLocomotion = massCrowdFollowerLocomotion;
                HasMassCrowdFollowerLocomotion = hasMassCrowdFollowerLocomotion;
                OnSpawnEffectTemplateId = onSpawnEffectTemplateId;
                TagComponentTypes = tagComponentTypes ?? Array.Empty<ComponentType>();
                _attributeSeeds = attributeSeeds ?? Array.Empty<AttributeSeed>();
            }

            public AttributeBuffer CreateAttributeBuffer()
            {
                var buffer = default(AttributeBuffer);
                for (int i = 0; i < _attributeSeeds.Length; i++)
                {
                    if (_attributeSeeds[i].HasBase)
                    {
                        buffer.SetBase(_attributeSeeds[i].AttributeId, _attributeSeeds[i].BaseValue);
                    }
                }

                for (int i = 0; i < _attributeSeeds.Length; i++)
                {
                    if (_attributeSeeds[i].HasCurrent)
                    {
                        buffer.SetCurrent(_attributeSeeds[i].AttributeId, _attributeSeeds[i].CurrentValue);
                    }
                }

                return buffer;
            }

            public unsafe AttributeLastSnapshot CreateAttributeLastSnapshot(ref AttributeBuffer buffer)
            {
                var snapshot = default(AttributeLastSnapshot);
                for (int i = 0; i < _attributeSeeds.Length; i++)
                {
                    int attributeId = _attributeSeeds[i].AttributeId;
                    snapshot.Values[attributeId] = buffer.GetCurrent(attributeId);
                }

                return snapshot;
            }

            public static TemplateSpawnDescriptor Create(string templateId, EntityTemplate template, EntityTemplateKeyRegistry templateKeys)
            {
                if (template == null)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' requires a non-null template.");
                }

                int onSpawnEffectTemplateId = 0;
                if (!string.IsNullOrWhiteSpace(template.OnSpawnEffect))
                {
                    onSpawnEffectTemplateId = EffectTemplateIdRegistry.GetId(template.OnSpawnEffect);
                    if (onSpawnEffectTemplateId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Entity template '{templateId}' references unknown onSpawnEffect '{template.OnSpawnEffect}'.");
                    }
                }

                if (template.Components == null)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' requires an explicit components object.");
                }

                if (!IsBatchCandidate(template.Components))
                {
                    return Incompatible(onSpawnEffectTemplateId);
                }

                if (!TryValidateSupportedComponents(template.Components))
                {
                    return Incompatible(onSpawnEffectTemplateId);
                }

                Name name = ParseName(templateId, template.Components);
                ComponentType[] tagComponentTypes = CollectTagComponentTypes(templateId, template.Components);

                var defaultWorldPosition = default(Ludots.Core.Mathematics.FixedPoint.Fix64Vec2);
                if (template.Components.TryGetValue("WorldPositionCm", out JsonNode worldPositionNode))
                {
                    defaultWorldPosition = ParseWorldPosition(templateId, worldPositionNode);
                }

                float facingAngle = 0f;
                if (template.Components.TryGetValue("FacingDirection", out JsonNode facingNode))
                {
                    facingAngle = ParseFacing(templateId, facingNode);
                }

                int templateKeyId = templateKeys.GetId(templateId);
                if (templateKeyId <= 0)
                {
                    templateKeyId = templateKeys.Register(templateId);
                }

                bool hasStaticTransform = template.Components.ContainsKey("PresentationStaticTransform");
                bool hasStaticHeightPending = template.Components.ContainsKey("PresentationStaticHeightPending");
                bool hasDynamicHeightSampling = template.Components.ContainsKey("VisualHeightmapSampleState");
                bool hasSpatialPartitionExcluded = template.Components.ContainsKey("SpatialPartitionExcluded");
                bool hasAttributeBuffer = template.Components.ContainsKey("AttributeBuffer");
                bool hasGameplayTagContainer = template.Components.ContainsKey("GameplayTagContainer");
                bool hasTagCountContainer = template.Components.ContainsKey("TagCountContainer");
                bool hasOrderBuffer = template.Components.ContainsKey("OrderBuffer");
                bool hasSelectionSelectableState = template.Components.ContainsKey("SelectionSelectableState");
                bool hasEntityLayer = template.Components.ContainsKey("EntityLayer");
                bool hasTeam = template.Components.ContainsKey("Team");
                bool hasPlayerOwner = template.Components.ContainsKey("PlayerOwner");
                bool hasMassCrowdAgent = template.Components.ContainsKey("MassCrowdAgent");
                bool hasMassCrowdBlocker = template.Components.ContainsKey("MassCrowdBlocker");
                bool hasMassCrowdFormationAnchor = template.Components.ContainsKey("MassCrowdFormationAnchor");
                bool hasMassCrowdFormationFollower = template.Components.ContainsKey("MassCrowdFormationFollower");
                bool hasMassCrowdFollowerLocomotion = template.Components.ContainsKey("MassCrowdFollowerLocomotion");
                SelectionSelectableState selectionSelectableState = hasSelectionSelectableState
                    ? ParseSelectionSelectableState(templateId, template.Components["SelectionSelectableState"])
                    : default;
                Ludots.Core.Gameplay.Components.EntityLayer entityLayer = hasEntityLayer
                    ? ParseEntityLayer(templateId, template.Components["EntityLayer"])
                    : default;
                Team team = hasTeam ? ParseTeam(templateId, template.Components["Team"]) : default;
                PlayerOwner playerOwner = hasPlayerOwner ? ParsePlayerOwner(templateId, template.Components["PlayerOwner"]) : default;
                MassCrowdAgent massCrowdAgent = hasMassCrowdAgent
                    ? ParseMassCrowdAgent(templateId, template.Components["MassCrowdAgent"])
                    : default;
                MassCrowdBlocker massCrowdBlocker = hasMassCrowdBlocker
                    ? ParseMassCrowdBlocker(templateId, template.Components["MassCrowdBlocker"])
                    : default;
                MassCrowdFormationAnchor massCrowdFormationAnchor = hasMassCrowdFormationAnchor
                    ? ParseMassCrowdFormationAnchor(templateId, template.Components["MassCrowdFormationAnchor"])
                    : default;
                MassCrowdFormationFollower massCrowdFormationFollower = hasMassCrowdFormationFollower
                    ? ParseMassCrowdFormationFollower(templateId, template.Components["MassCrowdFormationFollower"])
                    : default;
                MassCrowdFollowerLocomotion massCrowdFollowerLocomotion = hasMassCrowdFollowerLocomotion
                    ? ParseMassCrowdFollowerLocomotion(templateId, template.Components["MassCrowdFollowerLocomotion"])
                    : default;
                AttributeSeed[] attributeSeeds = hasAttributeBuffer
                    ? ParseAttributeSeeds(templateId, template.Components)
                    : Array.Empty<AttributeSeed>();

                if (hasGameplayTagContainer)
                {
                    RequireEmptyObject(templateId, template.Components, "GameplayTagContainer");
                }

                if (hasTagCountContainer)
                {
                    RequireEmptyObject(templateId, template.Components, "TagCountContainer");
                }

                if (hasOrderBuffer)
                {
                    RequireEmptyObject(templateId, template.Components, "OrderBuffer");
                }

                if (hasStaticTransform)
                {
                    RequireEmptyObject(templateId, template.Components, "PresentationStaticTransform");
                }

                if (hasStaticHeightPending)
                {
                    RequireEmptyObject(templateId, template.Components, "PresentationStaticHeightPending");
                }

                if (hasDynamicHeightSampling)
                {
                    RequireEmptyObject(templateId, template.Components, "VisualHeightmapSampleState");
                }

                if (hasSpatialPartitionExcluded)
                {
                    RequireEmptyObject(templateId, template.Components, "SpatialPartitionExcluded");
                }

                Signature signature =
                    Component<Name>.Signature +
                    Component<WorldPositionCm>.Signature +
                    Component<PreviousWorldPositionCm>.Signature +
                    Component<FacingDirection>.Signature +
                    Component<VisualTransform>.Signature +
                    Component<CullState>.Signature +
                    Component<EntityTemplateKeyRef>.Signature;

                if (hasAttributeBuffer)
                {
                    signature += Component<AttributeBuffer>.Signature;
                    signature += Component<AttributeLastSnapshot>.Signature;
                }

                if (hasGameplayTagContainer)
                {
                    signature += Component<GameplayTagContainer>.Signature;
                }

                if (hasTagCountContainer)
                {
                    signature += Component<TagCountContainer>.Signature;
                }

                if (hasDynamicHeightSampling)
                {
                    signature += Component<VisualHeightmapSampleState>.Signature;
                }

                if (hasStaticTransform)
                {
                    signature += Component<PresentationStaticTransform>.Signature;
                    signature += Component<PresentationStaticVisualPending>.Signature;
                    signature += Component<PresentationStaticCullPending>.Signature;
                }

                if (hasStaticHeightPending)
                {
                    signature += Component<PresentationStaticHeightPending>.Signature;
                }

                if (hasSpatialPartitionExcluded)
                {
                    signature += Component<SpatialPartitionExcluded>.Signature;
                }

                if (hasOrderBuffer)
                {
                    signature += Component<OrderBuffer>.Signature;
                }

                if (hasSelectionSelectableState)
                {
                    signature += Component<SelectionSelectableState>.Signature;
                }

                if (hasEntityLayer)
                {
                    signature += Component<Ludots.Core.Gameplay.Components.EntityLayer>.Signature;
                }

                if (hasTeam)
                {
                    signature += Component<Team>.Signature;
                }

                if (hasPlayerOwner)
                {
                    signature += Component<PlayerOwner>.Signature;
                }

                if (hasMassCrowdAgent)
                {
                    signature += Component<MassCrowdAgent>.Signature;
                }

                if (hasMassCrowdBlocker)
                {
                    signature += Component<MassCrowdBlocker>.Signature;
                }

                if (hasMassCrowdFormationAnchor)
                {
                    signature += Component<MassCrowdFormationAnchor>.Signature;
                }

                if (hasMassCrowdFormationFollower)
                {
                    signature += Component<MassCrowdFormationFollower>.Signature;
                }

                if (hasMassCrowdFollowerLocomotion)
                {
                    signature += Component<MassCrowdFollowerLocomotion>.Signature;
                }

                for (int i = 0; i < tagComponentTypes.Length; i++)
                {
                    signature += new Signature(tagComponentTypes[i]);
                }

                return new TemplateSpawnDescriptor(
                    isCompatible: true,
                    signature,
                    hasStaticTransform,
                    hasDynamicHeightSampling,
                    name,
                    defaultWorldPosition,
                    new FacingDirection { AngleRad = facingAngle },
                    VisualTransform.Default,
                    new CullState { IsVisible = false, LOD = LODLevel.Low },
                    hasAttributeBuffer,
                    hasGameplayTagContainer,
                    hasTagCountContainer,
                    default,
                    default,
                    new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                    hasOrderBuffer,
                    selectionSelectableState,
                    hasSelectionSelectableState,
                    entityLayer,
                    hasEntityLayer,
                    team,
                    hasTeam,
                    playerOwner,
                    hasPlayerOwner,
                    massCrowdAgent,
                    hasMassCrowdAgent,
                    massCrowdBlocker,
                    hasMassCrowdBlocker,
                    massCrowdFormationAnchor,
                    hasMassCrowdFormationAnchor,
                    massCrowdFormationFollower,
                    hasMassCrowdFormationFollower,
                    massCrowdFollowerLocomotion,
                    hasMassCrowdFollowerLocomotion,
                    onSpawnEffectTemplateId,
                    tagComponentTypes,
                    attributeSeeds);
            }

            private static TemplateSpawnDescriptor Incompatible(int onSpawnEffectTemplateId)
            {
                return new TemplateSpawnDescriptor(
                    isCompatible: false,
                    default,
                    false,
                    false,
                    default,
                    default,
                    default,
                    default,
                    default,
                    false,
                    false,
                    false,
                    default,
                    default,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    false,
                    onSpawnEffectTemplateId,
                    Array.Empty<ComponentType>(),
                    Array.Empty<AttributeSeed>());
            }

            private static bool IsBatchCandidate(IReadOnlyDictionary<string, JsonNode> components)
            {
                return components.ContainsKey("Name") &&
                       components.ContainsKey("WorldPositionCm") &&
                       components.ContainsKey("FacingDirection");
            }

            private static bool TryValidateSupportedComponents(IReadOnlyDictionary<string, JsonNode> components)
            {
                foreach (string componentName in components.Keys)
                {
                    if (string.Equals(componentName, "Name", StringComparison.Ordinal) ||
                        string.Equals(componentName, "WorldPositionCm", StringComparison.Ordinal) ||
                        string.Equals(componentName, "FacingDirection", StringComparison.Ordinal) ||
                        string.Equals(componentName, "VisualHeightmapSampleState", StringComparison.Ordinal) ||
                        string.Equals(componentName, "AttributeBuffer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "GameplayTagContainer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "TagCountContainer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "OrderBuffer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "SelectionSelectableState", StringComparison.Ordinal) ||
                        string.Equals(componentName, "EntityLayer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "Team", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PlayerOwner", StringComparison.Ordinal) ||
                        string.Equals(componentName, "MassCrowdAgent", StringComparison.Ordinal) ||
                        string.Equals(componentName, "MassCrowdBlocker", StringComparison.Ordinal) ||
                        string.Equals(componentName, "MassCrowdFormationAnchor", StringComparison.Ordinal) ||
                        string.Equals(componentName, "MassCrowdFormationFollower", StringComparison.Ordinal) ||
                        string.Equals(componentName, "MassCrowdFollowerLocomotion", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticTransform", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticCullPending", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticHeightPending", StringComparison.Ordinal) ||
                        string.Equals(componentName, "SpatialPartitionExcluded", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!IsEmptyJsonObject(components[componentName]) ||
                        !ComponentRegistry.TryGetComponentType(componentName, out _))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static ComponentType[] CollectTagComponentTypes(
                string templateId,
                IReadOnlyDictionary<string, JsonNode> components)
            {
                List<ComponentType>? collected = null;
                foreach ((string componentName, JsonNode node) in components)
                {
                    if (IsBuiltInBatchComponent(componentName))
                    {
                        continue;
                    }

                    if (!IsEmptyJsonObject(node) ||
                        !ComponentRegistry.TryGetComponentType(componentName, out ComponentType componentType))
                    {
                        throw new InvalidOperationException(
                            $"Entity template '{templateId}' component '{componentName}' is not supported by the template batch path.");
                    }

                    collected ??= new List<ComponentType>(2);
                    collected.Add(componentType);
                }

                return collected != null && collected.Count > 0
                    ? collected.ToArray()
                    : Array.Empty<ComponentType>();
            }

            private static bool IsBuiltInBatchComponent(string componentName)
            {
                return string.Equals(componentName, "Name", StringComparison.Ordinal) ||
                       string.Equals(componentName, "WorldPositionCm", StringComparison.Ordinal) ||
                       string.Equals(componentName, "FacingDirection", StringComparison.Ordinal) ||
                       string.Equals(componentName, "VisualHeightmapSampleState", StringComparison.Ordinal) ||
                       string.Equals(componentName, "AttributeBuffer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "GameplayTagContainer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "TagCountContainer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "OrderBuffer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "SelectionSelectableState", StringComparison.Ordinal) ||
                       string.Equals(componentName, "EntityLayer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "Team", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PlayerOwner", StringComparison.Ordinal) ||
                       string.Equals(componentName, "MassCrowdAgent", StringComparison.Ordinal) ||
                       string.Equals(componentName, "MassCrowdBlocker", StringComparison.Ordinal) ||
                       string.Equals(componentName, "MassCrowdFormationAnchor", StringComparison.Ordinal) ||
                       string.Equals(componentName, "MassCrowdFormationFollower", StringComparison.Ordinal) ||
                       string.Equals(componentName, "MassCrowdFollowerLocomotion", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticTransform", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticCullPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticHeightPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "SpatialPartitionExcluded", StringComparison.Ordinal);
            }

            private static bool IsEmptyJsonObject(JsonNode node)
            {
                return node is JsonObject obj && obj.Count == 0;
            }

            private static void RequireEmptyObject(
                string templateId,
                IReadOnlyDictionary<string, JsonNode> components,
                string componentName)
            {
                if (!components.TryGetValue(componentName, out JsonNode node) || node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Entity template '{templateId}' component '{componentName}' requires an empty object payload.");
                }

                if (obj.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"Entity template '{templateId}' component '{componentName}' does not accept authored fields.");
                }
            }

            private static void ValidateProperties(JsonObject obj, string context, params string[] allowedNames)
            {
                foreach (var kvp in obj)
                {
                    bool allowed = false;
                    for (int i = 0; i < allowedNames.Length; i++)
                    {
                        if (string.Equals(kvp.Key, allowedNames[i], StringComparison.Ordinal))
                        {
                            allowed = true;
                            break;
                        }
                    }

                    if (!allowed)
                    {
                        throw new InvalidOperationException($"{context} contains unsupported property '{kvp.Key}'.");
                    }
                }
            }

            private static JsonNode RequireProperty(JsonObject obj, string name, string context)
            {
                if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null)
                {
                    throw new InvalidOperationException($"{context} requires explicit '{name}'.");
                }

                return node;
            }

            private static string ParseRequiredString(JsonObject obj, string name, string context)
            {
                JsonNode node = RequireProperty(obj, name, context);
                if (node.GetValueKind() != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"{context}.{name} requires a string value.");
                }

                string value = node.GetValue<string>();
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"{context}.{name} requires a non-empty string value.");
                }

                return value;
            }

            private static int ParseRequiredInt(JsonObject obj, string name, string context)
            {
                JsonNode node = RequireProperty(obj, name, context);
                if (node.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"{context}.{name} requires an integer value.");
                }

                return node.GetValue<int>();
            }

            private static float ParseRequiredFloat(JsonObject obj, string name, string context)
            {
                JsonNode node = RequireProperty(obj, name, context);
                if (node.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"{context}.{name} requires a numeric value.");
                }

                return node.GetValue<float>();
            }

            private static Name ParseName(string templateId, IReadOnlyDictionary<string, JsonNode> components)
            {
                if (!components.TryGetValue("Name", out JsonNode node) || node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' Name requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' Name", "Value");
                JsonNode valueNode = RequireProperty(obj, "Value", $"Entity template '{templateId}' Name");
                if (valueNode.GetValueKind() != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' Name.Value requires a string value.");
                }

                string value = valueNode.GetValue<string>();
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' Name.Value requires a non-empty string value.");
                }

                return new Name { Value = value };
            }

            private static SelectionSelectableState ParseSelectionSelectableState(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' SelectionSelectableState requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' SelectionSelectableState", "IsEnabled");
                JsonNode isEnabledNode = RequireProperty(obj, "IsEnabled", $"Entity template '{templateId}' SelectionSelectableState");
                byte enabled = isEnabledNode.GetValueKind() switch
                {
                    JsonValueKind.True => (byte)1,
                    JsonValueKind.False => (byte)0,
                    _ => throw new InvalidOperationException($"Entity template '{templateId}' SelectionSelectableState.IsEnabled requires a boolean value."),
                };

                return new SelectionSelectableState { IsEnabled = enabled };
            }

            private static Ludots.Core.Gameplay.Components.EntityLayer ParseEntityLayer(string templateId, JsonNode node)
            {
                LayerMask layerMask = EntityLayerAuthoring.ReadLayerMask(node, $"Entity template '{templateId}'");
                return new Ludots.Core.Gameplay.Components.EntityLayer(layerMask);
            }

            private static Team ParseTeam(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' Team requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' Team", "Id");
                JsonNode idNode = RequireProperty(obj, "Id", $"Entity template '{templateId}' Team");
                if (idNode.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' Team.Id requires an integer value.");
                }

                return new Team { Id = idNode.GetValue<int>() };
            }

            private static PlayerOwner ParsePlayerOwner(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' PlayerOwner requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' PlayerOwner", "PlayerId");
                JsonNode idNode = RequireProperty(obj, "PlayerId", $"Entity template '{templateId}' PlayerOwner");
                if (idNode.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' PlayerOwner.PlayerId requires an integer value.");
                }

                return new PlayerOwner { PlayerId = idNode.GetValue<int>() };
            }

            private static MassCrowdAgent ParseMassCrowdAgent(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdAgent requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' MassCrowdAgent", "profileId");
                JsonNode profileNode = RequireProperty(obj, "profileId", $"Entity template '{templateId}' MassCrowdAgent");
                if (profileNode.GetValueKind() != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdAgent.profileId requires a string value.");
                }

                string profileId = profileNode.GetValue<string>();
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdAgent.profileId requires a non-empty string value.");
                }

                return new MassCrowdAgent { ProfileId = MassCrowdProfileRegistry.Register(profileId) };
            }

            private static MassCrowdBlocker ParseMassCrowdBlocker(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdBlocker requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' MassCrowdBlocker", "radiusCm");
                float radiusCm = 0f;
                if (obj.TryGetPropertyValue("radiusCm", out JsonNode radiusNode) && radiusNode != null)
                {
                    if (radiusNode.GetValueKind() != JsonValueKind.Number)
                    {
                        throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdBlocker.radiusCm requires a numeric value.");
                    }

                    radiusCm = radiusNode.GetValue<float>();
                    if (!(radiusCm > 0f))
                    {
                        throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdBlocker.radiusCm must be > 0 when authored.");
                    }
                }

                return new MassCrowdBlocker { RadiusCm = radiusCm };
            }

            private static MassCrowdFormationAnchor ParseMassCrowdFormationAnchor(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFormationAnchor requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' MassCrowdFormationAnchor", "formationId", "slotCount");
                if (obj.Count == 0)
                {
                    return default;
                }

                string formationId = ParseRequiredString(obj, "formationId", $"Entity template '{templateId}' MassCrowdFormationAnchor");
                int slotCount = ParseRequiredInt(obj, "slotCount", $"Entity template '{templateId}' MassCrowdFormationAnchor");
                if (slotCount <= 0)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFormationAnchor.slotCount must be > 0.");
                }

                return new MassCrowdFormationAnchor
                {
                    FormationId = MassCrowdFormationRegistry.Register(formationId),
                    SlotCount = slotCount,
                };
            }

            private static MassCrowdFormationFollower ParseMassCrowdFormationFollower(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFormationFollower requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' MassCrowdFormationFollower", "formationId", "slotIndex", "localOffsetXCm", "localOffsetYCm");
                if (obj.Count == 0)
                {
                    return default;
                }

                return new MassCrowdFormationFollower
                {
                    FormationId = MassCrowdFormationRegistry.Register(ParseRequiredString(obj, "formationId", $"Entity template '{templateId}' MassCrowdFormationFollower")),
                    Anchor = Entity.Null,
                    SlotIndex = ParseRequiredInt(obj, "slotIndex", $"Entity template '{templateId}' MassCrowdFormationFollower"),
                    LocalOffsetXCm = ParseRequiredFloat(obj, "localOffsetXCm", $"Entity template '{templateId}' MassCrowdFormationFollower"),
                    LocalOffsetYCm = ParseRequiredFloat(obj, "localOffsetYCm", $"Entity template '{templateId}' MassCrowdFormationFollower"),
                };
            }

            private static MassCrowdFollowerLocomotion ParseMassCrowdFollowerLocomotion(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFollowerLocomotion requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' MassCrowdFollowerLocomotion", "targetChangeEpsilonCm", "facingChangeEpsilonRadians");
                float targetChangeEpsilonCm = ParseRequiredFloat(obj, "targetChangeEpsilonCm", $"Entity template '{templateId}' MassCrowdFollowerLocomotion");
                float facingChangeEpsilonRadians = ParseRequiredFloat(obj, "facingChangeEpsilonRadians", $"Entity template '{templateId}' MassCrowdFollowerLocomotion");
                if (!(targetChangeEpsilonCm > 0f))
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFollowerLocomotion.targetChangeEpsilonCm must be > 0.");
                }

                if (!(facingChangeEpsilonRadians > 0f))
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' MassCrowdFollowerLocomotion.facingChangeEpsilonRadians must be > 0.");
                }

                return new MassCrowdFollowerLocomotion
                {
                    TargetChangeEpsilonCm = targetChangeEpsilonCm,
                    FacingChangeEpsilonRadians = facingChangeEpsilonRadians,
                };
            }

            private static float ParseFacing(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' FacingDirection requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' FacingDirection", "AngleRad");
                JsonNode angleNode = RequireProperty(obj, "AngleRad", $"Entity template '{templateId}' FacingDirection");
                if (angleNode.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' FacingDirection.AngleRad requires a numeric value.");
                }

                return angleNode.GetValue<float>();
            }

            private static Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 ParseWorldPosition(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' WorldPositionCm requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' WorldPositionCm", "Value");
                JsonNode valueNode = RequireProperty(obj, "Value", $"Entity template '{templateId}' WorldPositionCm");
                if (valueNode is not JsonObject valueObj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' WorldPositionCm.Value requires an object payload.");
                }

                ValidateProperties(valueObj, $"Entity template '{templateId}' WorldPositionCm.Value", "X", "Y");
                JsonNode xNode = RequireProperty(valueObj, "X", $"Entity template '{templateId}' WorldPositionCm.Value");
                JsonNode yNode = RequireProperty(valueObj, "Y", $"Entity template '{templateId}' WorldPositionCm.Value");
                if (xNode.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' WorldPositionCm.Value.X requires an integer value.");
                }

                if (yNode.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' WorldPositionCm.Value.Y requires an integer value.");
                }

                return Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(
                    xNode.GetValue<int>(),
                    yNode.GetValue<int>());
            }

            private static AttributeSeed[] ParseAttributeSeeds(string templateId, IReadOnlyDictionary<string, JsonNode> components)
            {
                if (!components.TryGetValue("AttributeBuffer", out JsonNode node))
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' requires AttributeBuffer for batch spawning.");
                }

                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' AttributeBuffer requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' AttributeBuffer", "base", "current");
                JsonObject baseObj = null;
                if (obj.TryGetPropertyValue("base", out JsonNode baseNode))
                {
                    if (baseNode is not JsonObject parsedBase)
                    {
                        throw new InvalidOperationException($"Entity template '{templateId}' AttributeBuffer.base requires an object payload.");
                    }

                    baseObj = parsedBase;
                }

                JsonObject currentObj = null;
                if (obj.TryGetPropertyValue("current", out JsonNode currentNode))
                {
                    if (currentNode is not JsonObject parsedCurrent)
                    {
                        throw new InvalidOperationException($"Entity template '{templateId}' AttributeBuffer.current requires an object payload.");
                    }

                    currentObj = parsedCurrent;
                }

                if ((baseObj == null || baseObj.Count == 0) &&
                    (currentObj == null || currentObj.Count == 0))
                {
                    return Array.Empty<AttributeSeed>();
                }

                int capacity = (baseObj?.Count ?? 0) + (currentObj?.Count ?? 0);
                AttributeSeed[] seeds = new AttributeSeed[capacity];
                int count = 0;

                if (baseObj != null)
                {
                    foreach (var kvp in baseObj)
                    {
                        if (kvp.Value == null)
                        {
                            throw new InvalidOperationException(
                                $"Entity template '{templateId}' AttributeBuffer.base.{kvp.Key} requires a non-null numeric value.");
                        }

                        if (kvp.Value.GetValueKind() != JsonValueKind.Number)
                        {
                            throw new InvalidOperationException(
                                $"Entity template '{templateId}' AttributeBuffer.base.{kvp.Key} requires a numeric value.");
                        }

                        int attributeId = ResolveAttributeId(kvp.Key);
                        UpsertAttributeSeed(
                            ref seeds,
                            ref count,
                            attributeId,
                            hasBase: true,
                            baseValue: kvp.Value.GetValue<float>(),
                            hasCurrent: false,
                            currentValue: 0f);
                    }
                }

                if (currentObj != null)
                {
                    foreach (var kvp in currentObj)
                    {
                        if (kvp.Value == null)
                        {
                            throw new InvalidOperationException(
                                $"Entity template '{templateId}' AttributeBuffer.current.{kvp.Key} requires a non-null numeric value.");
                        }

                        if (kvp.Value.GetValueKind() != JsonValueKind.Number)
                        {
                            throw new InvalidOperationException(
                                $"Entity template '{templateId}' AttributeBuffer.current.{kvp.Key} requires a numeric value.");
                        }

                        int attributeId = ResolveAttributeId(kvp.Key);
                        UpsertAttributeSeed(
                            ref seeds,
                            ref count,
                            attributeId,
                            hasBase: false,
                            baseValue: 0f,
                            hasCurrent: true,
                            currentValue: kvp.Value.GetValue<float>());
                    }
                }

                if (count != seeds.Length)
                {
                    Array.Resize(ref seeds, count);
                }

                return seeds;
            }

            private static int ResolveAttributeId(string attributeName)
            {
                int attributeId = AttributeRegistry.GetId(attributeName);
                return attributeId == AttributeRegistry.InvalidId
                    ? AttributeRegistry.Register(attributeName)
                    : attributeId;
            }

            private static void UpsertAttributeSeed(
                ref AttributeSeed[] seeds,
                ref int count,
                int attributeId,
                bool hasBase,
                float baseValue,
                bool hasCurrent,
                float currentValue)
            {
                for (int i = 0; i < count; i++)
                {
                    if (seeds[i].AttributeId != attributeId)
                    {
                        continue;
                    }

                    seeds[i] = new AttributeSeed(
                        attributeId,
                        hasBase || seeds[i].HasBase,
                        hasBase ? baseValue : seeds[i].BaseValue,
                        hasCurrent || seeds[i].HasCurrent,
                        hasCurrent ? currentValue : seeds[i].CurrentValue);
                    return;
                }

                seeds[count++] = new AttributeSeed(attributeId, hasBase, baseValue, hasCurrent, currentValue);
            }
        }

        private readonly record struct AttributeSeed(
            int AttributeId,
            bool HasBase,
            float BaseValue,
            bool HasCurrent,
            float CurrentValue);

        private static VisualTransform CreateStaticVisualTransform(
            in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPosition,
            float facingAngleRad)
        {
            const float cmToM = 0.01f;
            return new VisualTransform
            {
                Position = new System.Numerics.Vector3(
                    worldPosition.X.ToFloat() * cmToM,
                    0f,
                    worldPosition.Y.ToFloat() * cmToM),
                Rotation = WorldPlane2D.FacingRadToVisualYRotation(facingAngleRad),
                Scale = System.Numerics.Vector3.One,
            };
        }
    }
}
