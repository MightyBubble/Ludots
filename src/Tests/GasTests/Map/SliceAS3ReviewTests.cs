using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using Ludots.Core.Gameplay.Components;
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
    /// 切A 第 3 轮全面复审（S3）：锁死"带 children 的模板与 batch lane 的关系"这份合同——
    /// TemplateSpawnDescriptor.Create 把带 children 的模板判为 batch-Incompatible（逐子挂接只能走
    /// 单实体 lane），因此无论地图装载还是运行时出生、无论走哪条 lane，带 children 的模板都
    /// 必须完整物化后代、登记 local 路径、并受可寻址 instanceId 门约束。另锁 inline 引用环的
    /// 运行时守卫（环必须在根出生这一拍报"环"）。
    /// </summary>
    [TestFixture]
    public sealed class SliceAS3ReviewTests
    {
        private const string Pose =
            "\"localPose\": { \"offsetXCm\": 10, \"offsetYCm\": 0, \"facingDeg\": 0, \"inheritParentFacing\": false, \"offsetRotation\": \"None\" }";

        // 三件套 + localId child：按合同此模板 batch-Incompatible（children 排除），必走单实体 lane；
        // 本组样本锁的是"无论哪条 lane，后代与路径都不丢、门都生效"。
        private static readonly string BatchRootTemplates = """
        [
          {
            "id": "s3.batchroot",
            "components": {
              "Name": { "Value": "S3BatchRoot" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
              "FacingDirection": { "AngleRad": 0 }
            },
            "children": [ { "localId": "hq", "template": "s3.leaf", {{POSE}} } ]
          },
          { "id": "s3.leaf", "components": { "Name": { "Value": "S3Leaf" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        // 环样本：载体自身无模板 children，环藏在 InlineChildren 的模板引用里（x 自引用）。
        // 只供运行时 lane（直接装配 registry，不经 LoadTemplates 环校验）使用。
        private static readonly string NamePathTemplates = """
        [
          {
            "id": "name.camp",
            "components": {
              "Name": { "Value": "Camp" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
              "FacingDirection": { "AngleRad": 0 }
            },
            "children": [
              { "localId": "hq", "template": "name.tent", "overrides": { "Name": { "Value": "Shared Tent" } }, {{POSE}} },
              {
                "localId": "radio", "template": "name.radio", {{POSE}},
                "children": [ { "localId": "coil", "template": "name.coil", {{POSE}} } ]
              },
              { "localId": "guard", "template": "name.guard", {{POSE}} }
            ]
          },
          { "id": "name.tent", "components": { "Name": { "Value": "Tent" } } },
          { "id": "name.radio", "components": { "Name": { "Value": "Radio" } } },
          { "id": "name.coil", "components": { "Name": { "Value": "Coil" } } },
          { "id": "name.guard", "components": { "Name": { "Value": "Guard" }, "Team": { "Id": 1 } } }
        ]
        """.Replace("{{POSE}}", Pose);

        private static readonly string InlineCycleTemplates = """
        [
          { "id": "cyc3.carrier2", "components": { "Name": { "Value": "Cyc3Carrier2" } } },
          {
            "id": "cyc3.x", "components": { "Name": { "Value": "Cyc3X" } },
            "children": [ { "template": "cyc3.x", {{POSE}} } ]
          }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void S3_1_MapBatch_TemplateWithChildren_MaterializesDescendantsAndRegistersPaths()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, BatchRootTemplates);
            var map = new MapConfig { Id = "s3_map_batch_children" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "s3.a", Template = "s3.batchroot" });
            map.Entities.Add(new EntitySpawnData { InstanceId = "s3.b", Template = "s3.batchroot" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.Multiple(() =>
            {
                Assert.That(index.TryGetByLocalPath("s3.a.hq", out Entity hqA), Is.True, "带 children 的模板在任何 lane 都必须物化后代并登记 local 路径");
                Assert.That(index.TryGetByLocalPath("s3.b.hq", out Entity hqB), Is.True);
                Assert.That(world.Get<Name>(hqA).Value, Is.EqualTo("S3Leaf"));
                Assert.That(world.Get<Name>(hqB).Value, Is.EqualTo("S3Leaf"));
                Assert.That(hqB, Is.Not.EqualTo(hqA), "两个实例的子代必须各自独立物化");
                Assert.That(CountByName(world, "S3BatchRoot"), Is.EqualTo(2));
            });
        }

        [Test]
        public void MapInstance_EntityInfoPaths_NameAddressedChildrenAndKeepSystemNames()
        {
            using var world = World.Create();
            PresentationTextCatalog catalog = CreateCampTitleCatalog();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates, catalog);
            var map = new MapConfig { Id = "name_path_map" };
            map.Entities.Add(CreateNamedCamp("camp.harbor", "camp.harbor.title", renameChildren: true));
            map.Entities.Add(CreateNamedCamp("camp.alpine", "camp.alpine.title", renameChildren: false));

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.Multiple(() =>
            {
                AssertInfo(world, RequireInstance(index, "camp.harbor"), "Camp", catalog.GetTokenId("camp.harbor.title"));
                AssertInfo(world, RequireInstance(index, "camp.alpine"), "Camp", catalog.GetTokenId("camp.alpine.title"));
                AssertInfo(world, RequirePath(index, "camp.harbor.hq"), "Shared Tent", catalog.GetTokenId("camp.harbor.hq.title"));
                AssertInfo(world, RequirePath(index, "camp.alpine.hq"), "Shared Tent", 0);
                AssertInfo(world, RequirePath(index, "camp.harbor.radio"), "Radio", 0);
                AssertInfo(world, RequirePath(index, "camp.harbor.radio.coil"), "Coil", catalog.GetTokenId("camp.harbor.coil.title"));
                AssertInfo(world, RequirePath(index, "camp.alpine.radio.coil"), "Coil", 0);
                Entity harborGuard = RequirePath(index, "camp.harbor.guard");
                AssertInfo(world, harborGuard, "Guard", catalog.GetTokenId("camp.harbor.guard.title"));
                Assert.That(world.Get<Team>(harborGuard).Id, Is.EqualTo(1));
                Entity alpineGuard = RequirePath(index, "camp.alpine.guard");
                AssertInfo(world, alpineGuard, "Guard", 0);
                Assert.That(world.Get<Team>(alpineGuard).Id, Is.EqualTo(1));
            });
        }

        [Test]
        public void MapInstance_NamePath_UnknownChildFails()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates, CreateCampTitleCatalog());
            var map = new MapConfig { Id = "name_path_missing" };
            EntitySpawnData spawn = CreateNamedCamp("camp.harbor", "camp.harbor.title", renameChildren: false);
            spawn.OverridePaths = new List<EntityPathNameOverride>
            {
                InfoPath("tower", "camp.harbor.title"),
            };
            map.Entities.Add(spawn);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;
            Assert.That(ex.Message, Does.Contain("does not address a child"));
        }

        [Test]
        public void MapInstance_EntityInfoPath_RejectsComponentSet()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates, CreateCampTitleCatalog());
            InvalidOperationException team = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(MapWithGuardSet(
                "name_path_team",
                "Team",
                JsonNode.Parse(@"{ ""Id"": 4 }")!)))!;
            InvalidOperationException name = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(MapWithGuardSet(
                "name_path_system_name",
                "Name",
                JsonNode.Parse(@"{ ""Value"": ""Harbor Guard"" }")!)))!;
            Assert.Multiple(() =>
            {
                Assert.That(team.Message, Does.Contain("only accepts titleToken"));
                Assert.That(name.Message, Does.Contain("only accepts titleToken"));
            });
        }

        [Test]
        public void MapInstance_EntityInfoPath_EmptyTokenFails()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates, CreateCampTitleCatalog());
            var map = new MapConfig { Id = "name_path_empty" };
            EntitySpawnData spawn = CreateNamedCamp("camp.harbor", "camp.harbor.title", renameChildren: false);
            spawn.OverridePaths = new List<EntityPathNameOverride> { InfoPath("hq", " ") };
            map.Entities.Add(spawn);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;
            Assert.That(ex.Message, Does.Contain("non-empty"));
        }

        [Test]
        public void MapInstance_EntityInfoPath_UnknownTokenFails()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates, CreateCampTitleCatalog());
            var map = new MapConfig { Id = "name_path_unknown_token" };
            EntitySpawnData spawn = CreateNamedCamp("camp.harbor", "camp.missing.title", renameChildren: false);
            map.Entities.Add(spawn);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;
            Assert.That(ex.Message, Does.Contain("unknown text token"));
        }

        [Test]
        public void MapInstance_EntityInfoPath_RequiresTextCatalog()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, NamePathTemplates);
            var map = new MapConfig { Id = "name_path_no_catalog" };
            map.Entities.Add(CreateNamedCamp("camp.harbor", "camp.harbor.title", renameChildren: false));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.LoadEntitiesAndIndex(map))!;
            Assert.That(ex.Message, Does.Contain("presentation text catalog"));
        }

        [Test]
        public void S3_2_MapBatch_AddressableChildrenWithoutInstanceId_Throws()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, BatchRootTemplates);
            var map = new MapConfig { Id = "s3_map_batch_noid" };
            map.Entities.Add(new EntitySpawnData { Template = "s3.batchroot" });
            map.Entities.Add(new EntitySpawnData { Template = "s3.batchroot" });

            // instanceId 门必须盖住 batch 路径：此前 batch 摆放在 continue 时绕过慢速路径上的门。
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("addressable descendants"));
            Assert.That(ex.Message, Does.Contain("s3.batchroot"));
        }

        [Test]
        public void S3_3_RuntimeBatch_TemplateWithChildren_ExpandsAllDescendants()
        {
            using Harness harness = Harness.Create(BatchRootTemplates);
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "s3.batchroot",
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
                InstanceId = "g1",
            }), Is.True);
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "s3.batchroot",
                WorldPositionCm = Fix64Vec2.FromInt(200, 0),
                HasWorldPosition = 1,
                InstanceId = "g2",
            }), Is.True);

            for (int i = 0; i < 6; i++)
            {
                harness.System.Update(0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(CountByName(harness.World, "S3BatchRoot"), Is.EqualTo(2), "两个同模板请求应走单实体 lane（batch 合同排除带 children 模板）");
                Assert.That(CountByName(harness.World, "S3Leaf"), Is.EqualTo(2), "运行时任何 lane 都必须展开模板 children（单实体 lane 兜底）");
                Assert.That(harness.Requests.Count, Is.EqualTo(0), "子代请求由同一队列自消费，必须排空");
            });
        }

        [Test]
        public void S3_4_RuntimeCycleThroughInlineChildRef_FailsFastAtRootUpdate()
        {
            // 环经"内联 child 的模板引用"边到达：carrier2 自身无模板 children，
            // 环藏在 InlineChildren -> cyc3.x -> cyc3.x（自引用）。守卫必须连内联引用一起查
            // （此前只查模板 children，内联引用的环要晚一步 drain 才现形）。
            using Harness harness = Harness.Create(InlineCycleTemplates);

            var inline = new List<EntityTemplateChild>
            {
                new EntityTemplateChild { LocalId = "a", Template = "cyc3.x", LocalPose = PoseLiteral() },
            };
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "cyc3.carrier2",
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
                InstanceId = "g1",
                InlineChildren = inline,
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(0f))!;

            Assert.That(ex.Message, Does.Contain("环"), "经由内联引用的环必须在根出生这一拍就报'环'，而不是晚一步以队列溢出表达");
            Assert.That(ex.Message, Does.Contain("cyc3.x"));
        }

        private static EntityTemplateLocalPose PoseLiteral() => new EntityTemplateLocalPose
        {
            OffsetXCm = 10,
            OffsetYCm = 0,
            FacingDeg = 0,
            InheritParentFacing = false,
            OffsetRotation = "None",
        };

        private static readonly string NestedInlineCycleTemplates = """
        [
          { "id": "s3c.a", "components": { "Name": { "Value": "S3CRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "template": "s3c.b", "localPose": { "offsetXCm": 10, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" },
              "children": [ { "template": "s3c.a", "localPose": { "offsetXCm": 10, "offsetYCm": 0, "facingDeg": 0, "inheritParentFacing": false, "offsetRotation": "None" } } ] } ] },
          { "id": "s3c.b", "components": { "Name": { "Value": "S3CLeaf" } } }
        ]
        """;

        [Test]
        [Timeout(15000)]
        public void S3_5_RuntimeNestedInlineCycle_FailsFastInsteadOfInfiniteLoop()
        {
            // 环藏在 A 的 child（模板 B）的内联 children 里再指回 A——只走被引用模板
            // children 的守卫会漏，Update 将无限自旋。装载期/运行时都必须报'环'。
            using Harness harness = Harness.Create(NestedInlineCycleTemplates);
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "s3c.a",
                WorldPositionCm = Fix64Vec2.FromInt(0, 0),
                HasWorldPosition = 1,
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                harness.System.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("环"));
        }
        private static EntitySpawnData CreateNamedCamp(string instanceId, string titleToken, bool renameChildren)
        {
            var spawn = new EntitySpawnData
            {
                InstanceId = instanceId,
                Template = "name.camp",
                TitleToken = titleToken,
            };
            if (renameChildren)
            {
                spawn.OverridePaths = new List<EntityPathNameOverride>
                {
                    InfoPath("hq", "camp.harbor.hq.title"),
                    InfoPath("radio.coil", "camp.harbor.coil.title"),
                    InfoPath("guard", "camp.harbor.guard.title"),
                };
            }

            return spawn;
        }

        private static MapConfig MapWithGuardSet(string mapId, string key, JsonNode value)
        {
            EntitySpawnData spawn = CreateNamedCamp("camp.harbor", "camp.harbor.title", renameChildren: false);
            spawn.OverridePaths = new List<EntityPathNameOverride>
            {
                new EntityPathNameOverride
                {
                    Path = "guard",
                    TitleToken = "camp.harbor.guard.title",
                    Set = new Dictionary<string, JsonNode>
                    {
                        [key] = value,
                    },
                },
            };
            var map = new MapConfig { Id = mapId };
            map.Entities.Add(spawn);
            return map;
        }

        private static EntityPathNameOverride InfoPath(string path, string titleToken)
        {
            return new EntityPathNameOverride
            {
                Path = path,
                TitleToken = titleToken,
            };
        }

        private static void AssertInfo(World world, Entity entity, string systemName, int titleTokenId)
        {
            Assert.That(world.Get<Name>(entity).Value, Is.EqualTo(systemName));
            if (titleTokenId <= 0)
            {
                Assert.That(world.Has<EntityInfoTitleToken>(entity), Is.False);
                return;
            }

            Assert.That(world.Get<EntityInfoTitleToken>(entity).TokenId, Is.EqualTo(titleTokenId));
        }

        private static PresentationTextCatalog CreateCampTitleCatalog()
        {
            string[] keys =
            {
                "camp.harbor.title",
                "camp.alpine.title",
                "camp.harbor.hq.title",
                "camp.harbor.coil.title",
                "camp.harbor.guard.title",
            };
            var tokenIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var tokens = new PresentationTextTokenDefinition[keys.Length + 1];
            for (int i = 0; i < keys.Length; i++)
            {
                int id = tokenIds.Register(keys[i]);
                tokens[id] = new PresentationTextTokenDefinition { TokenId = id, Key = keys[i], ArgCount = 0 };
            }

            var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int en = localeIds.Register("en-US");
            int zh = localeIds.Register("zh-CN");
            var enTemplates = new PresentationTextTemplate[tokenIds.Count + 1];
            var zhTemplates = new PresentationTextTemplate[tokenIds.Count + 1];
            for (int id = 1; id <= keys.Length; id++)
            {
                enTemplates[id] = Literal(keys[id - 1] + ".en");
                zhTemplates[id] = Literal(keys[id - 1] + ".zh");
            }

            var locales = new PresentationTextLocaleTable[localeIds.Count + 1];
            locales[en] = new PresentationTextLocaleTable(en, "en-US", enTemplates);
            locales[zh] = new PresentationTextLocaleTable(zh, "zh-CN", zhTemplates);
            return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: en);
        }

        private static PresentationTextTemplate Literal(string text)
        {
            return new PresentationTextTemplate(
                text,
                new[] { new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, text, -1) });
        }

        private static Entity RequireInstance(MapLoadEntityIndex index, string instanceId)
        {
            Assert.That(index.TryGet(instanceId, out Entity entity), Is.True, instanceId);
            return entity;
        }

        private static Entity RequirePath(MapLoadEntityIndex index, string path)
        {
            Assert.That(index.TryGetByLocalPath(path, out Entity entity), Is.True, path);
            return entity;
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

        private static DataRegistry<EntityTemplate> LoadTemplates(string templatesJson)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_SliceAS3ReviewTests",
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

        private static MapLoader CreateMapLoader(World world, string templatesJson, PresentationTextCatalog textCatalog = null)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_SliceAS3ReviewTests",
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
                if (textCatalog != null)
                {
                    loader.SetPresentationTextCatalog(textCatalog);
                }

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
