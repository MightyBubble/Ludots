using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests
{
    [TestFixture]
    public sealed class EntityTemplateInheritanceTests
    {
        [Test]
        public void ExpandAll_MergesParentComponents_FieldLevel()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["grunt"] = Template("grunt", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 }, ""current"": { ""Health"": 80 } }
                }"),
                ["hero"] = Template("hero", extends: "grunt", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 250 } }
                }"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["hero"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(250), "子代字段胜出");
            That(buffer["current"]!["Health"]!.GetValue<int>(), Is.EqualTo(80), "未提及字段继承父代");
        }

        [Test]
        public void ExpandAll_InheritsScalarHooks_WhenChildSilent()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["grunt"] = Template("grunt", components: "{}", onSpawnEffect: "Effect.GruntIncome",
                    initialInteractionContext: "ctx.grunt"),
                ["hero"] = Template("hero", extends: "grunt", components: "{}"),
                ["elite"] = Template("elite", extends: "grunt", components: "{}",
                    onSpawnEffect: "Effect.EliteIncome", initialInteractionContext: "ctx.elite"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            That(templates["hero"].OnSpawnEffect, Is.EqualTo("Effect.GruntIncome"));
            That(templates["hero"].InitialInteractionContext, Is.EqualTo("ctx.grunt"));
            That(templates["elite"].OnSpawnEffect, Is.EqualTo("Effect.EliteIncome"), "子代非空才覆盖");
            That(templates["elite"].InitialInteractionContext, Is.EqualTo("ctx.elite"));
        }

        [Test]
        public void ExpandAll_AppendsChildren_ParentFirst_ClonesParentEntries()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["tank"] = Template("tank", components: "{}", children: new List<EntityTemplateChild>
                {
                    new() { Template = "chassis" },
                }),
                ["hero_tank"] = Template("hero_tank", extends: "tank", components: "{}", children: new List<EntityTemplateChild>
                {
                    new() { Template = "flag_bearer" },
                }),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var children = templates["hero_tank"].Children!;
            That(children.Select(c => c.Template).ToArray(), Is.EqualTo(new[] { "chassis", "flag_bearer" }));
            That(children[0], Is.Not.SameAs(templates["tank"].Children![0]), "父代 child 条目克隆，不共享可变实例");
            That(templates["tank"].Children!.Select(c => c.Template).ToArray(), Is.EqualTo(new[] { "chassis" }), "父代自身不被改动");
        }

        [Test]
        public void ExpandAll_InheritedChildren_PreserveLocalIdAttachAndNestedChildren()
        {
            var inherited = new EntityTemplateChild
            {
                LocalId = "turret",
                Template = "turret",
                Attach = false,
                Children = new List<EntityTemplateChild> { new() { LocalId = "barrel", Template = "barrel" } },
            };
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["tank"] = Template("tank", components: "{}", children: new List<EntityTemplateChild> { inherited }),
                ["hero_tank"] = Template("hero_tank", extends: "tank", components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            EntityTemplateChild clone = templates["hero_tank"].Children![0];
            That(clone.LocalId, Is.EqualTo("turret"), "克隆保留 localId");
            That(clone.Attach, Is.EqualTo(false), "克隆保留 attach 语义标记");
            That(clone.Children![0].LocalId, Is.EqualTo("barrel"), "克隆递归保留嵌套 children");
            That(inherited.Children![0], Is.Not.SameAs(clone.Children![0]), "嵌套条目同样不共享可变实例");
        }

        [Test]
        public void ExpandAll_AppendsTriggerGraphs_DeduplicatesExactDuplicates()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["grunt"] = Template("grunt", components: "{}", triggerGraphs: new List<string> { "graph.shared", "graph.grunt" }),
                ["hero"] = Template("hero", extends: "grunt", components: "{}", triggerGraphs: new List<string> { "graph.shared", "graph.hero" }),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            That(templates["hero"].TriggerGraphs!.ToArray(), Is.EqualTo(new[] { "graph.shared", "graph.grunt", "graph.hero" }),
                "同图双挂会让实体域触发反应翻倍，不是合法组合");
        }

        [Test]
        public void ExpandAll_ExpandsGrandparentChain_FirstAncestorWinsForUnmentioned()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["base_unit"] = Template("base_unit", components: @"{
                    ""Team"": { ""Id"": 1 }, ""AttributeBuffer"": { ""base"": { ""Health"": 100 } }
                }"),
                ["veteran"] = Template("veteran", extends: "base_unit", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 150 } }
                }"),
                ["hero"] = Template("hero", extends: "veteran", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Mana"": 40 } }
                }"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["hero"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(150), "最近父代胜出");
            That(buffer["base"]!["Mana"]!.GetValue<int>(), Is.EqualTo(40));
            That(templates["hero"].Components["Team"]!["Id"]!.GetValue<int>(), Is.EqualTo(1), "祖父代组件继承");
        }

        [Test]
        public void ExpandAll_UnknownParent_ThrowsWithBothIds()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["hero"] = Template("hero", extends: "missing_parent", components: "{}"),
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(
                () => EntityTemplateInheritance.ExpandAll(templates))!;

            That(ex.Message, Does.Contain("hero"));
            That(ex.Message, Does.Contain("missing_parent"));
        }

        [TestCase("self")]
        [TestCase("mutual")]
        public void ExpandAll_InheritanceCycle_Throws(string shape)
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["self"] = Template("self", extends: "self", components: "{}"),
                ["a"] = Template("a", extends: "b", components: "{}"),
                ["b"] = Template("b", extends: "a", components: "{}"),
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(
                () => EntityTemplateInheritance.ExpandAll(templates))!;

            That(ex.Message, Does.Contain("cycle"));
        }

        [Test]
        public void ExpandAll_ReplaceMarkerInChildComponent_ReplacesWholeComponent()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["zone"] = Template("zone", components: @"{
                    ""RegionVolumeCm"": { ""volumeKey"": ""ring"", ""shape"": ""circle"", ""radiusCm"": 50 }
                }"),
                ["yard"] = Template("yard", extends: "zone", components: @"{
                    ""RegionVolumeCm"": { ""__replace"": true, ""volumeKey"": ""yard"", ""shape"": ""rect"", ""halfWidthCm"": 50, ""halfHeightCm"": 40 }
                }"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var volume = templates["yard"].Components["RegionVolumeCm"].AsObject();
            That(volume.ContainsKey("__replace"), Is.False, "标记在展开时剥离");
            That(volume.ContainsKey("radiusCm"), Is.False, "变体形状组件整替，圆的 radiusCm 不得残留成嵌合体");
            That(volume["shape"]!.GetValue<string>(), Is.EqualTo("rect"));
            That(volume["volumeKey"]!.GetValue<string>(), Is.EqualTo("yard"));
        }

        [Test]
        public void ExpandAll_ConsumesExtendsField_SecondPassIsNoOp()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["grunt"] = Template("grunt", components: @"{ ""Team"": { ""Id"": 1 } }"),
                ["hero"] = Template("hero", extends: "grunt", components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);
            var firstPass = templates["hero"].Components["Team"]!.DeepClone();
            That(templates["hero"].Extends, Is.Null, "展开后字段清空，物化只消费展开结果");

            EntityTemplateInheritance.ExpandAll(templates);

            That(JsonNode.DeepEquals(templates["hero"].Components["Team"], firstPass), Is.True, "二次展开幂等");
        }

        [Test]
        public void LoadTemplates_ExtendsTemplate_SpawnSeesMergedComponents()
        {
            using var world = World.Create();
            var loader = CreateLoader(world, templatesJson: """
                [
                  {
                    "id": "test.tpl.grunt",
                    "components": {
                      "Name": { "Value": "Template:InheritanceGrunt" },
                      "WorldPositionCm": { "Value": { "X": 10, "Y": 20 } },
                      "FacingDirection": { "AngleRad": 0.5 },
                      "AttributeBuffer": { "base": {} },
                      "Team": { "Id": 1 }
                    }
                  },
                  {
                    "id": "test.tpl.hero",
                    "extends": "test.tpl.grunt",
                    "components": {
                      "Team": { "Id": 9 },
                      "AbilityTagGrantReceiver": {}
                    }
                  }
                ]
                """);
            var map = new MapConfig { Id = "tpl_inheritance_map" };
            map.Entities.Add(new EntitySpawnData { Template = "test.tpl.grunt" });
            map.Entities.Add(new EntitySpawnData { Template = "test.tpl.hero" });

            loader.LoadEntities(map);

            Entity hero = FindByTemplateMarker(world, hasAbilityTagGrantReceiver: true);
            Entity grunt = FindByTemplateMarker(world, hasAbilityTagGrantReceiver: false);
            That(world.Get<Team>(hero).Id, Is.EqualTo(9), "子代 Team 覆盖父代");
            That(world.Get<Name>(hero).Value, Is.EqualTo("Template:InheritanceGrunt"), "父代 Name 继承");
            That(world.Has<WorldPositionCm>(hero) && world.Has<FacingDirection>(hero), Is.True, "父代组件继承");
            That(world.Get<Team>(grunt).Id, Is.EqualTo(1), "父代模板不受展开影响");
        }

        [Test]
        public void LoadEntities_PartialInstanceOverride_InheritsUnmentionedTemplateFields()
        {
            using var world = World.Create();
            var loader = CreateLoader(world, templatesJson: """
                [
                  {
                    "id": "test.tpl.partial.unit",
                    "components": {
                      "Name": { "Value": "Template:PartialUnit" },
                      "WorldPositionCm": { "Value": { "X": 10, "Y": 20 } },
                      "FacingDirection": { "AngleRad": 0.5 },
                      "AttributeBuffer": { "base": {} }
                    }
                  }
                ]
                """);
            var map = new MapConfig { Id = "tpl_partial_override_map" };
            map.Entities.Add(new EntitySpawnData
            {
                Template = "test.tpl.partial.unit",
                Overrides = new Dictionary<string, JsonNode>
                {
                    // Team 覆盖把该实例踢出批量快速路径，走 EntityBuilder 单实体物化
                    ["Team"] = JsonNode.Parse(@"{ ""Id"": 2 }")!,
                    ["WorldPositionCm"] = JsonNode.Parse(@"{ ""Value"": { ""X"": 900 } }")!,
                },
            });

            loader.LoadEntities(map);

            Entity entity = FindByTemplateMarker(world, hasAbilityTagGrantReceiver: false);
            That(world.Get<WorldPositionCm>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(900, 20)),
                "覆盖只写了 X=900，未提及的 Y 继承模板值，不再吃默认 0");
            That(world.Get<Team>(entity).Id, Is.EqualTo(2));
            That(world.Get<Name>(entity).Value, Is.EqualTo("Template:PartialUnit"), "未覆盖组件保持模板值");
        }

        private static Entity FindByTemplateMarker(World world, bool hasAbilityTagGrantReceiver)
        {
            Entity found = default;
            int matches = 0;
            var query = new QueryDescription().WithAll<Name, Team>();
            world.Query(in query, (Entity entity) =>
            {
                if (world.Has<Ludots.Core.Gameplay.GAS.Components.AbilityTagGrantReceiver>(entity) == hasAbilityTagGrantReceiver)
                {
                    found = entity;
                    matches++;
                }
            });

            That(matches, Is.EqualTo(1), $"期望唯一匹配实体，实际 {matches}");
            return found;
        }

        private static EntityTemplate Template(
            string id,
            string? extends = null,
            string components = "{}",
            string? onSpawnEffect = null,
            string? initialInteractionContext = null,
            List<string>? triggerGraphs = null,
            List<EntityTemplateChild>? children = null)
        {
            return new EntityTemplate
            {
                Id = id,
                Extends = extends,
                OnSpawnEffect = onSpawnEffect,
                InitialInteractionContext = initialInteractionContext,
                Components = new Dictionary<string, JsonNode>(
                    (JsonNode.Parse(components)!.AsObject()).ToDictionary(k => k.Key, k => k.Value!.DeepClone()),
                    StringComparer.Ordinal),
                TriggerGraphs = triggerGraphs,
                Children = children,
            };
        }

        private static MapLoader CreateLoader(World world, string templatesJson)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_EntityTemplateInheritanceTests", Guid.NewGuid().ToString("N"));
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
