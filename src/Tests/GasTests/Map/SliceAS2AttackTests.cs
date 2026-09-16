using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// 切A 合前 S1 修复的第 2 轮对抗性审计（攻击性测试，只新增测试、不改核心代码）。
    /// 命名约定：方法名里 <c>Hit</c> = 我认为这是破绽（断言合同应有的行为，当前应失败）；
    /// <c>Control</c> / <c>Safe</c> = 对照或“此处安全”的证据（应通过）。
    /// </summary>
    [TestFixture]
    public sealed class SliceAS2AttackTests
    {
        private const string PoseLiteral =
            "\"localPose\": { \"offsetXCm\": 10, \"offsetYCm\": 0, \"facingDeg\": 0, \"inheritParentFacing\": false, \"offsetRotation\": \"None\" }";

        // ------------------------------------------------------------------
        // 攻击 1/2：batch lane 的父先子后自消费
        // 子节点引用的模板若是 batch 可批量（Name + WorldPositionCm + FacingDirection 三件套），
        // 它既过外层 batch 前置判定、又过不了 IsTemplateBatchMember（Parent/AttachedLocalPose/=1），
        // TryCopyTemplateBatch 于是返回 0，drain 循环直接 break。
        // ------------------------------------------------------------------

        private static readonly string BatchChildTemplates = """
        [
          {
            "id": "s2.root",
            "components": { "Name": { "Value": "S2Root" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "localId": "unit", "template": "s2.batchunit", {{POSE}} } ]
          },
          {
            "id": "s2.batchunit",
            "components": {
              "Name": { "Value": "S2BatchUnit" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
              "FacingDirection": { "AngleRad": 0 }
            }
          },
          {
            "id": "s2.plainroot",
            "components": { "Name": { "Value": "S2PlainRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "localId": "unit", "template": "s2.plainunit", {{POSE}} } ]
          },
          { "id": "s2.plainunit", "components": { "Name": { "Value": "S2PlainUnit" } } },
          {
            "id": "s2.inlineroot",
            "components": {
              "Name": { "Value": "S2InlineRoot" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
              "FacingDirection": { "AngleRad": 0 }
            }
          },
          { "id": "s2.leaf", "components": { "Name": { "Value": "S2Leaf" } } }
        ]
        """.Replace("{{POSE}}", PoseLiteral);

        [Test]
        public void S2_1_Hit_BatchCompatibleChildTemplate_ChildMustMaterializeAndQueueMustDrain()
        {
            using Harness harness = Harness.Create(BatchChildTemplates);
            SpawnRoot(harness, "s2.root", instanceId: "g1");

            // 一次 Update 不够就再给四帧：卡死的话永远都不会出现。
            for (int i = 0; i < 5; i++)
            {
                harness.System.Update(0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    CountByName(harness.World, "S2BatchUnit"),
                    Is.EqualTo(1),
                    "S1-1 承诺运行时递归 children 与地图装载对齐；子模板三件套（batch 可批量）时子实体从未出生");
                Assert.That(
                    harness.Requests.Count,
                    Is.EqualTo(0),
                    "队列头部的子请求永远消费不掉（break 出 drain 循环），队列被永久卡住");
            });
        }

        [Test]
        public void S2_2_Control_NameOnlyChildTemplate_ChildMaterializesAndQueueDrains()
        {
            using Harness harness = Harness.Create(BatchChildTemplates);
            SpawnRoot(harness, "s2.plainroot", instanceId: "g1");
            harness.System.Update(0f);

            Entity root = FindByName(harness.World, "S2PlainRoot");
            Entity child = FindByName(harness.World, "S2PlainUnit");

            Assert.Multiple(() =>
            {
                Assert.That(harness.Requests.Count, Is.EqualTo(0), "对照组：非 batch 模板的子请求应被 drain 消费");
                Assert.That(harness.World.Get<ChildOf>(child).Parent, Is.EqualTo(root));
                Assert.That(root.Id, Is.LessThan(child.Id), "父先于子");
            });
        }

        [Test]
        public void S2_3_Hit_RequestCarryingInlineChildren_OnBatchCompatibleTemplate_HeadStall()
        {
            using Harness harness = Harness.Create(BatchChildTemplates);

            var inline = new List<EntityTemplateChild>
            {
                new EntityTemplateChild { LocalId = "a", Template = "s2.leaf", LocalPose = Pose() },
            };
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "s2.inlineroot",
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
                InstanceId = "g1",
                InlineChildren = inline,
            }), Is.True);

            for (int i = 0; i < 5; i++)
            {
                harness.System.Update(0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    CountByName(harness.World, "S2InlineRoot"),
                    Is.EqualTo(1),
                    "S1-1 新加的 InlineChildren 刀口让该请求过不了 IsTemplateBatchMember，但外层前置判定只问 IsBatchCompatible——根实体都生不出来");
                Assert.That(CountByName(harness.World, "S2Leaf"), Is.EqualTo(1), "内联 child 也没出生");
                Assert.That(harness.Requests.Count, Is.EqualTo(0), "整条队列被队列头永久卡住");
            });
        }

        // ------------------------------------------------------------------
        // 攻击 5：InstanceId 首尾空白（合同写“非空、首尾无空白”，代码只查 IsNullOrWhiteSpace）
        // ------------------------------------------------------------------

        private static readonly string AddressableTemplates = """
        [
          {
            "id": "s2.addr.root",
            "components": { "Name": { "Value": "S2AddrRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "localId": "hq", "template": "s2.addr.leaf", {{POSE}} } ]
          },
          { "id": "s2.addr.leaf", "components": { "Name": { "Value": "S2AddrLeaf" } } },
          {
            "id": "s2.deep.root",
            "components": { "Name": { "Value": "S2DeepRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "template": "s2.deep.mid", {{POSE}} } ]
          },
          {
            "id": "s2.deep.mid",
            "components": { "Name": { "Value": "S2DeepMid" } },
            "children": [ { "localId": "x", "template": "s2.addr.leaf", {{POSE}} } ]
          }
        ]
        """.Replace("{{POSE}}", PoseLiteral);

        [Test]
        public void S2_4_Hit_RuntimeInstanceIdWithSurroundingWhitespace_MustFailFast()
        {
            using Harness harness = Harness.Create(AddressableTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnRoot(harness, "s2.addr.root", instanceId: " g1 "))!;

            Assert.That(ex.Message, Does.Contain("InstanceId"));
            Assert.That(ex.Message, Does.Contain("空白"), "运行时合同要求实例根首尾无空白，代码只查了 IsNullOrWhiteSpace");
        }

        [Test]
        public void S2_5_Control_MapLaneRejectsInstanceIdWithSurroundingWhitespace()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, AddressableTemplates);
            var map = new MapConfig { Id = "s2_ws" };
            map.Entities.Add(new EntitySpawnData { InstanceId = " camp ", Template = "s2.addr.root" });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("trimmed"), "装载侧由 MapLoadEntityIndex.Register 拒绝未 trim 的 InstanceId");
        }

        // ------------------------------------------------------------------
        // 攻击 3：attach:false 的判定覆盖面
        // ------------------------------------------------------------------

        private static readonly string AttachFalseTemplates = """
        [
          {
            "id": "s2.af.root",
            "components": { "Name": { "Value": "S2AfRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "localId": "hq", "template": "s2.af.tent", {{POSE}} } ]
          },
          {
            "id": "s2.af.tent",
            "components": { "Name": { "Value": "S2AfTent" } },
            "children": [ { "localId": "guard", "template": "s2.af.unit", "attach": false, {{POSE}} } ]
          },
          { "id": "s2.af.unit", "components": { "Name": { "Value": "S2AfGuard" } } },
          { "id": "s2.mid.leaf", "components": { "Name": { "Value": "S2MidLeaf" } } },
          {
            "id": "s2.af.deep",
            "components": { "Name": { "Value": "S2AfDeepRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [
              {
                "localId": "a", "template": "s2.mid.leaf", {{POSE}},
                "children": [
                  { "localId": "b", "template": "s2.mid.leaf", {{POSE}},
                    "children": [ { "localId": "guard", "template": "s2.af.unit", "attach": false, {{POSE}} } ] }
                ]
              }
            ]
          },
          {
            "id": "s2.mid",
            "components": { "Name": { "Value": "S2Mid" } }
          }
        ]
        """.Replace("{{POSE}}", PoseLiteral);

        [Test]
        public void S2_6_Safe_AttachFalseDeclaredInReferencedTemplateChildren_FailsFast()
        {
            using Harness harness = Harness.Create(AttachFalseTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnRoot(harness, "s2.af.root", instanceId: "g1"))!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AttachFalseUnsupported"));
            Assert.That(ex.Message, Does.Contain("g1.hq.guard"), "attach:false 在被引用模板自身的 children 里也必须被判到");
        }

        [Test]
        public void S2_7_Safe_AttachFalseThreeLevelsDeep_PathContextIsFullChain()
        {
            using Harness harness = Harness.Create(AttachFalseTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnRoot(harness, "s2.af.deep", instanceId: "g1"))!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AttachFalseUnsupported"));
            Assert.That(ex.Message, Does.Contain("g1.a.b.guard"), "内联 children 递归三层时路径链必须完整");
        }

        [Test]
        public void S2_8_Evidence_AttachFalseOnMapLaneIsStillAttachedStructurally_LaneDivergence()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, AttachFalseTemplates);
            var map = new MapConfig { Id = "s2_af_map" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "cmp", Template = "s2.af.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.Multiple(() =>
            {
                Assert.That(index.TryGetByLocalPath("cmp.hq.guard", out Entity guard), Is.True);
                Assert.That(world.Has<ChildOf>(guard), Is.True, "装载 lane 仍把 attach:false 当结构子件挂接（现状）");
            });
        }

        // ------------------------------------------------------------------
        // 攻击 1：中间层节点带可寻址内联 children、直接出生请求却没带 InstanceId
        // ------------------------------------------------------------------

        [Test]
        public void S2_9_Safe_MidLevelNodeWithAddressableInlineChildren_WithoutInstanceId_FailsFast()
        {
            using Harness harness = Harness.Create(AttachFalseTemplates);

            var inline = new List<EntityTemplateChild>
            {
                new EntityTemplateChild { LocalId = "x", Template = "s2.af.unit", LocalPose = Pose() },
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "s2.mid",
                    WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                    HasWorldPosition = 1,
                    InlineChildren = inline,
                }), Is.True);
                harness.System.Update(0f);
            })!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AddressableDescendantMissingInstanceId"));
        }

        // ------------------------------------------------------------------
        // 攻击 4：HasAddressableDescendant 的环 / null / 未知模板安全
        // ------------------------------------------------------------------

        private static readonly string CycleTemplates = """
        [
          { "id": "cyc.a", "components": { "Name": { "Value": "A" } }, "children": [ { "template": "cyc.b", {{POSE}} } ] },
          { "id": "cyc.b", "components": { "Name": { "Value": "B" } }, "children": [
              { "template": "cyc.a", {{POSE}} },
              { "template": "cyc.c", {{POSE}} } ] },
          { "id": "cyc.c", "components": { "Name": { "Value": "C" } }, "children": [ { "localId": "x", "template": "cyc.d", {{POSE}} } ] },
          { "id": "cyc.d", "components": { "Name": { "Value": "D" } } },
          { "id": "cyc.self", "components": { "Name": { "Value": "Self" } }, "children": [ { "template": "cyc.self", {{POSE}} } ] },
          { "id": "cyc.orphan", "components": { "Name": { "Value": "Orphan" } } }
        ]
        """.Replace("{{POSE}}", PoseLiteral);

        [Test]
        [Timeout(10000)]
        public void S2_10_Safe_HasAddressableDescendant_CyclicAndSelfReferencingGraphs_Terminate()
        {
            DataRegistry<EntityTemplate> registry = LoadTemplates(CycleTemplates);

            Assert.Multiple(() =>
            {
                Assert.That(
                    EntityTemplate.HasAddressableDescendant(registry.Get("cyc.a").Children, registry),
                    Is.True,
                    "环 cyc.a→cyc.b→cyc.a 之后仍有 cyc.c.x 可寻址，visited 集合必须防环且不漏报");
                Assert.That(
                    EntityTemplate.HasAddressableDescendant(registry.Get("cyc.self").Children, registry),
                    Is.False,
                    "自引用模板自身无 localId 后代，必须终止并返回 false");
            });
        }

        [Test]
        public void S2_11_Safe_HasAddressableDescendant_NullRegistryUnknownTemplateAndNullChildren()
        {
            DataRegistry<EntityTemplate> registry = LoadTemplates(CycleTemplates);
            List<EntityTemplateChild> nullRegistryInline = InlineWithLocalId();

            Assert.Multiple(() =>
            {
                Assert.That(EntityTemplate.HasAddressableDescendant(null, registry), Is.False);
                Assert.That(EntityTemplate.HasAddressableDescendant(nullRegistryInline, null!), Is.True, "registry 为空时内联 localId 仍应被判定");
                Assert.That(
                    EntityTemplate.HasAddressableDescendant(
                        new List<EntityTemplateChild> { new EntityTemplateChild { Template = "does.not.exist", LocalPose = Pose() } },
                        registry),
                    Is.False,
                    "未知模板必须安全跳过而不是抛空引用");
            });
        }

        // ------------------------------------------------------------------
        // 攻击 4（延伸）：运行时的 children 环没有任何守卫
        // ------------------------------------------------------------------

        private static readonly string BranchingCycleTemplates = """
        [
          { "id": "cyc2.a", "components": { "Name": { "Value": "A2" } }, "children": [
              { "template": "cyc2.b", {{POSE}} }, { "template": "cyc2.b", {{POSE}} } ] },
          { "id": "cyc2.b", "components": { "Name": { "Value": "B2" } }, "children": [
              { "template": "cyc2.a", {{POSE}} }, { "template": "cyc2.a", {{POSE}} } ] }
        ]
        """.Replace("{{POSE}}", PoseLiteral);

        [Test]
        [Timeout(15000)]
        public void S2_12_Hit_RuntimeChildrenCycle_FailsAsQueueOverflowInsteadOfCycleError()
        {
            // registry 直接由数据装载（不经 MapLoader.ValidateTemplateChildrenGraph）——
            // 这是 mod / 运行时装配的合法入口，装载期无环校验在此不成立。
            using Harness harness = Harness.Create(BranchingCycleTemplates, queueCapacity: 16);
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "cyc2.a",
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(0f))!;

            Assert.That(
                ex.Message,
                Does.Contain("环"),
                "无环校验收敛：运行时不得把模板 children 环表达成队列溢出（当前是 TemplateChildrenQueueFull，且已凭空造出若干实体）");
        }

        // ------------------------------------------------------------------
        // 攻击 1（延伸）：两 POI 共用同模板靠实例前缀隔离 —— 前缀本身唯一性由谁保证
        // ------------------------------------------------------------------

        [Test]
        public void S2_13_Safe_TwoPlacementsSharingInstanceId_FailFastOnDuplicatePrefix()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, AddressableTemplates);
            var map = new MapConfig { Id = "s2_dup" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "poi", Template = "s2.addr.root" });
            map.Entities.Add(new EntitySpawnData { InstanceId = "poi", Template = "s2.addr.root" });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("duplicate entity InstanceId"));
        }

        [Test]
        public void S2_14_Hit_StuckBatchHead_StarvesUnrelatedSpawnsBehindIt()
        {
            using Harness harness = Harness.Create(BatchChildTemplates);
            SpawnRoot(harness, "s2.root", instanceId: "g1");

            // 卡住的队列头后面，再排一个完全无关、名字里没有任何 batch/父链的普通 spawn。
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "s2.plainunit",
                WorldPositionCm = Fix64Vec2.FromInt(100, 0),
                HasWorldPosition = 1,
            }), Is.True);

            for (int i = 0; i < 5; i++)
            {
                harness.System.Update(0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    CountByName(harness.World, "S2PlainUnit"),
                    Is.EqualTo(1),
                    "队列头被 break 卡死后，排在它后面的一切 spawn 都不再被消费（整个 spawn 管线静默死亡）");
                Assert.That(harness.Requests.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void S2_15_Evidence_MapLaneMaterializesBatchCompatibleChild_SoRuntimeDiverges()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, BatchChildTemplates);
            var map = new MapConfig { Id = "s2_map_batchchild" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "cmp", Template = "s2.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.Multiple(() =>
            {
                Assert.That(index.TryGetByLocalPath("cmp.unit", out Entity unit), Is.True);
                Assert.That(world.Get<Name>(unit).Value, Is.EqualTo("S2BatchUnit"), "装载 lane 同一份资产会把子件物化出来——S1-1 声称运行时与之对齐");
            });
        }

        [Test]
        public void S2_16_Safe_AddressableDescendantAcrossTemplateBoundary_RequiresInstanceId()
        {
            using (Harness harness = Harness.Create(AddressableTemplates))
            {
                InvalidOperationException runtimeEx = Assert.Throws<InvalidOperationException>(
                    () => SpawnRoot(harness, "s2.deep.root", instanceId: null))!;
                Assert.That(runtimeEx.Message, Does.Contain("SPAWN.RUNTIME.ERR.AddressableDescendantMissingInstanceId"));
            }

            using (var world = World.Create())
            {
                MapLoader loader = CreateMapLoader(world, AddressableTemplates);
                var map = new MapConfig { Id = "s2_deep" };
                map.Entities.Add(new EntitySpawnData { Template = "s2.deep.root" });

                InvalidOperationException mapEx = Assert.Throws<InvalidOperationException>(
                    () => loader.LoadEntitiesAndIndex(map))!;
                Assert.That(mapEx.Message, Does.Contain("addressable descendants"));
            }
        }

        // ------------------------------------------------------------------
        // Harness
        // ------------------------------------------------------------------

        private static EntityTemplateLocalPose Pose() => new EntityTemplateLocalPose
        {
            OffsetXCm = 10,
            OffsetYCm = 0,
            FacingDeg = 0,
            InheritParentFacing = false,
            OffsetRotation = "None",
        };

        private static List<EntityTemplateChild> InlineWithLocalId() => new List<EntityTemplateChild>
        {
            new EntityTemplateChild { LocalId = "x", Template = "cyc.d", LocalPose = Pose() },
        };

        private sealed class Harness : IDisposable
        {
            public World World = null!;
            public RuntimeEntitySpawnQueue Requests = null!;
            public RuntimeEntitySpawnSystem System = null!;

            public static Harness Create(string templatesJson, int queueCapacity = 64)
            {
                DataRegistry<EntityTemplate> templates = LoadTemplates(templatesJson);
                World world = World.Create();
                var requests = new RuntimeEntitySpawnQueue(capacity: queueCapacity);
                var system = new RuntimeEntitySpawnSystem(
                    world,
                    requests,
                    templates,
                    new EntityTemplateKeyRegistry(),
                    new PresentationStableIdAllocator());
                return new Harness { World = world, Requests = requests, System = system };
            }

            public void Dispose() => World.Dispose();
        }

        private static void SpawnRoot(Harness harness, string templateId, string? instanceId)
        {
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = templateId,
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
                InstanceId = instanceId,
            }), Is.True);
            harness.System.Update(0f);
        }

        private static DataRegistry<EntityTemplate> LoadTemplates(string templatesJson)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_SliceAS2AttackTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Entities"));
                File.WriteAllText(Path.Combine(root, "Entities", "templates.json"), templatesJson);
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var registry = new DataRegistry<EntityTemplate>(pipeline);
                registry.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));
                return registry;
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static MapLoader CreateMapLoader(World world, string templatesJson)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_SliceAS2AttackTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Entities"));
                File.WriteAllText(Path.Combine(root, "Entities", "templates.json"), templatesJson);
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");

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

        private static Entity FindByName(World world, string name)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
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

        private static int CountByName(World world, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name component) =>
            {
                if (string.Equals(component.Value, name, StringComparison.Ordinal))
                {
                    count++;
                }
            });
            return count;
        }
    }
}
