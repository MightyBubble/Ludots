using System;
using System.IO;
using System.Numerics;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PrefabTransformUtilityTests
    {
        [Test]
        public void Compose_AppliesParentRotationAndLocalRotation()
        {
            Vector3 parentPosition = new Vector3(10f, 0f, 20f);
            Quaternion parentRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            Vector3 parentScale = new Vector3(2f, 1f, 3f);
            var part = new PrefabPart
            {
                MeshAssetId = 77,
                LocalPosition = new Vector3(1f, 0f, 0f),
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
                LocalScale = new Vector3(0.5f, 2f, 1.5f),
                ColorTint = Vector4.One,
            };

            PrefabTransformUtility.Compose(parentPosition, parentRotation, parentScale, in part, out Vector3 childPosition, out Quaternion childRotation, out Vector3 childScale);

            Assert.That(childPosition.X, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(childPosition.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(childPosition.Z, Is.EqualTo(18f).Within(0.0001f));
            Assert.That(childScale, Is.EqualTo(new Vector3(1f, 2f, 4.5f)));

            Vector3 expectedForward = Vector3.Transform(
                Vector3.Transform(Vector3.UnitZ, part.LocalRotation),
                parentRotation);
            Vector3 actualForward = Vector3.Transform(Vector3.UnitZ, childRotation);
            Assert.That(actualForward.X, Is.EqualTo(expectedForward.X).Within(0.0001f));
            Assert.That(actualForward.Y, Is.EqualTo(expectedForward.Y).Within(0.0001f));
            Assert.That(actualForward.Z, Is.EqualTo(expectedForward.Z).Within(0.0001f));
        }

        [Test]
        public void MeshAssetConfigLoader_ParsesPrefabPartLocalRotation()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PrefabTransformUtilityTests", Guid.NewGuid().ToString("N"));
            string mod = Path.Combine(root, "PrefabRotationTestMod");
            Directory.CreateDirectory(Path.Combine(mod, "assets", "Presentation"));

            File.WriteAllText(
                Path.Combine(mod, "assets", "Presentation", "mesh_assets.json"),
                """
                [
                  {
                    "id": "test.mesh.base",
                    "type": "Model",
                    "sourceUris": ["ue5.staticmesh:/Game/Test/Base.Base"]
                  }
                ]
                """);
            File.WriteAllText(
                Path.Combine(mod, "assets", "Presentation", "prefabs.json"),
                """
                [
                  {
                    "id": "test.prefab.rotated",
                    "parts": [
                      {
                        "meshAssetId": "test.mesh.base",
                        "localPosition": [100, 0, 0],
                        "localRotation": [0, 0.70710677, 0, 0.70710677],
                        "localScale": [1, 2, 3],
                        "colorTint": [1, 0.5, 0.25, 1],
                        "grounding": {
                          "mode": "VisualHeightmap",
                          "verticalOffsetMeters": 0.5,
                          "alignToGroundNormal": true,
                          "layerIndex": 2
                        }
                      }
                    ]
                  }
                ]
                """);

            var vfs = new VirtualFileSystem();
            vfs.Mount("PrefabRotationTestMod", mod);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("PrefabRotationTestMod");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var meshRegistry = new MeshAssetRegistry();
            var prefabRegistry = new PrefabRegistry();
            var loader = new MeshAssetConfigLoader(pipeline, meshRegistry, prefabRegistry);

            loader.Load();

            int prefabMeshId = meshRegistry.GetId("test.prefab.rotated");
            Assert.That(prefabMeshId, Is.GreaterThan(0));
            Assert.That(meshRegistry.TryGetDescriptor(prefabMeshId, out var descriptor), Is.True);
            Assert.That(descriptor.Type, Is.EqualTo(MeshAssetType.Prefab));
            Assert.That(descriptor.PrefabParts, Has.Length.EqualTo(1));

            PrefabPart part = descriptor.PrefabParts[0];
            Assert.That(part.LocalPosition, Is.EqualTo(new Vector3(100f, 0f, 0f)));
            Assert.That(part.LocalScale, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(part.ColorTint, Is.EqualTo(new Vector4(1f, 0.5f, 0.25f, 1f)));
            Assert.That(part.LocalRotation.Y, Is.EqualTo(0.70710677f).Within(0.0001f));
            Assert.That(part.LocalRotation.W, Is.EqualTo(0.70710677f).Within(0.0001f));
            Assert.That(part.Grounding.Mode, Is.EqualTo(PrefabPartGroundingMode.VisualHeightmap));
            Assert.That(part.Grounding.VerticalOffsetMeters, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(part.Grounding.AlignToGroundNormal, Is.True);
            Assert.That(part.Grounding.LayerIndex, Is.EqualTo(2));
        }
    }
}
