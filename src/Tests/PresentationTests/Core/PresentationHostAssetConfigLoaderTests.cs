using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationHostAssetConfigLoaderTests
    {
        private const int TestRuntimeFormatVersion = 1810;

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
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "material_assets.json"),
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
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "mesh_assets.json"),
                "[]");
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "material_assets.json"),
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
                Path.Combine(root, "Configs", "Presentation", "host_assets.json"),
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

        [TestCase(AssetKind.SpriteEmitter)]
        [TestCase(AssetKind.RibbonEmitter)]
        [TestCase(AssetKind.ModelEmitter)]
        [TestCase(AssetKind.TrackEmitter)]
        [TestCase(AssetKind.RingEmitter)]
        public void Apply_WhenEmitterBackendMatches_BindsSourceUrisWithoutChangingIntegrityContract(AssetKind emitterKind)
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            string assetKey = $"emitter.{emitterKind}";
            string sourceUri = $"TestMod:assets/Presentation/{emitterKind}.efkefc";
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "host_assets.json"),
                $$"""
                [
                  {
                    "id": "{{assetKey}}.raylib",
                    "assetKind": "{{emitterKind}}",
                    "assetId": "{{assetKey}}",
                    "backendId": "raylib",
                    "sourceUris": [ "{{sourceUri}}" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var emitters = new EmitterAssetRegistry();
            string sha256 = new string('a', 64);
            int id = emitters.Register(
                assetKey,
                emitterKind,
                TestRuntimeFormatVersion,
                sha256);

            new PresentationHostAssetConfigLoader(
                pipeline,
                new MeshAssetRegistry(),
                new PresentationMaterialRegistry(),
                emitters).Apply("raylib", BuildPresentationCatalog());

            Assert.That(emitters.Count, Is.EqualTo(1));
            Assert.That(emitters.TryGetDescriptor(id, out EmitterAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.AssetKind, Is.EqualTo(emitterKind));
            Assert.That(descriptor.RuntimeFormatVersion, Is.EqualTo(TestRuntimeFormatVersion));
            Assert.That(descriptor.Sha256, Is.EqualTo(sha256));
            Assert.That(descriptor.SourceUris, Is.EqualTo(new[] { sourceUri }));
        }

        [Test]
        public void Apply_WhenEmitterKindDoesNotMatchSemanticDescriptor_Throws()
        {
            string root = CreateTempCoreRoot();
            WriteEmitterHostAssets(root, "TrackEmitter", "trail.ribbon");
            var emitters = new EmitterAssetRegistry();
            emitters.Register(
                "trail.ribbon",
                AssetKind.RibbonEmitter,
                TestRuntimeFormatVersion,
                new string('b', 64));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(
                    BuildCorePipeline(root),
                    new MeshAssetRegistry(),
                    new PresentationMaterialRegistry(),
                    emitters).Apply("raylib", BuildPresentationCatalog()))!;

            Assert.That(ex.Message, Does.Contain("not requested kind 'TrackEmitter'"));
        }

        [Test]
        public void Apply_WhenEmitterBindingHasNoEmitterRegistry_Throws()
        {
            string root = CreateTempCoreRoot();
            WriteEmitterHostAssets(root, "TrackEmitter", "beam.core");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(
                    BuildCorePipeline(root),
                    new MeshAssetRegistry(),
                    new PresentationMaterialRegistry()).Apply("raylib", BuildPresentationCatalog()))!;

            Assert.That(ex.Message, Does.Contain("no EmitterAssetRegistry was supplied"));
        }

        [Test]
        public void Apply_WhenEmitterAssetIsUnknown_Throws()
        {
            string root = CreateTempCoreRoot();
            WriteEmitterHostAssets(root, "TrackEmitter", "beam.missing");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(
                    BuildCorePipeline(root),
                    new MeshAssetRegistry(),
                    new PresentationMaterialRegistry(),
                    new EmitterAssetRegistry()).Apply("raylib", BuildPresentationCatalog()))!;

            Assert.That(ex.Message, Does.Contain("Unknown emitter asset 'beam.missing'"));
        }

        private static void WriteEmitterHostAssets(string root, string assetKind, string assetId)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "Presentation", "host_assets.json"),
                $$"""
                [
                  {
                    "id": "{{assetId}}.raylib",
                    "assetKind": "{{assetKind}}",
                    "assetId": "{{assetId}}",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Presentation/test.efkefc" ]
                  }
                ]
                """);
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
