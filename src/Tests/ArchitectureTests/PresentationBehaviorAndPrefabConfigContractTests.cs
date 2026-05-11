using System;
using System.IO;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Events;
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
            WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id", "Presentation/prefabs.json", "ArrayById", "id");
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
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var meshes = new MeshAssetRegistry();
            var prefabs = new PrefabRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, meshes, prefabs).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("must declare an explicit kind"));
        }

        [Test]
        public void PresentationBehaviorConfigLoader_LoadsStatesThroughCoreConfigContract()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationBehaviorConfig", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            WriteCatalog(
                core,
                "Presentation/mesh_assets.json", "ArrayById", "id",
                "Presentation/prefabs.json", "ArrayById", "id",
                "Presentation/presentation_behaviors.json", "ArrayById", "id");
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
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var meshes = new MeshAssetRegistry();
            var prefabs = new PrefabRegistry();
            new MeshAssetConfigLoader(pipeline, meshes, prefabs).Load(catalog);

            var behaviors = new PresentationBehaviorRegistry();
            new PresentationBehaviorConfigLoader(pipeline, behaviors, meshes).Load(catalog);

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
        public void GameEngine_UsesMergedPresentationEventStreamCapacity()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            PresentationEventStream stream = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream service missing.");

            Assert.That(
                stream.Capacity,
                Is.EqualTo(engine.MergedConfig.Presentation.GetEffectivePresentationEventStreamCapacity()));
        }

        [Test]
        public void GameEngine_MergesSelectionMovePathPreviewOrderKeys()
        {
            string repoRoot = FindRepoRoot();
            using var coreEngine = new GameEngine();
            coreEngine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));

            Assert.That(
                coreEngine.MergedConfig.Selection.MovePathPreviewOrderTypeKeys,
                Is.EqualTo(new[] { "moveTo" }),
                "LudotsCoreMod should author the generic move path preview contract.");
            Assert.That(coreEngine.MergedConfig.Constants.OrderTypeIds.ContainsKey("moveTo"), Is.True);

            using var massNavigationEngine = new GameEngine();
            massNavigationEngine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    Path.Combine(repoRoot, "mods", "CoreInputMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                    Path.Combine(repoRoot, "mods", "showcases", "performer_blacksmith", "PerformerBlacksmithShowcaseMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
                },
                Path.Combine(repoRoot, "assets"));

            Assert.That(
                massNavigationEngine.MergedConfig.Selection.MovePathPreviewOrderTypeKeys,
                Is.EqualTo(new[] { "moveTo", "massNavigationMove" }),
                "MassNavigationMod should extend the preview contract through game.json, not CoreInputMod source.");
            Assert.That(massNavigationEngine.MergedConfig.Constants.OrderTypeIds.ContainsKey("moveTo"), Is.True);
            Assert.That(
                massNavigationEngine.MergedConfig.Constants.OrderTypeIds.ContainsKey("massNavigationMove"),
                Is.False,
                "MassNavigation move must not be double-authored in game.json constants; GAS/order_types.json owns order type definitions.");
            OrderTypeRegistry orderTypes = massNavigationEngine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
            int massNavigationMoveOrderTypeId = orderTypes.GetId("massNavigationMove");
            Assert.That(massNavigationMoveOrderTypeId, Is.GreaterThan(0));
            Assert.That(massNavigationMoveOrderTypeId, Is.Not.EqualTo(massNavigationEngine.MergedConfig.Constants.OrderTypeIds["moveTo"]));
        }

        [Test]
        public void OrderTypeConfigLoader_ResolvesRuleReferencesByKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeKeyRules", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "Configs", "GAS", "order_types.json"), """
{
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To"
    },
    "attackTarget": {
      "orderTypeId": 102,
      "label": "Attack Target"
    },
    "massNavigationMove": {
      "orderTypeId": "massNavigationMove",
      "label": "Mass Navigation Move"
    }
  },
  "orderRules": {
    "massNavigationMove": {
      "orderTypeKey": "massNavigationMove",
      "blockedActiveOrderTypeKeys": [],
      "interruptsActiveOrderTypeKeys": [ "moveTo", "attackTarget" ]
    }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry();
            var orderRules = new OrderRuleRegistry();

            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

            int massNavigationMoveId = orderTypes.GetId("massNavigationMove");
            ref readonly OrderRuleSet massNavigationRule = ref orderRules.Get(massNavigationMoveId);
            Assert.That(massNavigationRule.InterruptsActiveCount, Is.EqualTo(2));
            Assert.That(orderRules.Interrupts(massNavigationMoveId, orderTypes.GetId("moveTo")), Is.True);
            Assert.That(orderRules.Interrupts(massNavigationMoveId, orderTypes.GetId("attackTarget")), Is.True);
        }

        [Test]
        public void OrderTypeConfigLoader_RequiresExplicitOrderTypeIds()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeAllocatedIds", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "Configs", "GAS", "order_types.json"), """
{
  "orderTypes": {
    "explicitMove": {
      "orderTypeId": 1,
      "label": "Explicit Move"
    },
    "semanticMove": {
      "label": "Semantic Move",
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none"
    }
  },
  "orderRules": {
    "semanticMove": {
      "orderTypeKey": "semanticMove",
      "interruptsActiveOrderTypeKeys": [ "explicitMove" ]
    }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry();
            var orderRules = new OrderRuleRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog))!;
            Assert.That(ex.Message, Does.Contain("semanticMove"));
            Assert.That(ex.Message, Does.Contain("orderTypeId"));
        }

        [Test]
        public void OrderTypeConfigLoader_LoadsSemanticOrderTypeIdsDeterministically()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeDeterministicIds", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            string path = Path.Combine(core, "Configs", "GAS", "order_types.json");
            File.WriteAllText(path, """
{
  "orderTypes": {
    "explicitMove": { "orderTypeId": 1, "label": "Explicit Move" },
    "alphaMove": { "orderTypeId": "alphaMove", "label": "Alpha Move", "validationGraph": "none" },
    "omegaMove": { "orderTypeId": "omegaMove", "label": "Omega Move", "validationGraph": "none" }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry();
            var orderRules = new OrderRuleRegistry();

            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);
            int alphaMoveId = orderTypes.GetId("alphaMove");
            int omegaMoveId = orderTypes.GetId("omegaMove");
            Assert.That(alphaMoveId, Is.GreaterThan(0));
            Assert.That(omegaMoveId, Is.GreaterThan(0));
            Assert.That(alphaMoveId, Is.Not.EqualTo(omegaMoveId));

            File.WriteAllText(path, """
{
  "orderTypes": {
    "omegaMove": { "orderTypeId": "omegaMove", "label": "Omega Move", "validationGraph": "none" },
    "alphaMove": { "orderTypeId": "alphaMove", "label": "Alpha Move", "validationGraph": "none" },
    "explicitMove": { "orderTypeId": 1, "label": "Explicit Move" }
  }
}
""");
            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

            Assert.That(orderTypes.GetId("alphaMove"), Is.EqualTo(alphaMoveId));
            Assert.That(orderTypes.GetId("omegaMove"), Is.EqualTo(omegaMoveId));
        }

        [Test]
        public void ConfigPipeline_MergesPresentationRuntimeCapacities()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationRuntimeConfigContracts", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModPresentation");
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(mod, "assets"));

            File.WriteAllText(Path.Combine(core, "Configs", "game.json"), """
{
  "presentation": {
    "performerInstanceCapacity": 2048,
    "presentationEventStreamCapacity": 32768,
    "performerCommandCapacity": 4096,
    "primitiveDrawBufferCapacity": 8192,
    "visualSnapshotBufferCapacity": 16384,
    "visualProxyBufferCapacity": 16384,
    "skinnedVisualBatchCapacity": 2048,
    "presentationRequestCapacity": 16384,
    "groundOverlayCapacity": 1024,
    "roadSplineCapacity": 2048,
    "worldHudCapacity": 4096,
    "screenHudCapacity": 4096,
    "runtimeEntitySpawnQueueCapacity": 8192
  }
}
""");
            File.WriteAllText(Path.Combine(mod, "assets", "game.json"), """
{
  "presentation": {
    "performerInstanceCapacity": 8192,
    "presentationEventStreamCapacity": 65536,
    "performerCommandCapacity": 32768,
    "primitiveDrawBufferCapacity": 65536,
    "visualSnapshotBufferCapacity": 131072,
    "visualProxyBufferCapacity": 131072,
    "skinnedVisualBatchCapacity": 32768,
    "presentationRequestCapacity": 131072,
    "groundOverlayCapacity": 16384,
    "roadSplineCapacity": 32768,
    "worldHudCapacity": 65536,
    "screenHudCapacity": 65536,
    "runtimeEntitySpawnQueueCapacity": 65536
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            vfs.Mount("ModPresentation", mod);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("ModPresentation");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var config = pipeline.MergeGameConfig();

            Assert.That(config.Presentation.PerformerInstanceCapacity, Is.EqualTo(8192));
            Assert.That(config.Presentation.PresentationEventStreamCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.PerformerCommandCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.PrimitiveDrawBufferCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.VisualSnapshotBufferCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.VisualProxyBufferCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.SkinnedVisualBatchCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.PresentationRequestCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.GroundOverlayCapacity, Is.EqualTo(16384));
            Assert.That(config.Presentation.RoadSplineCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.WorldHudCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.ScreenHudCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.RuntimeEntitySpawnQueueCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.GetEffectivePerformerInstanceCapacity(), Is.EqualTo(8192));
            Assert.That(config.Presentation.GetEffectivePresentationEventStreamCapacity(), Is.EqualTo(65536));
            Assert.That(config.Presentation.GetEffectivePerformerCommandCapacity(), Is.EqualTo(32768));
            Assert.That(config.Presentation.GetEffectivePrimitiveDrawBufferCapacity(), Is.EqualTo(65536));
            Assert.That(config.Presentation.GetEffectiveVisualSnapshotBufferCapacity(), Is.EqualTo(131072));
            Assert.That(config.Presentation.GetEffectiveVisualProxyBufferCapacity(), Is.EqualTo(131072));
            Assert.That(config.Presentation.GetEffectiveSkinnedVisualBatchCapacity(), Is.EqualTo(32768));
            Assert.That(config.Presentation.GetEffectivePresentationRequestCapacity(), Is.EqualTo(131072));
            Assert.That(config.Presentation.GetEffectiveGroundOverlayCapacity(), Is.EqualTo(16384));
            Assert.That(config.Presentation.GetEffectiveRoadSplineCapacity(), Is.EqualTo(32768));
            Assert.That(config.Presentation.GetEffectiveWorldHudCapacity(), Is.EqualTo(65536));
            Assert.That(config.Presentation.GetEffectiveScreenHudCapacity(), Is.EqualTo(65536));
            Assert.That(config.Presentation.GetEffectiveRuntimeEntitySpawnQueueCapacity(), Is.EqualTo(65536));
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

        private static void WriteCatalog(string coreRoot, params string[] triples)
        {
            if (triples.Length % 3 != 0)
            {
                throw new ArgumentException("Catalog entries must be path/policy/idField triples.", nameof(triples));
            }

            Directory.CreateDirectory(Path.Combine(coreRoot, "Configs"));
            using var writer = new StringWriter();
            writer.WriteLine("[");
            for (int i = 0; i < triples.Length; i += 3)
            {
                if (i > 0)
                {
                    writer.WriteLine(",");
                }

                writer.Write($"  {{ \"Path\": \"{triples[i]}\", \"Policy\": \"{triples[i + 1]}\"");
                if (!string.IsNullOrWhiteSpace(triples[i + 2]))
                {
                    writer.Write($", \"IdField\": \"{triples[i + 2]}\"");
                }

                writer.Write(" }");
            }

            writer.WriteLine();
            writer.WriteLine("]");
            File.WriteAllText(Path.Combine(coreRoot, "Configs", "config_catalog.json"), writer.ToString());
        }
    }
}
