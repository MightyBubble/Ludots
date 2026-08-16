using System;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ConfigCatalogStrictnessTests
    {
        [Test]
        public void CollectFragmentsWithSources_ResolvesMissingEntryAsEmptyFragments()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                var entry = new ConfigCatalogEntry("Missing/value.json", ConfigMergePolicy.DeepObject);

                var fragments = pipeline.CollectFragmentsWithSources(in entry);

                Assert.That(fragments, Is.Empty);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void CollectFragmentsWithSources_AllowsMissingWhenCatalogEntryAllowsEmpty()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                var entry = new ConfigCatalogEntry(
                    "Extension/empty.json",
                    ConfigMergePolicy.ArrayById,
                    allowEmpty: true);

                var fragments = pipeline.CollectFragmentsWithSources(in entry);

                Assert.That(fragments, Is.Empty);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void RequireEntry_RejectsPolicyMismatch()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/abilities.json", ConfigMergePolicy.DeepObject));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => ConfigPipeline.RequireEntry(catalog, "GAS/abilities.json", ConfigMergePolicy.ArrayById, "id"))!;

            Assert.That(ex.Message, Does.Contain("DeepObject"));
            Assert.That(ex.Message, Does.Contain("ArrayById"));
        }

        [Test]
        public void RequireEntry_RejectsIdFieldMismatch()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/abilities.json", ConfigMergePolicy.ArrayById, idField: "key"));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => ConfigPipeline.RequireEntry(catalog, "GAS/abilities.json", ConfigMergePolicy.ArrayById, "id"))!;

            Assert.That(ex.Message, Does.Contain("IdField 'id'"));
            Assert.That(ex.Message, Does.Contain("'key'"));
        }

        [Test]
        public void ConfigCatalogLoader_RejectsDuplicateRawPathsBeforeMerge()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                WriteCatalog(root, """
                [
                  { "Path": "GAS/abilities.json", "Policy": "ArrayById", "IdField": "id" },
                  { "Path": "GAS/abilities.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => ConfigCatalogLoader.Load(pipeline))!;

                Assert.That(ex.Message, Does.Contain("duplicate Path 'GAS/abilities.json'"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [TestCase("../bad.json")]
        [TestCase("/bad.json")]
        [TestCase("C:/bad.json")]
        public void ConfigCatalogLoader_RejectsInvalidCatalogPath(string invalidPath)
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                string jsonPath = JsonSerializer.Serialize(invalidPath);
                WriteCatalog(root, $"[{{ \"Path\": {jsonPath}, \"Policy\": \"DeepObject\" }}]");

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => ConfigCatalogLoader.Load(pipeline))!;

                Assert.That(ex.Message, Does.Contain("Path"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [TestCase("../GAS/abilities")]
        [TestCase("/GAS/abilities")]
        [TestCase("C:/GAS/abilities")]
        public void ConfigCatalogLoader_RejectsInvalidShardDirectoryPath(string invalidShardPath)
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                string shardPath = JsonSerializer.Serialize(invalidShardPath);
                WriteCatalog(root, $$"""
                [
                  {
                    "Path": "GAS/abilities.json",
                    "Policy": "ArrayById",
                    "IdField": "id",
                    "ShardDirectories": [ {{shardPath}} ]
                  }
                ]
                """);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => ConfigCatalogLoader.Load(pipeline))!;

                Assert.That(ex.Message, Does.Contain("ShardDirectories[0]"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void ConfigCatalogLoader_PreservesShardDirectoriesAndAllowEmpty()
        {
            string root = CreateTempRoot(out ConfigPipeline pipeline);
            try
            {
                WriteCatalog(root, """
                [
                  {
                    "Path": "GAS/abilities.json",
                    "Policy": "ArrayById",
                    "IdField": "id",
                    "ShardDirectories": [ "GAS/abilities" ],
                    "AllowEmpty": true
                  }
                ]
                """);

                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                Assert.That(catalog.TryGet("GAS/abilities.json", out ConfigCatalogEntry entry), Is.True);
                Assert.That(entry.AllowEmpty, Is.True);
                Assert.That(entry.ShardDirectories, Is.EqualTo(new[] { "GAS/abilities" }));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        private static string CreateTempRoot(out ConfigPipeline pipeline)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_ConfigCatalogStrictnessTests",
                Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            pipeline = new ConfigPipeline(vfs, modLoader);
            return root;
        }

        private static void WriteCatalog(string root, string contents)
        {
            string path = Path.Combine(root, "Core", "config_catalog.json");
            File.WriteAllText(path, contents);
        }

        private static void DeleteTempRoot(string root)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
