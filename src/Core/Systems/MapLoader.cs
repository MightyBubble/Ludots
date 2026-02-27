using System;
using System.IO;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map;

namespace Ludots.Core.Systems
{
    public class MapLoader
    {
        private readonly World _world;
        private readonly WorldMap _worldMap;
        
        // New Registry
        public DataRegistry<EntityTemplate> TemplateRegistry { get; private set; }

        public MapLoader(World world, WorldMap worldMap, ConfigPipeline pipeline)
        {
            _world = world;
            _worldMap = worldMap;
            TemplateRegistry = new DataRegistry<EntityTemplate>(pipeline);
        }

        public void LoadTemplates()
        {
            // This loads "Entities/templates.json" from Core and all Mods
            // Merging them with priority
            TemplateRegistry.Load("Entities/templates.json");
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
                builder.UseTemplate(entityData.Template);
                
                if (entityData.Overrides != null)
                {
                    foreach (var kvp in entityData.Overrides)
                    {
                        builder.WithOverride(kvp.Key, kvp.Value);
                    }
                }
                
                var entity = builder.Build();
                _world.Add(entity, mapEntityTag);
            }
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
