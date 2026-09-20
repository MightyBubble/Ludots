using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.FieldRegions
{
    [TestFixture]
    public sealed class RegionHierarchyTests
    {
        private World _world = null!;
        private MapSession _session = null!;
        private FieldLayerRegistry _catalog = null!;
        private DiscreteIdFieldLayerData _layer = null!;
        private RegionEntityIndex _index = null!;

        private const string MapIdValue = "map_hierarchy_probe";

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _catalog = new FieldLayerRegistry();
            _catalog.Register(
                "layerX", FieldLayerKind.DiscreteId, cellSizeCm: 100, chunkSizeCells: 8,
                FieldLayerDefaultValue.None, persistent: true, "test.writer", maxRegionIds: 16);
            _session = new MapSession(new MapId(MapIdValue), new MapConfig());
            _session.Fields = FieldSessionStore.Create(_catalog, new[] { "layerX" });
            _layer = _session.Fields.Get<DiscreteIdFieldLayerData>(_catalog.GetId("layerX"));
            foreach (string key in new[] { "zone.a1", "zone.a2", "zone.b1" })
            {
                _layer.Regions.Register(key);
            }

            _index = FieldRegionMaterializer.Materialize(_world, _session);
            _session.RegionIndex = _index;
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            _world.Dispose();
        }

        [Test]
        public void Build_WiresChildOfEdges_AndCreatesCelllessGroupParents()
        {
            var runtime = RegionHierarchyBuilder.Build(_world, _session, new List<FieldHierarchyRoster>
            {
                new("group.alpha", new List<string> { "zone.a1", "zone.a2" }),
                new("group.beta", new List<string> { "zone.b1" }),
            });

            Entity alpha = runtime.GroupByKey["group.alpha"];
            Assert.That(_world.Has<ChildOf>(_index.TryResolve(_layer.LayerId, 1, out Entity a1) ? a1 : default), Is.True);
            Assert.That(_world.Get<ChildOf>(a1).Parent, Is.EqualTo(alpha));
            Assert.That(_world.Get<RegionGroupCm>(alpha).GroupKey, Is.EqualTo("group.alpha"));
            var members = new List<Entity>();
            Assert.That(RegionHierarchyBuilder.TryEnumerateGroupMembers(_world, alpha, members), Is.True);
            Assert.That(members, Has.Count.EqualTo(2));
        }

        [Test]
        public void Build_NestedRosters_ResolveRegardlessOfOrder()
        {
            var runtime = RegionHierarchyBuilder.Build(_world, _session, new List<FieldHierarchyRoster>
            {
                new("group.top", new List<string> { "group.mid" }),
                new("group.mid", new List<string> { "zone.a1" }),
            });

            var chain = new List<string>();
            _index.TryResolve(_layer.LayerId, 1, out Entity a1);
            Assert.That(RegionHierarchyBuilder.TryResolveChain(_world, a1, chain), Is.True);
            Assert.That(chain, Is.EqualTo(new[] { "zone.a1", "group.mid", "group.top" }));
            Assert.That(runtime.GroupByKey.ContainsKey("group.mid"), Is.True);
        }

        [Test]
        public void Build_ChildClaimedByTwoParents_FailsClosed()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionHierarchyBuilder.Build(
                _world, _session, new List<FieldHierarchyRoster>
                {
                    new("group.alpha", new List<string> { "zone.a1" }),
                    new("group.beta", new List<string> { "zone.a1" }),
                }));
            Assert.That(exception!.Message, Does.Contain("zone.a1"));
            Assert.That(exception.Message, Does.Contain("group.alpha"));
            Assert.That(exception.Message, Does.Contain("group.beta"));
        }

        [Test]
        public void Build_UnknownChildKey_FailsClosed()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionHierarchyBuilder.Build(
                _world, _session, new List<FieldHierarchyRoster>
                {
                    new("group.alpha", new List<string> { "zone.missing" }),
                }));
            Assert.That(exception!.Message, Does.Contain("zone.missing"));
        }

        [Test]
        public void Build_CycleAcrossRosters_FailsClosed()
        {
            Assert.Throws<InvalidOperationException>(() => RegionHierarchyBuilder.Build(
                _world, _session, new List<FieldHierarchyRoster>
                {
                    new("group.top", new List<string> { "group.mid" }),
                    new("group.mid", new List<string> { "group.top" }),
                }));
        }

        [Test]
        public void Build_EmptyRosters_NoEdgesNoGroups()
        {
            var runtime = RegionHierarchyBuilder.Build(_world, _session, new List<FieldHierarchyRoster>());
            Assert.That(runtime.GroupByKey, Is.Empty);
            var chain = new List<string>();
            _index.TryResolve(_layer.LayerId, 1, out Entity a1);
            Assert.That(RegionHierarchyBuilder.TryResolveChain(_world, a1, chain), Is.True);
            Assert.That(chain, Is.EqualTo(new[] { "zone.a1" }));
        }

        [Test]
        public void ReRoster_ChildMovesToNewParent_ChainFollows()
        {
            var runtime = RegionHierarchyBuilder.Build(_world, _session, new List<FieldHierarchyRoster>
            {
                new("group.alpha", new List<string> { "zone.a1", "zone.a2" }),
                new("group.alt", new List<string> { "zone.b1" }),
            });
            _index.TryResolve(_layer.LayerId, 1, out Entity a1);

            var rebuilt = RegionHierarchyBuilder.Build(_world, _session, new List<FieldHierarchyRoster>
            {
                new("group.alpha", new List<string> { "zone.a2" }),
                new("group.alt", new List<string> { "zone.b1", "zone.a1" }),
            });

            var chain = new List<string>();
            RegionHierarchyBuilder.TryResolveChain(_world, a1, chain);
            Assert.That(chain, Is.EqualTo(new[] { "zone.a1", "group.alt" }),
                "re-rostering moves the child; the child entity keeps its identity");
            var alphaMembers = new List<Entity>();
            RegionHierarchyBuilder.TryEnumerateGroupMembers(_world, rebuilt.GroupByKey["group.alpha"], alphaMembers);
            Assert.That(alphaMembers, Has.Count.EqualTo(1), "the parent that lost the child holds only the remaining member");
        }

        [Test]
        public void Chain_ForNonRegionEntity_ReturnsFalse()
        {
            var plain = _world.Create();
            Assert.That(RegionHierarchyBuilder.TryResolveChain(_world, plain, new List<string>()), Is.False);
        }
    }

    [TestFixture]
    public sealed class FieldHierarchyConfigLoaderTests
    {
        [Test]
        public void Load_LaterFragmentOverwritesChildren_FieldWise()
        {
            string root = CreateTempRoot();
            try
            {
                WriteHierarchy(root, "Core", """
                [ { "parent": "group.alpha", "children": [ "zone.a1" ] } ]
                """);
                WriteHierarchy(root, "ModA", """
                [ { "parent": "group.alpha", "children": [ "zone.a2", "zone.b1" ] } ]
                """);

                List<FieldHierarchyRoster> rosters = CreateLoader(root).Load();

                Assert.That(rosters, Has.Count.EqualTo(1));
                Assert.That(rosters[0].Children, Is.EqualTo(new[] { "zone.a2", "zone.b1" }),
                    "ArrayById field-wise semantics: the later fragment owns 'children' wholesale");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_MissingFile_IsEmpty()
        {
            string root = CreateTempRoot();
            try
            {
                Assert.That(CreateLoader(root).Load(), Is.Empty);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase("""[ { "parent": "group.alpha", "children": [] } ]""", "at least one")]
        [TestCase("""[ { "parent": "  ", "children": [ "zone.a1" ] } ]""", "non-empty string field 'parent'")]
        [TestCase("""[ { "parent": "group.alpha", "children": [ " zone.a1" ] } ]""", "whitespace")]
        [TestCase("""[ { "parent": "group.alpha", "children": [ "zone.a1" ], "surprise": 1 } ]""", "surprise")]
        public void Load_RejectsMalformedRosters(string json, string expectedMessagePart)
        {
            string root = CreateTempRoot();
            try
            {
                WriteHierarchy(root, "Core", json);
                var exception = Assert.Throws<InvalidOperationException>(() => CreateLoader(root).Load());
                Assert.That(exception!.Message, Does.Contain(expectedMessagePart));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static void CollectionAssertAreEquivalent(string[] expected, List<string> actual)
        {
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        private static FieldHierarchyConfigLoader CreateLoader(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "core"));
            vfs.Mount("ModA", Path.Combine(root, "modA"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("ModA");
            return new FieldHierarchyConfigLoader(new ConfigPipeline(vfs, modLoader));
        }

        private static void WriteHierarchy(string root, string source, string json)
        {
            string dir = source == "Core"
                ? Path.Combine(root, "core", "Fields")
                : Path.Combine(root, "modA", "assets", "Fields");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "hierarchies.json"), json);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_FieldHierarchyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "core"));
            Directory.CreateDirectory(Path.Combine(root, "modA"));
            File.WriteAllText(Path.Combine(root, "core", "config_catalog.json"),
                """[{ "Path": "Fields/hierarchies.json", "Policy": "ArrayById", "IdField": "parent" }]""");
            return root;
        }

        private static void TryDeleteDirectory(string path)
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
    }
}
