using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public class MapLoader
    {
        private const int TemplateBatchScratchCapacity = 4096;

        private readonly World _world;
        private readonly WorldMap _worldMap;
        private EffectRequestQueue _effectRequests;
        private EntityTriggerGraphMounts? _entityTriggerGraphMounts;
        private TemplateEntityBatchSpawner _templateBatchSpawner;
        private PresentationStableIdAllocator _stableIds;
        private PresenterEntityRuntime _presenterRuntime;
        private PresenterDefinitionRegistry _presenterDefinitions;
        private CompiledPresenterBootstrapRegistry _presenterBootstrap;
        private readonly Entity[] _presenterBatchOwners = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _presenterBatchScopeIds = new int[TemplateBatchScratchCapacity];
        private readonly int[] _presenterBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly Entity[] _presenterBatchCreated = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _ownerBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly VisualTransform[] _ownerBatchTransforms = new VisualTransform[TemplateBatchScratchCapacity];
        private readonly CullState[] _ownerBatchCulls = new CullState[TemplateBatchScratchCapacity];
        private readonly ParamDefault[][] _ownerBatchParamOverrides = new ParamDefault[TemplateBatchScratchCapacity][];
        private readonly ParamDefault[][] _presenterBatchParamOverrides = new ParamDefault[TemplateBatchScratchCapacity][];
        private ComponentAuthoringContext _authoringContext = ComponentAuthoringContext.Empty;
        
        // New Registry
        public DataRegistry<EntityTemplate> TemplateRegistry { get; private set; }
        public EntityTemplateKeyRegistry EntityTemplateKeys { get; }
        private readonly Dictionary<string, string> _templateSources = new Dictionary<string, string>(StringComparer.Ordinal);

        public MapLoader(World world, WorldMap worldMap, ConfigPipeline pipeline)
        {
            _world = world;
            _worldMap = worldMap;
            TemplateRegistry = new DataRegistry<EntityTemplate>(pipeline);
            EntityTemplateKeys = new EntityTemplateKeyRegistry();
            _templateBatchSpawner = new TemplateEntityBatchSpawner(world, EntityTemplateKeys, scratchCapacity: TemplateBatchScratchCapacity);
        }

        public void SetEffectRequestQueue(EffectRequestQueue effectRequests)
        {
            _effectRequests = effectRequests;
        }

        public void SetEntityTriggerGraphMounts(EntityTriggerGraphMounts entityTriggerGraphMounts)
        {
            _entityTriggerGraphMounts = entityTriggerGraphMounts ?? throw new ArgumentNullException(nameof(entityTriggerGraphMounts));
        }

        public void SetComponentAuthoringContext(ComponentAuthoringContext authoringContext)
        {
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
        }

        public void SetPresentationRuntime(
            PresentationStableIdAllocator stableIds,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry presenterDefinitions,
            ISpatialPartitionWorld spatialPartition,
            WorldSizeSpec worldSizeSpec)
        {
            _stableIds = stableIds;
            _presenterRuntime = presenterRuntime;
            _presenterDefinitions = presenterDefinitions;
            _presenterBootstrap = presenterDefinitions?.BootstrapRegistry;
            _templateBatchSpawner = new TemplateEntityBatchSpawner(
                _world,
                EntityTemplateKeys,
                stableIds,
                spatialPartition,
                worldSizeSpec,
                TemplateBatchScratchCapacity);
        }

        public void LoadTemplates(ConfigCatalog catalog, ConfigConflictReport report = null)
        {
            // This loads "Entities/templates.json" from Core and all Mods
            // Merging them with priority
            TemplateRegistry.Load("Entities/templates.json", catalog, report);
            EntityTemplateKeys.Clear();
            _templateSources.Clear();
            foreach (var template in TemplateRegistry.GetAll())
            {
                ValidateTemplateTriggerGraphs(template);
                EntityTemplateKeys.Register(template.Id);
                if (report != null && report.TryGetWinner("Entities/templates.json", template.Id, out string sourceUri))
                {
                    _templateSources[template.Id] = sourceUri;
                }
            }
        }

        private static void ValidateTemplateTriggerGraphs(EntityTemplate template)
        {
            List<string>? graphs = template.TriggerGraphs;
            if (graphs == null)
            {
                return;
            }

            for (int i = 0; i < graphs.Count; i++)
            {
                string? name = graphs[i];
                if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Entity template '{template.Id}' TriggerGraphs[{i}] must be a trimmed non-empty graph id string.");
                }
            }
        }

        public void LoadEntities(MapConfig mapConfig)
        {
            LoadEntitiesAndIndex(mapConfig);
        }

        public MapLoadEntityIndex LoadEntitiesAndIndex(MapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            if (mapConfig.Entities == null)
            {
                throw new InvalidOperationException($"Map '{mapConfig.Id}' requires an explicit entities list.");
            }

            // We need to extract the dictionary from the registry to pass to EntityBuilder
            // Or better, update EntityBuilder to accept DataRegistry or just the Interface.
            // For now, let's just create a dictionary snapshot or pass the lookup function.
            
            // Current EntityBuilder expects Dictionary<string, EntityTemplate>.
            // We can convert DataRegistry content to Dictionary easily.
            var templates = new System.Collections.Generic.Dictionary<string, EntityTemplate>();
            foreach(var t in TemplateRegistry.GetAll())
            {
                templates[t.Id] = t;
            }

            var builder = new EntityBuilder(_world, templates, _templateSources, _authoringContext);
            var mapEntityTag = new MapEntity { MapId = new MapId(mapConfig.Id) };
            var entityIndex = new MapLoadEntityIndex();
            var pendingBatchRequests = new List<TemplateEntityBatchSpawner.TemplateBatchSpawnRequest>(_templateBatchSpawner.ScratchCapacity);
            var pendingBatchEntityData = new List<EntitySpawnData>(_templateBatchSpawner.ScratchCapacity);
            string? activeBatchTemplateId = null;

            void FlushPendingTemplateBatch()
            {
                if (activeBatchTemplateId == null || pendingBatchRequests.Count == 0)
                {
                    pendingBatchRequests.Clear();
                    pendingBatchEntityData.Clear();
                    activeBatchTemplateId = null;
                    return;
                }

                int templateKeyId = ResolveTemplateKeyId(activeBatchTemplateId);
                EntityTemplate activeBatchTemplate = templates[activeBatchTemplateId];
                bool hasDirectBootstrap = HasDirectEntitySpawnBootstrap(templateKeyId);
                bool publishSpawnedEvent = ShouldPublishSpawnedEvent(templateKeyId, hasDirectBootstrap);

                TemplateBatchSpawnFeatures features = TemplateBatchSpawnFeatures.MapEntity;
                if (_stableIds != null)
                {
                    features |= TemplateBatchSpawnFeatures.PresentationStableId;
                    if (!publishSpawnedEvent)
                    {
                        features |= TemplateBatchSpawnFeatures.PresentationLifecycleState;
                    }
                }

                if (hasDirectBootstrap)
                {
                    features |= TemplateBatchSpawnFeatures.PresenterRootBootstrapHandled;
                    if (TemplateBatchOwnerPayloadPreseedPolicy.CanPreseedOwnerPayloadMarker(_presenterBootstrap, activeBatchTemplate, templateKeyId))
                    {
                        features |= TemplateBatchSpawnFeatures.PresentationOwnerHasPresenterPayload;
                    }
                }

                int batchCount = pendingBatchRequests.Count;
                Span<int> stableIds = hasDirectBootstrap ? _ownerBatchStableIds.AsSpan(0, batchCount) : default;
                Span<VisualTransform> ownerTransforms = hasDirectBootstrap ? _ownerBatchTransforms.AsSpan(0, batchCount) : default;
                Span<CullState> ownerCulls = hasDirectBootstrap ? _ownerBatchCulls.AsSpan(0, batchCount) : default;
                bool hasPresenterParamOverrides = BatchContainsPresenterParamOverrides(pendingBatchRequests);
                if (hasPresenterParamOverrides && !CanApplyPresenterParamOverrides())
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' template '{activeBatchTemplateId}' declares PresenterParamOverrides but presentation runtime is not installed.");
                }

                if (hasPresenterParamOverrides && !hasDirectBootstrap)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' template '{activeBatchTemplateId}' declares PresenterParamOverrides but has no direct presenter bootstrap.");
                }

                if (!_templateBatchSpawner.TryCreateBatch(
                    activeBatchTemplateId,
                    activeBatchTemplate,
                    CollectionsMarshal.AsSpan(pendingBatchRequests),
                    features,
                    out var created,
                    stableIds,
                    ownerTransforms,
                    ownerCulls))
                {
                    throw new InvalidOperationException(
                        $"Map template batch spawn failed after template '{activeBatchTemplateId}' was classified as batch-compatible.");
                }

                for (int i = 0; i < created.Length; i++)
                {
                    entityIndex.Register(mapConfig.Id, pendingBatchEntityData[i].InstanceId, created[i]);
                    PublishTemplateOnSpawnEffect(created[i], activeBatchTemplateId);
                    BufferEntityTriggerGraphs(created[i], activeBatchTemplateId, activeBatchTemplate);
                }

                if (hasDirectBootstrap)
                {
                    for (int i = 0; i < batchCount; i++)
                    {
                        _ownerBatchParamOverrides[i] = pendingBatchRequests[i].PresenterParamOverrides;
                    }

                    try
                    {
                        TryBootstrapPresenterBatch(
                            templateKeyId,
                            created,
                            stableIds,
                            ownerTransforms,
                            ownerCulls,
                            _ownerBatchParamOverrides.AsSpan(0, batchCount));
                    }
                    finally
                    {
                        for (int i = 0; i < batchCount; i++)
                        {
                            _ownerBatchParamOverrides[i] = null!;
                        }
                    }
                }

                pendingBatchRequests.Clear();
                pendingBatchEntityData.Clear();
                activeBatchTemplateId = null;
            }
            
            foreach (var entityData in mapConfig.Entities)
            {
                if (entityData == null)
                {
                    throw new InvalidOperationException($"Map '{mapConfig.Id}' contains a null entity entry.");
                }
                if (string.IsNullOrWhiteSpace(entityData.Template))
                {
                    throw new InvalidOperationException($"Map '{mapConfig.Id}' contains an entity entry without a template.");
                }

                if (!templates.ContainsKey(entityData.Template))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' references unknown entity template '{entityData.Template}'.");
                }

                bool isBatchCompatible = _templateBatchSpawner.IsBatchCompatible(entityData.Template, templates[entityData.Template]);
                if (isBatchCompatible && TryBuildBatchRequest(mapConfig.Id, entityData, mapEntityTag, out var batchRequest))
                {
                    if (!string.Equals(activeBatchTemplateId, entityData.Template, StringComparison.Ordinal) ||
                        pendingBatchRequests.Count >= _templateBatchSpawner.ScratchCapacity)
                    {
                        FlushPendingTemplateBatch();
                    }

                    activeBatchTemplateId = entityData.Template;
                    pendingBatchRequests.Add(batchRequest);
                    pendingBatchEntityData.Add(entityData);
                    continue;
                }

                if (HasPresenterParamOverrides(entityData))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' entity template '{entityData.Template}' declares PresenterParamOverrides but is not compatible with the map template batch path.");
                }

                FlushPendingTemplateBatch();

                builder
                    .UseTemplate(entityData.Template)
                    .WithEntityContext($"Map '{mapConfig.Id}' entity '{ResolveMapEntityContextId(entityData)}'");
                
                if (entityData.Overrides != null)
                {
                    foreach (var kvp in entityData.Overrides)
                    {
                        builder.WithOverride(kvp.Key, kvp.Value);
                    }
                }
                
                var entity = builder.Build();
                TryApplyTemplateKey(entity, entityData.Template);
                _world.Add(entity, mapEntityTag);
                entityIndex.Register(mapConfig.Id, entityData.InstanceId, entity);
                PublishTemplateOnSpawnEffect(entity, entityData.Template);
                BufferEntityTriggerGraphs(entity, entityData.Template, templates[entityData.Template]);
            }

            FlushPendingTemplateBatch();
            return entityIndex;
        }

        private void BufferEntityTriggerGraphs(Entity entity, string templateId, EntityTemplate template)
        {
            if (_entityTriggerGraphMounts == null || template.TriggerGraphs is not { Count: > 0 })
            {
                return;
            }

            _entityTriggerGraphMounts.BufferMapLoadSpawn(entity, templateId, template.TriggerGraphs);
        }

        private static bool TryBuildBatchRequest(
            string mapId,
            EntitySpawnData entityData,
            in MapEntity mapEntity,
            out TemplateEntityBatchSpawner.TemplateBatchSpawnRequest request)
        {
            request = default;
            var worldPosition = default(Ludots.Core.Mathematics.FixedPoint.Fix64Vec2);
            bool hasWorldPosition = false;
            float facingAngleRad = 0f;
            bool hasFacing = false;

            if (entityData.Overrides != null && entityData.Overrides.Count > 0)
            {
                bool containsWorldPosition = entityData.Overrides.ContainsKey("WorldPositionCm");
                bool containsFacing = entityData.Overrides.ContainsKey("FacingDirection");
                int supportedOverrideCount = (containsWorldPosition ? 1 : 0) + (containsFacing ? 1 : 0);
                if (entityData.Overrides.Count != supportedOverrideCount)
                {
                    return false;
                }

                if (containsWorldPosition)
                {
                    worldPosition = ParseWorldPositionOverride(
                        mapId,
                        entityData,
                        entityData.Overrides["WorldPositionCm"]);
                    hasWorldPosition = true;
                }

                if (containsFacing)
                {
                    facingAngleRad = ParseFacingOverride(
                        mapId,
                        entityData,
                        entityData.Overrides["FacingDirection"]);
                    hasFacing = true;
                }
            }

            // Map-authored placement yaw is authored as core FacingDirection.
            // Presentation VisualTransform.Rotation remains derived by presentation sync/static lowering.
            request = new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(
                worldPosition,
                hasWorldPosition,
                facingAngleRad,
                hasFacing,
                mapEntity,
                hasMapEntity: true,
                presenterParamOverrides: ParsePresenterParamOverrides(mapId, entityData));
            return true;
        }

        private static string ResolveMapEntityContextId(EntitySpawnData entityData)
        {
            return string.IsNullOrWhiteSpace(entityData.InstanceId)
                ? $"template:{entityData.Template}"
                : entityData.InstanceId;
        }

        private static bool BatchContainsPresenterParamOverrides(List<TemplateEntityBatchSpawner.TemplateBatchSpawnRequest> requests)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].PresenterParamOverrides.Length != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPresenterParamOverrides(EntitySpawnData entityData)
        {
            return entityData.PresenterParamOverrides != null && entityData.PresenterParamOverrides.Count != 0;
        }

        private bool CanApplyPresenterParamOverrides()
        {
            return _presenterRuntime != null &&
                   _presenterDefinitions != null &&
                   _presenterBootstrap != null &&
                   _stableIds != null;
        }

        private static ParamDefault[] ParsePresenterParamOverrides(string mapId, EntitySpawnData entityData)
        {
            List<ParamOverrideData> overrides = entityData.PresenterParamOverrides;
            if (overrides == null || overrides.Count == 0)
            {
                return Array.Empty<ParamDefault>();
            }

            var result = new ParamDefault[overrides.Count];
            for (int i = 0; i < overrides.Count; i++)
            {
                ParamOverrideData item = overrides[i];
                if (item == null)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}] requires an object payload.");
                }

                string paramKey = item.ParamKey;
                if (string.IsNullOrWhiteSpace(paramKey) ||
                    !string.Equals(paramKey, paramKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].ParamKey must be a trimmed semantic string.");
                }

                if (!item.Lane.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].Lane requires an explicit param lane.");
                }

                ParamLane lane = item.Lane.Value;
                var parsed = new ParamDefault
                {
                    ParamKey = PresenterParamKeyRegistry.Register(paramKey),
                    Lane = lane,
                };

                switch (lane)
                {
                    case ParamLane.Float:
                        parsed.FloatValue = item.FloatValue;
                        break;
                    case ParamLane.Int:
                        parsed.IntValue = item.IntValue;
                        break;
                    case ParamLane.Vector:
                        parsed.VectorValue = ParsePresenterParamOverrideVector(mapId, entityData, item, i);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{i}].Lane '{lane}' is unsupported.");
                }

                result[i] = parsed;
            }

            return result;
        }

        private static Vector4 ParsePresenterParamOverrideVector(
            string mapId,
            EntitySpawnData entityData,
            ParamOverrideData item,
            int index)
        {
            float[] values = item.VectorValue;
            if (values == null || values.Length != 4)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' PresenterParamOverrides[{index}].VectorValue requires four numeric values.");
            }

            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        private static Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 ParseWorldPositionOverride(
            string mapId,
            EntitySpawnData entityData,
            JsonNode worldPositionNode)
        {
            if (worldPositionNode is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm override requires an object payload.");
            }

            ValidateProperties(obj, $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm", "Value");
            JsonNode valueNode = RequireProperty(
                obj,
                "Value",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm");
            if (valueNode is not JsonObject valueObj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value requires an object payload.");
            }

            ValidateProperties(valueObj, $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value", "X", "Y");
            JsonNode xNode = RequireProperty(
                valueObj,
                "X",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value");
            JsonNode yNode = RequireProperty(
                valueObj,
                "Y",
                $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value");
            if (xNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value.X requires an integer value.");
            }

            if (yNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' WorldPositionCm.Value.Y requires an integer value.");
            }

            int x = xNode.GetValue<int>();
            int y = yNode.GetValue<int>();
            return Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(x, y);
        }

        private static float ParseFacingOverride(
            string mapId,
            EntitySpawnData entityData,
            JsonNode facingNode)
        {
            if (facingNode is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection override requires an object payload.");
            }

            ValidateProperties(obj, $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection", "AngleRad");
            JsonNode angleNode = RequireProperty(
                obj,
                "AngleRad",
                $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection");
            if (angleNode.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' entity template '{entityData.Template}' FacingDirection.AngleRad requires a numeric value.");
            }

            return angleNode.GetValue<float>();
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

        private void TryApplyTemplateKey(Entity entity, string templateId)
        {
            int templateKeyId = EntityTemplateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                throw new InvalidOperationException($"Entity template key '{templateId}' is not registered.");
            }

            var templateKey = new EntityTemplateKeyRef { TemplateKeyId = templateKeyId };
            if (_world.Has<EntityTemplateKeyRef>(entity))
            {
                _world.Set(entity, templateKey);
            }
            else
            {
                _world.Add(entity, templateKey);
            }
        }

        private int ResolveTemplateKeyId(string templateId)
        {
            int templateKeyId = EntityTemplateKeys.GetId(templateId);
            return templateKeyId > 0 ? templateKeyId : EntityTemplateKeys.Register(templateId);
        }

        private bool HasDirectEntitySpawnBootstrap(int templateKeyId)
        {
            if (_presenterBootstrap == null)
            {
                return false;
            }

            return templateKeyId > 0 &&
                   _presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules) &&
                   rules.Length > 0;
        }

        private bool ShouldPublishSpawnedEvent(int templateKeyId, bool hasDirectBootstrap)
        {
            if (!hasDirectBootstrap || _presenterBootstrap == null)
            {
                return true;
            }

            return _presenterBootstrap.HasNonBootstrapEntitySpawnRules(templateKeyId);
        }

        private void TryBootstrapPresenterBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> ownerStableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            ReadOnlySpan<ParamDefault[]> ownerParamOverrides)
        {
            if (_presenterRuntime == null ||
                _presenterDefinitions == null ||
                _presenterBootstrap == null ||
                _stableIds == null ||
                owners.Length == 0)
            {
                return;
            }

            if (owners.Length != ownerStableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length ||
                owners.Length != ownerParamOverrides.Length)
            {
                throw new ArgumentException("Map presenter bootstrap batch spans must have matching lengths.");
            }

            if (templateKeyId <= 0 ||
                !_presenterBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            for (int ri = 0; ri < rules.Length; ri++)
            {
                ref readonly var rule = ref rules[ri];
                if (!_presenterDefinitions.TryGet(rule.PresenterDefinitionId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Presenter definition id={rule.PresenterDefinitionId} is not registered.");
                }

                int createCount = 0;
                for (int oi = 0; oi < owners.Length; oi++)
                {
                    Entity owner = owners[oi];
                    if (!PassesBootstrapCondition(rule, owner))
                    {
                        continue;
                    }

                    int scopeTag = rule.ResolveScopeTag(ownerStableIds[oi]);
                    if (scopeTag <= 0)
                    {
                        continue;
                    }

                    if (_presenterRuntime.HasActiveScopedInstance(
                            rule.PresenterDefinitionId,
                            owner,
                            scopeTag,
                            PresentationAnchorKind.Entity,
                            default))
                    {
                        continue;
                    }

                    _presenterBatchOwners[createCount] = owner;
                    _presenterBatchScopeIds[createCount] = scopeTag;
                    _presenterBatchStableIds[createCount] = _stableIds.Allocate();
                    _ownerBatchTransforms[createCount] = ownerTransforms[oi];
                    _ownerBatchCulls[createCount] = ownerCulls[oi];
                    _presenterBatchParamOverrides[createCount] = ownerParamOverrides[oi] ?? Array.Empty<ParamDefault>();
                    createCount++;
                }

                if (createCount == 0)
                {
                    continue;
                }

                _presenterRuntime.CreateEntityAnchoredRootBatch(
                    _presenterDefinitions,
                    rule.PresenterDefinitionId,
                    _presenterBatchOwners.AsSpan(0, createCount),
                    _presenterBatchScopeIds.AsSpan(0, createCount),
                    _presenterBatchStableIds.AsSpan(0, createCount),
                    _ownerBatchTransforms.AsSpan(0, createCount),
                    _ownerBatchCulls.AsSpan(0, createCount),
                    definition,
                    _presenterBatchCreated.AsSpan(0, createCount),
                    _stableIds.Allocate,
                    _presenterBatchParamOverrides.AsSpan(0, createCount));

                for (int i = 0; i < createCount; i++)
                {
                    _presenterBatchParamOverrides[i] = null!;
                    MarkHierarchyForBootstrapIfNeeded(_presenterBatchCreated[i]);
                }
            }
        }

        private bool PassesBootstrapCondition(CompiledPresenterBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => _world.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => _world.Has<AttributeBuffer>(owner),
                _ => throw new InvalidOperationException($"Unsupported presenter bootstrap inline condition '{rule.InlineCondition}'."),
            };
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!_world.IsAlive(root) || !_world.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(root);
            if (_presenterDefinitions != null &&
                _presenterDefinitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPresenter(root);
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (_world.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkPresenter(Entity presenter)
        {
            if (_world.Has<PresenterBootstrapPending>(presenter))
            {
                return;
            }

            _world.Add(presenter, new PresenterBootstrapPending());
        }

        private void PublishTemplateOnSpawnEffect(Entity entity, string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return;
            }

            EntityTemplate template = TemplateRegistry.Get(templateId);
            if (template == null || string.IsNullOrWhiteSpace(template.OnSpawnEffect))
            {
                return;
            }

            if (_effectRequests == null)
            {
                throw new InvalidOperationException(
                    $"MapLoader has no EffectRequestQueue; cannot publish onSpawnEffect '{template.OnSpawnEffect}' for template '{templateId}'.");
            }

            int effectTemplateId = EffectTemplateIdRegistry.GetId(template.OnSpawnEffect);
            if (effectTemplateId <= 0)
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' references unknown onSpawnEffect '{template.OnSpawnEffect}'.");
            }

            _effectRequests.Publish(new EffectRequest
            {
                Source = entity,
                Target = entity,
                TargetContext = entity,
                TemplateId = effectTemplateId,
            });
        }
        
    }
}
