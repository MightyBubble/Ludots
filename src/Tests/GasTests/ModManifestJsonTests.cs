using System;
using NUnit.Framework;
using Ludots.Core.Modding;

namespace GasTests
{
    [TestFixture]
    public class ModManifestJsonTests
    {
        [Test]
        public void ParseStrict_WithCanonicalFields_Succeeds()
        {
            string json = """
            {
              "name": "ExampleMod",
              "version": "1.0.0",
              "description": "Demo",
              "main": "bin/Release/net8.0/ExampleMod.dll",
              "priority": 10,
              "dependencies": {
                "Core": "1.0.0"
              }
            }
            """;

            var manifest = ModManifestJson.ParseStrict(json, "mem://mod.json");

            Assert.That(manifest.Name, Is.EqualTo("ExampleMod"));
            Assert.That(manifest.Version, Is.EqualTo("1.0.0"));
            Assert.That(manifest.Main, Is.EqualTo("bin/Release/net8.0/ExampleMod.dll"));
            Assert.That(manifest.Priority, Is.EqualTo(10));
            Assert.That(manifest.Dependencies["Core"], Is.EqualTo("1.0.0"));
        }

        [Test]
        public void ParseStrict_WithLegacyUppercaseFields_Throws()
        {
            string json = """
            {
              "Id": "LegacyMod",
              "Version": "1.0.0",
              "Dependencies": []
            }
            """;

            var ex = Assert.Throws<Exception>(() => ModManifestJson.ParseStrict(json, "mem://legacy.mod.json"));
            Assert.That(ex!.Message, Does.Contain("unknown/forbidden field"));
        }

        [Test]
        public void ToCanonicalJson_EmitsLowercaseSchema()
        {
            var manifest = new ModManifest
            {
                Name = "CanonMod",
                Version = "1.2.3",
                Description = "Canon",
                Main = "bin/Release/net8.0/CanonMod.dll",
                Priority = 0
            };

            var json = ModManifestJson.ToCanonicalJson(manifest);

            Assert.That(json, Does.Contain("\"name\""));
            Assert.That(json, Does.Contain("\"version\""));
            Assert.That(json, Does.Contain("\"dependencies\""));
            Assert.That(json, Does.Not.Contain("\"Id\""));
            Assert.That(json, Does.Not.Contain("\"Version\""));
            Assert.That(json, Does.Not.Contain("\"Dependencies\""));
        }
    }
}
