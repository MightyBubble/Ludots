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
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;

namespace Ludots.Core.Systems
{
    public class MapLoader
    {
        private readonly World _world;
        private readonly WorldMap _worldMap;
        private EffectRequestQueue _effectRequests;
        private readonly TemplateEntityBatchSpawner _templateBatchSpawner;
        
        // New Registry
        public DataRegistry<EntityTemplate> TemplateRegistry { get; private set; }
        public EntityTemplateKeyRegistry EntityTemplateKeys { get; }

        public MapLoader(World world, WorldMap worldMap, ConfigPipeline pipeline)
        {
            _world = world;
            _worldMap = worldMap;
            TemplateRegistry = new DataRegistry<EntityTemplate>(pipeline);
            EntityTemplateKeys = new EntityTemplateKeyRegistry();
            _templateBatchSpawner = new TemplateEntityBatchSpawner(world, EntityTemplateKeys);
        }

        public void SetEffectRequestQueue(EffectRequestQueue effectRequests)
        {
            _effectRequests = effectRequests;
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

                if (_templateBatchSpawner.TryCreateBatch(
                    activeBatchTemplateId,
                    templates[activeBatchTemplateId],
                    CollectionsMarshal.AsSpan(pendingBatchRequests),
                    TemplateBatchSpawnFeatures.MapEntity,
                    out var created))
                {
                    for (int i = 0; i < created.Length; i++)
                    {
                        PublishTemplateOnSpawnEffect(created[i], activeBatchTemplateId);
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
