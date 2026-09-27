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
    public sealed class EntityTemplateUsesTests
    {
        [Test]
        public void ExpandAll_LaterDeclaredBlockWins_OverridesEarlierBlockField()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 } }
                }"),
                ["block.b"] = Template("block.b", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 220 } }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.a", "block.b" }, components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["elite"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(220),
                "声明越靠后优先级越高；逐个 merge 进 template 的实现会让先声明的 block.a 恒胜");
        }

        [Test]
        public void ExpandAll_SelfComponents_AlwaysHighestOverBlocks()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 999 } },
                    ""Team"": { ""Id"": 1 }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.a" }, components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 220 } }
                }"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["elite"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(220), "自身 components 永远最高");
            That(templates["elite"].Components["Team"]!["Id"]!.GetValue<int>(), Is.EqualTo(1), "块独有的组件继承");
        }

        [Test]
        public void ExpandAll_BlockConflictMerges_FieldLevel_UnmentionedFieldsInherit()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 }, ""current"": { ""Health"": 80 } }
                }"),
                ["block.b"] = Template("block.b", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 220 } }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.a", "block.b" }, components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["elite"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(220), "后声明块覆盖改动字段");
            That(buffer["current"]!["Health"]!.GetValue<int>(), Is.EqualTo(80), "未提及字段留底继承前一个源");
        }

        [Test]
        public void ExpandAll_ExtendsBaseThenUsesThenSelf_LayeredPriority()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["unit"] = Template("unit", components: @"{
                    ""Team"": { ""Id"": 1 },
                    ""AttributeBuffer"": { ""base"": { ""Health"": 50, ""Mana"": 40 } }
                }"),
                ["block.elite"] = Template("block.elite", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 150 } }
                }"),
                ["hero"] = Template("hero",
                    extends: "unit", uses: new List<string> { "block.elite" }, components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 300 } }
                }"),
                ["champion"] = Template("champion",
                    extends: "unit", uses: new List<string> { "block.elite" }, components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var heroBuffer = templates["hero"].Components["AttributeBuffer"].AsObject();
            That(heroBuffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(300), "自身最高");
            That(heroBuffer["base"]!["Mana"]!.GetValue<int>(), Is.EqualTo(40), "extends 父打底字段继承");
            var championBuffer = templates["champion"].Components["AttributeBuffer"].AsObject();
            That(championBuffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(150),
                "自身静默时 uses 块覆盖 extends 父打底值");
            That(templates["champion"].Components["Team"]!["Id"]!.GetValue<int>(), Is.EqualTo(1));
        }

        [Test]
        public void ExpandAll_ScalarHooks_LaterBlockNonEmptyWins_SelfHighest()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: "{}",
                    onSpawnEffect: "Effect.A", initialInteractionContext: "ctx.a"),
                ["block.b"] = Template("block.b", components: "{}",
                    onSpawnEffect: "Effect.B", initialInteractionContext: "ctx.b"),
                ["recruit"] = Template("recruit", uses: new List<string> { "block.a", "block.b" }, components: "{}"),
                ["hero"] = Template("hero", uses: new List<string> { "block.a", "block.b" }, components: "{}",
                    onSpawnEffect: "Effect.Hero", initialInteractionContext: "ctx.hero"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            That(templates["recruit"].OnSpawnEffect, Is.EqualTo("Effect.B"), "后声明块非空覆盖");
            That(templates["recruit"].InitialInteractionContext, Is.EqualTo("ctx.b"));
            That(templates["hero"].OnSpawnEffect, Is.EqualTo("Effect.Hero"), "自身非空最高");
            That(templates["hero"].InitialInteractionContext, Is.EqualTo("ctx.hero"));
        }

        [Test]
        public void ExpandAll_BlockWithOwnExtends_ExpandsChainBeforeFolding()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.base_health"] = Template("block.base_health", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 } },
                    ""Team"": { ""Id"": 1 }
                }"),
                ["block.tough"] = Template("block.tough", extends: "block.base_health", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 200 } }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.tough" }, components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var buffer = templates["elite"].Components["AttributeBuffer"].AsObject();
            That(buffer["base"]!["Health"]!.GetValue<int>(), Is.EqualTo(200), "块先展开自己的继承链再参与折叠");
            That(templates["elite"].Components["Team"]!["Id"]!.GetValue<int>(), Is.EqualTo(1), "块的父代组件随链继承");
        }

        [Test]
        public void ExpandAll_AppendsChildrenAndTriggerGraphs_AcrossBlocks_DeduplicatesGraphs()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: "{}",
                    triggerGraphs: new List<string> { "graph.shared", "graph.a" },
                    children: new List<EntityTemplateChild> { new() { Template = "chassis" } }),
                ["block.b"] = Template("block.b", components: "{}",
                    triggerGraphs: new List<string> { "graph.shared", "graph.b" },
                    children: new List<EntityTemplateChild> { new() { Template = "flag_bearer" } }),
                ["elite"] = Template("elite", uses: new List<string> { "block.a", "block.b" }, components: "{}",
                    triggerGraphs: new List<string> { "graph.elite" },
                    children: new List<EntityTemplateChild> { new() { Template = "rider" } }),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            That(templates["elite"].TriggerGraphs!.ToArray(),
                Is.EqualTo(new[] { "graph.shared", "graph.a", "graph.b", "graph.elite" }),
                "跨块追加且精确去重，同图双挂不是合法组合");
            var children = templates["elite"].Children!;
            That(children.Select(c => c.Template).ToArray(), Is.EqualTo(new[] { "chassis", "flag_bearer", "rider" }));
            That(children[0], Is.Not.SameAs(templates["block.a"].Children![0]), "块 child 条目克隆，不共享可变实例");
        }

        [Test]
        public void ExpandAll_ReplaceMarkerInLaterBlock_ReplacesWholeComponent()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.zone"] = Template("block.zone", components: @"{
                    ""RegionVolumeCm"": { ""volumeKey"": ""ring"", ""shape"": ""circle"", ""radiusCm"": 50 }
                }"),
                ["block.yard"] = Template("block.yard", components: @"{
                    ""RegionVolumeCm"": { ""__replace"": true, ""volumeKey"": ""yard"", ""shape"": ""rect"", ""halfWidthCm"": 50, ""halfHeightCm"": 40 }
                }"),
                ["yard_user"] = Template("yard_user",
                    uses: new List<string> { "block.zone", "block.yard" }, components: "{}"),
            };

            EntityTemplateInheritance.ExpandAll(templates);

            var volume = templates["yard_user"].Components["RegionVolumeCm"].AsObject();
            That(volume.ContainsKey("__replace"), Is.False, "标记在展开时剥离");
            That(volume.ContainsKey("radiusCm"), Is.False, "变体形状组件整替，圆的 radiusCm 不得残留成嵌合体");
            That(volume["shape"]!.GetValue<string>(), Is.EqualTo("rect"));
            That(volume["volumeKey"]!.GetValue<string>(), Is.EqualTo("yard"));
        }

        [Test]
        public void ExpandAll_UnknownBlock_ThrowsWithBothIds()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["hero"] = Template("hero", uses: new List<string> { "missing_block" }, components: "{}"),
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(
                () => EntityTemplateInheritance.ExpandAll(templates))!;

            That(ex.Message, Does.Contain("hero"));
            That(ex.Message, Does.Contain("missing_block"));
        }

        [TestCase("self_uses")]
        [TestCase("mutual_uses")]
        [TestCase("extends_uses_mixed")]
        public void ExpandAll_UsesCycle_Throws(string shape)
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["self_uses"] = Template("self_uses", uses: new List<string> { "self_uses" }, components: "{}"),
                ["x"] = Template("x", uses: new List<string> { "y" }, components: "{}"),
                ["y"] = Template("y", uses: new List<string> { "x" }, components: "{}"),
                ["a"] = Template("a", extends: "b", components: "{}"),
                ["b"] = Template("b", uses: new List<string> { "a" }, components: "{}"),
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
            {
                if (shape == "self_uses")
                {
                    EntityTemplateInheritance.ExpandAll(new Dictionary<string, EntityTemplate>
                    {
                        ["self_uses"] = templates["self_uses"],
                    });
                }
                else if (shape == "mutual_uses")
                {
                    EntityTemplateInheritance.ExpandAll(new Dictionary<string, EntityTemplate>
                    {
                        ["x"] = templates["x"],
                        ["y"] = templates["y"],
                    });
                }
                else
                {
                    EntityTemplateInheritance.ExpandAll(new Dictionary<string, EntityTemplate>
                    {
                        ["a"] = templates["a"],
                        ["b"] = templates["b"],
                    });
                }
            })!;

            That(ex.Message, Does.Contain("cycle"));
        }

        [Test]
        public void ExpandAll_ConsumesUsesField_BlocksUntouched_SecondPassIsNoOp()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 } }
                }"),
                ["block.b"] = Template("block.b", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 220 } }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.a", "block.b" }, components: "{}"),
            };
            JsonNode blockAComponents = templates["block.a"].Components["AttributeBuffer"].DeepClone();
            JsonNode blockBComponents = templates["block.b"].Components["AttributeBuffer"].DeepClone();

            EntityTemplateInheritance.ExpandAll(templates);
            var firstPass = templates["elite"].Components["AttributeBuffer"].DeepClone();
            That(templates["elite"].Uses, Is.Null, "展开后 uses 清空，物化只消费展开结果");

            That(JsonNode.DeepEquals(templates["block.a"].Components["AttributeBuffer"], blockAComponents),
                Is.True, "被引用的块不受折叠污染，块自己 spawn 的实体不受使用者影响");
            That(JsonNode.DeepEquals(templates["block.b"].Components["AttributeBuffer"], blockBComponents), Is.True);
            That(templates["block.a"].Uses, Is.Null);
            That(templates["elite"].Components["AttributeBuffer"], Is.Not.SameAs(templates["block.a"].Components["AttributeBuffer"]));

            EntityTemplateInheritance.ExpandAll(templates);

            That(JsonNode.DeepEquals(templates["elite"].Components["AttributeBuffer"], firstPass), Is.True, "二次展开幂等");
            That(JsonNode.DeepEquals(templates["block.a"].Components["AttributeBuffer"], blockAComponents), Is.True);
        }

        [Test]
        public void ExpandAll_RecordsComponentOverrideChains_InConflictReport()
        {
            var templates = new Dictionary<string, EntityTemplate>
            {
                ["block.a"] = Template("block.a", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 100 } },
                    ""Team"": { ""Id"": 1 }
                }"),
                ["block.b"] = Template("block.b", components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 220 } }
                }"),
                ["elite"] = Template("elite", uses: new List<string> { "block.a", "block.b" }, components: @"{
                    ""AttributeBuffer"": { ""base"": { ""Health"": 300 } }
                }"),
            };
            var report = new ConfigConflictReport();

            EntityTemplateInheritance.ExpandAll(templates, report);

            var chains = report.GetComponentOverrideChains("elite");
            var bufferChain = chains.Single(c => c.Component == "AttributeBuffer");
            That(bufferChain.WriterChain, Is.EqualTo("block.a -> block.b -> self"),
                "覆盖链按声明顺序记录，自身记作 self");
            That(chains.Any(c => c.Component == "Team"), Is.False, "单一写入者不是冲突，不记录");
            That(report.GetComponentOverrideChains("block.a"), Is.Empty, "块自身无 uses 折叠，无记录");
        }

        [Test]
        public void LoadTemplates_UsesBlocks_SpawnSeesFoldedComponents()
        {
            using var world = World.Create();
            var loader = CreateLoader(world, templatesJson: """
                [
                  {
                    "id": "test.block.mortal",
                    "components": {
                      "Name": { "Value": "BlockMortal" },
                      "WorldPositionCm": { "Value": { "X": 10, "Y": 20 } },
                      "FacingDirection": { "AngleRad": 0.5 },
                      "AttributeBuffer": { "base": {} }
                    }
                  },
                  {
                    "id": "test.block.selectable",
                    "components": { "Team": { "Id": 2 } }
                  },
                  {
                    "id": "test.tpl.archer",
                    "uses": [ "test.block.mortal", "test.block.selectable" ],
                    "components": {
                      "Team": { "Id": 9 },
                      "AbilityTagGrantReceiver": {}
                    }
                  },
                  {
                    "id": "test.tpl.recruit",
                    "uses": [ "test.block.mortal", "test.block.selectable" ],
                    "components": { "AbilityTagGrantReceiver": {} }
                  }
                ]
                """);
            var map = new MapConfig { Id = "tpl_uses_map" };
            map.Entities.Add(new EntitySpawnData { Template = "test.block.mortal" });
            map.Entities.Add(new EntitySpawnData { Template = "test.tpl.recruit" });
            map.Entities.Add(new EntitySpawnData { Template = "test.tpl.archer" });

            loader.LoadEntities(map);

            Entity archer = FindByTeam(world, 9);
            Entity recruit = FindByTeam(world, 2);
            That(world.Get<Name>(archer).Value, Is.EqualTo("BlockMortal"), "块组件继承");
            That(world.Has<WorldPositionCm>(archer) && world.Has<FacingDirection>(archer), Is.True, "块组件继承");
            That(world.Get<Team>(recruit).Id, Is.EqualTo(2),
                "自身静默时块值直接生效——这正是与'逐块 merge 进 template'错误实现的分水岭");
            That(world.Get<Name>(recruit).Value, Is.EqualTo("BlockMortal"));
            Entity mortal = FindWithoutTeam(world);
            That(world.Get<Name>(mortal).Value, Is.EqualTo("BlockMortal"), "块模板直接布阵仍可用");
            That(world.Has<Team>(mortal), Is.False, "块不受使用者折叠污染，selectable 的 Team 不回流进 mortal");
        }

        private static Entity FindByTeam(World world, int teamId)
        {
            Entity found = default;
            int matches = 0;
            var query = new QueryDescription().WithAll<Name, Team>();
            world.Query(in query, (Entity entity) =>
            {
                if (world.Get<Team>(entity).Id == teamId)
                {
                    found = entity;
                    matches++;
                }
            });

            That(matches, Is.EqualTo(1), $"期望唯一 Team={teamId} 实体，实际 {matches}");
            return found;
        }

        private static Entity FindWithoutTeam(World world)
        {
            Entity found = default;
            int matches = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity) =>
            {
                if (!world.Has<Team>(entity))
                {
                    found = entity;
                    matches++;
                }
            });

            That(matches, Is.EqualTo(1), $"期望唯一无 Team 实体，实际 {matches}");
            return found;
        }

        private static EntityTemplate Template(
            string id,
            string? extends = null,
            List<string>? uses = null,
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
                Uses = uses,
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
            string root = Path.Combine(Path.GetTempPath(), "Ludots_EntityTemplateUsesTests", Guid.NewGuid().ToString("N"));
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
