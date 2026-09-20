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
                    "textures": { "albedo": "TestMod:assets/Materials/surface.mat" }
                  },
                  {
                    "id": "surface.grid.ue5",
                    "assetKind": "Material",
                    "assetId": "surface.grid",
                    "backendId": "ue5",
                    "textures": { "material": "ue5.material:/Game/Test/Surface.Surface" }
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
            Assert.That(materials.TryResolve(materialId, out var semanticOnly), Is.True);
            Assert.That(semanticOnly.TextureUris, Is.Empty);

            new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog);

            Assert.That(materials.TryResolve(materialId, out var bound), Is.True);
            Assert.That(bound.TextureUris["albedo"], Is.EqualTo("TestMod:assets/Materials/surface.mat"));
            Assert.That(bound.TextureUris.ContainsKey("material"), Is.False);
            Assert.That(bound.Flags, Is.EqualTo(MaterialAssetFlags.Transparent | MaterialAssetFlags.DoubleSided));
        }

        [Test]
        public void Apply_WhenMaterialRowDeclaresSourceUris_ThrowsNamedTexturesError()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  { "id": "surface.grid", "domain": "Surface" }
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
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog));
            Assert.That(ex!.Message, Does.Contain("textures"));
        }

        [Test]
        public void Apply_WhenSoundBackendMatches_InjectsHostUrisIntoPrimitivePlaceholder()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "sound_test.tone", "type": "Primitive", "primitiveKind": "Cube" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "sound_test.tone.raylib",
                    "assetKind": "Sound",
                    "assetId": "sound_test.tone",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Sounds/tone.wav" ]
                  },
                  {
                    "id": "sound_test.tone.ue5",
                    "assetKind": "Sound",
                    "assetId": "sound_test.tone",
                    "backendId": "ue5",
                    "sourceUris": [ "ue5.sound:/Game/Test/Tone.Tone" ]
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);

            new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog);

            int toneId = meshes.GetId("sound_test.tone");
            Assert.That(meshes.TryGetDescriptor(toneId, out var tone), Is.True);
            Assert.That(tone.Type, Is.EqualTo(MeshAssetType.Primitive));
            Assert.That(tone.PrimitiveKind, Is.EqualTo(PrimitiveMeshKind.Cube), "sound binding must not rewrite the placeholder mesh type");
            Assert.That(tone.SourceUris, Is.EqualTo(new[] { "TestMod:assets/Sounds/tone.wav" }));
        }

        [Test]
        public void Apply_WhenSoundRowTargetsModelAsset_ThrowsPrimitivePlaceholderError()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "mesh_assets.json"),
                """
                [
                  { "id": "sound_test.model", "type": "Model" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "sound_test.model.raylib",
                    "assetKind": "Sound",
                    "assetId": "sound_test.model",
                    "backendId": "raylib",
                    "sourceUris": [ "TestMod:assets/Sounds/tone.wav" ]
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
            Assert.That(ex!.Message, Does.Contain("Sound assets must be Primitive placeholders"));
        }

        [Test]
        public void Resolve_WhenInstanceChainMerges_ChildOverridesParentSparsely()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "demo.metal",
                    "domain": "Surface",
                    "shaderKey": "lit",
                    "params": {
                      "floats": { "roughness": 0.2, "metallic": 1.0 },
                      "colors": { "tint": [ 1.0, 0.8, 0.6, 1.0 ] }
                    }
                  },
                  {
                    "id": "demo.metal.rusty",
                    "domain": "Surface",
                    "parent": "demo.metal",
                    "params": { "floats": { "roughness": 0.9 } }
                  }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Presentation", "host_assets.json"),
                """
                [
                  {
                    "id": "demo.metal.raylib",
                    "assetKind": "Material",
                    "assetId": "demo.metal",
                    "backendId": "raylib",
                    "textures": {
                      "albedo": "TestMod:assets/Textures/metal.png",
                      "normal": "TestMod:assets/Textures/metal_n.png"
                    }
                  },
                  {
                    "id": "demo.metal.rusty.raylib",
                    "assetKind": "Material",
                    "assetId": "demo.metal.rusty",
                    "backendId": "raylib",
                    "textures": { "albedo": "TestMod:assets/Textures/rusty.png" }
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);
            new PresentationHostAssetConfigLoader(pipeline, meshes, materials).Apply("raylib", catalog);

            int instanceId = materials.GetId("demo.metal.rusty");
            Assert.That(materials.TryResolve(instanceId, out var resolved), Is.True);
            Assert.That(resolved.ShaderKey, Is.EqualTo("lit"));
            Assert.That(resolved.Roughness, Is.EqualTo(0.9f));
            Assert.That(resolved.Metallic, Is.EqualTo(1.0f));
            Assert.That(resolved.Colors["tint"].Y, Is.EqualTo(0.8f));
            Assert.That(resolved.TextureUris["albedo"], Is.EqualTo("TestMod:assets/Textures/rusty.png"));
            Assert.That(resolved.TextureUris["normal"], Is.EqualTo("TestMod:assets/Textures/metal_n.png"));
        }

        [Test]
        public void Load_WhenInstanceDeclaresShaderKeyOrFlags_Throws()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  { "id": "demo.base", "domain": "Surface" },
                  { "id": "demo.bad", "domain": "Surface", "parent": "demo.base", "flags": [ "Cutout" ] }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var materials = new PresentationMaterialRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("instances cannot declare shaderKey/flags"));
        }

        [Test]
        public void Resolve_WhenParentChainHasCycle_Throws()
        {
            var materials = new PresentationMaterialRegistry();
            materials.Register("cycle.a", MaterialAssetDomain.Surface, MaterialAssetFlags.None, parentKey: "cycle.b");
            materials.Register("cycle.b", MaterialAssetDomain.Surface, MaterialAssetFlags.None, parentKey: "cycle.a");

            int id = materials.GetId("cycle.a");
            var ex = Assert.Throws<InvalidOperationException>(() => materials.TryResolve(id, out _));
            Assert.That(ex!.Message, Does.Contain("cycle"));
        }

        [Test]
        public void Resolve_WhenParentIsMissing_Throws()
        {
            var materials = new PresentationMaterialRegistry();
            materials.Register("orphan.instance", MaterialAssetDomain.Surface, MaterialAssetFlags.None, parentKey: "ghost.parent");

            int id = materials.GetId("orphan.instance");
            var ex = Assert.Throws<InvalidOperationException>(() => materials.TryResolve(id, out _));
            Assert.That(ex!.Message, Does.Contain("ghost.parent"));
        }

        [Test]
        public void Load_ParsesShaderKeyAndNamedParams()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "demo.emissive",
                    "domain": "Surface",
                    "shaderKey": "emissive",
                    "params": {
                      "floats": { "roughness": 0.6, "uEmissiveStrength": 3.0 },
                      "colors": { "uEmissiveColor": [ 1.0, 0.35, 0.15, 1.0 ] }
                    }
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var materials = new PresentationMaterialRegistry();
            new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);

            int id = materials.GetId("demo.emissive");
            Assert.That(materials.TryGet(id, out var descriptor), Is.True);
            Assert.That(descriptor.ShaderKey, Is.EqualTo("emissive"));
            Assert.That(descriptor.Roughness, Is.EqualTo(0.6f));
            Assert.That(descriptor.FloatParams["uEmissiveStrength"], Is.EqualTo(3.0f));
            Assert.That(descriptor.ColorParams["uEmissiveColor"].Y, Is.EqualTo(0.35f));
        }

        [Test]
        public void Load_WhenTopLevelAndParamsDeclareSameWellKnownScalar_Throws()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "demo.conflict",
                    "domain": "Surface",
                    "roughness": 0.4,
                    "params": { "floats": { "roughness": 0.8 } }
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var materials = new PresentationMaterialRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("both at top level and in params.floats"));
        }

        [Test]
        public void Load_WhenWellKnownFloatParamOutOfUnitRange_Throws()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Presentation"));
            File.WriteAllText(
                Path.Combine(root, "Presentation", "material_assets.json"),
                """
                [
                  {
                    "id": "demo.bad_range",
                    "domain": "Surface",
                    "params": { "floats": { "metallic": 1.5 } }
                  }
                ]
                """);

            var pipeline = BuildCorePipeline(root);
            var catalog = BuildPresentationCatalog();
            var materials = new PresentationMaterialRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("within [0, 1]"));
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
