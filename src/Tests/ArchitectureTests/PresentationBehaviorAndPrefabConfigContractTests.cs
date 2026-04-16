using System;
using System.IO;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;
using System.Linq;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PresentationBehaviorAndPrefabConfigContractTests
    {
        [Test]
        public void MeshAssetConfigLoader_WhenPrefabPartKindMissing_ThrowsExplicitly()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationConfigContracts", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "mesh_assets.json"), """
[
  { "id": "cube", "type": "Primitive", "primitiveKind": "Cube" }
]
""");
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "prefabs.json"), """
[
  {
    "id": "prefab.bad",
    "parts": [
      {
        "meshAssetId": "cube"
      }
    ]
  }
]
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var meshes = new MeshAssetRegistry();
            var prefabs = new PrefabRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, meshes, prefabs).Load());
            Assert.That(ex!.Message, Does.Contain("must declare an explicit kind"));
        }

        [Test]
        public void PresentationBehaviorConfigLoader_LoadsStatesThroughCoreConfigContract()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationBehaviorConfig", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "mesh_assets.json"), """
[
  { "id": "cube", "type": "Primitive", "primitiveKind": "Cube" },
  { "id": "prefab.crop.stage0", "type": "Prefab", "parts": [ { "kind": "Mesh", "meshAssetId": "cube" } ] }
]
""");
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "presentation_behaviors.json"), """
[
  {
    "id": "behavior.crop",
    "states": [
      { "stateId": "Growing", "prefabAssetId": "prefab.crop.stage0" }
    ]
  }
]
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var meshes = new MeshAssetRegistry();
            var prefabs = new PrefabRegistry();
            new MeshAssetConfigLoader(pipeline, meshes, prefabs).Load();

            var behaviors = new PresentationBehaviorRegistry();
            new PresentationBehaviorConfigLoader(pipeline, behaviors, meshes).Load();

            int behaviorId = behaviors.GetId("behavior.crop");
            Assert.That(behaviorId, Is.GreaterThan(0));
            Assert.That(behaviors.TryGet(behaviorId, out var behavior), Is.True);
            Assert.That(behavior.States, Has.Length.EqualTo(1));
            Assert.That(behavior.States[0].StateId, Is.EqualTo("Growing"));
            Assert.That(behavior.States[0].PrefabAssetId, Is.EqualTo(meshes.GetId("prefab.crop.stage0")));
        }

        [Test]
        public void GameEngine_RegistersPresentationBehaviorServices()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            Assert.That(engine.GetService(CoreServiceKeys.PresentationBehaviorRegistry), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.PresentationBehaviorResolver), Is.Not.Null);
        }

        [Test]
        public void LudotsCoreCueMarkerPrefab_RemainsSharedMeshOnlyContract()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            var prefabs = engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) as PrefabRegistry
                ?? throw new InvalidOperationException("PresentationPrefabRegistry missing.");
            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");

            int cuePrefabId = prefabs.GetId("cue_marker");
            Assert.That(cuePrefabId, Is.GreaterThan(0), "cue_marker prefab must stay registered in LudotsCoreMod.");
            Assert.That(prefabs.TryGet(cuePrefabId, out PrefabDefinition cuePrefab), Is.True);
            int cueMeshAssetId = meshes.GetId("cue_marker");
            Assert.That(cueMeshAssetId, Is.GreaterThan(0), "cue_marker mesh asset must resolve to the prefab contract asset.");

            var output = new PrefabFinalizedVisualBuffer();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshes,
                cueMeshAssetId,
                stableId: 7,
                position: default,
                rotation: System.Numerics.Quaternion.Identity,
                scale: System.Numerics.Vector3.One * cuePrefab.BaseScale,
                color: System.Numerics.Vector4.One,
                output);

            PrefabVisualPartKind[] kinds = output.GetSpan().ToArray().Select(static visual => visual.Kind).ToArray();
            Assert.That(kinds, Is.EqualTo(new[] { PrefabVisualPartKind.Mesh }));
        }

        [Test]
        public void CameraAcceptanceProjectionCueFixturePrefab_UsesFullMultiPartVisualContract()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    Path.Combine(repoRoot, "mods", "CoreInputMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "camera", "SharedThreeCProfilesMod"),
                    Path.Combine(repoRoot, "mods", "fixtures", "camera", "CameraAcceptanceMod"),
                },
                Path.Combine(repoRoot, "assets"));

            var prefabs = engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) as PrefabRegistry
                ?? throw new InvalidOperationException("PresentationPrefabRegistry missing.");
            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");

            int fixturePrefabId = prefabs.GetId("camera_acceptance_projection_cue_fixture_prefab");
            Assert.That(fixturePrefabId, Is.GreaterThan(0), "camera_acceptance_projection_cue_fixture_prefab must stay registered in CameraAcceptanceMod.");
            Assert.That(prefabs.TryGet(fixturePrefabId, out PrefabDefinition fixturePrefab), Is.True);
            int fixtureMeshAssetId = meshes.GetId("camera_acceptance_projection_cue_fixture_prefab");
            Assert.That(fixtureMeshAssetId, Is.GreaterThan(0), "camera_acceptance_projection_cue_fixture_prefab mesh asset must resolve to the prefab contract asset.");

            var output = new PrefabFinalizedVisualBuffer();
            PrefabFinalizationPipeline.FinalizeVisuals(
                meshes,
                fixtureMeshAssetId,
                stableId: 9,
                position: default,
                rotation: System.Numerics.Quaternion.Identity,
                scale: System.Numerics.Vector3.One * fixturePrefab.BaseScale,
                color: System.Numerics.Vector4.One,
                output);

            PrefabVisualPartKind[] kinds = output.GetSpan().ToArray().Select(static visual => visual.Kind).ToArray();
            Assert.That(kinds, Does.Contain(PrefabVisualPartKind.Mesh));
            Assert.That(kinds, Does.Contain(PrefabVisualPartKind.Decal));
            Assert.That(kinds, Does.Contain(PrefabVisualPartKind.Vfx));
            Assert.That(kinds, Does.Contain(PrefabVisualPartKind.Surface));
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
