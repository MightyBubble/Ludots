using System;
using System.IO;
using System.Numerics;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerDefinitionConfigLoaderTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PerformerDefinitionLoader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PerformerScopeTagRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PerformerScopeTagRegistry.Clear();
            TagRegistry.Clear();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failures in test teardown.
            }
        }

        [Test]
        public void Load_ParsesChildrenBehaviorsAndExtendsIntoSingleDefinition()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  { "id": "child_a" },
                  {
                    "id": "base_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Base" },
                        "command": { "kind": "SetParam", "paramKey": 300, "paramLane": "Int", "valueSource": "EventKeyId" }
                      }
                    ],
                    "bindings": [
                      { "paramKey": 1, "source": "constant", "constantValue": 5 }
                    ],
                    "paramDefaults": [
                      { "paramKey": 10, "lane": "Int", "intValue": 1 }
                    ],
                    "children": [
                      { "definitionId": "child_a", "scopeTag": "structure" }
                    ],
                    "behaviors": [
                      {
                        "slot": 2,
                        "kind": "Material",
                        "material": {
                          "baseMaterialId": "knight_base",
                          "materialSwapParamKey": 300,
                          "swapTable": [
                            { "paramValue": 0, "materialId": "brick_black" }
                          ]
                        }
                      },
                      {
                        "slot": 3,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "materialId": "knight_base",
                          "renderPath": "StaticMesh",
                          "mobility": "Static",
                          "localOffset": [1, 2, 3],
                          "localRotation": [0, 0, 0, 1],
                          "localScale": [2, 2, 2]
                        }
                      }
                    ]
                  },
                  {
                    "id": "knight",
                    "extends": "base_unit",
                    "bindings": [
                      { "paramKey": 1, "source": "constant", "constantValue": 9 }
                    ],
                    "paramDefaults": [
                      { "paramKey": 10, "lane": "Int", "intValue": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": 2,
                        "kind": "Material",
                        "material": {
                          "baseMaterialId": "knight_armor",
                          "materialSwapParamKey": 301,
                          "swapTable": [
                            { "paramValue": 1, "materialId": "brick_red" }
                          ]
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMeshId: key => string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0,
                resolveMaterialId: key => key switch
                {
                    "knight_base" => 101,
                    "knight_armor" => 102,
                    "brick_black" => 201,
                    "brick_red" => 202,
                    _ => 0,
                },
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0);

            loader.Load(catalog);

            int knightId = registry.GetId("knight");
            Assert.That(registry.TryGet(knightId, out var knight), Is.True);
            Assert.That(knight.Extends, Is.EqualTo("base_unit"));
            Assert.That(knight.Children.Length, Is.EqualTo(1));
            Assert.That(knight.Children[0].DefinitionId, Is.EqualTo(registry.GetId("child_a")));
            Assert.That(knight.Children[0].ScopeTag, Is.EqualTo(PerformerScopeTagRegistry.GetId("structure")));
            Assert.That(knight.Rules.Length, Is.EqualTo(1));
            Assert.That(knight.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.GameplayEvent));
            Assert.That(knight.Rules[0].Command.CommandKind, Is.EqualTo(PerformerCommandKind.SetParam));
            Assert.That(knight.Rules[0].Command.ParamKey, Is.EqualTo(300));
            Assert.That(knight.Rules[0].Command.ParamLane, Is.EqualTo(ParamLane.Int));
            Assert.That(knight.Rules[0].Command.ValueSource, Is.EqualTo(PerformerCommandValueSource.EventKeyId));
            Assert.That(knight.Bindings.Length, Is.EqualTo(1));
            Assert.That(knight.Bindings[0].Value.ConstantValue, Is.EqualTo(9f));
            Assert.That(knight.ParamDefaults.Length, Is.EqualTo(1));
            Assert.That(knight.ParamDefaults[0].Lane, Is.EqualTo(ParamLane.Int));
            Assert.That(knight.ParamDefaults[0].IntValue, Is.EqualTo(7));
            Assert.That(knight.Behaviors.Length, Is.EqualTo(2));
            Assert.That(knight.Behaviors[0].SlotIndex, Is.EqualTo(2));
            Assert.That(knight.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.Material));
            Assert.That(knight.Behaviors[0].Material.BaseMaterialId, Is.EqualTo(102));
            Assert.That(knight.Behaviors[0].Material.SwapTable[0].MaterialId, Is.EqualTo(202));
            Assert.That(knight.Behaviors[1].SlotIndex, Is.EqualTo(3));
            Assert.That(knight.Behaviors[1].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(knight.Behaviors[1].AssetBinding.AssetId, Is.EqualTo(42));
            Assert.That(knight.Behaviors[1].AssetBinding.MaterialId, Is.EqualTo(101));
            Assert.That(knight.Behaviors[1].AssetBinding.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(knight.Behaviors[1].AssetBinding.Mobility, Is.EqualTo(VisualMobility.Static));
            Assert.That(knight.Behaviors[1].AssetBinding.LocalOffset, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(knight.Behaviors[1].AssetBinding.LocalScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }

        [Test]
        public void Load_RejectsGroundingInsideAssetBinding()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "bad_asset_grounding",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "renderPath": "InstancedStaticMesh",
                          "grounding": "SnapToGround"
                        }
                      }
                    ]
                  },
                  {
                    "id": "good_mesh",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "renderPath": "InstancedStaticMesh"
                        }
                      },
                      {
                        "slot": 1,
                        "kind": "Grounding",
                        "activeByDefault": true,
                        "grounding": {
                          "mode": "SnapToGround",
                          "updatePolicy": "Once"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("bad_asset_grounding"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("good_mesh"), out var good), Is.True);
            Assert.That(good.Behaviors[1].Grounding.UpdatePolicy, Is.EqualTo(GroundingUpdatePolicy.Once));
        }

        [Test]
        public void Load_ParsesMinimapMarkerBehaviorAsAuthoredCoreSignal()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "marker_actor",
                    "behaviors": [
                      {
                        "slot": 4,
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [0.18, 0.82, 1.0, 1.0],
                          "sizePx": 8.0,
                          "colorParamKey": 21,
                          "sizeParamKey": 22,
                          "visibilityParamKey": 23
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("marker_actor"), out var definition), Is.True);
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.MinimapMarker));
            Assert.That(definition.Behaviors[0].SlotIndex, Is.EqualTo(4));
            Assert.That(definition.Behaviors[0].ActiveByDefault, Is.True);
            Assert.That(definition.Behaviors[0].MinimapMarker.Shape, Is.EqualTo(MinimapMarkerShape.Circle));
            Assert.That(definition.Behaviors[0].MinimapMarker.Color, Is.EqualTo(new Vector4(0.18f, 0.82f, 1.0f, 1.0f)));
            Assert.That(definition.Behaviors[0].MinimapMarker.SizePx, Is.EqualTo(8f));
            Assert.That(definition.Behaviors[0].MinimapMarker.ColorParamKey, Is.EqualTo(21));
            Assert.That(definition.Behaviors[0].MinimapMarker.SizeParamKey, Is.EqualTo(22));
            Assert.That(definition.Behaviors[0].MinimapMarker.VisibilityParamKey, Is.EqualTo(23));
        }

        [Test]
        public void Load_SkipsInvalidMinimapMarkerShapeWithoutDefaultingToAssetBinding()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "bad_marker",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "MinimapMarker",
                        "minimapMarker": {
                          "shape": "Square",
                          "color": [1.0, 0.0, 0.0, 1.0],
                          "sizePx": 8.0
                        }
                      }
                    ]
                  },
                  {
                    "id": "good_marker",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "MinimapMarker",
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [0.0, 1.0, 0.0, 1.0],
                          "sizePx": 6.0
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load(catalog));

            Assert.That(registry.GetId("bad_marker"), Is.EqualTo(0));
            Assert.That(registry.TryGet(registry.GetId("good_marker"), out var good), Is.True);
            Assert.That(good.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.MinimapMarker));
        }

        [Test]
        public void Load_PreservesChildrenAsDeclarativeHierarchy_WithoutSyntheticRules()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  { "id": "child_a" },
                  {
                    "id": "root",
                    "children": [
                      { "definitionId": "child_a", "scopeTag": "structure" }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("root"), out var root), Is.True);
            Assert.That(root.Rules.Length, Is.EqualTo(0));
            Assert.That(root.Children.Length, Is.EqualTo(1));
            Assert.That(root.Children[0].DefinitionId, Is.EqualTo(registry.GetId("child_a")));
            Assert.That(root.Children[0].ScopeTag, Is.EqualTo(PerformerScopeTagRegistry.GetId("structure")));
        }

        [Test]
        public void Load_ExpandsExtendsChain_AppendsRules_AndOverridesBehaviorsBySlot()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "base_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Base" },
                        "command": { "kind": "DestroyPerformerScope", "scopeTag": "base" }
                      }
                    ],
                    "behaviors": [
                      {
                        "slot": 2,
                        "kind": "Material",
                        "material": { "baseMaterialId": "knight_base" }
                      }
                    ]
                  },
                  {
                    "id": "knight",
                    "extends": "base_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Child" },
                        "command": { "kind": "DestroyPerformerScope", "scopeTag": "child" }
                      }
                    ],
                    "behaviors": [
                      {
                        "slot": 2,
                        "kind": "Material",
                        "material": { "baseMaterialId": "knight_armor" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => key == "knight_base" ? 100 : key == "knight_armor" ? 200 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("knight"), out var knight), Is.True);
            Assert.That(knight.Rules.Length, Is.EqualTo(2));
            Assert.That(knight.Rules[0].Event.KeyId, Is.EqualTo(TagRegistry.GetId("Event.Base")));
            Assert.That(knight.Rules[1].Event.KeyId, Is.EqualTo(TagRegistry.GetId("Event.Child")));
            Assert.That(knight.Behaviors.Length, Is.EqualTo(1));
            Assert.That(knight.Behaviors[0].SlotIndex, Is.EqualTo(2));
            Assert.That(knight.Behaviors[0].Material.BaseMaterialId, Is.EqualTo(200));
        }

        [Test]
        public void Load_SkipsLegacyInvalidFields_AndKeepsValidDefinitions()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "bad_performer",
                    "visualKind": "WorldBar",
                    "entityScope": "AllWithAttributes",
                    "requiredTemplate": "moba_hero"
                  },
                  {
                    "id": "good_performer",
                    "rules": [
                      {
                        "event": { "kind": "TagEffectiveChanged", "keyId": "Status.Working" },
                        "command": { "kind": "DestroyPerformerScope", "scopeTag": "working" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.TryGet(registry.GetId("bad_performer"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("good_performer"), out var good), Is.True);
            Assert.That(good.Rules[0].Event.KeyId, Is.EqualTo(TagRegistry.GetId("Status.Working")));
            Assert.That(good.Rules[0].Command.ScopeTag, Is.EqualTo(PerformerScopeTagRegistry.GetId("working")));
        }

        [Test]
        public void Load_WorldTextDefaultTextIdAndAssetId_ResolveThroughTextTokenRegistry()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "floating_text",
                    "defaultTextId": "hud.combat.delta",
                    "legacyWorldTextMode": "AttributeCurrent",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldText",
                          "assetId": "hud.combat.delta"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.combat.delta" ? 777 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("floating_text"), out var definition), Is.True);
            Assert.That(definition.DefaultTextId, Is.EqualTo(777));
            Assert.That(definition.LegacyWorldTextMode, Is.EqualTo(WorldHudValueMode.AttributeCurrent));
            Assert.That(definition.Behaviors[0].AssetBinding.AssetKind, Is.EqualTo(AssetKind.WorldText));
            Assert.That(definition.Behaviors[0].AssetBinding.AssetId, Is.EqualTo(777));
        }

        [Test]
        public void Load_SkipsDefinitionWithInheritanceCycle_AndKeepsIndependentDefinitions()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  { "id": "cycle_a", "extends": "cycle_b" },
                  { "id": "cycle_b", "extends": "cycle_a" },
                  { "id": "ok_root" }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.TryGet(registry.GetId("cycle_a"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("cycle_b"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("ok_root"), out _), Is.True);
        }

        [Test]
        public void Load_SkipsDefinitionUsingRemovedSchemaAliases_AndKeepsCanonicalDefinition()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "legacy_aliases",
                    "visualKind": "Marker3D",
                    "bindings": [
                      { "paramKey": 1, "source": "textToken", "sourceKey": "hud.current_over_base" }
                    ],
                    "paramDefaults": [
                      { "paramKey": 10, "value": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "TagBinding",
                        "tagBinding": {
                          "tag": "Status.Working",
                          "targetParamKey": 11
                        }
                      }
                    ]
                  },
                  {
                    "id": "canonical",
                    "bindings": [
                      { "paramKey": 1, "source": "textToken", "textToken": "hud.current_over_base" }
                    ],
                    "paramDefaults": [
                      { "paramKey": 10, "lane": "Int", "intValue": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "TagBinding",
                        "tagBinding": {
                          "tagId": "Status.Working",
                          "targetParamKey": 11
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.current_over_base" ? 42 : 0);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.TryGet(registry.GetId("legacy_aliases"), out _), Is.False);
            Assert.That(registry.TryGet(registry.GetId("canonical"), out var canonical), Is.True);
            Assert.That(canonical.Bindings[0].Value.ConstantValue, Is.EqualTo(42f));
            Assert.That(canonical.ParamDefaults[0].Lane, Is.EqualTo(ParamLane.Int));
            Assert.That(canonical.ParamDefaults[0].IntValue, Is.EqualTo(7));
            Assert.That(canonical.Behaviors[0].TagBinding.TagId, Is.EqualTo(TagRegistry.GetId("Status.Working")));
        }

        [Test]
        public void Load_RemovesFailedDefinitionIdMappings_AndSkipsParentsThatReferenceThem()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "broken_child",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldText",
                          "assetId": "hud.missing.token"
                        }
                      }
                    ]
                  },
                  {
                    "id": "root",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Spawn" },
                        "command": { "kind": "CreatePerformer", "definitionId": "broken_child", "scopeTag": "structure" }
                      }
                    ]
                  },
                  {
                    "id": "ok_root"
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: _ => 0);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.GetId("broken_child"), Is.EqualTo(0), "Failed definitions must not leave ghost ids behind.");
            Assert.That(registry.GetId("root"), Is.EqualTo(0), "Parents that reference failed child definitions must also be rejected.");
            Assert.That(registry.TryGet(registry.GetId("ok_root"), out _), Is.True);
        }

        [Test]
        public void Load_RemovesTransitiveParentsWhenReferencedDefinitionFailsLateValidation()
        {
            WriteCatalog();
            WritePerformers(
                """
                [
                  {
                    "id": "bad_leaf",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldText",
                          "assetId": "hud.missing.token"
                        }
                      }
                    ]
                  },
                  {
                    "id": "mid_node",
                    "children": [
                      { "definitionId": "bad_leaf", "scopeTag": "structure" }
                    ]
                  },
                  {
                    "id": "top_root",
                    "children": [
                      { "definitionId": "mid_node", "scopeTag": "structure" }
                    ]
                  },
                  {
                    "id": "ok_root"
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: _ => 0);

            Assert.DoesNotThrow(() => loader.Load(catalog));
            Assert.That(registry.GetId("bad_leaf"), Is.EqualTo(0), "Failed leaf definitions must not leave ghost ids behind.");
            Assert.That(registry.GetId("mid_node"), Is.EqualTo(0), "Parents that auto-expand children into failed leaf definitions must also be rejected.");
            Assert.That(registry.GetId("top_root"), Is.EqualTo(0), "Transitive parents must be removed when a downstream child definition fails validation.");
            Assert.That(registry.TryGet(registry.GetId("ok_root"), out _), Is.True);
        }

        private (VirtualFileSystem Vfs, ModLoader ModLoader, ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private void WriteCatalog()
        {
            WriteFile(
                "Core",
                "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePerformers(string content)
        {
            WriteFile("Core", "Presentation/performers.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }
    }
}
