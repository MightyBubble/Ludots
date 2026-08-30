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
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

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
        PresenterRootBootstrapHandled = 1 << 4,
        PresentationOwnerHasPresenterPayload = 1 << 5,
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

        public bool TryGetAuthoredTeam(string templateId, EntityTemplate template, out Team team)
        {
            if (TryGetDescriptor(templateId, template, out TemplateSpawnDescriptor descriptor) &&
                descriptor.IsCompatible &&
                descriptor.HasTeam)
            {
                team = descriptor.Team;
                return true;
            }

            team = default;
            return false;
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

            if ((features & TemplateBatchSpawnFeatures.PresenterRootBootstrapHandled) != 0)
            {
                signature += Component<PresenterRootBootstrapHandled>.Signature;
            }

            if ((features & TemplateBatchSpawnFeatures.PresentationOwnerHasPresenterPayload) != 0)
            {
                signature += Component<PresentationOwnerHasPresenterPayload>.Signature;
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
            bool includeBootstrapHandled = (features & TemplateBatchSpawnFeatures.PresenterRootBootstrapHandled) != 0;
            bool includeOwnerPayload = (features & TemplateBatchSpawnFeatures.PresentationOwnerHasPresenterPayload) != 0;
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
                Span<ContinuousHeightmapSampleState> heightSamples = includeDynamicHeightSampling
                    ? chunk.GetSpan<ContinuousHeightmapSampleState>()
                    : default;
                Span<CullState> culls = chunk.GetSpan<CullState>();
                Span<AttributeBuffer> attributes = descriptor.HasAttributeBuffer ? chunk.GetSpan<AttributeBuffer>() : default;
                Span<AttributeLastSnapshot> attributeSnapshots = descriptor.HasAttributeBuffer ? chunk.GetSpan<AttributeLastSnapshot>() : default;
                Span<GameplayTagContainer> gameplayTags = descriptor.HasGameplayTagContainer ? chunk.GetSpan<GameplayTagContainer>() : default;
                Span<TagCountContainer> tagCounts = descriptor.HasTagCountContainer ? chunk.GetSpan<TagCountContainer>() : default;
                Span<DirtyFlags> dirtyFlags = descriptor.HasDirtyFlags ? chunk.GetSpan<DirtyFlags>() : default;
                Span<TimedTagBuffer> timedTags = descriptor.HasTimedTagBuffer ? chunk.GetSpan<TimedTagBuffer>() : default;
                Span<EntityTemplateKeyRef> templateKeys = chunk.GetSpan<EntityTemplateKeyRef>();
                Span<OrderBuffer> orderBuffers = descriptor.HasOrderBuffer ? chunk.GetSpan<OrderBuffer>() : default;
                Span<BlackboardIntBuffer> blackboardInts = descriptor.HasOrderBuffer ? chunk.GetSpan<BlackboardIntBuffer>() : default;
                Span<BlackboardFloatBuffer> blackboardFloats = descriptor.HasOrderBuffer ? chunk.GetSpan<BlackboardFloatBuffer>() : default;
                Span<BlackboardSpatialBuffer> blackboardSpatial = descriptor.HasOrderBuffer ? chunk.GetSpan<BlackboardSpatialBuffer>() : default;
                Span<BlackboardEntityBuffer> blackboardEntities = descriptor.HasOrderBuffer ? chunk.GetSpan<BlackboardEntityBuffer>() : default;
                Span<CommandSourceSelectableState> commandSourceStates = descriptor.HasCommandSourceSelectableState ? chunk.GetSpan<CommandSourceSelectableState>() : default;
                Span<Ludots.Core.Gameplay.Components.EntityLayer> entityLayers = descriptor.HasEntityLayer ? chunk.GetSpan<Ludots.Core.Gameplay.Components.EntityLayer>() : default;
                Span<Team> teams = descriptor.HasTeam ? chunk.GetSpan<Team>() : default;
                Span<PlayerOwner> playerOwners = descriptor.HasPlayerOwner ? chunk.GetSpan<PlayerOwner>() : default;
                Span<MapEntity> mapEntities = includeMapEntity ? chunk.GetSpan<MapEntity>() : default;
                Span<PresentationStableId> stableIds = includeStableId ? chunk.GetSpan<PresentationStableId>() : default;
                Span<PresentationLifecycleState> lifecycleStates = includeLifecycleState ? chunk.GetSpan<PresentationLifecycleState>() : default;
                Span<SpatialCellRef> spatialRefs = includeSpatialCellRef ? chunk.GetSpan<SpatialCellRef>() : default;
                Span<PresenterRootBootstrapHandled> bootstrapHandled = includeBootstrapHandled ? chunk.GetSpan<PresenterRootBootstrapHandled>() : default;
                Span<PresentationOwnerHasPresenterPayload> ownerPayloads = includeOwnerPayload ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>() : default;

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
                    // Seed VisualTransform from the effective batch placement.
                    VisualTransform visual = CreatePlacementVisualTransform(in worldPosition, facingAngle);
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
                    if (descriptor.HasDirtyFlags)
                    {
                        dirtyFlags[componentIndex] = default;
                    }
                    if (descriptor.HasTimedTagBuffer)
                    {
                        timedTags[componentIndex] = default;
                    }
                    templateKeys[componentIndex] = descriptor.TemplateKey;
                    if (descriptor.HasOrderBuffer)
                    {
                        orderBuffers[componentIndex] = OrderBuffer.CreateEmpty();
                        blackboardInts[componentIndex] = default;
                        blackboardFloats[componentIndex] = default;
                        blackboardSpatial[componentIndex] = default;
                        blackboardEntities[componentIndex] = default;
                    }

                    if (descriptor.HasCommandSourceSelectableState)
                    {
                        commandSourceStates[componentIndex] = descriptor.CommandSourceSelectableState;
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
                        ownerPayloads[componentIndex] = new PresentationOwnerHasPresenterPayload
                        {
                            Count = 0,
                            RootCount = 0,
                            SingleRootPresenter = Entity.Null,
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
                ParamDefault[]? presenterParamOverrides = null)
            {
                WorldPositionCm = worldPositionCm;
                HasWorldPosition = hasWorldPosition;
                FacingAngleRad = facingAngleRad;
                HasFacing = hasFacing;
                MapEntity = mapEntity;
                HasMapEntity = hasMapEntity;
                PresentationStableId = presentationStableId;
                HasPresentationStableId = hasPresentationStableId;
                PresenterParamOverrides = presenterParamOverrides ?? Array.Empty<ParamDefault>();
            }

            public Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 WorldPositionCm { get; }

            public bool HasWorldPosition { get; }

            public float FacingAngleRad { get; }

            public bool HasFacing { get; }

            public MapEntity MapEntity { get; }

            public bool HasMapEntity { get; }

            public int PresentationStableId { get; }

            public bool HasPresentationStableId { get; }

            public ParamDefault[] PresenterParamOverrides { get; }
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
            public readonly CullState CullState;
            public readonly bool HasAttributeBuffer;
            public readonly bool HasGameplayTagContainer;
            public readonly bool HasTagCountContainer;
            public readonly bool HasDirtyFlags;
            public readonly bool HasTimedTagBuffer;
            public readonly GameplayTagContainer GameplayTags;
            public readonly TagCountContainer TagCounts;
            public readonly EntityTemplateKeyRef TemplateKey;
            public readonly bool HasOrderBuffer;
            public readonly bool HasCommandSourceSelectableState;
            public readonly CommandSourceSelectableState CommandSourceSelectableState;
            public readonly bool HasEntityLayer;
            public readonly Ludots.Core.Gameplay.Components.EntityLayer EntityLayer;
            public readonly bool HasTeam;
            public readonly Team Team;
            public readonly bool HasPlayerOwner;
            public readonly PlayerOwner PlayerOwner;
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
                CullState cullState,
                bool hasAttributeBuffer,
                bool hasGameplayTagContainer,
                bool hasTagCountContainer,
                bool hasDirtyFlags,
                bool hasTimedTagBuffer,
                GameplayTagContainer gameplayTags,
                TagCountContainer tagCounts,
                EntityTemplateKeyRef templateKey,
                bool hasOrderBuffer,
                CommandSourceSelectableState commandSourceSelectableState,
                bool hasCommandSourceSelectableState,
                Ludots.Core.Gameplay.Components.EntityLayer entityLayer,
                bool hasEntityLayer,
                Team team,
                bool hasTeam,
                PlayerOwner playerOwner,
                bool hasPlayerOwner,
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
                CullState = cullState;
                HasAttributeBuffer = hasAttributeBuffer;
                HasGameplayTagContainer = hasGameplayTagContainer;
                HasTagCountContainer = hasTagCountContainer;
                HasDirtyFlags = hasDirtyFlags;
                HasTimedTagBuffer = hasTimedTagBuffer;
                GameplayTags = gameplayTags;
                TagCounts = tagCounts;
                TemplateKey = templateKey;
                HasOrderBuffer = hasOrderBuffer;
                CommandSourceSelectableState = commandSourceSelectableState;
                HasCommandSourceSelectableState = hasCommandSourceSelectableState;
                EntityLayer = entityLayer;
                HasEntityLayer = hasEntityLayer;
                Team = team;
                HasTeam = hasTeam;
                PlayerOwner = playerOwner;
                HasPlayerOwner = hasPlayerOwner;
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

                // children 预置组合需要逐子挂接与局部姿落位，只能走单实体 spawn lane。
                if (template.Children is { Count: > 0 })
                {
                    return Incompatible(onSpawnEffectTemplateId);
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
                bool hasDynamicHeightSampling = template.Components.ContainsKey("ContinuousHeightmapSampleState");
                bool hasSpatialPartitionExcluded = template.Components.ContainsKey("SpatialPartitionExcluded");
                bool hasAttributeBuffer = template.Components.ContainsKey("AttributeBuffer");
                bool hasAbilityTagGrantReceiver = template.Components.ContainsKey("AbilityTagGrantReceiver");
                EntityRuntimeStatePlan runtimeStatePlan = EntityRuntimeStatePlan.FromAuthoredComponents(template.Components);
                bool hasGameplayTagContainer = runtimeStatePlan.HasGameplayTagContainer;
                bool hasTagCountContainer = runtimeStatePlan.HasTagCountContainer;
                bool hasDirtyFlags = runtimeStatePlan.HasDirtyFlags;
                bool hasTimedTagBuffer = runtimeStatePlan.HasTimedTagBuffer;
                bool hasOrderBuffer = runtimeStatePlan.HasOrderRuntimeState;
                bool hasCommandSourceSelectableState = template.Components.ContainsKey("CommandSourceSelectableState");
                bool hasEntityLayer = template.Components.ContainsKey("EntityLayer");
                bool hasTeam = template.Components.ContainsKey("Team");
                bool hasPlayerOwner = template.Components.ContainsKey("PlayerOwner");
                CommandSourceSelectableState commandSourceSelectableState = hasCommandSourceSelectableState
                    ? ParseCommandSourceSelectableState(templateId, template.Components["CommandSourceSelectableState"])
                    : default;
                Ludots.Core.Gameplay.Components.EntityLayer entityLayer = hasEntityLayer
                    ? ParseEntityLayer(templateId, template.Components["EntityLayer"])
                    : default;
                Team team = hasTeam ? ParseTeam(templateId, template.Components["Team"]) : default;
                PlayerOwner playerOwner = hasPlayerOwner ? ParsePlayerOwner(templateId, template.Components["PlayerOwner"]) : default;
                AttributeSeed[] attributeSeeds = hasAttributeBuffer
                    ? ParseAttributeSeeds(templateId, template.Components)
                    : Array.Empty<AttributeSeed>();

                if (template.Components.ContainsKey("GameplayTagContainer"))
                {
                    RequireEmptyObject(templateId, template.Components, "GameplayTagContainer");
                }

                if (template.Components.ContainsKey("TagCountContainer"))
                {
                    RequireEmptyObject(templateId, template.Components, "TagCountContainer");
                }
                if (template.Components.ContainsKey("DirtyFlags"))
                {
                    RequireEmptyObject(templateId, template.Components, "DirtyFlags");
                }
                if (hasAbilityTagGrantReceiver)
                {
                    RequireEmptyObject(templateId, template.Components, "AbilityTagGrantReceiver");
                }
                if (template.Components.ContainsKey("TimedTagBuffer"))
                {
                    RequireEmptyObject(templateId, template.Components, "TimedTagBuffer");
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
                    RequireEmptyObject(templateId, template.Components, "ContinuousHeightmapSampleState");
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

                if (hasDirtyFlags)
                {
                    signature += Component<DirtyFlags>.Signature;
                }

                if (hasGameplayTagContainer)
                {
                    signature += Component<GameplayTagContainer>.Signature;
                }

                if (hasTagCountContainer)
                {
                    signature += Component<TagCountContainer>.Signature;
                }

                if (hasTimedTagBuffer)
                {
                    signature += Component<TimedTagBuffer>.Signature;
                }

                if (hasAbilityTagGrantReceiver)
                {
                    signature += Component<AbilityTagGrantReceiver>.Signature;
                }

                if (hasDynamicHeightSampling)
                {
                    signature += Component<ContinuousHeightmapSampleState>.Signature;
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
                    signature += Component<BlackboardIntBuffer>.Signature;
                    signature += Component<BlackboardFloatBuffer>.Signature;
                    signature += Component<BlackboardSpatialBuffer>.Signature;
                    signature += Component<BlackboardEntityBuffer>.Signature;
                    signature += Component<OrderContinuationBuffer>.Signature;
                }

                if (hasCommandSourceSelectableState)
                {
                    signature += Component<CommandSourceSelectableState>.Signature;
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
                    new CullState { IsVisible = false, LOD = LODLevel.Low },
                    hasAttributeBuffer,
                    hasGameplayTagContainer,
                    hasTagCountContainer,
                    hasDirtyFlags,
                    hasTimedTagBuffer,
                    default,
                    default,
                    new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                    hasOrderBuffer,
                    commandSourceSelectableState,
                    hasCommandSourceSelectableState,
                    entityLayer,
                    hasEntityLayer,
                    team,
                    hasTeam,
                    playerOwner,
                    hasPlayerOwner,
                    onSpawnEffectTemplateId: onSpawnEffectTemplateId,
                    tagComponentTypes,
                    attributeSeeds);
            }

            private static TemplateSpawnDescriptor Incompatible(int onSpawnEffectTemplateId)
            {
                return new TemplateSpawnDescriptor(
                    isCompatible: false,
                    baseSignature: default,
                    hasStaticTransform: false,
                    hasDynamicHeightSampling: false,
                    name: default,
                    defaultWorldPosition: default,
                    facing: default,
                    cullState: default,
                    hasAttributeBuffer: false,
                    hasGameplayTagContainer: false,
                    hasTagCountContainer: false,
                    hasDirtyFlags: false,
                    hasTimedTagBuffer: false,
                    gameplayTags: default,
                    tagCounts: default,
                    templateKey: default,
                    hasOrderBuffer: false,
                    commandSourceSelectableState: default,
                    hasCommandSourceSelectableState: false,
                    entityLayer: default,
                    hasEntityLayer: false,
                    team: default,
                    hasTeam: false,
                    playerOwner: default,
                    hasPlayerOwner: false,
                    onSpawnEffectTemplateId,
                    tagComponentTypes: Array.Empty<ComponentType>(),
                    attributeSeeds: Array.Empty<AttributeSeed>());
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
                        string.Equals(componentName, "ContinuousHeightmapSampleState", StringComparison.Ordinal) ||
                        string.Equals(componentName, "AttributeBuffer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "GameplayTagContainer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "TagCountContainer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "DirtyFlags", StringComparison.Ordinal) ||
                        string.Equals(componentName, "TimedTagBuffer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "AbilityTagGrantReceiver", StringComparison.Ordinal) ||
                        string.Equals(componentName, "OrderBuffer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "CommandSourceSelectableState", StringComparison.Ordinal) ||
                        string.Equals(componentName, "EntityLayer", StringComparison.Ordinal) ||
                        string.Equals(componentName, "Team", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PlayerOwner", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticTransform", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticCullPending", StringComparison.Ordinal) ||
                        string.Equals(componentName, "PresentationStaticHeightPending", StringComparison.Ordinal) ||
                        string.Equals(componentName, "SpatialPartitionExcluded", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!IsEmptyTagComponent(components[componentName]) ||
                        !ComponentRegistry.TryGetComponentType(componentName, out ComponentType componentType) ||
                        componentType.ByteSize != 0)
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

                    if (!IsEmptyTagComponent(node) ||
                        !ComponentRegistry.TryGetComponentType(componentName, out ComponentType componentType) ||
                        componentType.ByteSize != 0)
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
                       string.Equals(componentName, "ContinuousHeightmapSampleState", StringComparison.Ordinal) ||
                       string.Equals(componentName, "AttributeBuffer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "GameplayTagContainer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "TagCountContainer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "DirtyFlags", StringComparison.Ordinal) ||
                       string.Equals(componentName, "TimedTagBuffer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "AbilityTagGrantReceiver", StringComparison.Ordinal) ||
                       string.Equals(componentName, "OrderBuffer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "CommandSourceSelectableState", StringComparison.Ordinal) ||
                       string.Equals(componentName, "EntityLayer", StringComparison.Ordinal) ||
                       string.Equals(componentName, "Team", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PlayerOwner", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticTransform", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticCullPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticHeightPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "SpatialPartitionExcluded", StringComparison.Ordinal);
            }

            private static bool IsEmptyTagComponent(JsonNode node)
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

            private static CommandSourceSelectableState ParseCommandSourceSelectableState(string templateId, JsonNode node)
            {
                if (node is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Entity template '{templateId}' CommandSourceSelectableState requires an object payload.");
                }

                ValidateProperties(obj, $"Entity template '{templateId}' CommandSourceSelectableState", "IsEnabled");
                JsonNode isEnabledNode = RequireProperty(obj, "IsEnabled", $"Entity template '{templateId}' CommandSourceSelectableState");
                byte enabled = isEnabledNode.GetValueKind() switch
                {
                    JsonValueKind.True => (byte)1,
                    JsonValueKind.False => (byte)0,
                    _ => throw new InvalidOperationException($"Entity template '{templateId}' CommandSourceSelectableState.IsEnabled requires a boolean value."),
                };

                return new CommandSourceSelectableState { IsEnabled = enabled };
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
                if (attributeId != AttributeRegistry.InvalidId)
                {
                    return attributeId;
                }

                if (!AttributeRegistry.IsFrozen)
                {
                    return AttributeRegistry.Register(attributeName);
                }

                throw new InvalidOperationException(
                    $"Entity template AttributeBuffer references unregistered attribute '{attributeName}'. Declare it in startup GAS attribute config before map loading.");
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

        private static VisualTransform CreatePlacementVisualTransform(
            in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPosition,
            float facingAngleRad)
        {
            return new VisualTransform
            {
                Position = WorldPlane2D.LogicCmToVisualMeters(in worldPosition),
                Rotation = WorldPlane2D.FacingRadToVisualYRotation(facingAngleRad),
                Scale = System.Numerics.Vector3.One,
            };
        }
    }
}
