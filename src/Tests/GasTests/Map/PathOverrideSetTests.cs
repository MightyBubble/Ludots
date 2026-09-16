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
    /// <summary>
    /// 切B 验收：实例级 overridePaths 以绝对 localId 路径向后代实体做字段 deep-merge。
    /// 作用范围与 overrides（自身整组件替换）区分：本套件只覆盖后代路径 set。
    /// </summary>
    [TestFixture]
    public sealed class PathOverrideSetTests
    {
        private const string MapId = "path_override_set";

        private const string Pose = "\"localPose\": { \"offsetXCm\": 10, \"offsetYCm\": 0, \"facingDeg\": 0, \"inheritParentFacing\": false, \"offsetRotation\": \"None\" }";

        private static readonly string Templates = """
        [
          {
            "id": "po.root",
            "components": { "Name": { "Value": "Root" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [
              {
                "localId": "hq", "template": "po.hq", {{POSE}},
                "children": [
                  {
                    "localId": "chest", "template": "po.chest", {{POSE}},
                    "children": [ { "localId": "cargo", "template": "po.cargo", {{POSE}} } ]
                  }
                ]
              },
              { "localId": "guard", "template": "po.guard", {{POSE}} }
            ]
          },
          { "id": "po.hq",    "components": { "Name": { "Value": "Hq" } } },
          { "id": "po.chest", "components": { "Name": { "Value": "Chest" } } },
          { "id": "po.cargo", "components": { "Name": { "Value": "Cargo" }, "Health": { "Current": 8, "Max": 8 } } },
          { "id": "po.guard", "components": { "Name": { "Value": "Guard" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void DeepPathSet_DeepMergesFields_AndKeepsUnsetFields()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            MapConfig map = MapWith(PathOverride("hq.chest.cargo", "Health", new JsonObject { ["Max"] = 12 }));

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Entity cargo = GetByPath(index, "camp.hq.chest.cargo");
            Assert.That(world.Get<Name>(cargo).Value, Is.EqualTo("Cargo"), "untouched component survives the deep-merge");
            Assert.That(world.Get<Health>(cargo).Current, Is.EqualTo(8), "unset deep field keeps the template value");
            Assert.That(world.Get<Health>(cargo).Max, Is.EqualTo(12), "set deep field overwrites");
        }

        [Test]
        public void DeepPathSet_AbsoluteInstancePrefixedPath_BehavesLikeRelative()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            MapConfig map = MapWith(PathOverride("camp.hq.chest.cargo", "Health", new JsonObject { ["Max"] = 21 }));

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Entity cargo = GetByPath(index, "camp.hq.chest.cargo");
            Assert.That(world.Get<Health>(cargo).Current, Is.EqualTo(8));
            Assert.That(world.Get<Health>(cargo).Max, Is.EqualTo(21));
        }

        [Test]
        public void PathSet_ComponentAbsentOnTemplate_IsWrittenAsNewComponent()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            MapConfig map = MapWith(PathOverride("guard", "Health", new JsonObject { ["Current"] = 5, ["Max"] = 5 }));

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Entity guard = GetByPath(index, "camp.guard");
            Assert.That(world.Get<Health>(guard).Current, Is.EqualTo(5));
            Assert.That(world.Get<Health>(guard).Max, Is.EqualTo(5));
        }

        [Test]
        public void UnmatchedPath_ThrowsWithPathInMessage()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            MapConfig map = MapWith(PathOverride("hq.chest.missing", "Health", new JsonObject { ["Max"] = 1 }));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("camp.hq.chest.missing"));
            Assert.That(ex.Message, Does.Contain("overridePaths"));
        }

        [Test]
        public void EmptySet_Throws()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            var entry = new PathOverrideEntry { Path = "guard", Set = new Dictionary<string, JsonNode>() };
            MapConfig map = MapWith(entry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("at least one component"));
        }

        [Test]
        public void NonObjectSetPayload_Throws()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            var entry = new PathOverrideEntry
            {
                Path = "guard",
                Set = new Dictionary<string, JsonNode> { ["Health"] = JsonValue.Create(5) },
            };
            MapConfig map = MapWith(entry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("JSON object"));
        }

        [Test]
        public void NoOverridePaths_LeavesDescendantsAtTemplateValues()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Template = "po.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Entity cargo = GetByPath(index, "camp.hq.chest.cargo");
            Assert.That(world.Get<Health>(cargo).Current, Is.EqualTo(8));
            Assert.That(world.Get<Health>(cargo).Max, Is.EqualTo(8));
        }

        [Test]
        public void SelfOverrides_StayOnThePlacedEntity_AndDoNotReachDescendants()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, Templates);
            var entityData = new EntitySpawnData
            {
                InstanceId = "camp",
                Template = "po.root",
                Overrides = new Dictionary<string, JsonNode>
                {
                    ["Name"] = new JsonObject { ["Value"] = "Renamed Root" },
                },
            };
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(entityData);

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.TryGet("camp", out Entity root), Is.True);
            Assert.That(world.Get<Name>(root).Value, Is.EqualTo("Renamed Root"));
            Assert.That(world.Get<Name>(GetByPath(index, "camp.hq.chest.cargo")).Value, Is.EqualTo("Cargo"));
        }

        private static PathOverrideEntry PathOverride(string path, string component, JsonObject set)
        {
            return new PathOverrideEntry
            {
                Path = path,
                Set = new Dictionary<string, JsonNode> { [component] = set },
            };
        }

        private static MapConfig MapWith(params PathOverrideEntry[] entries)
        {
            var entityData = new EntitySpawnData
            {
                InstanceId = "camp",
                Template = "po.root",
                OverridePaths = new List<PathOverrideEntry>(entries),
            };
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(entityData);
            return map;
        }

        private static Entity GetByPath(MapLoadEntityIndex index, string path)
        {
            Assert.That(index.TryGetByLocalPath(path, out Entity entity), Is.True, $"addressable path '{path}' should resolve");
            return entity;
        }

        private static MapLoader CreateLoader(World world, string templatesJson)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_PathOverrideSetTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Entities"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(Path.Combine(root, "Entities", "templates.json"), templatesJson);

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
