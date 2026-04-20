using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationHostAssetConfigLoaderTests
    {
        [Test]
        public void MeshAssetConfigLoader_WhenModelDeclaresSourceUris_ThrowsExplicitHostAssetError()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "mesh_assets.json"),
                """
                [
                  {
                    "id": "test.model",
                    "type": "Model",
                    "sourceUris": [ "TestMod:assets/Models/test.glb" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var meshes = new MeshAssetRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, meshes).Load());
            Assert.That(ex!.Message, Does.Contain("Presentation/host_assets.json"));
        }

        [Test]
        public void Apply_WhenBackendMatches_InjectsHostUrisIntoExistingMeshDescriptor()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" },
                  { "id": "test.billboard", "type": "Billboard" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "test.model.raylib",
                    "assetKind": "Mesh",
                    "assetId": "test.model",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Models/test.glb" ]
                  },
                  {
                    "id": "test.model.ue5",
                    "assetKind": "Mesh",
                    "assetId": "test.model",
                    "backendId": "ue5",
                    "sourceUris": [ "ue5.staticmesh:/Game/Test/Test.Test" ]
                  },
                  {
                    "id": "test.billboard.raylib",
                    "assetKind": "Mesh",
                    "assetId": "test.billboard",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Textures/test.png" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var meshes = new MeshAssetRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load();

            new PresentationHostAssetConfigLoader(pipeline, meshes).Apply("raylib");

            int modelId = meshes.GetId("test.model");
            Assert.That(meshes.TryGetDescriptor(modelId, out var model), Is.True);
            Assert.That(model.SourceUris, Is.EqualTo(new[] { "TestMod:assets/Models/test.glb" }));

            int billboardId = meshes.GetId("test.billboard");
            Assert.That(meshes.TryGetDescriptor(billboardId, out var billboard), Is.True);
            Assert.That(billboard.SourceUris, Is.EqualTo(new[] { "TestMod:assets/Textures/test.png" }));
        }

        [Test]
        public void Apply_WhenHostAssetTargetsUnknownMesh_ThrowsExplicitly()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "missing.raylib",
                    "assetKind": "Mesh",
                    "assetId": "missing.model",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Models/missing.glb" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var meshes = new MeshAssetRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(pipeline, meshes).Apply("raylib"));
            Assert.That(ex!.Message, Does.Contain("unknown mesh asset 'missing.model'"));
        }

        private static string CreateTempCoreRoot()
        {
            return Path.Combine(Path.GetTempPath(), "Ludots_HostAssetConfigTests", Guid.NewGuid().ToString("N"));
        }

        private static ConfigPipeline BuildCorePipeline(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            return new ConfigPipeline(vfs, modLoader: null!);
        }
    }
}
