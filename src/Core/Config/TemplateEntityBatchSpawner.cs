using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
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
        private readonly Dictionary<string, TemplateSpawnDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

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
                Span<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
                Span<AttributeLastSnapshot> attributeSnapshots = chunk.GetSpan<AttributeLastSnapshot>();
                Span<GameplayTagContainer> gameplayTags = chunk.GetSpan<GameplayTagContainer>();
                Span<TagCountContainer> tagCounts = chunk.GetSpan<TagCountContainer>();
                Span<EntityTemplateKeyCm> templateKeys = chunk.GetSpan<EntityTemplateKeyCm>();
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
                    AttributeBuffer attributeBuffer = descriptor.CreateAttributeBuffer();
                    attributes[componentIndex] = attributeBuffer;
                    attributeSnapshots[componentIndex] = descriptor.CreateAttributeLastSnapshot(ref attributeBuffer);
                    gameplayTags[componentIndex] = descriptor.GameplayTags;
                    tagCounts[componentIndex] = descriptor.TagCounts;
                    templateKeys[componentIndex] = descriptor.TemplateKey;

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
                bool hasPresentationStableId = false)
            {
                WorldPositionCm = worldPositionCm;
                HasWorldPosition = hasWorldPosition;
                FacingAngleRad = facingAngleRad;
                HasFacing = hasFacing;
                MapEntity = mapEntity;
                HasMapEntity = hasMapEntity;
                PresentationStableId = presentationStableId;
                HasPresentationStableId = hasPresentationStableId;
            }

            public Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 WorldPositionCm { get; }

            public bool HasWorldPosition { get; }

            public float FacingAngleRad { get; }

            public bool HasFacing { get; }

            public MapEntity MapEntity { get; }

            public bool HasMapEntity { get; }

            public int PresentationStableId { get; }

            public bool HasPresentationStableId { get; }
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
            public readonly GameplayTagContainer GameplayTags;
            public readonly TagCountContainer TagCounts;
            public readonly EntityTemplateKeyCm TemplateKey;
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
                GameplayTagContainer gameplayTags,
                TagCountContainer tagCounts,
                EntityTemplateKeyCm templateKey,
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
                GameplayTags = gameplayTags;
                TagCounts = tagCounts;
                TemplateKey = templateKey;
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
                    return default;
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
                        default,
                        default,
                        default,
                        onSpawnEffectTemplateId,
                        Array.Empty<ComponentType>(),
                        Array.Empty<AttributeSeed>());
                }

                if (!TryParseName(template.Components, out var name) ||
                    !TryParseAttributeSeeds(template.Components, out var attributeSeeds) ||
                    !TryCollectTagComponentTypes(template.Components, out ComponentType[] tagComponentTypes))
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
                        default,
                        default,
                        default,
                        onSpawnEffectTemplateId,
                        Array.Empty<ComponentType>(),
                        Array.Empty<AttributeSeed>());
                }

                if (!TryValidateSupportedComponents(template.Components))
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
                        default,
                        default,
                        default,
                        onSpawnEffectTemplateId,
                        Array.Empty<ComponentType>(),
                        Array.Empty<AttributeSeed>());
                }

                var defaultWorldPosition = default(Ludots.Core.Mathematics.FixedPoint.Fix64Vec2);
                if (template.Components.TryGetValue("WorldPositionCm", out JsonNode worldPositionNode) &&
                    !TryParseWorldPosition(worldPositionNode, out defaultWorldPosition))
                {
                    return default;
                }

                float facingAngle = 0f;
                if (template.Components.TryGetValue("FacingDirection", out JsonNode facingNode) &&
                    !TryParseFacing(facingNode, out facingAngle))
                {
                    return default;
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

                Signature signature =
                    Component<Name>.Signature +
                    Component<WorldPositionCm>.Signature +
                    Component<PreviousWorldPositionCm>.Signature +
                    Component<FacingDirection>.Signature +
                    Component<VisualTransform>.Signature +
                    Component<CullState>.Signature +
                    Component<AttributeBuffer>.Signature +
                    Component<AttributeLastSnapshot>.Signature +
                    Component<GameplayTagContainer>.Signature +
                    Component<TagCountContainer>.Signature +
                    Component<EntityTemplateKeyCm>.Signature;

                if (hasDynamicHeightSampling)
                {
                    signature += Component<VisualHeightmapSampleState>.Signature;
                }

                if (hasStaticTransform)
                {
                    signature += Component<PresentationStaticTransform>.Signature;
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
                    default,
                    default,
                    new EntityTemplateKeyCm { TemplateKeyId = templateKeyId },
                    onSpawnEffectTemplateId,
                    tagComponentTypes,
                    attributeSeeds);
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

            private static bool TryCollectTagComponentTypes(
                IReadOnlyDictionary<string, JsonNode> components,
                out ComponentType[] tagComponentTypes)
            {
                tagComponentTypes = Array.Empty<ComponentType>();
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
                        return false;
                    }

                    collected ??= new List<ComponentType>(2);
                    collected.Add(componentType);
                }

                if (collected != null && collected.Count > 0)
                {
                    tagComponentTypes = collected.ToArray();
                }

                return true;
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
                       string.Equals(componentName, "PresentationStaticTransform", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticCullPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "PresentationStaticHeightPending", StringComparison.Ordinal) ||
                       string.Equals(componentName, "SpatialPartitionExcluded", StringComparison.Ordinal);
            }

            private static bool IsEmptyJsonObject(JsonNode node)
            {
                return node is JsonObject obj && obj.Count == 0;
            }

            private static bool TryParseName(IReadOnlyDictionary<string, JsonNode> components, out Name name)
            {
                name = default;
                if (!components.TryGetValue("Name", out JsonNode node) || node is not JsonObject obj)
                {
                    return false;
                }

                JsonNode valueNode = obj["Value"] ?? obj["value"];
                string value = valueNode?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                name = new Name { Value = value };
                return true;
            }

            private static bool TryParseFacing(JsonNode node, out float angleRad)
            {
                angleRad = 0f;
                if (node is not JsonObject obj)
                {
                    return false;
                }

                JsonNode angleNode = obj["AngleRad"] ?? obj["angleRad"];
                if (angleNode == null)
                {
                    return false;
                }

                angleRad = angleNode.GetValue<float>();
                return true;
            }

            private static bool TryParseWorldPosition(JsonNode node, out Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPosition)
            {
                worldPosition = default;
                if (node is not JsonObject obj)
                {
                    return false;
                }

                JsonNode valueNode = obj["Value"] ?? obj["value"] ?? node;
                if (valueNode is not JsonObject valueObj)
                {
                    return false;
                }

                int x = 0;
                int y = 0;
                if (valueObj.TryGetPropertyValue("X", out JsonNode xNode) && xNode != null)
                {
                    x = xNode.GetValue<int>();
                }

                if (valueObj.TryGetPropertyValue("Y", out JsonNode yNode) && yNode != null)
                {
                    y = yNode.GetValue<int>();
                }

                worldPosition = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(x, y);
                return true;
            }

            private static bool TryParseAttributeSeeds(IReadOnlyDictionary<string, JsonNode> components, out AttributeSeed[] seeds)
            {
                seeds = Array.Empty<AttributeSeed>();
                if (!components.TryGetValue("AttributeBuffer", out JsonNode node))
                {
                    return false;
                }

                if (node is not JsonObject obj)
                {
                    return false;
                }

                JsonObject baseObj = null;
                if (obj.TryGetPropertyValue("base", out JsonNode baseNode) && baseNode is JsonObject parsedBase)
                {
                    baseObj = parsedBase;
                }

                JsonObject currentObj = null;
                if (obj.TryGetPropertyValue("current", out JsonNode currentNode) && currentNode is JsonObject parsedCurrent)
                {
                    currentObj = parsedCurrent;
                }

                if ((baseObj == null || baseObj.Count == 0) &&
                    (currentObj == null || currentObj.Count == 0))
                {
                    return true;
                }

                int capacity = (baseObj?.Count ?? 0) + (currentObj?.Count ?? 0);
                seeds = new AttributeSeed[capacity];
                int count = 0;

                if (baseObj != null)
                {
                    foreach (var kvp in baseObj)
                    {
                        if (kvp.Value == null)
                        {
                            continue;
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
                            continue;
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

                return true;
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
