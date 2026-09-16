using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class EntityGroupPlacementTests
    {
        private const string TemplatesJson = """
        [
          {
            "id": "tower",
            "components": {
              "Health": { "Value": { "X": 1, "Y": 2 }, "Extra": true }
            }
          },
          { "id": "gate", "components": {} },
          { "id": "leader", "components": {} }
        ]
        """;

        [Test]
        public void Expand_GroupPlacement_PrefixesSlotsInDeclarationOrderAndAppliesAnchor()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      {
                        "localId": "tower",
                        "template": "tower",
                        "localPose": { "offsetXCm": 100, "offsetYCm": -50, "facingDeg": 90 }
                      },
                      { "localId": "gate", "template": "gate" }
                    ]
                  }
                ]
                """);

            EntityGroupPlacement.Validate(registries.Groups, registries.Templates);

            var before = new EntitySpawnData { InstanceId = "before", Template = "gate" };
            var camp = new EntitySpawnData
            {
                InstanceId = "camp1",
                Group = "g",
                PositionXCm = 1000,
                PositionYCm = 2000,
            };
            var after = new EntitySpawnData { InstanceId = "after", Template = "tower" };
            var map = new MapConfig { Id = "map.expand" };
            map.Entities.Add(before);
            map.Entities.Add(camp);
            map.Entities.Add(after);

            List<EntitySpawnData> expanded = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);

            Assert.That(
                expanded.Select(e => e.InstanceId),
                Is.EqualTo(new[] { "before", "camp1.tower", "camp1.gate", "after" }));
            Assert.That(expanded[0], Is.SameAs(before));
            Assert.That(expanded[3], Is.SameAs(after));

            EntitySpawnData tower = expanded[1];
            Assert.That(tower.Template, Is.EqualTo("tower"));
            Assert.That(tower.Group, Is.Null);
            Assert.That(tower.PositionXCm, Is.Null);
            Assert.That(tower.PositionYCm, Is.Null);
            Assert.That(tower.Overrides!["WorldPositionCm"]!["Value"]!["X"]!.GetValue<int>(), Is.EqualTo(1100));
            Assert.That(tower.Overrides!["WorldPositionCm"]!["Value"]!["Y"]!.GetValue<int>(), Is.EqualTo(1950));
            Assert.That(
                tower.Overrides!["FacingDirection"]!["AngleRad"]!.GetValue<float>(),
                Is.EqualTo(MathF.PI / 2f).Within(1e-6f));

            EntitySpawnData gate = expanded[2];
            Assert.That(gate.Template, Is.EqualTo("gate"));
            Assert.That(gate.Overrides!.ContainsKey("FacingDirection"), Is.False);
            Assert.That(gate.Overrides!["WorldPositionCm"]!["Value"]!["X"]!.GetValue<int>(), Is.EqualTo(1000));
            Assert.That(gate.Overrides!["WorldPositionCm"]!["Value"]!["Y"]!.GetValue<int>(), Is.EqualTo(2000));
        }

        [Test]
        public void Expand_NestedGroup_AccumulatesPrefixAndAnchor()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "outer",
                    "slots": [
                      {
                        "localId": "slot",
                        "group": "inner",
                        "localPose": { "offsetXCm": 10, "offsetYCm": 20 }
                      }
                    ]
                  },
                  {
                    "id": "inner",
                    "slots": [
                      { "localId": "leader", "template": "leader", "localPose": { "offsetXCm": 5, "offsetYCm": -5 } }
                    ]
                  }
                ]
                """);

            EntityGroupPlacement.Validate(registries.Groups, registries.Templates);

            var map = new MapConfig { Id = "map.nested" };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "root",
                Group = "outer",
                PositionXCm = 100,
                PositionYCm = 200,
            });

            List<EntitySpawnData> expanded = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);

            Assert.That(expanded.Select(e => e.InstanceId), Is.EqualTo(new[] { "root.slot.leader" }));
            Assert.That(expanded[0].Template, Is.EqualTo("leader"));
            Assert.That(expanded[0].Overrides!["WorldPositionCm"]!["Value"]!["X"]!.GetValue<int>(), Is.EqualTo(115));
            Assert.That(expanded[0].Overrides!["WorldPositionCm"]!["Value"]!["Y"]!.GetValue<int>(), Is.EqualTo(215));
        }

        [Test]
        public void Expand_TemplateComponentOverride_DeepMergesFieldsOntoRegistryNode()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      {
                        "localId": "unit",
                        "template": "tower",
                        "componentOverrides": { "Health": { "Value": { "X": 9 } } }
                      }
                    ]
                  }
                ]
                """);

            EntityGroupPlacement.Validate(registries.Groups, registries.Templates);

            var map = new MapConfig { Id = "map.merge" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Group = "g" });

            List<EntitySpawnData> expanded = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);

            JsonNode health = expanded[0].Overrides!["Health"]!;
            Assert.That(health["Value"]!["X"]!.GetValue<int>(), Is.EqualTo(9));
            Assert.That(health["Value"]!["Y"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(health["Extra"]!.GetValue<bool>(), Is.True);

            health["Value"]!["X"] = 99;
            health["Extra"] = false;
            JsonNode templateHealth = registries.Templates.Get("tower")!.Components["Health"]!;
            Assert.That(templateHealth["Value"]!["X"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(templateHealth["Extra"]!.GetValue<bool>(), Is.True);
            Assert.That(
                registries.Groups.Get("g")!.Slots![0].ComponentOverrides!["Health"]!["Value"]!["X"]!.GetValue<int>(),
                Is.EqualTo(9));
        }

        [Test]
        public void Expand_ComponentOverrideForAbsentKey_ClonesSlotNodeWithoutAliasingRegistry()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      {
                        "localId": "unit",
                        "template": "tower",
                        "componentOverrides": { "Mana": { "Current": 5 } }
                      }
                    ]
                  }
                ]
                """);

            EntityGroupPlacement.Validate(registries.Groups, registries.Templates);

            var map = new MapConfig { Id = "map.absent.key" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Group = "g" });

            List<EntitySpawnData> expanded = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);

            JsonNode mana = expanded[0].Overrides!["Mana"]!;
            Assert.That(mana["Current"]!.GetValue<int>(), Is.EqualTo(5));

            mana["Current"] = 7;
            Assert.That(
                registries.Groups.Get("g")!.Slots![0].ComponentOverrides!["Mana"]!["Current"]!.GetValue<int>(),
                Is.EqualTo(5));
        }

        [Test]
        public void Expand_RepeatedInvocations_ProduceIdenticalInstanceIdSequence()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      { "localId": "tower", "template": "tower", "localPose": { "offsetXCm": 1, "offsetYCm": 2 } },
                      { "localId": "gate", "template": "gate" }
                    ]
                  }
                ]
                """);

            var map = new MapConfig { Id = "map.determinism" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "solo", Template = "leader" });
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Group = "g", PositionXCm = 7, PositionYCm = 8 });

            List<EntitySpawnData> first = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);
            List<EntitySpawnData> second = EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates);

            Assert.That(second.Select(e => e.InstanceId), Is.EqualTo(first.Select(e => e.InstanceId)));
            Assert.That(
                second[1].Overrides!["WorldPositionCm"]!["Value"]!.ToJsonString(),
                Is.EqualTo(first[1].Overrides!["WorldPositionCm"]!["Value"]!.ToJsonString()));
        }

        [Test]
        public void Validate_RepeatedLocalId_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      { "localId": "dup", "template": "tower" },
                      { "localId": "dup", "template": "gate" }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("localId 'dup' 在同一组内重复"));
            Assert.That(ex.Message, Does.Contain("Entity group 'g' slots[1]"));
        }

        [Test]
        public void Validate_SlotWithoutTemplateAndGroup_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [ { "id": "g", "slots": [ { "localId": "empty" } ] } ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("必须恰好声明 template 或 group 之一"));
            Assert.That(ex.Message, Does.Contain("localId 'empty'"));
        }

        [Test]
        public void Validate_SlotWithBothTemplateAndGroup_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [ { "id": "g", "slots": [ { "localId": "both", "template": "tower", "group": "g" } ] } ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("必须恰好声明 template 或 group 之一"));
            Assert.That(ex.Message, Does.Contain("template='tower'"));
        }

        [Test]
        public void Validate_UnknownTemplateReference_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [ { "id": "g", "slots": [ { "localId": "ghost", "template": "missing.template" } ] } ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("引用未知实体模板 'missing.template'"));
        }

        [Test]
        public void Validate_UnknownGroupReference_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [ { "id": "g", "slots": [ { "localId": "ghost", "group": "missing.group" } ] } ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("引用未知实体组 'missing.group'"));
        }

        [Test]
        public void Validate_NestedGroupSlotWithComponentOverrides_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "outer",
                    "slots": [
                      {
                        "localId": "nested",
                        "group": "inner",
                        "componentOverrides": { "Health": { "Value": { "X": 3 } } }
                      }
                    ]
                  },
                  { "id": "inner", "slots": [ { "localId": "leaf", "template": "tower" } ] }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("嵌套组槽位不能声明 componentOverrides"));
            Assert.That(ex.Message, Does.Contain("Entity group 'outer' slots[0]"));
        }

        [Test]
        public void Validate_SlotWithInheritParentFacing_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      {
                        "localId": "facing",
                        "template": "tower",
                        "localPose": { "inheritParentFacing": true }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("localPose.inheritParentFacing"));
        }

        [Test]
        public void Validate_SlotWithOffsetRotation_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [
                      {
                        "localId": "rotated",
                        "template": "tower",
                        "localPose": { "offsetRotation": "yaw:45" }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("localPose.offsetRotation"));
        }

        [Test]
        public void Validate_GroupReferenceCycle_Throws()
        {
            var registries = LoadRegistries(
                TemplatesJson,
                """
                [
                  { "id": "a", "slots": [ { "localId": "to.b", "group": "b" } ] },
                  { "id": "b", "slots": [ { "localId": "to.a", "group": "a" } ] }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Validate(registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("Entity group 引用图存在环"));
        }

        [Test]
        public void Expand_GroupEntryWithTemplate_Throws()
        {
            var registries = LoadGroupOnly();
            var map = new MapConfig { Id = "map.bad.template" };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "camp",
                Group = "g",
                Template = "tower",
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("组摆放不能同时声明 template"));
            Assert.That(ex.Message, Does.Contain("Map 'map.bad.template' entity 'camp'"));
        }

        [Test]
        public void Expand_GroupEntryWithoutInstanceId_Throws()
        {
            var registries = LoadGroupOnly();
            var map = new MapConfig { Id = "map.bad.instance" };
            map.Entities.Add(new EntitySpawnData { Group = "g" });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("必须显式声明首尾无空白的 instanceId"));
        }

        [Test]
        public void Expand_GroupEntryWithOverrides_Throws()
        {
            var registries = LoadGroupOnly();
            var map = new MapConfig { Id = "map.bad.overrides" };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "camp",
                Group = "g",
                Overrides = new Dictionary<string, JsonNode>
                {
                    ["Health"] = new JsonObject { ["Value"] = new JsonObject { ["X"] = 1 } },
                },
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("组摆放不能声明 overrides"));
        }

        [Test]
        public void Expand_GroupEntryWithPartialAnchor_Throws()
        {
            var registries = LoadGroupOnly();
            var map = new MapConfig { Id = "map.bad.anchor" };
            map.Entities.Add(new EntitySpawnData
            {
                InstanceId = "camp",
                Group = "g",
                PositionXCm = 5,
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("锚点 positionXCm/positionYCm 必须同时声明或同时省略"));
        }

        [Test]
        public void Expand_UnknownGroupId_Throws()
        {
            var registries = LoadGroupOnly();
            var map = new MapConfig { Id = "map.bad.group" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "camp", Group = "missing.group" });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityGroupPlacement.Expand(map, registries.Groups, registries.Templates))!;
            Assert.That(ex.Message, Does.Contain("引用未知实体组模板 'missing.group'"));
            Assert.That(ex.Message, Does.Contain("Map 'map.bad.group' entity 'camp'"));
        }

        private static LoadedRegistries LoadGroupOnly()
        {
            return LoadRegistries(
                TemplatesJson,
                """
                [
                  {
                    "id": "g",
                    "slots": [ { "localId": "tower", "template": "tower" } ]
                  }
                ]
                """);
        }

        private static LoadedRegistries LoadRegistries(string templatesJson, string groupsJson)
        {
            string root = CreateTempDir();
            try
            {
                string entitiesDir = Path.Combine(root, "Entities");
                Directory.CreateDirectory(entitiesDir);
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    """
                    [
                      { "Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id" },
                      { "Path": "Entities/groups.json", "Policy": "ArrayById", "IdField": "id" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(entitiesDir, "templates.json"), templatesJson);
                File.WriteAllText(Path.Combine(entitiesDir, "groups.json"), groupsJson);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var templates = new DataRegistry<EntityTemplate>(pipeline);
                templates.Load("Entities/templates.json", catalog);
                var groups = new DataRegistry<EntityGroupTemplate>(pipeline);
                groups.Load("Entities/groups.json", catalog);
                return new LoadedRegistries(templates, groups);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string CreateTempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "ludots_entitygroup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        private sealed record LoadedRegistries(
            DataRegistry<EntityTemplate> Templates,
            DataRegistry<EntityGroupTemplate> Groups);
    }
}
