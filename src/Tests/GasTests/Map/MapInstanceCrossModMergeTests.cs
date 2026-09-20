using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Hosting;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1554：跨 mod 地图实例合并（by-instanceId 深合并 + __delete 墓碑）与实例 relations
    /// 装载站物化（InstanceRelationMaterializer）的合同锁定。事件端（Relation* 键注册与
    /// 变更缓冲 Kind）一并断言；trigger 图执行端到端归 showcase 验收。
    /// </summary>
    [TestFixture]
    public sealed class MapInstanceCrossModMergeTests
    {
        private static void WriteMod(string root, string modId, string mapJson)
        {
            string modDir = Path.Combine(root, modId);
            Directory.CreateDirectory(Path.Combine(modDir, "assets", "Maps"));
            File.WriteAllText(Path.Combine(modDir, "mod.json"), $$"""
            {
              "name": "{{modId}}",
              "version": "1.0.0"
            }
            """);
            File.WriteAllText(Path.Combine(modDir, "assets", "Maps", "harbor.json"), mapJson);
        }

        private static MapManager CreateManager(string root, params string[] modIds)
        {
            var vfs = new VirtualFileSystem();
            foreach (string modId in modIds)
            {
                vfs.Mount(modId, Path.Combine(root, modId));
            }

            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            modLoader.LoadResolvedPlan(modIds
                .Select(id => new ResolvedModLoadEntry(id, Path.Combine(root, id)))
                .ToList());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return new MapManager(vfs, trigger, modLoader, pipeline);
        }

        private const string BaseMap = """
        {
          "id": "harbor",
          "entities": [
            { "instanceId": "harbor.zhangsan", "template": "gang.member",
              "overrides": { "AttributeTable": { "Hp": 100, "Atk": 10 } },
              "relations": [
                { "to": "harbor.wangwu", "type": "WorksFor", "metric": { "Loyalty": 80 } }
              ] },
            { "instanceId": "harbor.lisi", "template": "gang.member",
              "overrides": { "AttributeTable": { "Hp": 90 } } },
            { "instanceId": "harbor.wangwu", "template": "gang.member",
              "positionXCm": 1600, "positionYCm": -400 }
          ]
        }
        """;

        [Test]
        public void CrossMod_SameInstanceId_DeepMergesInsteadOfAppending()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModB", """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.zhangsan",
                      "overrides": { "AttributeTable": { "Hp": 200 } } },
                    { "instanceId": "harbor.wangwu",
                      "positionXCm": 2000, "positionYCm": 0 }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModB");
                MapConfig merged = manager.LoadMap("harbor")!;

                Assert.That(merged.Entities.Count, Is.EqualTo(3), "同 instanceId 深合并，不得追加成第 4 个");

                EntitySpawnData zhangsan = merged.Entities.Single(e => e.InstanceId == "harbor.zhangsan");
                Assert.That(zhangsan.Overrides!["AttributeTable"]!["Hp"]!.GetValue<int>(), Is.EqualTo(200), "后写赢");
                Assert.That(zhangsan.Overrides!["AttributeTable"]!["Atk"]!.GetValue<int>(), Is.EqualTo(10), "未写字段保留");
                Assert.That(zhangsan.Relations!.Count, Is.EqualTo(1), "未写 relations 保留");

                EntitySpawnData wangwu = merged.Entities.Single(e => e.InstanceId == "harbor.wangwu");
                Assert.That(wangwu.PositionXCm, Is.EqualTo(2000));
                Assert.That(wangwu.PositionYCm, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_Tombstone_RemovesInstanceAndLaterFragmentRevives()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModC", """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.lisi", "__delete": true }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModC");
                MapConfig merged = manager.LoadMap("harbor")!;

                Assert.That(merged.Entities.Count, Is.EqualTo(2), "墓碑删除李四");
                Assert.That(merged.Entities.Any(e => e.InstanceId == "harbor.lisi"), Is.False);
                Assert.That(manager.LastMergeReport.Deletions.Count, Is.EqualTo(1));
                Assert.That(manager.LastMergeReport.Deletions[0].InstanceId, Is.EqualTo("harbor.lisi"));

                // 墓碑打空：可观测不阻断
                WriteMod(root, "ModC", """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.ghost", "__delete": true }
                  ]
                }
                """);
                MapManager manager2 = CreateManager(root, "ModA", "ModC");
                MapConfig merged2 = manager2.LoadMap("harbor")!;
                Assert.That(merged2.Entities.Count, Is.EqualTo(3));
                Assert.That(manager2.LastMergeReport.DeletionsNotFound.Count, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_AnonymousEntityInNonBaseFragment_AppendsWithReportEntry()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModB", """
                {
                  "id": "harbor",
                  "entities": [
                    { "template": "gang.member" }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModB");
                MapConfig merged = manager.LoadMap("harbor")!;

                Assert.That(merged.Entities.Count, Is.EqualTo(4), "匿名实体纯追加（现状语义）");
                Assert.That(manager.LastMergeReport.AnonymousNonBaseFragments.Count, Is.EqualTo(1), "非首片段匿名实体必须可观测，专防'想打补丁漏写 id'的静默双实体");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_RelationsMerge_ByToAndTypeLaterWinsAndDeleteEdge()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModD", """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.zhangsan",
                      "relations": [
                        { "to": "harbor.wangwu", "type": "WorksFor", "metric": { "Loyalty": 95 } },
                        { "to": "harbor.lisi", "type": "SwornBrothers" }
                      ] }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModD");
                MapConfig merged = manager.LoadMap("harbor")!;

                EntitySpawnData zhangsan = merged.Entities.Single(e => e.InstanceId == "harbor.zhangsan");
                Assert.That(zhangsan.Relations!.Count, Is.EqualTo(2));
                EntityRelationAuthoring worksFor = zhangsan.Relations.Single(r => r.Type == "WorksFor");
                Assert.That(worksFor.Metric!["Loyalty"], Is.EqualTo(95), "同 (to,type) 后写赢");
                Assert.That(zhangsan.Relations.Any(r => r.Type == "SwornBrothers"), Is.True, "新 (to,type) 追加");

                // 边墓碑
                WriteMod(root, "ModD", """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.zhangsan",
                      "relations": [
                        { "to": "harbor.wangwu", "type": "WorksFor", "__delete": true }
                      ] }
                  ]
                }
                """);
                MapManager manager2 = CreateManager(root, "ModA", "ModD");
                MapConfig merged2 = manager2.LoadMap("harbor")!;
                EntitySpawnData zhangsan2 = merged2.Entities.Single(e => e.InstanceId == "harbor.zhangsan");
                Assert.That(zhangsan2.Relations!.Count, Is.EqualTo(0), "__delete 删边");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Tombstone_WithoutInstanceId_FailsFast()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModC", """
                {
                  "id": "harbor",
                  "entities": [
                    { "template": "gang.member", "__delete": true }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModC");
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("harbor"))!;
                Assert.That(ex.Message, Does.Contain("__delete"));
                Assert.That(ex.Message, Does.Contain("InstanceId"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // ------------------------------------------------------------------
        // 实例 relations 物化（InstanceRelationMaterializer）与事件配套
        // ------------------------------------------------------------------

        private sealed class RelationHarness : IDisposable
        {
            public World World = null!;
            public RelationshipRuntime Runtime = null!;
            public RelationshipTypeRegistry Types = null!;
            public RelationshipMetricRegistry Metrics = null!;
            public RelationshipFlagRegistry Flags = null!;
            public RelationshipChangeBuffer Changes = null!;

            public static RelationHarness Create()
            {
                var world = World.Create();
                var types = new RelationshipTypeRegistry();
                types.Register("WorksFor");
                types.Register("SwornBrothers");
                var metrics = new RelationshipMetricRegistry();
                metrics.Register("Loyalty");
                var flags = new RelationshipFlagRegistry();
                var bands = new RelationshipBandRegistry();
                var changes = new RelationshipChangeBuffer();
                metrics.RegisterAliasAttribute(metrics.GetId("Loyalty"), "Loyalty");
                var runtime = new RelationshipRuntime(world, types, metrics, flags, bands, changes, new RelationshipReverseIndex(world));
                runtime.InstallTagOps(new Ludots.Core.Gameplay.GAS.TagOps(new Ludots.Core.Gameplay.GAS.DirtyEntityQueue(1024), new Ludots.Core.Gameplay.GAS.TagRuleRegistry(), new Ludots.Core.Gameplay.GAS.GasBudget(), new Ludots.Core.Gameplay.GAS.AttributeAggregateDirtyRegistry()));
                return new RelationHarness { World = world, Runtime = runtime, Types = types, Metrics = metrics, Flags = flags, Changes = changes };
            }

            public void Dispose() => World.Dispose();
        }

        private static MapSession CreateSession(string mapId)
        {
            return new MapSession(new MapId(mapId), new MapConfig { Id = mapId });
        }

        private static (MapLoadEntityIndex Index, World World) SpawnIndexedEntities(World world, params string[] instanceIds)
        {
            var index = new MapLoadEntityIndex();
            foreach (string id in instanceIds)
            {
                Entity entity = world.Create();
                world.Add(entity, new Ludots.Core.Components.MapEntity { MapId = new MapId("harbor") });
                index.Register("harbor", id, entity);
            }

            return (index, world);
        }

        [Test]
        public void Materialize_InstanceRelations_CreateEdgesMetricsAndChangeRecords()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.zhangsan", "harbor.wangwu", "harbor.lisi");

            var mapConfig = new MapConfig { Id = "harbor" };
            mapConfig.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.zhangsan",
                Relations = new List<EntityRelationAuthoring>
                {
                    new() { To = "harbor.wangwu", Type = "WorksFor", Metric = new Dictionary<string, int> { ["Loyalty"] = 80 } },
                },
            });

            InstanceRelationMaterializer.Materialize(CreateSession("harbor"), mapConfig, index, harness.Runtime, harness.Types, harness.Metrics);

            Entity zhangsan = index.GetRequired("harbor", "harbor.zhangsan", "test");
            Entity wangwu = index.GetRequired("harbor", "harbor.wangwu", "test");
            Assert.That(harness.Runtime.HasLink(zhangsan, wangwu, harness.Types.GetId("WorksFor")), Is.True, "EnsureLink 物化成功");
            Assert.That(harness.Runtime.GetMetric(zhangsan, wangwu, harness.Types.GetId("WorksFor"), harness.Metrics.GetId("Loyalty")), Is.EqualTo(80));

            Assert.That(harness.Changes.Count, Is.EqualTo(2), "LinkAdded + MetricChanged 两条记录进缓冲（下一拍变 Relation* 事件）");
            Assert.That(harness.Changes.GetSpan()[0].Kind, Is.EqualTo(RelationshipChangeKind.LinkAdded));
            Assert.That(harness.Changes.GetSpan()[1].Kind, Is.EqualTo(RelationshipChangeKind.MetricChanged));
        }

        [Test]
        public void Materialize_EdgeTombstoneLeakingToMaterializer_FailsFast()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.zhangsan", "harbor.wangwu");

            // 边墓碑在合并层消化；直接带 Delete 到物化层=绕过合并，必须 fail-fast。
            var mapConfig = new MapConfig { Id = "harbor" };
            mapConfig.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.zhangsan",
                Relations = [new() { To = "harbor.wangwu", Type = "WorksFor", Delete = true }],
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), mapConfig, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(ex.Message, Does.Contain("__delete"));
        }

        [Test]
        public void Materialize_UnknownTargetOrTypeOrMetric_FailsFast()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.zhangsan", "harbor.wangwu");

            var unknownTarget = new MapConfig { Id = "harbor" };
            unknownTarget.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.zhangsan",
                Relations = [new() { To = "harbor.nobody", Type = "WorksFor" }],
            });
            InvalidOperationException targetEx = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), unknownTarget, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(targetEx.Message, Does.Contain("harbor.nobody"));

            var unknownType = new MapConfig { Id = "harbor" };
            unknownType.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.zhangsan",
                Relations = [new() { To = "harbor.wangwu", Type = "NotInCatalog" }],
            });
            InvalidOperationException typeEx = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), unknownType, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(typeEx.Message, Does.Contain("NotInCatalog"));

            var unknownMetric = new MapConfig { Id = "harbor" };
            unknownMetric.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.zhangsan",
                Relations = [new() { To = "harbor.wangwu", Type = "WorksFor", Metric = new Dictionary<string, int> { ["NotAMetric"] = 1 } }],
            });
            InvalidOperationException metricEx = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), unknownMetric, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(metricEx.Message, Does.Contain("NotAMetric"));
        }

        [Test]
        public void Materialize_RelationsWithoutInstanceId_FailsFast()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.zhangsan");

            var mapConfig = new MapConfig { Id = "harbor" };
            mapConfig.Entities.Add(new EntitySpawnData
            {
                Template = "gang.member",
                Relations = [new() { To = "harbor.zhangsan", Type = "WorksFor" }],
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), mapConfig, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(ex.Message, Does.Contain("InstanceId"));
        }

        [Test]
        public void Inheritance_TombstoneInChild_DeletesParentInstanceAndReportSurvives()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Maps"));
                File.WriteAllText(Path.Combine(root, "Maps", "base.json"),
                """
                {
                  "id": "base",
                  "entities": [
                    { "instanceId": "harbor.lisi", "template": "gang.member" },
                    { "instanceId": "harbor.zhangsan", "template": "gang.member" }
                  ]
                }
                """);
                File.WriteAllText(Path.Combine(root, "Maps", "child.json"),
                """
                {
                  "id": "child",
                  "parentId": "base",
                  "entities": [
                    { "instanceId": "harbor.lisi", "__delete": true }
                  ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var trigger = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var manager = new MapManager(vfs, trigger, modLoader, pipeline);

                MapConfig merged = manager.LoadMap("child")!;
                Assert.That(merged.Entities.Count, Is.EqualTo(1), "子图墓碑必须命中父图实例");
                Assert.That(merged.Entities.Any(e => e.InstanceId == "harbor.lisi"), Is.False);
                Assert.That(manager.LastMergeReport.Deletions.Count, Is.EqualTo(1), "继承帧不得抹掉墓碑报告");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SameFragment_DuplicateInstanceId_FailsFast()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "dup.guy", "template": "gang.a" },
                    { "instanceId": "dup.guy", "template": "gang.b" }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA");
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("harbor"))!;
                Assert.That(ex.Message, Does.Contain("duplicate InstanceId 'dup.guy' within the same fragment"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Tombstone_UntrimmedInstanceId_FailsFast()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": " harbor.lisi ", "__delete": true }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA");
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("harbor"))!;
                Assert.That(ex.Message, Does.Contain("must be trimmed"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_RelationsMetric_DeepMergesPerKey()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.zhangsan", "template": "gang.member",
                      "relations": [
                        { "to": "harbor.wangwu", "type": "WorksFor", "metric": { "Loyalty": 80, "Trust": 50 } }
                      ] },
                    { "instanceId": "harbor.wangwu", "template": "gang.member" }
                  ]
                }
                """);
                WriteMod(root, "ModD",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.zhangsan",
                      "relations": [
                        { "to": "harbor.wangwu", "type": "WorksFor", "metric": { "Loyalty": 95 } }
                      ] }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModD");
                MapConfig merged = manager.LoadMap("harbor")!;
                EntityRelationAuthoring worksFor = merged.Entities.Single(e => e.InstanceId == "harbor.zhangsan")
                    .Relations!.Single(r => r.Type == "WorksFor");
                Assert.That(worksFor.Metric!["Loyalty"], Is.EqualTo(95), "后写键覆盖");
                Assert.That(worksFor.Metric!["Trust"], Is.EqualTo(50), "未写键保留（metric 深合并）");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_TombstoneThenRevive_EntitySurvives()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA", BaseMap);
                WriteMod(root, "ModC",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.lisi", "__delete": true }
                  ]
                }
                """);
                WriteMod(root, "ModE",
                """
                {
                  "id": "harbor",
                  "entities": [
                    { "instanceId": "harbor.lisi", "template": "gang.revived" }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModC", "ModE");
                MapConfig merged = manager.LoadMap("harbor")!;
                EntitySpawnData lisi = merged.Entities.Single(e => e.InstanceId == "harbor.lisi");
                Assert.That(lisi.Template, Is.EqualTo("gang.revived"), "更晚片段重新声明 = 撤销墓碑复活");
                Assert.That(manager.LastMergeReport.Deletions.Count, Is.EqualTo(0), "复活撤销后墓碑不再命中");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Materialize_ToByLocalPath_SelfLoopAndNullServices()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.crew");
            Entity child = world.Create();
            world.Add(child, new Ludots.Core.Components.MapEntity { MapId = new MapId("harbor") });
            index.RegisterLocalPath("harbor", "harbor.crew.hq", child);

            var byPath = new MapConfig { Id = "harbor" };
            byPath.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.crew",
                Relations = [new() { To = "harbor.crew.hq", Type = "WorksFor" }],
            });
            InstanceRelationMaterializer.Materialize(CreateSession("harbor"), byPath, index, harness.Runtime, harness.Types, harness.Metrics);
            Entity crew = index.GetRequired("harbor", "harbor.crew", "test");
            Assert.That(harness.Runtime.HasLink(crew, child, harness.Types.GetId("WorksFor")), Is.True, "to 支持组内可寻址路径");

            var selfLoop = new MapConfig { Id = "harbor" };
            selfLoop.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.crew",
                Relations = [new() { To = "harbor.crew", Type = "WorksFor" }],
            });
            InvalidOperationException loopEx = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), selfLoop, index, harness.Runtime, harness.Types, harness.Metrics))!;
            Assert.That(loopEx.Message, Does.Contain("self-edges"));

            var withRelations = new MapConfig { Id = "harbor" };
            withRelations.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.crew",
                Relations = [new() { To = "harbor.crew.hq", Type = "WorksFor" }],
            });
            InvalidOperationException nullEx = Assert.Throws<InvalidOperationException>(() =>
                InstanceRelationMaterializer.Materialize(CreateSession("harbor"), withRelations, index, null!, harness.Types, harness.Metrics))!;
            Assert.That(nullEx.Message, Does.Contain("RelationshipRuntime is not installed"));
        }

        [Test]
        public void RelationshipRuntime_LinkRemovedAndFlagChanged_RecordKinds()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.a", "harbor.b");
            Entity a = index.GetRequired("harbor", "harbor.a", "t");
            Entity b = index.GetRequired("harbor", "harbor.b", "t");
            int typeId = harness.Types.GetId("WorksFor");
            harness.Flags.Register("Trusted");
            int flagId = harness.Flags.GetId("Trusted");

            harness.Runtime.EnsureLink(a, b, typeId);
            harness.Changes.Clear();
            harness.Runtime.SetFlag(a, b, typeId, flagId, true);
            ReadOnlySpan<RelationshipChangeRecord> span1 = harness.Changes.GetSpan();
            bool hasFlagChanged = false;
            for (int i = 0; i < span1.Length; i++) hasFlagChanged |= span1[i].Kind == RelationshipChangeKind.FlagChanged;
            Assert.That(hasFlagChanged, Is.True, "SetFlag 落 FlagChanged 记录");

            harness.Changes.Clear();
            harness.Runtime.RemoveLink(a, b, typeId);
            ReadOnlySpan<RelationshipChangeRecord> span2 = harness.Changes.GetSpan();
            Assert.That(span2.Length, Is.EqualTo(1));
            Assert.That(span2[0].Kind, Is.EqualTo(RelationshipChangeKind.LinkRemoved), "RemoveLink 落 LinkRemoved 记录");
        }
        [Test]
        public void CrossMod_VariableTombstone_DeletesAndRevives()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "variables": [
                    { "name": "killCount", "type": "int", "initial": 0 },
                    { "name": "morale", "type": "float", "initial": 75.5 }
                  ],
                  "entities": []
                }
                """);
                WriteMod(root, "ModC",
                """
                {
                  "id": "harbor",
                  "variables": [
                    { "name": "killCount", "__delete": true }
                  ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModC");
                MapConfig merged = manager.LoadMap("harbor")!;
                Assert.That(merged.Variables.Count, Is.EqualTo(1), "变量墓碑删除 killCount");
                Assert.That(merged.Variables[0].Name, Is.EqualTo("morale"));
                Assert.That(manager.LastMergeReport.VariableDeletions.Count, Is.EqualTo(1));

                // 复活：更晚片段重新声明
                WriteMod(root, "ModE",
                """
                {
                  "id": "harbor",
                  "variables": [
                    { "name": "killCount", "type": "int", "initial": 10 }
                  ]
                }
                """);
                MapManager manager2 = CreateManager(root, "ModA", "ModC", "ModE");
                MapConfig merged2 = manager2.LoadMap("harbor")!;
                Assert.That(merged2.Variables.Single(v => v.Name == "killCount").Initial, Is.EqualTo(10), "更晚声明 = 撤销墓碑复活");
                Assert.That(manager2.LastMergeReport.VariableDeletions.Count, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Inheritance_VariableTombstoneInChild_DeletesParentVariable()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Maps"));
                File.WriteAllText(Path.Combine(root, "Maps", "base.json"),
                """
                {
                  "id": "base",
                  "variables": [ { "name": "killCount", "type": "int", "initial": 0 } ]
                }
                """);
                File.WriteAllText(Path.Combine(root, "Maps", "child.json"),
                """
                {
                  "id": "child",
                  "parentId": "base",
                  "variables": [ { "name": "killCount", "__delete": true } ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var trigger = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var manager = new MapManager(vfs, trigger, modLoader, pipeline);

                MapConfig merged = manager.LoadMap("child")!;
                Assert.That(merged.Variables.Count, Is.EqualTo(0), "子图变量墓碑命中父图声明");
                Assert.That(manager.LastMergeReport.VariableDeletions.Count, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_VariableTypeChange_FailsFast()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "morale", "type": "int", "initial": 75 } ],
                  "entities": []
                }
                """);
                WriteMod(root, "ModB",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "morale", "type": "float", "initial": 75.5 } ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModB");
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("harbor"))!;
                Assert.That(ex.Message, Does.Contain("morale"));
                Assert.That(ex.Message, Does.Contain("type"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_VariableSameType_LaterInitialWins()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "killCount", "type": "int", "initial": 0 } ],
                  "entities": []
                }
                """);
                WriteMod(root, "ModB",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "killCount", "type": "int", "initial": 42 } ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModB");
                MapConfig merged = manager.LoadMap("harbor")!;
                Assert.That(merged.Variables.Single(v => v.Name == "killCount").Initial, Is.EqualTo(42), "同型后写 initial 赢");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
        [Test]
        public void LoadMap_MissingMap_ReturnsNullInsteadOfThrowing()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var trigger = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var manager = new MapManager(vfs, trigger, modLoader, pipeline);

                Assert.That(manager.LoadMap("no-such-map"), Is.Null, "缺失地图维持 main 的 null 合同，不得退化为 NRE");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_VariableDeleteThenRedeclareWithNewType_Succeeds()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "morale", "type": "int", "initial": 75 } ],
                  "entities": []
                }
                """);
                WriteMod(root, "ModB",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "morale", "__delete": true } ]
                }
                """);
                WriteMod(root, "ModC",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": "morale", "type": "float", "initial": 75.5 } ]
                }
                """);

                MapManager manager = CreateManager(root, "ModA", "ModB", "ModC");
                MapConfig merged = manager.LoadMap("harbor")!;
                MapVariableDeclaration morale = merged.Variables.Single(v => v.Name == "morale");
                Assert.That(morale.Type.ToString(), Is.EqualTo("Float"), "墓碑后的重新声明允许改型（delete-then-redeclare 处方可执行）");
                Assert.That(morale.Initial, Is.EqualTo(75.5));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void CrossMod_VariableUntrimmedName_FailsFast()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteMod(root, "ModA",
                """
                {
                  "id": "harbor",
                  "variables": [ { "name": " morale ", "type": "int", "initial": 1 } ],
                  "entities": []
                }
                """);

                MapManager manager = CreateManager(root, "ModA");
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("harbor"))!;
                Assert.That(ex.Message, Does.Contain("must be trimmed"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
        [Test]
        public void Materialize_TruthLivesOnEdgeEntityAttributeBuffer()
        {
            using RelationHarness harness = RelationHarness.Create();
            (MapLoadEntityIndex index, World world) = SpawnIndexedEntities(harness.World, "harbor.a", "harbor.b");
            var mapConfig = new MapConfig { Id = "harbor" };
            mapConfig.Entities.Add(new EntitySpawnData
            {
                InstanceId = "harbor.a",
                Relations = [new() { To = "harbor.b", Type = "WorksFor", Metric = new Dictionary<string, int> { ["Loyalty"] = 80 } }],
            });

            InstanceRelationMaterializer.Materialize(CreateSession("harbor"), mapConfig, index, harness.Runtime, harness.Types, harness.Metrics);

            Entity a = index.GetRequired("harbor", "harbor.a", "t");
            Entity b = index.GetRequired("harbor", "harbor.b", "t");
            int typeId = harness.Types.GetId("WorksFor");
            Assert.That(harness.Runtime.HasLink(a, b, typeId), Is.True);

            // #1570 单轨化：SetMetric 写穿边实体 AttributeBuffer（唯一真相），SoA 是缓存
            harness.Runtime.SetMetric(a, b, typeId, harness.Metrics.GetId("Loyalty"), 42);
            Entity edgeEntity = harness.Runtime.MaterializeRelationshipEntity(a, b, typeId);
            Assert.That(world.Has<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(edgeEntity), Is.True, "边实体携带 AttributeBuffer");
            if (harness.Metrics.TryGetAttributeId(harness.Metrics.GetId("Loyalty"), out int attrId))
            {
                float truth = world.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(edgeEntity).GetCurrent(attrId);
                Assert.That(truth, Is.EqualTo(42f), "真相在边实体 AttributeBuffer 上");
            }
        }

        [Test]
        public void RelationEvents_RegisteredAsMapScopedPresetEvents()
        {
            Assert.That(GameEvents.IsMapScoped(GameEvents.RelationLinkAdded.Value), Is.True);
            Assert.That(GameEvents.IsMapScoped(GameEvents.RelationLinkRemoved.Value), Is.True);
            Assert.That(GameEvents.IsMapScoped(GameEvents.RelationMetricChanged.Value), Is.True);
            Assert.That(GameEvents.IsMapScoped(GameEvents.RelationFlagChanged.Value), Is.True);
        }

        [Test]
        public void MapInheritance_ChildOverridesParentInstanceByIdentity()
        {
            var root = Path.Combine(Path.GetTempPath(), "Ludots_1554_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Maps"));
                File.WriteAllText(Path.Combine(root, "Maps", "base.json"), """
                {
                  "id": "base",
                  "entities": [
                    { "instanceId": "shared.guy", "template": "gang.member",
                      "overrides": { "AttributeTable": { "Hp": 50 } } }
                  ]
                }
                """);
                File.WriteAllText(Path.Combine(root, "Maps", "child.json"), """
                {
                  "id": "child",
                  "parentId": "base",
                  "entities": [
                    { "instanceId": "shared.guy",
                      "overrides": { "AttributeTable": { "Hp": 77 } } }
                  ]
                }
                """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var trigger = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var manager = new MapManager(vfs, trigger, modLoader, pipeline);

                MapConfig merged = manager.LoadMap("child")!;
                Assert.That(merged.Entities.Count, Is.EqualTo(1), "继承链与跨 mod 共用同一合并语义");
                Assert.That(merged.Entities[0].Overrides!["AttributeTable"]!["Hp"]!.GetValue<int>(), Is.EqualTo(77));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
