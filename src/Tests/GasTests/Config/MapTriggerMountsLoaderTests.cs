using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Config
{
    /// <summary>
    /// Loads the mod mount family (GAS/map_trigger_mounts.json) into the
    /// TriggerGraphMountTable, attributing each mount to its authoring mod and failing
    /// closed on duplicates, missing ids, non-array fragments, and domain violations.
    /// </summary>
    [TestFixture]
    public sealed class MapTriggerMountsLoaderTests
    {
        private const string RelativePath = "GAS/map_trigger_mounts.json";
        private const string GraphName = "Graph.TriggerGraph.LoaderProbe";
        private const string MapId = "loader_probe_map";

        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_MapTriggerMountsLoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Core"));
            Directory.CreateDirectory(Path.Combine(_root, "MountMod", "assets", "GAS"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void Load_ModFamilyMounts_AttributedToOwningMod()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "id": "wave", "graph": "Graph.TriggerGraph.LoaderProbe", "priority": 3, "domain": "mod" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            LoadInto(table);

            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, null);

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].OwnerModId, Is.EqualTo("MountMod"), "Mount must be attributed to the mod that authored it.");
            That(resolved[0].Mount.Id, Is.EqualTo("wave"));
        }

        [Test]
        public void Load_ReplacesTargetingBaseResolves()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                $$"""
                [
                  { "id": "replacer", "graph": "Graph.TriggerGraph.Replacer", "replaces": "{{MapId}}.base", "domain": "mod" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            LoadInto(table);

            var baseMount = TriggerGraphMount.ParseObject(
                System.Text.Json.Nodes.JsonNode.Parse($$"""{ "id": "base", "graph": "Graph.TriggerGraph.Base" }""")!.AsObject(),
                "ctx");
            IReadOnlyList<ResolvedTriggerGraphMount> resolved = table.ResolveForMap(MapId, new[] { baseMount });

            That(resolved.Count, Is.EqualTo(1));
            That(resolved[0].Mount.Id, Is.EqualTo("replacer"), "Explicit replaces must drop the base mount.");
        }

        [Test]
        public void Load_UnknownField_FailsClosed()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "id": "wave", "graph": "Graph.TriggerGraph.LoaderProbe", "bogus": 1 }
                ]
                """);

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("bogus"));
        }

        [Test]
        public void Load_MissingId_FailsClosed()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "graph": "Graph.TriggerGraph.LoaderProbe" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("requires field 'id'"));
        }

        [Test]
        public void Load_EntityDomain_FailsClosedInModFamily()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "id": "ent", "graph": "Graph.TriggerGraph.LoaderProbe", "domain": "entity", "scopeInstanceId": "x" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("entity"));
        }

        [Test]
        public void Load_NonArrayFragment_FailsClosed()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts("{ \"not\": \"an array\" }");

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("array"));
        }

        [Test]
        public void Load_DuplicateKeyWithinFragment_FailsClosed()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "id": "shared", "graph": "Graph.TriggerGraph.A", "domain": "mod" },
                  { "id": "shared", "graph": "Graph.TriggerGraph.B", "domain": "mod" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("MountMod.shared"));
            That(ex.Message, Does.Contain("duplicate"));
        }

        [Test]
        public void Load_MapDomainWithoutMap_FailsClosed()
        {
            WriteCatalogEntry();
            WriteModManifest("MountMod");
            WriteModMounts(
                """
                [
                  { "id": "mapd", "graph": "Graph.TriggerGraph.LoaderProbe", "domain": "map" }
                ]
                """);

            var table = new TriggerGraphMountTable();
            var ex = Throws<InvalidOperationException>(() => LoadInto(table));
            That(ex!.Message, Does.Contain("requires field 'map'"));
        }

        private void LoadInto(TriggerGraphMountTable table, params string[] modIds)
        {
            string[] effective = modIds.Length == 0 ? new[] { "MountMod" } : modIds;
            var (pipeline, catalog) = BuildPipeline(effective);
            var loader = new MapTriggerMountsLoader(pipeline, table);
            loader.Load(catalog);
        }

        private (ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline(string[] modIds)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            string[] modDirs = modIds
                .Select(id => Path.Combine(_root, id))
                .ToArray();
            modLoader.LoadMods(modDirs);
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return (pipeline, ConfigCatalogLoader.Load(pipeline));
        }

        private void WriteCatalogEntry()
        {
            File.WriteAllText(
                Path.Combine(_root, "Core", "config_catalog.json"),
                $$"""[ { "Path": "{{RelativePath}}", "Policy": "ArrayById", "IdField": "id" } ]""");
        }

        private void WriteModManifest(string modId)
        {
            string dir = Path.Combine(_root, modId);
            Directory.CreateDirectory(Path.Combine(dir, "assets", "GAS"));
            File.WriteAllText(
                Path.Combine(dir, "mod.json"),
                $$"""
                {
                  "name": "{{modId}}",
                  "version": "1.0.0",
                  "description": "Asset-only mount loader fixture"
                }
                """);
        }

        private void WriteModMounts(string json) => WriteModMountsFor("MountMod", json);

        private void WriteModMountsFor(string modId, string json)
        {
            string dir = Path.Combine(_root, modId, "assets", "GAS");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "map_trigger_mounts.json"), json);
        }
    }
}
