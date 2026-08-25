using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FieldLayerConfigLoaderTests
    {
        [Test]
        public void Load_ValidLayers_RegisterIntoInjectedRegistry()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);
                WriteLayers(root, """
                [
                  { "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "default": 5.5, "updateHz": 0, "persistent": false, "writerDomain": "test.writer" },
                  { "id": "layerY", "kind": "vector2", "cellSizeCm": 50, "chunkSizeCells": 4, "default": [1, 2], "writerDomain": "test.writer" },
                  { "id": "layerZ", "kind": "discreteId", "cellSizeCm": 25, "chunkSizeCells": 16, "default": 0, "writerDomain": "test.writer", "maxRegionIds": 64 }
                ]
                """);

                ConfigPipeline pipeline = CreatePipeline(root);
                var registry = new FieldLayerRegistry();
                new FieldLayerConfigLoader(pipeline, registry).Load(LoadCatalog(pipeline));

                Assert.That(registry.Count, Is.EqualTo(3));

                FieldLayerDefinition scalar = registry.Get(registry.GetId("layerX"));
                Assert.That(scalar.Id, Is.EqualTo(registry.GetId("layerX")));
                Assert.That(scalar.Kind, Is.EqualTo(FieldLayerKind.Scalar32));
                Assert.That(scalar.CellSizeCm, Is.EqualTo(100));
                Assert.That(scalar.ChunkSizeCells, Is.EqualTo(8));
                Assert.That(scalar.DefaultValue.Scalar, Is.EqualTo(5.5f));
                Assert.That(scalar.UpdateHz, Is.EqualTo(0));
                Assert.That(scalar.Persistent, Is.False);
                Assert.That(scalar.WriterDomain, Is.EqualTo("test.writer"));
                Assert.That(scalar.MaxRegionIds, Is.EqualTo(0), "non-discreteId layers carry no region capacity");

                FieldLayerDefinition vector = registry.Get(registry.GetId("layerY"));
                Assert.That(vector.Kind, Is.EqualTo(FieldLayerKind.Vector2));
                Assert.That(vector.DefaultValue.Vector2, Is.EqualTo(new Vector2(1f, 2f)));
                Assert.That(vector.UpdateHz, Is.EqualTo(0), "omitted updateHz means no tick simulation");
                Assert.That(vector.Persistent, Is.True, "omitted persistent defaults to true");

                FieldLayerDefinition discrete = registry.Get(registry.GetId("layerZ"));
                Assert.That(discrete.Kind, Is.EqualTo(FieldLayerKind.DiscreteId));
                Assert.That(discrete.MaxRegionIds, Is.EqualTo(64));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_KindValuesAreCaseInsensitive()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);
                WriteLayers(root, """
                [
                  { "id": "layerX", "kind": "VECTOR2", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2], "writerDomain": "test.writer" }
                ]
                """);

                ConfigPipeline pipeline = CreatePipeline(root);
                var registry = new FieldLayerRegistry();
                new FieldLayerConfigLoader(pipeline, registry).Load(LoadCatalog(pipeline));

                Assert.That(registry.Get(registry.GetId("layerX")).Kind, Is.EqualTo(FieldLayerKind.Vector2));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_OmittedDefaultsAndCapacity_ResolveToContractDefaults()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);
                WriteLayers(root, """
                [
                  { "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" },
                  { "id": "layerY", "kind": "vector3", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2, 3], "writerDomain": "test.writer" },
                  { "id": "layerZ", "kind": "discreteId", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }
                ]
                """);

                ConfigPipeline pipeline = CreatePipeline(root);
                var registry = new FieldLayerRegistry();
                new FieldLayerConfigLoader(pipeline, registry).Load(LoadCatalog(pipeline));

                Assert.That(registry.Get(registry.GetId("layerX")).DefaultValue.Scalar, Is.EqualTo(0f), "omitted scalar32 default is zero");
                FieldLayerDefinition vector = registry.Get(registry.GetId("layerY"));
                Assert.That(vector.DefaultValue.Vector3, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(vector.UpdateHz, Is.EqualTo(0));
                FieldLayerDefinition discrete = registry.Get(registry.GetId("layerZ"));
                Assert.That(discrete.MaxRegionIds, Is.EqualTo(256), "omitted maxRegionIds defaults to 256");
                Assert.That(discrete.DefaultValue.Scalar, Is.EqualTo(0f), "discreteId default is region id 0");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_MissingLayersFile_IsNoOp()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);

                ConfigPipeline pipeline = CreatePipeline(root);
                var registry = new FieldLayerRegistry();
                new FieldLayerConfigLoader(pipeline, registry).Load(LoadCatalog(pipeline));

                Assert.That(registry.Count, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_MalformedJson_Throws()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);
                Directory.CreateDirectory(Path.Combine(root, "Fields"));
                File.WriteAllText(Path.Combine(root, "Fields", "layers.json"), "{ this is not json");

                ConfigPipeline pipeline = CreatePipeline(root);
                Assert.Throws<JsonException>(
                    () => new FieldLayerConfigLoader(pipeline, new FieldLayerRegistry()).Load(LoadCatalog(pipeline)));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SameIdAcrossFragments_FieldWiseOverwrite_WinnerRecorded()
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: true);
                Directory.CreateDirectory(Path.Combine(root, "Fields", "layers_shards"));
                WriteLayers(root, """
                [
                  { "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }
                ]
                """);
                File.WriteAllText(Path.Combine(root, "Fields", "layers_shards", "override.json"), """
                [
                  { "id": "layerX", "cellSizeCm": 250, "updateHz": 3 }
                ]
                """);

                ConfigPipeline pipeline = CreatePipeline(root);
                var registry = new FieldLayerRegistry();
                var report = new ConfigConflictReport();
                new FieldLayerConfigLoader(pipeline, registry).Load(LoadCatalog(pipeline), report);

                Assert.That(registry.Count, Is.EqualTo(1));
                FieldLayerDefinition layer = registry.Get(registry.GetId("layerX"));
                Assert.That(layer.CellSizeCm, Is.EqualTo(250), "the later fragment overwrites field-wise");
                Assert.That(layer.UpdateHz, Is.EqualTo(3));
                Assert.That(layer.Kind, Is.EqualTo(FieldLayerKind.Scalar32), "fields absent from the later fragment are kept");
                Assert.That(layer.ChunkSizeCells, Is.EqualTo(8));
                Assert.That(layer.WriterDomain, Is.EqualTo("test.writer"));

                Assert.That(report.TryGetWinner("Fields/layers.json", "layerX", out string winner), Is.True);
                Assert.That(winner, Is.EqualTo("Core:Fields/layers_shards/override.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase("""{ "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""")]
        [TestCase("""{ "id": "", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""")]
        [TestCase("""{ "id": "   ", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""")]
        public void Load_MissingOrBlankId_Rejects(string layerJson)
        {
            AssertRejected(layerJson, expectedField: "id");
        }

        [Test]
        public void Load_WhitespacePaddedId_Rejects()
        {
            AssertRejected(
                """{ "id": " layerX ", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""",
                expectedId: "layerX",
                expectedField: "id");
        }

        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer", "surpriseField": 1 }""", "layerX", "surpriseField")]
        [TestCase("""{ "id": "layerX", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "kind")]
        [TestCase("""{ "id": "layerX", "kind": "hex", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "kind")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "cellSizeCm")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 0, "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "cellSizeCm")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": -5, "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "cellSizeCm")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 2.5, "chunkSizeCells": 8, "writerDomain": "test.writer" }""", "layerX", "cellSizeCm")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "writerDomain": "test.writer" }""", "layerX", "chunkSizeCells")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 0, "writerDomain": "test.writer" }""", "layerX", "chunkSizeCells")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": -8, "writerDomain": "test.writer" }""", "layerX", "chunkSizeCells")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 6, "writerDomain": "test.writer" }""", "layerX", "chunkSizeCells")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 2.5, "writerDomain": "test.writer" }""", "layerX", "chunkSizeCells")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "updateHz": -1, "writerDomain": "test.writer" }""", "layerX", "updateHz")]
        [TestCase("""{ "id": "layerX", "kind": "discreteId", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer", "maxRegionIds": 0 }""", "layerX", "maxRegionIds")]
        [TestCase("""{ "id": "layerX", "kind": "discreteId", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer", "maxRegionIds": -4 }""", "layerX", "maxRegionIds")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "test.writer", "maxRegionIds": 8 }""", "layerX", "maxRegionIds")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8 }""", "layerX", "writerDomain")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "" }""", "layerX", "writerDomain")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "writerDomain": "   " }""", "layerX", "writerDomain")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "default": "high", "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "scalar32", "cellSizeCm": 100, "chunkSizeCells": 8, "default": {}, "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector2", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector2", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2, 3], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector2", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, "x"], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector2", "cellSizeCm": 100, "chunkSizeCells": 8, "default": 5, "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector3", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "vector3", "cellSizeCm": 100, "chunkSizeCells": 8, "default": [1, 2, "x"], "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "discreteId", "cellSizeCm": 100, "chunkSizeCells": 8, "default": "r1", "writerDomain": "test.writer" }""", "layerX", "default")]
        [TestCase("""{ "id": "layerX", "kind": "discreteId", "cellSizeCm": 100, "chunkSizeCells": 8, "default": 5, "writerDomain": "test.writer" }""", "layerX", "default")]
        public void Load_RejectsInvalidLayers(string layerJson, string expectedId, string expectedField)
        {
            AssertRejected(layerJson, expectedId, expectedField);
        }

        private static void AssertRejected(string layerJson, string expectedId = "", string expectedField = "")
        {
            string root = CreateTempRoot();
            try
            {
                WriteCatalog(root, withShards: false);
                WriteLayers(root, $"[{layerJson}]");

                ConfigPipeline pipeline = CreatePipeline(root);
                var exception = Assert.Throws<InvalidOperationException>(
                    () => new FieldLayerConfigLoader(pipeline, new FieldLayerRegistry()).Load(LoadCatalog(pipeline)));
                Assert.That(exception, Is.Not.Null);
                if (expectedId.Length > 0)
                {
                    Assert.That(exception!.Message, Does.Contain(expectedId), "error must name the layer id");
                }

                Assert.That(exception!.Message, Does.Contain(expectedField), "error must name the offending field");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static ConfigPipeline CreatePipeline(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }

        private static ConfigCatalog LoadCatalog(ConfigPipeline pipeline) => ConfigCatalogLoader.Load(pipeline);

        private static void WriteCatalog(string root, bool withShards)
        {
            string catalogJson = withShards
                ? """[{ "Path": "Fields/layers.json", "Policy": "ArrayById", "IdField": "id", "ShardDirectories": ["Fields/layers_shards"] }]"""
                : """[{ "Path": "Fields/layers.json", "Policy": "ArrayById", "IdField": "id" }]""";
            File.WriteAllText(Path.Combine(root, "config_catalog.json"), catalogJson);
        }

        private static void WriteLayers(string root, string layersJson)
        {
            Directory.CreateDirectory(Path.Combine(root, "Fields"));
            File.WriteAllText(Path.Combine(root, "Fields", "layers.json"), layersJson);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_FieldLayerConfigTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
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
