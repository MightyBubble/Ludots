using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ConfigCatalogTests
    {
        [Test]
        public void ConfigMerger_ArrayById_MergesById()
        {
            var baseArr = JsonNode.Parse(@"[
  { ""id"": ""A"", ""X"": 1, ""Tags"": [""t0""] },
  { ""id"": ""B"", ""X"": 2 }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""id"": ""A"", ""X"": 9, ""Tags"": [""t1""] }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById)) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(2));
            Assert.That(merged[0]!["X"]!.ToString(), Is.EqualTo("9"));
            Assert.That(merged[0]!["Tags"]!.AsArray().Count, Is.EqualTo(1));
        }

        [Test]
        public void ConfigMerger_ArrayById_DeletesByDisabled()
        {
            var baseArr = JsonNode.Parse(@"[
  { ""id"": ""A"", ""X"": 1 },
  { ""id"": ""B"", ""X"": 2 }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""id"": ""B"", ""Disabled"": true }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById)) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(1));
            Assert.That(merged[0]!["id"]!.ToString(), Is.EqualTo("A"));
        }

        [Test]
        public void ConfigMerger_ArrayById_AppendsConfiguredArrayFields()
        {
            var baseArr = JsonNode.Parse(@"[
  { ""id"": ""A"", ""Tags"": [""t0""] }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""id"": ""A"", ""Tags"": [""t1""] }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById, arrayAppendFields: new[] { "Tags" })) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(1));
            Assert.That(merged[0]!["Tags"]!.AsArray().Count, Is.EqualTo(2));
        }

        [Test]
        public void ConfigMerger_ArrayById_IsCaseExact()
        {
            var baseArr = JsonNode.Parse(@"[
  { ""id"": ""Agent.Light"", ""X"": 1 }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""id"": ""agent.light"", ""X"": 2 }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById)) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(2));
            Assert.That(merged[0]!["id"]!.ToString(), Is.EqualTo("Agent.Light"));
            Assert.That(merged[1]!["id"]!.ToString(), Is.EqualTo("agent.light"));
        }

        [Test]
        public void ConfigPipeline_MergeFromCatalog_LoadsCoreAndMods()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModA");
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs"));

            Directory.CreateDirectory(Path.Combine(core, "Configs", "AI"));
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "atoms.json"), "[ { \"id\": \"A\" } ]");

            Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs", "AI"));
            File.WriteAllText(Path.Combine(mod, "assets", "Configs", "AI", "atoms.json"), "[ { \"id\": \"B\" } ]");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            vfs.Mount("ModA", mod);
            var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
            modLoader.LoadedModIds.Add("ModA");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var node = pipeline.MergeFromCatalog(new ConfigCatalogEntry("AI/atoms.json", ConfigMergePolicy.ArrayById));

            Assert.That(node, Is.TypeOf<JsonArray>());
            var arr = (JsonArray)node!;
            Assert.That(arr.Count, Is.EqualTo(2));
        }

        [Test]
        public void ConfigCatalog_PathsAreCaseExact()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("MassNavigationConfig.json", ConfigMergePolicy.DeepObject));

            Assert.That(catalog.TryGet("MassNavigationConfig.json", out _), Is.True);
            Assert.That(catalog.TryGet("massnavigationconfig.json", out _), Is.False);
        }

        [Test]
        public void ConfigCatalogLoader_RejectsPolicyCaseAlias()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string core = Path.Combine(root, "Core");
                Directory.CreateDirectory(Path.Combine(core, "Configs"));
                File.WriteAllText(
                    Path.Combine(core, "Configs", "config_catalog.json"),
                    "[{ \"Path\": \"MassNavigationConfig.json\", \"Policy\": \"replace\" }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ConfigCatalogLoader.Load(pipeline))!;
                Assert.That(ex.Message, Does.Contain("replace"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void ConfigCatalogLoader_RejectsMalformedCatalogEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string core = Path.Combine(root, "Core");
                Directory.CreateDirectory(Path.Combine(core, "Configs"));
                File.WriteAllText(
                    Path.Combine(core, "Configs", "config_catalog.json"),
                    "[{ \"Path\": \"MassNavigationConfig.json\" }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ConfigCatalogLoader.Load(pipeline))!;
                Assert.That(ex.Message, Does.Contain("Policy"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void GameConfig_LoadsCamelCaseAndRejectsPascalCaseAliases()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string core = Path.Combine(root, "Core");
                Directory.CreateDirectory(Path.Combine(core, "Configs"));
                File.WriteAllText(
                    Path.Combine(core, "Configs", "game.json"),
                    "{ \"startupMapId\": \"canonical_map\", \"windowWidth\": 1440 }");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var config = pipeline.MergeGameConfig();
                Assert.That(config.StartupMapId, Is.EqualTo("canonical_map"));
                Assert.That(config.WindowWidth, Is.EqualTo(1440));

                File.WriteAllText(
                    Path.Combine(core, "Configs", "game.json"),
                    "{ \"StartupMapId\": \"alias_map\" }");

                JsonException ex = Assert.Throws<JsonException>(() => pipeline.MergeGameConfig())!;
                Assert.That(ex.Message, Does.Contain("StartupMapId"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void DataRegistry_Load_RequiresExactCatalogEntryAndExactIdProperty()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ConfigCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string core = Path.Combine(root, "Core");
                Directory.CreateDirectory(Path.Combine(core, "Configs", "Entities"));
                File.WriteAllText(
                    Path.Combine(core, "Configs", "config_catalog.json"),
                    "[{ \"Path\": \"Entities/templates.json\", \"Policy\": \"ArrayById\", \"IdField\": \"id\" }]");
                File.WriteAllText(
                    Path.Combine(core, "Configs", "Entities", "templates.json"),
                    "[{ \"id\": \"case.exact.template\", \"components\": {} }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var templates = new DataRegistry<EntityTemplate>(pipeline);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => templates.Load("entities/templates.json", catalog))!;
                Assert.That(ex.Message, Does.Contain("entities/templates.json"));

                templates.Load("Entities/templates.json", catalog);
                Assert.That(templates.Contains("case.exact.template"), Is.True);
                Assert.That(templates.Contains("Case.Exact.Template"), Is.False);
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
