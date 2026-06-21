using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class MapLoadEntityIndexContractTests
    {
        private const string MapId = "map_load_entity_index_contract";
        private const string SpatialTemplateId = "test.map.index.spatial";

        [Test]
        public void LoadEntitiesAndIndex_IndexesAuthoredInstanceIds()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "entity.alpha",
                Template = SpatialTemplateId,
            });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(index.TryGet("entity.alpha", out Entity entity), Is.True);
            Assert.That(world.IsAlive(entity), Is.True);
            Assert.That(world.Get<Name>(entity).Value, Is.EqualTo("Indexed Spatial Entity"));
            Assert.That(world.Get<MapEntity>(entity).MapId.Value, Is.EqualTo(MapId));
        }

        [Test]
        public void LoadEntitiesAndIndex_BlankInstanceIdFailsExplicitly()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = " ",
                Template = SpatialTemplateId,
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("InstanceId requires a non-empty value"));
        }

        private static MapLoader CreateLoader(World world)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MapLoadEntityIndexContractTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Entities"));
                File.WriteAllText(
                    Path.Combine(root, "Configs", "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(
                    Path.Combine(root, "Configs", "Entities", "templates.json"),
                    $$"""
                    [
                      {
                        "id": "{{SpatialTemplateId}}",
                        "components": {
                          "Name": { "Value": "Indexed Spatial Entity" },
                          "WorldPositionCm": { "Value": { "X": 12, "Y": 34 } },
                          "FacingDirection": { "AngleRad": 0.5 },
                          "AttributeBuffer": { "base": {} },
                          "GameplayTagContainer": {},
                          "TagCountContainer": {}
                        }
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var loader = new MapLoader(world, new WorldMap(), pipeline);
                loader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
                return loader;
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
