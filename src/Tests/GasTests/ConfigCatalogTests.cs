using System;
using System.IO;
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
  { ""Id"": ""A"", ""X"": 1, ""Tags"": [""t0""] },
  { ""Id"": ""B"", ""X"": 2 }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""Id"": ""A"", ""X"": 9, ""Tags"": [""t1""] }
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
  { ""Id"": ""A"", ""X"": 1 },
  { ""Id"": ""B"", ""X"": 2 }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""Id"": ""B"", ""Disabled"": true }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById)) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(1));
            Assert.That(merged[0]!["Id"]!.ToString(), Is.EqualTo("A"));
        }

        [Test]
        public void ConfigMerger_ArrayById_AppendsConfiguredArrayFields()
        {
            var baseArr = JsonNode.Parse(@"[
  { ""Id"": ""A"", ""Tags"": [""t0""] }
]")!;

            var modArr = JsonNode.Parse(@"[
  { ""Id"": ""A"", ""Tags"": [""t1""] }
]")!;

            var merged = ConfigMerger.MergeMany(
                new[] { baseArr, modArr },
                new ConfigCatalogEntry("x.json", ConfigMergePolicy.ArrayById, arrayAppendFields: new[] { "Tags" })) as JsonArray;

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged!.Count, Is.EqualTo(1));
            Assert.That(merged[0]!["Tags"]!.AsArray().Count, Is.EqualTo(2));
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
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "atoms.json"), "[ { \"Id\": \"A\" } ]");

            Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs", "AI"));
            File.WriteAllText(Path.Combine(mod, "assets", "Configs", "AI", "atoms.json"), "[ { \"Id\": \"B\" } ]");

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
    }
}
