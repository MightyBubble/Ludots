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
    public sealed class Physics2DClockConfigLoaderTests
    {
        [Test]
        public void Load_ParsesBroadphaseConfigThroughConfigPipeline()
        {
            string root = CreateTempRoot();
            try
            {
                WritePhysicsClockConfig(root, @"{
  ""PhysicsHz"": 15,
  ""MaxStepsPerFixedTick"": 8,
  ""Broadphase"": {
    ""Strategy"": ""UniformGrid"",
    ""CellSizeCm"": 512
  }
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);
                Physics2DClockConfig config = loader.Load(catalog);

                Assert.That(config.PhysicsHz, Is.EqualTo(15));
                Assert.That(config.MaxStepsPerFixedTick, Is.EqualTo(8));
                Assert.That(config.Broadphase.Strategy, Is.EqualTo(Physics2DBroadphaseStrategyKind.UniformGrid));
                Assert.That(config.Broadphase.CellSizeCm, Is.EqualTo(512));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsUnknownPhysicsClockFields()
        {
            string root = CreateTempRoot();
            try
            {
                WritePhysicsClockConfig(root, @"{
  ""PhysicsHz"": 15,
  ""MaxStepsPerFixedTick"": 8,
  ""Unsupported"": true
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.TypeOf<System.Text.Json.JsonException>().With.Message.Contains("Unsupported"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Load_RejectsInvalidBroadphaseCellSize()
        {
            string root = CreateTempRoot();
            try
            {
                WritePhysicsClockConfig(root, @"{
  ""PhysicsHz"": 15,
  ""MaxStepsPerFixedTick"": 8,
  ""Broadphase"": {
    ""Strategy"": ""UniformGrid"",
    ""CellSizeCm"": 0
  }
}");

                var loader = CreateLoader(root, out ConfigCatalog catalog);

                Assert.That(
                    () => loader.Load(catalog),
                    Throws.InvalidOperationException.With.Message.Contains("CellSizeCm"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static Physics2DClockConfigLoader CreateLoader(string root, out ConfigCatalog catalog)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            catalog = ConfigCatalogLoader.Load(pipeline);
            return new Physics2DClockConfigLoader(pipeline);
        }

        private static void WritePhysicsClockConfig(string root, string clockJson)
        {
            Directory.CreateDirectory(Path.Combine(root, "Physics2D"));
            File.WriteAllText(
                Path.Combine(root, "config_catalog.json"),
                "[{ \"Path\": \"Physics2D/clock.json\", \"Policy\": \"DeepObject\" }]");
            File.WriteAllText(Path.Combine(root, "Physics2D", "clock.json"), clockJson);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_Physics2DClockConfigLoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
