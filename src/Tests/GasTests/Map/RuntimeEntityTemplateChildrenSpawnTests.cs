using System;
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
    /// 切A 合前对抗性审查 S1：运行时出生路径必须与地图装载对齐——
    /// S1-1 递归展开内联 children（不丢孙代）、S1-2 attach:false 出生前 fail-fast、
    /// S1-3 带可寻址后代的实体必须有实例根（缺则 fail-fast）。
    /// </summary>
    [TestFixture]
    public sealed class RuntimeEntityTemplateChildrenSpawnTests
    {
        private const string Pose =
            "\"localPose\": { \"offsetXCm\": 10, \"offsetYCm\": 0, \"facingDeg\": 0, \"inheritParentFacing\": false, \"offsetRotation\": \"None\" }";

        // ---------------------------------------------------------------------
        // S1-1: 运行时出生递归展开内联 children 与被引用模板自身的 children
        // ---------------------------------------------------------------------

        private static readonly string RecursiveTemplates = """
        [
          {
            "id": "rt.root",
            "components": {
              "Name": { "Value": "Root" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } }
            },
            "children": [
              {
                "localId": "hq", "template": "rt.tent", {{POSE}},
                "children": [
                  {
                    "localId": "chest", "template": "rt.chest", {{POSE}},
                    "children": [ { "localId": "cargo", "template": "rt.cargo", {{POSE}} } ]
                  },
                  { "template": "rt.noid", {{POSE}} }
                ]
              },
              { "localId": "tower", "template": "rt.tower", {{POSE}} }
            ]
          },
          {
            "id": "rt.tent",
            "components": { "Name": { "Value": "Tent" } },
            "children": [ { "localId": "lamp", "template": "rt.lamp", {{POSE}} } ]
          },
          { "id": "rt.chest", "components": { "Name": { "Value": "Chest" } } },
          { "id": "rt.cargo", "components": { "Name": { "Value": "Cargo" } } },
          { "id": "rt.noid",  "components": { "Name": { "Value": "NoId" } } },
          { "id": "rt.tower", "components": { "Name": { "Value": "Tower" } } },
          { "id": "rt.lamp",  "components": { "Name": { "Value": "Lamp" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void RuntimeSpawn_InlineAndReferencedChildren_ExpandRecursivelyWithParentBeforeChild()
        {
            using Harness harness = Harness.Create(RecursiveTemplates);
            SpawnGroupRoot(harness, "rt.root", instanceId: "g1");

            Entity root = FindByName(harness.World, "Root");
            Entity tent = FindByName(harness.World, "Tent");
            Entity lamp = FindByName(harness.World, "Lamp");
            Entity chest = FindByName(harness.World, "Chest");
            Entity noId = FindByName(harness.World, "NoId");
            Entity cargo = FindByName(harness.World, "Cargo");
            Entity tower = FindByName(harness.World, "Tower");

            // 孙代（内联 chest→cargo）与“被引用模板自身的 child”（tent→lamp）都在，未静默丢失。
            Assert.That(harness.World.IsAlive(root) && harness.World.IsAlive(tent) && harness.World.IsAlive(lamp) &&
                        harness.World.IsAlive(chest) && harness.World.IsAlive(noId) && harness.World.IsAlive(cargo) &&
                        harness.World.IsAlive(tower), Is.True);

            // 父先于子。
            Assert.That(root.Id, Is.LessThan(tent.Id));
            Assert.That(root.Id, Is.LessThan(tower.Id));
            Assert.That(tent.Id, Is.LessThan(lamp.Id));
            Assert.That(tent.Id, Is.LessThan(chest.Id));
            Assert.That(tent.Id, Is.LessThan(noId.Id));
            Assert.That(chest.Id, Is.LessThan(cargo.Id));

            // 声明序：同层 sibling 按 children 数组序（root 的 hq 先于 tower）。
            Assert.That(tent.Id, Is.LessThan(tower.Id));
            // 同一节点：先展开被引用模板自身的 children（lamp），再展开内联 children（chest、noid）。
            // 队列复用 FIFO：跨 sibling 子树按入队序出队，故这里只锁 parent-before-child 与声明序，
            // 不锁 MapLoader 的跨子树 DFS 实体 id 序（见交付说明的偏差条目）。
            Assert.That(lamp.Id, Is.LessThan(chest.Id));
            Assert.That(chest.Id, Is.LessThan(noId.Id));

            // 父子链接落在正确的父实体上（孙代的 parent 是子代，不是组根）。
            Assert.That(harness.World.Get<ChildOf>(tent).Parent, Is.EqualTo(root));
            Assert.That(harness.World.Get<ChildOf>(tower).Parent, Is.EqualTo(root));
            Assert.That(harness.World.Get<ChildOf>(lamp).Parent, Is.EqualTo(tent));
            Assert.That(harness.World.Get<ChildOf>(chest).Parent, Is.EqualTo(tent));
            Assert.That(harness.World.Get<ChildOf>(noId).Parent, Is.EqualTo(tent));
            Assert.That(harness.World.Get<ChildOf>(cargo).Parent, Is.EqualTo(chest));
        }

        [Test]
        public void RuntimeSpawn_QueueDrainedOnce_ExpandsEachNodeExactlyOnce()
        {
            using Harness harness = Harness.Create(RecursiveTemplates);
            SpawnGroupRoot(harness, "rt.root", instanceId: "g1");

            Assert.That(harness.Requests.Count, Is.EqualTo(0), "the spawn queue must be fully drained");
            Assert.That(CountByName(harness.World, "Cargo"), Is.EqualTo(1), "each inline descendant materializes exactly once");
            Assert.That(CountByName(harness.World, "Tent"), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------------
        // S1-2: attach:false 出生前 fail-fast（切片E 前不支持）
        // ---------------------------------------------------------------------

        private static readonly string AttachFalseTemplates = """
        [
          {
            "id": "rt.dyn.root",
            "components": {
              "Name": { "Value": "DynRoot" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } }
            },
            "children": [
              { "localId": "guard", "template": "rt.dyn.unit", "attach": false, {{POSE}} }
            ]
          },
          { "id": "rt.dyn.unit", "components": { "Name": { "Value": "Guard" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void RuntimeSpawn_AttachFalseChild_FailsFastWithTemplateAndPathContext()
        {
            using Harness harness = Harness.Create(AttachFalseTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnGroupRoot(harness, "rt.dyn.root", instanceId: "g1"))!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AttachFalseUnsupported"));
            Assert.That(ex.Message, Does.Contain("rt.dyn.root"));
            Assert.That(ex.Message, Does.Contain("g1.guard"));
            Assert.That(ex.Message, Does.Contain("slice E"));
        }

        private static readonly string NestedAttachFalseTemplates = """
        [
          {
            "id": "rt.dyn2.root",
            "components": {
              "Name": { "Value": "Dyn2Root" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } }
            },
            "children": [
              {
                "localId": "hq", "template": "rt.dyn2.tent", {{POSE}},
                "children": [
                  { "localId": "guard", "template": "rt.dyn2.unit", "attach": false, {{POSE}} }
                ]
              }
            ]
          },
          { "id": "rt.dyn2.tent", "components": { "Name": { "Value": "Dyn2Tent" } } },
          { "id": "rt.dyn2.unit", "components": { "Name": { "Value": "Dyn2Guard" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void RuntimeSpawn_NestedAttachFalseDescendant_FailsFastWithAddressablePath()
        {
            using Harness harness = Harness.Create(NestedAttachFalseTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnGroupRoot(harness, "rt.dyn2.root", instanceId: "g1"))!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AttachFalseUnsupported"));
            Assert.That(ex.Message, Does.Contain("rt.dyn2.unit"));
            Assert.That(ex.Message, Does.Contain("g1.hq.guard"));
        }

        // ---------------------------------------------------------------------
        // S1-3: 带可寻址后代必须有实例根
        // ---------------------------------------------------------------------

        private static readonly string AddressableTemplates = """
        [
          {
            "id": "rt.addr.root",
            "components": {
              "Name": { "Value": "AddrRoot" },
              "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } }
            },
            "children": [ { "localId": "hq", "template": "rt.addr.leaf", {{POSE}} } ]
          },
          { "id": "rt.addr.leaf", "components": { "Name": { "Value": "AddrLeaf" } } }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void RuntimeSpawn_AddressableDescendantWithoutInstanceId_FailsFast()
        {
            using Harness harness = Harness.Create(AddressableTemplates);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => SpawnGroupRoot(harness, "rt.addr.root", instanceId: null))!;

            Assert.That(ex.Message, Does.Contain("SPAWN.RUNTIME.ERR.AddressableDescendantMissingInstanceId"));
            Assert.That(ex.Message, Does.Contain("rt.addr.root"));
            Assert.That(ex.Message, Does.Contain("InstanceId"));
        }

        // ---------------------------------------------------------------------
        // S1-3（装载侧）：缺 instanceId 的摆放 fail-fast；共用模板靠实例前缀天然不碰撞
        // ---------------------------------------------------------------------

        private static readonly string MapTemplates = """
        [
          {
            "id": "poi.root",
            "components": { "Name": { "Value": "PoiRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "localId": "hq", "template": "poi.leaf", {{POSE}} } ]
          },
          { "id": "poi.leaf", "components": { "Name": { "Value": "PoiLeaf" } } },
          {
            "id": "anon.root",
            "components": { "Name": { "Value": "AnonRoot" }, "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } } },
            "children": [ { "template": "poi.leaf", {{POSE}} } ]
          }
        ]
        """.Replace("{{POSE}}", Pose);

        [Test]
        public void MapLoad_AddressableDescendantWithoutInstanceId_Throws()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, MapTemplates);
            var map = new MapConfig { Id = "rt_addr_missing" };
            map.Entities.Add(new EntitySpawnData { Template = "poi.root" });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => loader.LoadEntitiesAndIndex(map))!;

            Assert.That(ex.Message, Does.Contain("poi.root"));
            Assert.That(ex.Message, Does.Contain("InstanceId"));
        }

        [Test]
        public void MapLoad_TwoPlacementsSharingTemplate_DistinctInstancePrefixesDoNotCollide()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, MapTemplates);
            var map = new MapConfig { Id = "rt_addr_two" };
            map.Entities.Add(new EntitySpawnData { InstanceId = "poi.alpha", Template = "poi.root" });
            map.Entities.Add(new EntitySpawnData { InstanceId = "poi.beta", Template = "poi.root" });

            MapLoadEntityIndex index = loader.LoadEntitiesAndIndex(map);

            Assert.That(index.Count, Is.EqualTo(2));
            Assert.That(index.LocalPathCount, Is.EqualTo(2));
            Assert.That(index.TryGetByLocalPath("poi.alpha.hq", out Entity alpha), Is.True);
            Assert.That(index.TryGetByLocalPath("poi.beta.hq", out Entity beta), Is.True);
            Assert.That(beta, Is.Not.EqualTo(alpha), "instance prefix is the namespace root; shared templates must not collide");
            Assert.That(world.Get<Name>(alpha).Value, Is.EqualTo("PoiLeaf"));
            Assert.That(world.Get<Name>(beta).Value, Is.EqualTo("PoiLeaf"));
        }

        [Test]
        public void MapLoad_AnonymousChildrenWithoutLocalId_NoInstanceIdIsAllowed()
        {
            using var world = World.Create();
            MapLoader loader = CreateMapLoader(world, MapTemplates);
            var map = new MapConfig { Id = "rt_addr_anon" };
            map.Entities.Add(new EntitySpawnData { Template = "anon.root" });

            MapLoadEntityIndex index = null!;
            Assert.DoesNotThrow(() => index = loader.LoadEntitiesAndIndex(map));
            Assert.That(index.Count, Is.EqualTo(0), "an anonymous placement contributes no flat InstanceId entry");
            Assert.That(index.LocalPathCount, Is.EqualTo(0), "children without localId are not addressable");
            Assert.That(CountMapEntities(world), Is.EqualTo(2), "root + anonymous child still materialize");
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private sealed class Harness : IDisposable
        {
            public World World = null!;
            public RuntimeEntitySpawnQueue Requests = null!;
            public RuntimeEntitySpawnSystem System = null!;

            public static Harness Create(string templatesJson)
            {
                DataRegistry<EntityTemplate> templates = LoadTemplates(templatesJson);
                World world = World.Create();
                var requests = new RuntimeEntitySpawnQueue(capacity: 64);
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

        private static void SpawnGroupRoot(Harness harness, string templateId, string? instanceId)
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
                "Ludots_RuntimeEntityTemplateChildrenSpawnTests",
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
                "Ludots_RuntimeEntityTemplateChildrenSpawnTests",
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

        private static int CountMapEntities(World world)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<MapEntity>();
            world.Query(in query, (Entity _) => count++);
            return count;
        }
    }
}
