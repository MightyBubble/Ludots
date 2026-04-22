using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public class MapLoader
    {
        private const int TemplateBatchScratchCapacity = 4096;

        private readonly World _world;
        private readonly WorldMap _worldMap;
        private EffectRequestQueue _effectRequests;
        private TemplateEntityBatchSpawner _templateBatchSpawner;
        private PresentationStableIdAllocator _stableIds;
        private PerformerEntityRuntime _performerRuntime;
        private PerformerDefinitionRegistry _performerDefinitions;
        private CompiledPerformerBootstrapRegistry _performerBootstrap;
        private readonly Entity[] _performerBatchOwners = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _performerBatchScopeIds = new int[TemplateBatchScratchCapacity];
        private readonly int[] _performerBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly Entity[] _performerBatchCreated = new Entity[TemplateBatchScratchCapacity];
        private readonly int[] _ownerBatchStableIds = new int[TemplateBatchScratchCapacity];
        private readonly VisualTransform[] _ownerBatchTransforms = new VisualTransform[TemplateBatchScratchCapacity];
        private readonly CullState[] _ownerBatchCulls = new CullState[TemplateBatchScratchCapacity];
        
        // New Registry
        public DataRegistry<EntityTemplate> TemplateRegistry { get; private set; }
        public EntityTemplateKeyRegistry EntityTemplateKeys { get; }

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

        public void SetPresentationRuntime(
            PresentationStableIdAllocator stableIds,
            PerformerEntityRuntime performerRuntime,
            PerformerDefinitionRegistry performerDefinitions,
            ISpatialPartitionWorld spatialPartition,
            WorldSizeSpec worldSizeSpec)
        {
            _stableIds = stableIds;
            _performerRuntime = performerRuntime;
            _performerDefinitions = performerDefinitions;
            _performerBootstrap = performerDefinitions?.BootstrapRegistry;
            _templateBatchSpawner = new TemplateEntityBatchSpawner(
                _world,
                EntityTemplateKeys,
                stableIds,
                spatialPartition,
                worldSizeSpec,
                TemplateBatchScratchCapacity);
        }

        public void LoadTemplates()
        {
            // This loads "Entities/templates.json" from Core and all Mods
            // Merging them with priority
            TemplateRegistry.Load("Entities/templates.json");
            EntityTemplateKeys.Clear();
            foreach (var template in TemplateRegistry.GetAll())
            {
                EntityTemplateKeys.Register(template.Id);
            }
        }

        public void LoadEntities(MapConfig mapConfig)
        {
            if (mapConfig == null) return;
            if (mapConfig.Entities == null) return;

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

            var builder = new EntityBuilder(_world, templates);
            var mapEntityTag = new MapEntity { MapId = new MapId(mapConfig.Id) };
            var pendingBatchRequests = new List<TemplateEntityBatchSpawner.TemplateBatchSpawnRequest>(_templateBatchSpawner.ScratchCapacity);
            string? activeBatchTemplateId = null;

            void FlushPendingTemplateBatch()
            {
                if (activeBatchTemplateId == null || pendingBatchRequests.Count == 0)
                {
                    pendingBatchRequests.Clear();
                    activeBatchTemplateId = null;
                    return;
                }

                int templateKeyId = ResolveTemplateKeyId(activeBatchTemplateId);
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
                    features |= TemplateBatchSpawnFeatures.PerformerRootBootstrapHandled;
                }

                int batchCount = pendingBatchRequests.Count;
                Span<int> stableIds = hasDirectBootstrap ? _ownerBatchStableIds.AsSpan(0, batchCount) : default;
                Span<VisualTransform> ownerTransforms = hasDirectBootstrap ? _ownerBatchTransforms.AsSpan(0, batchCount) : default;
                Span<CullState> ownerCulls = hasDirectBootstrap ? _ownerBatchCulls.AsSpan(0, batchCount) : default;

                if (_templateBatchSpawner.TryCreateBatch(
                    activeBatchTemplateId,
                    templates[activeBatchTemplateId],
                    CollectionsMarshal.AsSpan(pendingBatchRequests),
                    features,
                    out var created,
                    stableIds,
                    ownerTransforms,
                    ownerCulls))
                {
                    for (int i = 0; i < created.Length; i++)
                    {
                        PublishTemplateOnSpawnEffect(created[i], activeBatchTemplateId);
                    }

                    if (hasDirectBootstrap)
                    {
                        TryBootstrapPerformerBatch(
                            templateKeyId,
                            created,
                            stableIds,
                            ownerTransforms,
                            ownerCulls);
                    }
                }
                else
                {
                    for (int i = 0; i < pendingBatchRequests.Count; i++)
                    {
                        builder.UseTemplate(activeBatchTemplateId);
                        if (pendingBatchRequests[i].HasWorldPosition)
                        {
                            builder.WithOverride("WorldPositionCm", BuildWorldPositionNode(pendingBatchRequests[i].WorldPositionCm));
                        }

                        var entity = builder.Build();
                        TryApplyTemplateKey(entity, activeBatchTemplateId);
                        _world.Add(entity, pendingBatchRequests[i].MapEntity);
                        PublishTemplateOnSpawnEffect(entity, activeBatchTemplateId);
                    }
                }

                pendingBatchRequests.Clear();
                activeBatchTemplateId = null;
            }
            
            foreach (var entityData in mapConfig.Entities)
            {
                if (entityData == null)
                {
                    Log.Warn(in LogChannels.Map, $"Null entity entry in map '{mapConfig.Id}', skipping.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entityData.Template))
                {
                    Log.Warn(in LogChannels.Map, $"Entity entry missing template in map '{mapConfig.Id}', skipping.");
                    continue;
                }

                if (!templates.ContainsKey(entityData.Template))
                {
                    Log.Warn(in LogChannels.Map, $"Unknown entity template '{entityData.Template}' in map '{mapConfig.Id}', skipping.");
                    continue;
                }

                if (TryBuildBatchRequest(entityData, mapEntityTag, out var batchRequest))
                {
                    if (!string.Equals(activeBatchTemplateId, entityData.Template, StringComparison.Ordinal) ||
                        pendingBatchRequests.Count >= _templateBatchSpawner.ScratchCapacity)
                    {
                        FlushPendingTemplateBatch();
                    }

                    activeBatchTemplateId = entityData.Template;
                    pendingBatchRequests.Add(batchRequest);
                    continue;
                }

                FlushPendingTemplateBatch();

                builder.UseTemplate(entityData.Template);
                
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
                PublishTemplateOnSpawnEffect(entity, entityData.Template);
            }

            FlushPendingTemplateBatch();
        }

        private static bool TryBuildBatchRequest(
            EntitySpawnData entityData,
            in MapEntity mapEntity,
            out TemplateEntityBatchSpawner.TemplateBatchSpawnRequest request)
        {
            request = default;
            if (entityData.Overrides == null || entityData.Overrides.Count == 0)
            {
                request = new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(
                    default,
                    hasWorldPosition: false,
                    mapEntity: mapEntity,
                    hasMapEntity: true);
                return true;
            }

            if (entityData.Overrides.Count != 1 ||
                !entityData.Overrides.TryGetValue("WorldPositionCm", out var worldPositionNode) ||
                worldPositionNode is not JsonObject obj)
            {
                return false;
            }

            JsonNode valueNode = obj["Value"] ?? obj["value"] ?? worldPositionNode;
            if (valueNode is not JsonObject valueObj)
            {
                return false;
            }

            int x = valueObj["X"]?.GetValue<int>() ?? 0;
            int y = valueObj["Y"]?.GetValue<int>() ?? 0;
            request = new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(
                Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(x, y),
                hasWorldPosition: true,
                mapEntity: mapEntity,
                hasMapEntity: true);
            return true;
        }

        private static JsonObject BuildWorldPositionNode(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldPositionCm)
        {
            var vector = worldPositionCm.ToWorldCmInt2();
            return new JsonObject
            {
                ["Value"] = new JsonObject
                {
                    ["X"] = vector.X,
                    ["Y"] = vector.Y,
                }
            };
        }

        private void TryApplyTemplateKey(Entity entity, string templateId)
        {
            int templateKeyId = EntityTemplateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                return;
            }

            var templateKey = new EntityTemplateKeyCm { TemplateKeyId = templateKeyId };
            if (_world.Has<EntityTemplateKeyCm>(entity))
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
            if (_performerBootstrap == null)
            {
                return false;
            }

            return templateKeyId > 0 &&
                   _performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules) &&
                   rules.Length > 0;
        }

        private bool ShouldPublishSpawnedEvent(int templateKeyId, bool hasDirectBootstrap)
        {
            if (!hasDirectBootstrap || _performerBootstrap == null)
            {
                return true;
            }

            return _performerBootstrap.HasNonBootstrapEntitySpawnRules(templateKeyId);
        }

        private void TryBootstrapPerformerBatch(
            int templateKeyId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> ownerStableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls)
        {
            if (_performerRuntime == null ||
                _performerDefinitions == null ||
                _performerBootstrap == null ||
                _stableIds == null ||
                owners.Length == 0)
            {
                return;
            }

            if (owners.Length != ownerStableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length)
            {
                throw new ArgumentException("Map performer bootstrap batch spans must have matching lengths.");
            }

            if (templateKeyId <= 0 ||
                !_performerBootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return;
            }

            for (int ri = 0; ri < rules.Length; ri++)
            {
                ref readonly var rule = ref rules[ri];
                if (!_performerDefinitions.TryGet(rule.PerformerDefinitionId, out PerformerDefinition definition))
                {
                    throw new InvalidOperationException($"Performer definition id={rule.PerformerDefinitionId} is not registered.");
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

                    if (_performerRuntime.HasActiveScopedInstance(
                            rule.PerformerDefinitionId,
                            owner,
                            scopeTag,
                            PresentationAnchorKind.Entity,
                            default))
                    {
                        continue;
                    }

                    _performerBatchOwners[createCount] = owner;
                    _performerBatchScopeIds[createCount] = scopeTag;
                    _performerBatchStableIds[createCount] = _stableIds.Allocate();
                    _ownerBatchTransforms[createCount] = ownerTransforms[oi];
                    _ownerBatchCulls[createCount] = ownerCulls[oi];
                    createCount++;
                }

                if (createCount == 0)
                {
                    continue;
                }

                _performerRuntime.CreateEntityAnchoredRootBatch(
                    _performerDefinitions,
                    rule.PerformerDefinitionId,
                    _performerBatchOwners.AsSpan(0, createCount),
                    _performerBatchScopeIds.AsSpan(0, createCount),
                    _performerBatchStableIds.AsSpan(0, createCount),
                    _ownerBatchTransforms.AsSpan(0, createCount),
                    _ownerBatchCulls.AsSpan(0, createCount),
                    definition,
                    _performerBatchCreated.AsSpan(0, createCount),
                    _stableIds.Allocate);

                for (int i = 0; i < createCount; i++)
                {
                    MarkHierarchyForBootstrapIfNeeded(_performerBatchCreated[i]);
                }
            }
        }

        private bool PassesBootstrapCondition(CompiledPerformerBootstrapRegistry.BootstrapCreateRule rule, Entity owner)
        {
            return rule.InlineCondition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => _world.Has<VisualTransform>(owner),
                InlineConditionKind.SourceHasAttributes => _world.Has<AttributeBuffer>(owner),
                _ => false,
            };
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!_world.IsAlive(root) || !_world.Has<PerformerState>(root))
            {
                return;
            }

            ref readonly PerformerState state = ref _world.Get<PerformerState>(root);
            if (_performerDefinitions != null &&
                _performerDefinitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkPerformer(root);
            }

            ref PerformerChildren children = ref _world.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (_world.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }

        private void MarkPerformer(Entity performer)
        {
            if (_world.Has<PerformerBootstrapPending>(performer))
            {
                return;
            }

            _world.Add(performer, new PerformerBootstrapPending());
        }

        private void PublishTemplateOnSpawnEffect(Entity entity, string templateId)
        {
            if (_effectRequests == null || string.IsNullOrWhiteSpace(templateId))
            {
                return;
            }

            EntityTemplate template = TemplateRegistry.Get(templateId);
            if (template == null || string.IsNullOrWhiteSpace(template.OnSpawnEffect))
            {
                return;
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
        
        public void LoadMapBinary(byte[] data)
        {
             // Same as before
             if (data == null || data.Length < 16) return;
             
             using (var reader = new BinaryReader(new MemoryStream(data)))
             {
                 string magic = new string(reader.ReadChars(4));
                 if (magic != "LMAP") return;
                 
                 int version = reader.ReadInt32();
                 int width = reader.ReadInt32();
                 int height = reader.ReadInt32();
                 
                 if (width != _worldMap.WidthInTiles || height != _worldMap.HeightInTiles)
                 {
                     Log.Warn(in LogChannels.Map, $"Map dimensions mismatch. Expected {_worldMap.WidthInTiles}x{_worldMap.HeightInTiles}, got {width}x{height}");
                 }
                 
                 // Skip content for now as per previous implementation
             }
        }
    }
}
