using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationAssetConfigLoaderTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationAssetConfigLoader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [Test]
        public void MeshAssetConfigLoader_WhenCoreAndModDeclareSameLiveId_ThrowsWithBothSources()
        {
            WriteCoreConfig(
                "Presentation/mesh_assets.json",
                """
                [
                  { "id": "shared.model", "type": "Model" }
                ]
                """);
            WriteModAsset(
                "TestMod",
                "Presentation/mesh_assets.json",
                """
                [
                  { "id": "shared.model", "type": "Billboard" }
                ]
                """);

            var (_, pipeline, catalog) = BuildPipelineWithMods("TestMod");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, new MeshAssetRegistry()).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("duplicate id 'shared.model'"));
            Assert.That(ex.Message, Does.Contain("Core:Configs/Presentation/mesh_assets.json"));
            Assert.That(ex.Message, Does.Contain("TestMod:assets/Presentation/mesh_assets.json"));
        }

        [Test]
        public void MeshAssetConfigLoader_WhenModDeletesThenRedefinesId_LoadsRedefinition()
        {
            WriteCoreConfig(
                "Presentation/mesh_assets.json",
                """
                [
                  { "id": "shared.model", "type": "Model" }
                ]
                """);
            WriteModAsset(
                "TestMod",
                "Presentation/mesh_assets.json",
                """
                [
                  { "id": "shared.model", "__delete": true },
                  { "id": "shared.model", "type": "Billboard" }
                ]
                """);

            var (_, pipeline, catalog) = BuildPipelineWithMods("TestMod");
            var meshes = new MeshAssetRegistry();

            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            int id = meshes.GetId("shared.model");
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(meshes.TryGetDescriptor(id, out var descriptor), Is.True);
            Assert.That(descriptor.Type, Is.EqualTo(MeshAssetType.Billboard));
        }

        [Test]
        public void PresentationLodProfileConfigLoader_LoadsConfiguredProfile()
        {
            WriteCoreConfig(
                "Presentation/lod_profiles.json",
                """
                [
                  {
                    "id": "custom_surface_lod",
                    "high": { "maxDistanceCm": 1000, "minScreenCoverage01": 0.6 },
                    "medium": { "maxDistanceCm": 5000, "minScreenCoverage01": 0.2 },
                    "low": { "maxDistanceCm": 20000, "minScreenCoverage01": 0.01 }
                  }
                ]
                """);

            var (_, pipeline, catalog) = BuildPipelineWithMods();
            var profiles = new PresentationLodProfileRegistry();

            new PresentationLodProfileConfigLoader(pipeline, profiles).Load(catalog);

            Assert.That(profiles.TryGet("custom_surface_lod", out var profile), Is.True);
            Assert.That(profile.High.MaxDistanceCm, Is.EqualTo(1000f));
            Assert.That(profile.High.MinScreenCoverage01, Is.EqualTo(0.6f));
            Assert.That(profile.Medium.MaxDistanceCm, Is.EqualTo(5000f));
            Assert.That(profile.Low.MaxDistanceCm, Is.EqualTo(20000f));
        }

        [Test]
        public void PresentationLodProfileConfigLoader_WhenDistancesAreOutOfOrder_Throws()
        {
            WriteCoreConfig(
                "Presentation/lod_profiles.json",
                """
                [
                  {
                    "id": "bad_surface_lod",
                    "high": { "maxDistanceCm": 5000, "minScreenCoverage01": 0.6 },
                    "medium": { "maxDistanceCm": 4000, "minScreenCoverage01": 0.2 },
                    "low": { "maxDistanceCm": 20000, "minScreenCoverage01": 0.01 }
                  }
                ]
                """);

            var (_, pipeline, catalog) = BuildPipelineWithMods();
            var profiles = new PresentationLodProfileRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationLodProfileConfigLoader(pipeline, profiles).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("distances must increase"));
        }

        private (ModLoader ModLoader, ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipelineWithMods(params string[] modIds)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            for (int i = 0; i < modIds.Length; i++)
            {
                string modId = modIds[i];
                vfs.Mount(modId, Path.Combine(_root, modId));
                modLoader.LoadedModIds.Add(modId);
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = BuildPresentationCatalog();
            return (modLoader, pipeline, catalog);
        }

        private static ConfigCatalog BuildPresentationCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Presentation/mesh_assets.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry(PresentationLodProfileConfigLoader.DefaultRelativePath, ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private void WriteCoreConfig(string relativePath, string content)
        {
            WriteFile(Path.Combine(_root, "Core", "Configs"), relativePath, content);
        }

        private void WriteModAsset(string modId, string relativePath, string content)
        {
            WriteFile(Path.Combine(_root, modId, "assets"), relativePath, content);
        }

        private static void WriteFile(string basePath, string relativePath, string content)
        {
            string path = Path.Combine(basePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
