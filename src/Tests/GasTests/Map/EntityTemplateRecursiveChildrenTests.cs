using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class EntityTemplateRecursiveChildrenTests
    {
        private const string MapId = "entity_template_recursive_children";

        private const string Pose = "\"localPose\": { \"offsetXCm\": 10, \"offsetYCm\": 0, \"facingDeg\": 0, \"inheritParentFacing\": false, \"offsetRotation\": \"None\" }";

        private static readonly string ExpansionTemplates = """
        [
          {
            "id": "rec.root",
            "components": {
              "Name": { "Value": "Root" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } }
            },
            "children": [
              {
                "localId": "hq", "template": "rec.tent", {{POSE}},
                "children": [
                  {
                    "localId": "chest", "template": "rec.chest", {{POSE}},
                    "children": [ { "localId": "cargo", "template": "rec.cargo", {{POSE}} } ]
                  },
                  { "template": "rec.noid", {{POSE}} }
                ]
              },
              { "localId": "tower", "template": "rec.tower", {{POSE}} }
            ]
          },
          { "id": "rec.tent",  "components": { "Name": { "Value": "Tent" } } },
          { "id": "rec.chest", "components": { "Name": { "Value": "Chest" } } },
          { "id": "rec.cargo", "components": { "Name": { "Value": "Cargo" } } },
          { "id": "rec.noid",  "components": { "Name": { "Value": "NoId" } } },
          { "id": "rec.tower", "components": { "Name": { "Value": "Tower" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void LoadEntities_RecursiveChildren_ExpandsDepthFirstParentBeforeChild()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, ExpansionTemplates);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Template = "rec.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.Count, Is.EqualTo(1), "only the placed root is a flat InstanceId entry");
            Assert.That(index.LocalPathCount, Is.EqualTo(4), "camp.hq / camp.hq.chest / camp.hq.chest.cargo / camp.tower");
            Assert.That(CountMapEntities(world), Is.EqualTo(6), "root + hq + chest + cargo + no-localId sibling + tower");

            Assert.That(index.TryGet("camp", out Entity root), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.hq", out Entity hq), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.hq.chest", out Entity chest), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.hq.chest.cargo", out Entity cargo), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.tower", out Entity tower), Is.True);
            Entity noId = FindByName(world, "NoId");

            Assert.That(world.IsAlive(root) && world.IsAlive(hq) && world.IsAlive(chest) &&
                        world.IsAlive(cargo) && world.IsAlive(tower) && world.IsAlive(noId), Is.True);

            // Depth first × declaration order: parent before child, sibling subtrees in order.
            Assert.That(root.Id, Is.LessThan(hq.Id));
            Assert.That(hq.Id, Is.LessThan(chest.Id));
            Assert.That(chest.Id, Is.LessThan(cargo.Id));
            Assert.That(cargo.Id, Is.LessThan(noId.Id));
            Assert.That(noId.Id, Is.LessThan(tower.Id));

            Assert.That(world.Get<Name>(hq).Value, Is.EqualTo("Tent"));
            Assert.That(world.Get<Name>(chest).Value, Is.EqualTo("Chest"));
            Assert.That(world.Get<Name>(cargo).Value, Is.EqualTo("Cargo"));
            Assert.That(world.Get<Name>(tower).Value, Is.EqualTo("Tower"));
        }

        [Test]
        public void LoadEntities_LocalIdPaths_AreInstanceRootedAndSkipAnonymousSiblings()
        {
            using var world = World.Create();
            MapLoader loader = CreateLoader(world, ExpansionTemplates);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Template = "rec.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.TryGetByLocalPath("camp.hq", out Entity hq), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.hq.chest", out Entity chest), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.hq.chest.cargo", out Entity cargo), Is.True);
            Assert.That(index.TryGetByLocalPath("camp.tower", out Entity tower), Is.True);

            Assert.That(world.Get<Name>(hq).Value, Is.EqualTo("Tent"));
            Assert.That(world.Get<Name>(chest).Value, Is.EqualTo("Chest"));
            Assert.That(world.Get<Name>(cargo).Value, Is.EqualTo("Cargo"));
            Assert.That(world.Get<Name>(tower).Value, Is.EqualTo("Tower"));

            // The sibling without localId has no addressable path.
            Assert.That(index.TryGetByLocalPath("camp.NoId", out _), Is.False);
            Assert.That(index.LocalPathCount, Is.EqualTo(4));
        }

        [Test]
        public void LoadTemplates_DuplicateSiblingLocalId_ThrowsWithLocalIdContext()
        {
            using var world = World.Create();
            const string json = """
            [
              {
                "id": "dup.root",
                "components": { "Name": { "Value": "Root" } },
                "children": [
                  { "localId": "dup", "template": "dup.leaf", "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } },
                  { "localId": "dup", "template": "dup.leaf", "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } }
                ]
              },
              { "id": "dup.leaf", "components": { "Name": { "Value": "Leaf" } } }
            ]
            """;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => CreateLoader(world, json))!;

            Assert.That(ex.Message, Does.Contain("localId 'dup' 重复"));
        }

        [Test]
        public void LoadTemplates_TemplateChildrenCycle_Throws()
        {
            using var world = World.Create();
            const string json = """
            [
              {
                "id": "cycle.a",
                "components": { "Name": { "Value": "A" } },
                "children": [ { "template": "cycle.b", "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } } ]
              },
              {
                "id": "cycle.b",
                "components": { "Name": { "Value": "B" } },
                "children": [ { "template": "cycle.a", "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } } ]
              }
            ]
            """;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => CreateLoader(world, json))!;

            Assert.That(ex.Message, Does.Contain("环"));
        }

        [Test]
        public void LoadTemplates_InheritParentFacingWithOwnFacingOffsetRotation_Throws()
        {
            using var world = World.Create();
            const string json = """
            [
              {
                "id": "facing.root",
                "components": { "Name": { "Value": "Root" } },
                "children": [
                  { "template": "facing.leaf", "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": true, "offsetRotation": "OwnFacing" } }
                ]
              },
              { "id": "facing.leaf", "components": { "Name": { "Value": "Leaf" } } }
            ]
            """;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => CreateLoader(world, json))!;

            Assert.That(ex.Message, Does.Contain("互斥"));
        }

        [Test]
        public void LoadTemplates_AttachFalseChild_WithMovementParticipation_DoesNotThrow()
        {
            using var world = World.Create();
            string json = MovementParticipationTemplate(attachLiteral: "\"attach\": false");

            Assert.DoesNotThrow(() => CreateLoader(world, json));
        }

        [Test]
        public void LoadTemplates_AttachTrueChild_WithMovementParticipation_Throws()
        {
            using var world = World.Create();
            string json = MovementParticipationTemplate(attachLiteral: "\"attach\": true");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => CreateLoader(world, json))!;

            Assert.That(ex.Message, Does.Contain("MovementParticipation"));
        }

        private static string MovementParticipationTemplate(string attachLiteral)
        {
            return $$"""
            [
              {
                "id": "move.root",
                "components": { "Name": { "Value": "Root" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
                "children": [
                  { "template": "move.unit", {{attachLiteral}}, "localPose": { "offsetXCm": 0, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } }
                ]
              },
              {
                "id": "move.unit",
                "components": {
                  "Name": { "Value": "Unit" },
                  "MovementParticipation": {
                    "physicsPresence": "none",
                    "displacement": { "allowed": true, "handbackSpeedThresholdCmPerSec": 20, "maxDurationMs": 2000 }
                  }
                }
              }
            ]
            """;
        }

        private static MapLoader CreateLoader(World world, string templatesJson)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_EntityTemplateRecursiveChildrenTests",
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

        private static int CountMapEntities(World world)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<MapEntity>();
            world.Query(in query, (Entity _) => count++);
            return count;
        }

        private static Entity FindByName(World world, string name)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            world.Query(in query, (Entity entity, ref Name component) =>
            {
                if (string.Equals(component.Value, name, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"entity named '{name}' was not spawned");
            return found;
        }
    }
}
