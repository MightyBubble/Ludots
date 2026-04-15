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
