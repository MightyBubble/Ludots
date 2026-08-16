using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace GasTests.Physics2D
{
    [TestFixture]
    public sealed class Physics2DKinematicConfigLoaderTests
    {
        [Test]
        public void Load_ParsesExplicitKinematicConfig()
        {
            string root = CreateTempRoot();
            try
            {
                WriteKinematicConfig(root, @"{
  ""kinematicBodyCapacity"": 128,
  ""contactEventQueueCapacity"": 512,
  ""contactEventEmitterLayers"": [""PressurePlate"", ""Sensor""]
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);
                Physics2DKinematicConfig config = loader.Load(catalog);

                Assert.That(config.KinematicBodyCapacity, Is.EqualTo(128));
                Assert.That(config.ContactEventQueueCapacity, Is.EqualTo(512));
                Assert.That(config.ContactEventEmitterLayers, Is.EqualTo(new[] { "PressurePlate", "Sensor" }));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_FailsStartup_WhenConfigFileMissing()
        {
            string root = CreateTempRoot();
            try
            {
                // Catalog declares the entry, but no source provides the file: strict fail, no default injection.
                Directory.CreateDirectory(Path.Combine(root, "Configs"));
                File.WriteAllText(
                    Path.Combine(root, "Configs", "config_catalog.json"),
                    "[{ \"Path\": \"Physics2D/kinematic.json\", \"Policy\": \"DeepObject\" }]");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("required"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_FailsStartup_WhenCatalogDoesNotDeclareEntry()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs"));
                File.WriteAllText(Path.Combine(root, "Configs", "config_catalog.json"), "[]");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("Physics2D/kinematic.json"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsUnknownFields()
        {
            string root = CreateTempRoot();
            try
            {
                WriteKinematicConfig(root, @"{
  ""kinematicBodyCapacity"": 128,
  ""contactEventQueueCapacity"": 512,
  ""contactEventEmitterLayers"": [],
  ""unsupportedKnob"": true
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.TypeOf<System.Text.Json.JsonException>().With.Message.Contains("unsupportedKnob"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsMissingOrNonPositiveCapacities()
        {
            string root = CreateTempRoot();
            try
            {
                WriteKinematicConfig(root, @"{
  ""contactEventQueueCapacity"": 512,
  ""contactEventEmitterLayers"": []
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("kinematicBodyCapacity"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsMissingEmitterLayerList()
        {
            string root = CreateTempRoot();
            try
            {
                WriteKinematicConfig(root, @"{
  ""kinematicBodyCapacity"": 128,
  ""contactEventQueueCapacity"": 512
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("contactEventEmitterLayers"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsDuplicateEmitterLayers()
        {
            string root = CreateTempRoot();
            try
            {
                WriteKinematicConfig(root, @"{
  ""kinematicBodyCapacity"": 128,
  ""contactEventQueueCapacity"": 512,
  ""contactEventEmitterLayers"": [""Sensor"", ""Sensor""]
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("duplicate"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static Physics2DKinematicConfigLoader CreateLoader(string root, out ConfigCatalog catalog)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            catalog = ConfigCatalogLoader.Load(pipeline);
            return new Physics2DKinematicConfigLoader(pipeline);
        }

        private static void WriteKinematicConfig(string root, string kinematicJson)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Physics2D"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "config_catalog.json"),
                "[{ \"Path\": \"Physics2D/kinematic.json\", \"Policy\": \"DeepObject\" }]");
            File.WriteAllText(Path.Combine(root, "Configs", "Physics2D", "kinematic.json"), kinematicJson);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_Physics2DKinematicConfigLoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
