using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationHostAssetConfigLoaderTests
    {
        [Test]
        public void MeshAssetConfigLoader_WhenModelDeclaresSourceUris_ThrowsExplicitHostAssetError()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
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
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, meshes).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("Presentation/host_assets.json"));
        }

        [Test]
        public void Apply_WhenBackendMatches_InjectsHostUrisIntoExistingMeshDescriptor()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" },
                  { "id": "test.billboard", "type": "Billboard" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
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
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog);

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
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
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
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog));
            Assert.That(ex!.Message, Does.Contain("unknown mesh asset 'missing.model'"));
        }

        [TestCase(
            "{ \"id\": \"test.model.raylib\", \"assetId\": \"test.model\", \"backendId\": \"raylib\", \"sourceUris\": [ \"TestMod:assets/Models/test.glb\" ] }",
            "assetKind")]
        [TestCase(
            "{ \"id\": \"test.model.raylib\", \"assetKind\": \"mesh\", \"assetId\": \"test.model\", \"backendId\": \"raylib\", \"sourceUris\": [ \"TestMod:assets/Models/test.glb\" ] }",
            "unsupported assetKind 'mesh'")]
        [TestCase(
            "{ \"id\": \"test.model.raylib\", \"assetKind\": \"Mesh\", \"assetId\": \"test.model\", \"backendId\": \"raylib \", \"sourceUris\": [ \"TestMod:assets/Models/test.glb\" ] }",
            "backendId")]
        [TestCase(
            "{ \"id\": \"test.model.raylib\", \"assetKind\": \"Mesh\", \"assetId\": \"test.model\", \"backendId\": \"raylib\", \"sourceUris\": [ \" TestMod:assets/Models/test.glb\" ] }",
            "sourceUris[0]")]
        public void Apply_WhenHostAssetSchemaIsNotCanonical_Throws(string hostAssetRowJson, string expectedMessage)
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                $"[ {hostAssetRowJson} ]");

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog));
            Assert.That(ex!.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Apply_WhenRequestedBackendHasBoundaryWhitespace_Throws()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "test.model", "type": "Model" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "test.model.raylib",
                    "assetKind": "Mesh",
                    "assetId": "test.model",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Models/test.glb" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            Assert.That(
                () => new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib ", catalog),
                Throws.InvalidOperationException.With.Message.Contains("backendId"));
        }

        [Test]
        public void MaterialAssetConfigLoader_WhenMaterialDeclaresSourceUris_ThrowsExplicitHostAssetError()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "surface.grid",
                    "domain": "Surface",
                    "sourceUris": [ "TestMod:assets/Materials/surface.mat" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var materials = new PresentationMaterialRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("Presentation/host_assets.json"));
        }

        [Test]
        public void Apply_WhenMaterialBackendMatches_InjectsHostUrisIntoExistingMaterialDescriptor()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                "[]");
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "surface.grid",
                    "domain": "Surface",
                    "flags": [ "Transparent", "DoubleSided" ]
                  }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "surface.grid.raylib",
                    "assetKind": "Material",
                    "assetId": "surface.grid",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Materials/surface.mat" ]
                  },
                  {
                    "id": "surface.grid.ue5",
                    "assetKind": "Material",
                    "assetId": "surface.grid",
                    "backendId": "ue5",
                    "sourceUris": [ "ue5.material:/Game/Test/Surface.Surface" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);
            new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);

            int materialId = materials.GetId("surface.grid");
            Assert.That(materials.TryGet(materialId, out var semanticDescriptor), Is.True);
            Assert.That(semanticDescriptor.SourceUris, Is.Empty);

            new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog);

            Assert.That(materials.TryGet(materialId, out var boundDescriptor), Is.True);
            Assert.That(boundDescriptor.SourceUris, Is.EqualTo(new[] { "TestMod:assets/Materials/surface.mat" }));
            Assert.That(boundDescriptor.Flags, Is.EqualTo(MaterialAssetFlags.Transparent | MaterialAssetFlags.DoubleSided));
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

        private static ConfigCatalog BuildPresentationCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Presentation/mesh_assets.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Presentation/material_assets.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Presentation/host_assets.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }
    }
}
