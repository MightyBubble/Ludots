using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Dialect rename migration guards: the retired MapTrigger dialect name must be
    /// gone from the authoring surface (src dialect identifiers, assets registries,
    /// production graphs and maps) and must fail closed everywhere it can still be
    /// authored, with messages naming the rename.
    /// </summary>
    [TestFixture]
    public sealed class TriggerGraphRenameMigrationTests
    {
        private static readonly string[] DialectIdentifierNeedles =
        {
            "GraphKind.MapTrigger",
            "MapTriggerGraphMount",
            "MapTriggerGraphEntry",
            "MapTriggerGraphLimits",
            "MapTriggerGraphResume",
            "MapTriggerGraphRefire",
            "MapTriggerEntryFilters",
            "\"kind\": \"MapTrigger\"",
        };

        [Test]
        public void GraphKind_EnumHasTriggerGraph_AndNoMapTriggerMember()
        {
            string[] names = Enum.GetNames(typeof(GraphKind));

            Assert.That(names, Does.Contain("TriggerGraph"));
            Assert.That(names, Does.Not.Contain("MapTrigger"));
        }

        [Test]
        public void GraphKindParser_RetiredMapTriggerKind_FailsClosedNamingRename()
        {
            string graphId = "Graph.Rename.Probe";

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GraphKindParser.ParseRequired("MapTrigger", graphId))!;

            Assert.That(ex.Message, Does.Contain("MapTrigger"));
            Assert.That(ex.Message, Does.Contain("TriggerGraph"));
            Assert.That(ex.Message, Does.Contain(graphId));
        }

        [Test]
        public void SourceTree_HasNoDialectMapTriggerIdentifiers()
        {
            string repoRoot = FindRepoRoot();
            string srcRoot = Path.Combine(repoRoot, "src");
            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                        !Path.GetFileName(path).Equals("TriggerGraphRenameMigrationTests.cs", StringComparison.Ordinal)))
            {
                string text = File.ReadAllText(file);
                foreach (string needle in DialectIdentifierNeedles)
                {
                    if (text.Contains(needle, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {needle}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Retired MapTrigger dialect identifiers must not remain in src:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Test]
        public void CoverageRegistry_ContainsNoMapTriggerKind()
        {
            string repoRoot = FindRepoRoot();
            string registry = File.ReadAllText(Path.Combine(repoRoot, "assets", "GAS", "graph_node_op_coverage.registry.json"));

            Assert.That(registry.Contains("MapTrigger", StringComparison.Ordinal), Is.False,
                "The coverage registry authorableKinds projection must use the renamed TriggerGraph kind.");
        }

        [Test]
        public void ProductionGraphsAndMaps_UseRenamedKindAndMountField()
        {
            string repoRoot = FindRepoRoot();
            string[] graphFiles =
            {
                Path.Combine(repoRoot, "mods", "showcases", "panel_fireball_shared", "FireballSharedMod", "assets", "GAS", "graphs.json"),
                Path.Combine(repoRoot, "mods", "showcases", "map_trigger_night_raid", "MapTriggerNightRaidMod", "assets", "GAS", "graphs.json"),
            };
            string[] mapFiles =
            {
                Path.Combine(repoRoot, "mods", "showcases", "panel_fireball_shared", "FireballSharedMod", "assets", "Maps", "fireball_arena.json"),
                Path.Combine(repoRoot, "mods", "showcases", "map_trigger_night_raid", "MapTriggerNightRaidMod", "assets", "Maps", "night_raid.json"),
            };

            foreach (string graphFile in graphFiles)
            {
                string text = File.ReadAllText(graphFile);
                Assert.That(text.Contains("\"kind\": \"TriggerGraph\"", StringComparison.Ordinal), Is.True,
                    $"{graphFile} must author the renamed kind.");
                Assert.That(text.Contains("MapTrigger", StringComparison.Ordinal), Is.False,
                    $"{graphFile} must not carry the retired dialect name.");
            }

            foreach (string mapFile in mapFiles)
            {
                string text = File.ReadAllText(mapFile);
                Assert.That(text.Contains("\"TriggerGraphs\"", StringComparison.Ordinal), Is.True,
                    $"{mapFile} must use the renamed mount field.");
                Assert.That(text.Contains("MapTriggerGraphs", StringComparison.Ordinal), Is.False,
                    $"{mapFile} must not carry the retired mount field.");
            }
        }

        [Test]
        public void MapConfig_LegacyMountField_FailsClosedNamingRename()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_TriggerGraphRenameMigration", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tempRoot, "Maps"));
                File.WriteAllText(
                    Path.Combine(tempRoot, "Maps", "legacy_mount_map.json"),
                    """{ "id": "legacy_mount_map", "MapTriggerGraphs": [ { "graph": "Graph.Legacy.Mount" } ] }""");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var triggerManager = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), triggerManager);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var manager = new MapManager(vfs, triggerManager, modLoader, pipeline);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    manager.LoadMap("legacy_mount_map"))!;

                Assert.That(ex.Message, Does.Contain("MapTriggerGraphs"));
                Assert.That(ex.Message, Does.Contain("TriggerGraphs"));
            }
            finally
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void ParseObject_MountDomain_AuthoringRules()
        {
            JsonObject abilityWithoutScope = (JsonObject)JsonNode.Parse(
                """{ "graph": "Graph.Probe", "domain": "ability" }""")!;
            InvalidOperationException abilityScopeEx = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMount.ParseObject(abilityWithoutScope, "map probe"))!;
            Assert.That(abilityScopeEx.Message, Does.Contain("scopeInstanceId"));
            Assert.That(abilityScopeEx.Message, Does.Contain("map probe"));

            JsonObject abilityWithoutBinding = (JsonObject)JsonNode.Parse(
                """{ "graph": "Graph.Probe", "scopeInstanceId": "hero", "domain": "ability" }""")!;
            InvalidOperationException abilityBindingEx = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMount.ParseObject(abilityWithoutBinding, "map probe"))!;
            Assert.That(abilityBindingEx.Message, Does.Contain("ability"));
            Assert.That(abilityBindingEx.Message, Does.Contain("map probe"));

            JsonObject unknown = (JsonObject)JsonNode.Parse(
                """{ "graph": "Graph.Probe", "domain": "galaxy" }""")!;
            Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMount.ParseObject(unknown, "map probe"));

            JsonObject entityWithoutScope = (JsonObject)JsonNode.Parse(
                """{ "graph": "Graph.Probe", "domain": "entity" }""")!;
            InvalidOperationException entityEx = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMount.ParseObject(entityWithoutScope, "map probe"))!;
            Assert.That(entityEx.Message, Does.Contain("scopeInstanceId"));

            TriggerGraphMount mapDefault = TriggerGraphMount.ParseObject(
                (JsonObject)JsonNode.Parse("""{ "graph": "Graph.Probe" }""")!, "map probe");
            Assert.That(mapDefault.Domain, Is.EqualTo(TriggerGraphMountDomain.Map));

            TriggerGraphMount mapExplicit = TriggerGraphMount.ParseObject(
                (JsonObject)JsonNode.Parse("""{ "graph": "Graph.Probe", "domain": "map" }""")!, "map probe");
            Assert.That(mapExplicit.Domain, Is.EqualTo(TriggerGraphMountDomain.Map));

            TriggerGraphMount entity = TriggerGraphMount.ParseObject(
                (JsonObject)JsonNode.Parse("""{ "graph": "Graph.Probe", "scopeInstanceId": "hero", "domain": "entity" }""")!, "map probe");
            Assert.That(entity.Domain, Is.EqualTo(TriggerGraphMountDomain.Entity));

            TriggerGraphMount ability = TriggerGraphMount.ParseObject(
                (JsonObject)JsonNode.Parse(
                    """{ "graph": "Graph.Probe", "scopeInstanceId": "hero", "domain": "ability", "ability": "Ability.Probe" }"""),
                "map probe");
            Assert.That(ability.Domain, Is.EqualTo(TriggerGraphMountDomain.Ability));
            Assert.That(ability.Ability, Is.EqualTo("Ability.Probe"));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
