using System;
using System.IO;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using System.Reflection;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;
using System.Linq;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PresentationBehaviorAndPrefabConfigContractTests
    {
        [Test]
        public void MeshAssetConfigLoader_WhenTypeIsPrefab_ThrowsTellingAuthorsToUsePresenter()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationConfigContracts", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            WriteCatalog(core, "Presentation/mesh_assets.json", "ArrayById", "id");
            File.WriteAllText(Path.Combine(core, "Presentation", "mesh_assets.json"), """
[
  { "id": "legacy.composite", "type": "Prefab", "parts": [ { "kind": "Mesh", "meshAssetId": "cube" } ] }
]
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var meshes = new MeshAssetRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new MeshAssetConfigLoader(pipeline, meshes).Load(catalog));
            Assert.That(ex!.Message, Does.Contain("type Prefab"));
            Assert.That(ex.Message, Does.Contain("Presenter"));
        }

        [Test]
        public void GameEngine_RegistersParticleVfxRegistryBeforeMeshAssets()
        {
            Assert.That(
                typeof(CoreServiceKeys).GetField("PresentationParticleVfxRegistry", BindingFlags.Public | BindingFlags.Static),
                Is.Not.Null);

            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            Assert.That(engine.GetService(CoreServiceKeys.PresentationParticleVfxRegistry), Is.Not.Null);
        }

        [Test]
        public void GameEngine_DoesNotRegisterPrefabOrPresentationBehaviorServices()
        {
            Assert.That(
                typeof(CoreServiceKeys).GetField("PresentationPrefabRegistry", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
            Assert.That(
                typeof(CoreServiceKeys).GetField("PresentationBehaviorRegistry", BindingFlags.Public | BindingFlags.Static),
                Is.Null);

            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
            int cueMeshId = meshes.GetId("cue_marker");
            Assert.That(cueMeshId, Is.GreaterThan(0), "cue_marker must stay registered as a leaf mesh.");
            Assert.That(meshes.TryGetDescriptor(cueMeshId, out MeshAssetDescriptor cue), Is.True);
            Assert.That(cue.Type, Is.EqualTo(MeshAssetType.Primitive));
        }

        [Test]
        public void GameEngine_UsesMergedPresentationEventStreamCapacity()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            PresentationEventStream stream = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream service missing.");
            PresentationOwnerChangeBuffer ownerChanges = engine.GetService(CoreServiceKeys.PresentationOwnerChangeBuffer)
                ?? throw new InvalidOperationException("PresentationOwnerChangeBuffer service missing.");
            GasPresentationEventBuffer gasEvents = engine.GetService(CoreServiceKeys.GasPresentationEventBuffer)
                ?? throw new InvalidOperationException("GasPresentationEventBuffer service missing.");

            Assert.That(
                gasEvents.Capacity,
                Is.EqualTo(engine.MergedConfig.Presentation.GasPresentationEventCapacity));
            Assert.That(
                stream.Capacity,
                Is.EqualTo(engine.MergedConfig.Presentation.PresentationEventStreamCapacity));
            Assert.That(
                ownerChanges.Capacity,
                Is.EqualTo(engine.MergedConfig.Presentation.PresentationOwnerChangeCapacity));
        }

        [Test]
        public void GameEngine_MergesCommandSourceMovePathPreviewOrderKeys()
        {
            string repoRoot = FindRepoRoot();
            using var coreEngine = new GameEngine();
            coreEngine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));
            GameConfig coreConfig = coreEngine.MergedConfig
                ?? throw new InvalidOperationException("Core engine config was not merged.");
            CommandSourceAcquisitionConfig coreCommandSource = coreConfig.CommandSource
                ?? throw new InvalidOperationException("Core command-source config was not merged.");

            Assert.That(
                coreCommandSource.MovePathPreviewOrderTypeKeys,
                Is.EqualTo(new[] { "moveTo" }),
                "LudotsCoreMod should author the generic move path preview contract.");
            Assert.That(coreConfig.Constants.OrderTypeIds.ContainsKey("moveTo"), Is.True);
            Assert.That(EntityCollectionKeys.CommandSource, Is.EqualTo("collection.command.source"));

            using var massNavigationEngine = new GameEngine();
            massNavigationEngine.InitializeWithConfigPipeline(
                new List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    Path.Combine(repoRoot, "mods", "CoreInputMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                    Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
                },
                Path.Combine(repoRoot, "assets"));
            GameConfig massNavigationConfig = massNavigationEngine.MergedConfig
                ?? throw new InvalidOperationException("MassNavigation engine config was not merged.");
            CommandSourceAcquisitionConfig massNavigationCommandSource = massNavigationConfig.CommandSource
                ?? throw new InvalidOperationException("MassNavigation command-source config was not merged.");

            Assert.That(
                massNavigationCommandSource.MovePathPreviewOrderTypeKeys,
                Is.EqualTo(new[] { "massNavigationMove" }),
                "MassNavigationMod should author only its formal order key for command-source move path preview.");
            EntityCollectionStore collections = massNavigationEngine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            Assert.That(collections.KeyRegistry.GetId(EntityCollectionKeys.CommandSource), Is.GreaterThan(0));
            Assert.That(massNavigationConfig.Constants.OrderTypeIds.ContainsKey("moveTo"), Is.True);
            Assert.That(
                massNavigationConfig.Constants.OrderTypeIds.ContainsKey("massNavigationMove"),
                Is.False,
                "MassNavigation move must not be double-authored in game.json constants; GAS/order_types.json owns order type definitions.");
            OrderTypeRegistry orderTypes = massNavigationEngine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
            int massNavigationMoveOrderTypeId = orderTypes.GetId("massNavigationMove");
            Assert.That(massNavigationMoveOrderTypeId, Is.GreaterThan(0));
            Assert.That(massNavigationMoveOrderTypeId, Is.Not.EqualTo(massNavigationEngine.MergedConfig.Constants.OrderTypeIds["moveTo"]));
            Assert.That(orderTypes.TryGetId("moveTo", out int coreMoveToOrderTypeId), Is.True);
            Assert.That(coreMoveToOrderTypeId, Is.Not.EqualTo(massNavigationMoveOrderTypeId));
            Assert.That(orderTypes.TryGetId("MassNavigationMove", out _), Is.False);
        }

        [Test]
        public void OrderTypeConfigLoader_ResolvesRuleReferencesByKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeKeyRules", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "GAS", "order_types.json"), """
{
  "orderBlackboardKeys": {
    "Attack.MovePosition": true,
    "Attack.TargetEntity": true
  },
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "attackTarget": {
      "orderTypeId": 102,
      "label": "Attack Target",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "queueFullPolicy": "DropOldest",
      "priority": 75,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 400,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 1,
      "allowQueuedMode": false,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Attack.MovePosition",
      "entityBlackboardKey": "Attack.TargetEntity",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "massNavigationMove": {
      "orderTypeId": "massNavigationMove",
      "label": "Mass Navigation Move",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "queueFullPolicy": "DropOldest",
      "priority": 70,
      "bufferWindowMs": 0,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": true,
      "queuedModeMaxSize": 1,
      "allowQueuedMode": false,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "none",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {
    "massNavigationMove": {
      "orderTypeKey": "massNavigationMove",
      "blockedActiveOrderTypeKeys": [],
      "interruptsActiveOrderTypeKeys": [ "attackTarget" ]
    }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            var orderRules = new OrderRuleRegistry();

            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

            int massNavigationMoveId = orderTypes.GetId("massNavigationMove");
            ref readonly OrderRuleSet massNavigationRule = ref orderRules.Get(massNavigationMoveId);
            Assert.That(massNavigationRule.InterruptsActiveCount, Is.EqualTo(1));
            Assert.That(orderRules.Interrupts(massNavigationMoveId, orderTypes.GetId("moveTo")), Is.False);
            Assert.That(orderRules.Interrupts(massNavigationMoveId, orderTypes.GetId("attackTarget")), Is.True);
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsCaseMismatchedRuleReferenceKeys()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeKeyCaseStrict", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "GAS", "order_types.json"), """
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "massNavigationMove": {
      "orderTypeId": "massNavigationMove",
      "label": "Mass Navigation Move",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "queueFullPolicy": "DropOldest",
      "priority": 70,
      "bufferWindowMs": 0,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": true,
      "queuedModeMaxSize": 1,
      "allowQueuedMode": false,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "none",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {
    "massNavigationMove": {
      "orderTypeKey": "MassNavigationMove"
    }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            var orderRules = new OrderRuleRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog))!;
            Assert.That(ex.Message, Does.Contain("MassNavigationMove"));
            Assert.That(ex.Message, Does.Contain("unknown order type key"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsWhitespacePaddedPolicyStrings()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": " Queue ",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("sameTypePolicy"));
            Assert.That(ex.Message, Does.Contain("leading or trailing whitespace"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsWhitespacePaddedOrderTypeIdSemanticKey()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "massNavigationMove": {
      "orderTypeId": " massNavigationMove ",
      "label": "Mass Navigation Move",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "queueFullPolicy": "DropOldest",
      "priority": 70,
      "bufferWindowMs": 0,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": true,
      "queuedModeMaxSize": 1,
      "allowQueuedMode": false,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "none",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("orderTypeId semantic key"));
            Assert.That(ex.Message, Does.Contain("leading or trailing whitespace"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsWhitespacePaddedRuleReferenceKey()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "massNavigationMove": {
      "orderTypeId": "massNavigationMove",
      "label": "Mass Navigation Move",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "queueFullPolicy": "DropOldest",
      "priority": 70,
      "bufferWindowMs": 0,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": true,
      "queuedModeMaxSize": 1,
      "allowQueuedMode": false,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "none",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {
    "massNavigationMove": {
      "orderTypeKey": " massNavigationMove ",
      "blockedActiveOrderTypeKeys": [],
      "interruptsActiveOrderTypeKeys": []
    }
  }
}
""");

            Assert.That(ex.Message, Does.Contain("orderTypeKey"));
            Assert.That(ex.Message, Does.Contain("leading or trailing whitespace"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsWhitespacePaddedBlackboardAndValidationGraphKeys()
        {
            InvalidOperationException blackboard = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": " none ",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(blackboard.Message, Does.Contain("entityBlackboardKey"));
            Assert.That(blackboard.Message, Does.Contain("leading or trailing whitespace"));

            InvalidOperationException validationGraph = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": " none "
    }
  },
  "orderRules": {}
}
""");

            Assert.That(validationGraph.Message, Does.Contain("validationGraph"));
            Assert.That(validationGraph.Message, Does.Contain("leading or trailing whitespace"));
        }

        [Test]
        public void OrderTypeConfigLoader_RequiresExplicitOrderTypeIds()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeAllocatedIds", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "GAS", "order_types.json"), """
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "explicitMove": {
      "orderTypeId": 1,
      "label": "Explicit Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "semanticMove": {
      "label": "Semantic Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {
    "semanticMove": {
      "orderTypeKey": "semanticMove",
      "blockedActiveOrderTypeKeys": [],
      "interruptsActiveOrderTypeKeys": [ "explicitMove" ]
    }
  }
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
            Directory.CreateDirectory(Path.Combine(core, "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            string path = Path.Combine(core, "GAS", "order_types.json");
            File.WriteAllText(path, """
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "explicitMove": {
      "orderTypeId": 1,
      "label": "Explicit Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "alphaMove": {
      "orderTypeId": "alphaMove",
      "label": "Alpha Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "omegaMove": {
      "orderTypeId": "omegaMove",
      "label": "Omega Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            var orderRules = new OrderRuleRegistry();

            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);
            int alphaMoveId = orderTypes.GetId("alphaMove");
            int omegaMoveId = orderTypes.GetId("omegaMove");
            Assert.That(alphaMoveId, Is.GreaterThan(0));
            Assert.That(omegaMoveId, Is.GreaterThan(0));
            Assert.That(alphaMoveId, Is.Not.EqualTo(omegaMoveId));

            File.WriteAllText(path, """
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "omegaMove": {
      "orderTypeId": "omegaMove",
      "label": "Omega Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "alphaMove": {
      "orderTypeId": "alphaMove",
      "label": "Alpha Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    },
    "explicitMove": {
      "orderTypeId": 1,
      "label": "Explicit Move",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");
            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

            Assert.That(orderTypes.GetId("alphaMove"), Is.EqualTo(alphaMoveId));
            Assert.That(orderTypes.GetId("omegaMove"), Is.EqualTo(omegaMoveId));
        }

        [Test]
        public void OrderTypeConfigLoader_RequiresExplicitOrderTypeRuntimeFields()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("moveTo"));
            Assert.That(ex.Message, Does.Contain("priority"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsNumericBlackboardKeys()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": 201,
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("spatialBlackboardKey"));
            Assert.That(ex.Message, Does.Contain("numeric id"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsNumericBlackboardKeyDeclarations()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {
    "Test.Order.CustomInt": 9001
  },
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("Order blackboard key"));
            Assert.That(ex.Message, Does.Contain("numeric id"));
        }

        [Test]
        public void OrderTypeConfigLoader_ResolvesRegisteredSemanticBlackboardKeys()
        {
            OrderBlackboardKeyRegistry.ResetToBuiltins();
            const string customKey = "Test.Order.CustomInt";

            try
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeCustomBlackboardKey", Guid.NewGuid().ToString("N"));
                string core = Path.Combine(root, "Core");
                Directory.CreateDirectory(Path.Combine(core, "GAS"));
                WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
                File.WriteAllText(Path.Combine(core, "GAS", "order_types.json"), """
{
  "orderBlackboardKeys": {
    "Test.Order.CustomInt": true
  },
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "Test.Order.CustomInt",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                var catalog = ConfigCatalogLoader.Load(pipeline);
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                var orderRules = new OrderRuleRegistry();

                new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

                Assert.That(OrderBlackboardKeyRegistry.TryGetId(customKey, out int customKeyId), Is.True);
                Assert.That(customKeyId, Is.GreaterThan(0));
                Assert.That(OrderBlackboardKeyRegistry.GetKey(customKeyId), Is.EqualTo(customKey));
                Assert.That(orderTypes.Get(101).IntArg0BlackboardKey, Is.EqualTo(customKeyId));
            }
            finally
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
            }
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsUnknownSemanticBlackboardKeys()
        {
            OrderBlackboardKeyRegistry.ResetToBuiltins();
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "Test.Order.UnknownInt",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("intArg0BlackboardKey"));
            Assert.That(ex.Message, Does.Contain("Test.Order.UnknownInt"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsCaseMismatchedSemanticBlackboardKeys()
        {
            OrderBlackboardKeyRegistry.ResetToBuiltins();
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": "none",
      "instantComplete": false
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("spatialBlackboardKey"));
            Assert.That(ex.Message, Does.Contain("generic.TargetPosition"));
        }

        [Test]
        public void OrderTypeConfigLoader_RejectsNumericValidationGraph()
        {
            InvalidOperationException ex = LoadInvalidOrderTypesJson("""
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "moveTo": {
      "orderTypeId": 101,
      "label": "Move To",
      "maxQueueSize": 8,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 60,
      "bufferWindowMs": 300,
      "pendingBufferWindowMs": 0,
      "canInterruptSelf": false,
      "queuedModeMaxSize": 8,
      "allowQueuedMode": true,
      "clearQueueOnActivate": true,
      "spatialBlackboardKey": "Generic.TargetPosition",
      "entityBlackboardKey": "none",
      "intArg0BlackboardKey": "none",
      "validationGraph": 0
    }
  },
  "orderRules": {}
}
""");

            Assert.That(ex.Message, Does.Contain("validationGraph"));
            Assert.That(ex.Message, Does.Contain("numeric id"));
        }

        [Test]
        public void ConfigPipeline_MergesPresentationRuntimeCapacities()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationRuntimeConfigContracts", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModPresentation");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(mod, "assets"));

            File.WriteAllText(Path.Combine(core, "game.json"), """
{
  "presentation": {
    "presenterInstanceCapacity": 2048,
    "gasPresentationEventCapacity": 16384,
    "presentationEventStreamCapacity": 32768,
    "presentationOwnerChangeCapacity": 12288,
    "presenterCommandCapacity": 4096,
    "presenterTimerCapacity": 4096,
    "primitiveDrawBufferCapacity": 8192,
    "visualSnapshotBufferCapacity": 16384,
    "visualProxyBufferCapacity": 16384,
    "skinnedVisualBatchCapacity": 2048,
    "presentationRequestCapacity": 16384,
    "instancedBatchRequestCapacity": 2048,
    "instancedBatchOperationCapacity": 4096,
    "globalFieldVisualRecordCapacity": 128,
    "globalFieldVisualCellCapacity": 65536,
    "globalFieldVisualDirtyRectCapacity": 1024,
    "groundOverlayCapacity": 1024,
    "splineRibbonCapacity": 2048,
    "worldHudCapacity": 4096,
    "screenHudCapacity": 4096,
    "minimapMarkerCapacity": 4096,
    "runtimeEntitySpawnQueueCapacity": 8192,
    "runtimeEntitySpawnReceiptQueueCapacity": 8192,
    "runtimeEntityLifecycleQueueCapacity": 8192,
    "runtimeEntityLifecycleReceiptQueueCapacity": 8192,
    "cameraCulling": {
      "highLodDistanceCm": 4000.0,
      "mediumLodDistanceCm": 10000.0,
      "lowLodDistanceCm": 20000.0
    },
    "minimap": {
      "initialZoomNormalized": 1.0,
      "wheelZoomNormalizedStep": 0.08,
      "buttonZoomNormalizedStep": 0.18,
      "zoomSliderEnabled": true,
      "modeToggleEnabled": true,
      "rotateToggleEnabled": true,
      "debugMarkerSampleCapacity": 64,
      "minZoomExtentMode": "OneChunk",
      "maxZoomExtentMode": "FullMap",
      "minZoomExplicitHalfExtentCm": 750.0,
      "maxZoomExplicitHalfExtentCm": 0.0
    }
  }
}
""");
            File.WriteAllText(Path.Combine(mod, "assets", "game.json"), """
{
  "presentation": {
    "presenterInstanceCapacity": 8192,
    "gasPresentationEventCapacity": 32768,
    "presentationEventStreamCapacity": 65536,
    "presentationOwnerChangeCapacity": 24576,
    "presenterCommandCapacity": 32768,
    "presenterTimerCapacity": 32768,
    "primitiveDrawBufferCapacity": 65536,
    "visualSnapshotBufferCapacity": 131072,
    "visualProxyBufferCapacity": 131072,
    "skinnedVisualBatchCapacity": 32768,
    "presentationRequestCapacity": 131072,
    "instancedBatchRequestCapacity": 8192,
    "instancedBatchOperationCapacity": 16384,
    "globalFieldVisualRecordCapacity": 512,
    "globalFieldVisualCellCapacity": 262144,
    "globalFieldVisualDirtyRectCapacity": 4096,
    "groundOverlayCapacity": 16384,
    "splineRibbonCapacity": 32768,
    "worldHudCapacity": 65536,
    "screenHudCapacity": 65536,
    "minimapMarkerCapacity": 65536,
    "runtimeEntitySpawnQueueCapacity": 65536,
    "runtimeEntitySpawnReceiptQueueCapacity": 32768,
    "runtimeEntityLifecycleQueueCapacity": 65536,
    "runtimeEntityLifecycleReceiptQueueCapacity": 32768
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

            Assert.That(config.Presentation.PresenterInstanceCapacity, Is.EqualTo(8192));
            Assert.That(config.Presentation.GasPresentationEventCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.PresentationEventStreamCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.PresentationOwnerChangeCapacity, Is.EqualTo(24576));
            Assert.That(config.Presentation.PresenterCommandCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.PrimitiveDrawBufferCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.VisualSnapshotBufferCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.VisualProxyBufferCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.SkinnedVisualBatchCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.PresentationRequestCapacity, Is.EqualTo(131072));
            Assert.That(config.Presentation.InstancedBatchRequestCapacity, Is.EqualTo(8192));
            Assert.That(config.Presentation.InstancedBatchOperationCapacity, Is.EqualTo(16384));
            Assert.That(config.Presentation.GlobalFieldVisualRecordCapacity, Is.EqualTo(512));
            Assert.That(config.Presentation.GlobalFieldVisualCellCapacity, Is.EqualTo(262144));
            Assert.That(config.Presentation.GlobalFieldVisualDirtyRectCapacity, Is.EqualTo(4096));
            Assert.That(config.Presentation.GroundOverlayCapacity, Is.EqualTo(16384));
            Assert.That(config.Presentation.SplineRibbonCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.WorldHudCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.ScreenHudCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.MinimapMarkerCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.RuntimeEntitySpawnQueueCapacity, Is.EqualTo(65536));
            Assert.That(config.Presentation.RuntimeEntitySpawnReceiptQueueCapacity, Is.EqualTo(32768));
            Assert.That(config.Presentation.CameraCulling.HighLodDistanceCm, Is.EqualTo(4000.0f));
            Assert.That(config.Presentation.Minimap.DebugMarkerSampleCapacity, Is.EqualTo(64));
            config.Presentation.Validate();
        }

        [Test]
        public void LudotsCoreCueMarker_IsLeafMeshNotPrefab()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") }, Path.Combine(repoRoot, "assets"));

            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");

            int cueMeshAssetId = meshes.GetId("cue_marker");
            Assert.That(cueMeshAssetId, Is.GreaterThan(0), "cue_marker mesh asset must stay registered in LudotsCoreMod.");
            Assert.That(meshes.TryGetDescriptor(cueMeshAssetId, out MeshAssetDescriptor cue), Is.True);
            Assert.That(cue.Type, Is.EqualTo(MeshAssetType.Primitive));

            var presenters = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) as PresenterDefinitionRegistry
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            CueMarkerAuthoredVisual authored = CueMarkerAuthoredVisual.Resolve(meshes, presenters);
            Assert.That(authored.MeshAssetId, Is.EqualTo(cueMeshAssetId));
            Assert.That(authored.Scale, Is.EqualTo(new System.Numerics.Vector3(0.2f, 0.2f, 0.2f)));
            Assert.That(authored.AnchorOffset.Y, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(authored.LifetimeSeconds, Is.EqualTo(0.35f).Within(0.001f));

            var constructorMeshes = new MeshAssetRegistry();
            Assert.That(
                constructorMeshes.GetId(WellKnownMeshKeys.CueMarker),
                Is.EqualTo(0),
                "cue_marker must not be dual-registered in MeshAssetRegistry constructor; mesh_assets.json is the mesh SSOT.");
            Assert.That(constructorMeshes.GetId(WellKnownMeshKeys.Cube), Is.GreaterThan(0));

            Assert.That(
                File.Exists(Path.Combine(repoRoot, "mods", "LudotsCoreMod", "assets", "Presentation", "prefabs.json")),
                Is.False);
        }

        [Test]
        public void CameraAcceptanceMod_DoesNotShipPrefabConfig()
        {
            string repoRoot = FindRepoRoot();
            Assert.That(
                File.Exists(Path.Combine(repoRoot, "mods", "fixtures", "camera", "CameraAcceptanceMod", "assets", "Presentation", "prefabs.json")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(repoRoot, "mods", "showcases", "raylib_client_parity", "RaylibClientParityShowcaseMod", "assets", "Presentation", "prefabs.json")),
                Is.False);

            string presentersPath = Path.Combine(
                repoRoot,
                "mods",
                "fixtures",
                "camera",
                "CameraAcceptanceMod",
                "assets",
                "Presentation",
                "presenters.json");
            Assert.That(File.Exists(presentersPath), Is.True);
            string json = File.ReadAllText(presentersPath);
            Assert.That(json, Does.Contain("camera_acceptance_projection_cue_fixture"));
            Assert.That(json, Does.Contain("\"assetKind\": \"Mesh\""));
            Assert.That(json, Does.Contain("\"assetKind\": \"Decal\""));
            Assert.That(json, Does.Contain("\"assetKind\": \"VFX\""));
            Assert.That(json, Does.Contain("\"assetKind\": \"Surface\""));
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

            Directory.CreateDirectory(coreRoot);
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
            File.WriteAllText(Path.Combine(coreRoot, "config_catalog.json"), writer.ToString());
        }

        private static InvalidOperationException LoadInvalidOrderTypesJson(string json)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderTypeStrictness", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(Path.Combine(core, "GAS"));
            WriteCatalog(core, "GAS/order_types.json", "DeepObject", string.Empty);
            File.WriteAllText(Path.Combine(core, "GAS", "order_types.json"), json);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            var orderRules = new OrderRuleRegistry();

            return Assert.Throws<InvalidOperationException>(
                () => new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog))!;
        }
    }
}
