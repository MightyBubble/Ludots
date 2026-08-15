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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterDefinitionConfigLoaderTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterDefinitionLoader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();

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
            WritePresenters(
                """
                [
                  { "id": "child_a" },
                  {
                    "id": "base_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Base" },
                        "command": { "kind": "SetParam", "paramKey": "test.material.state", "paramLane": "Int", "valueSource": "EventKeyId" }
                      }
                    ],
                    "bindings": [
                      { "paramKey": "test.binding.value", "source": "constant", "constantValue": 5 }
                    ],
                    "paramDefaults": [
                      { "paramKey": "test.default.int", "lane": "Int", "intValue": 1 }
                    ],
                    "children": [
                      { "definitionId": "child_a", "scopeTag": "structure" }
                    ],
                    "behaviors": [
                      {
                        "slot": "material",
                        "kind": "Material",
                        "material": {
                          "baseMaterialId": "knight_base",
                          "materialSwapParamKey": "test.material.state",
                          "swapTable": [
                            { "paramValue": 0, "materialId": "brick_black" }
                          ]
                        }
                      },
                      {
                        "slot": "body",
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
                      { "paramKey": "test.binding.value", "source": "constant", "constantValue": 9 }
                    ],
                    "paramDefaults": [
                      { "paramKey": "test.default.int", "lane": "Int", "intValue": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": "material",
                        "kind": "Material",
                        "material": {
                          "baseMaterialId": "knight_armor",
                          "materialSwapParamKey": "test.material.variant",
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
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
            Assert.That(knight.Children[0].ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("structure")));
            Assert.That(knight.Rules.Length, Is.EqualTo(1));
            Assert.That(knight.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.GameplayEvent));
            Assert.That(knight.Rules[0].Command.CommandKind, Is.EqualTo(PresenterCommandKind.SetParam));
            Assert.That(knight.Rules[0].Command.ParamKey, Is.EqualTo(PresenterParamKeyRegistry.Register("test.material.state")));
            Assert.That(knight.Rules[0].Command.ParamLane, Is.EqualTo(ParamLane.Int));
            Assert.That(knight.Rules[0].Command.ValueSource, Is.EqualTo(PresenterCommandValueSource.EventKeyId));
            Assert.That(knight.Bindings.Length, Is.EqualTo(1));
            Assert.That(knight.Bindings[0].Value.ConstantValue, Is.EqualTo(9f));
            Assert.That(knight.ParamDefaults.Length, Is.EqualTo(1));
            Assert.That(knight.ParamDefaults[0].Lane, Is.EqualTo(ParamLane.Int));
            Assert.That(knight.ParamDefaults[0].IntValue, Is.EqualTo(7));
            Assert.That(knight.Behaviors.Length, Is.EqualTo(2));
            Assert.That(knight.Behaviors[0].SlotIndex, Is.EqualTo(5));
            Assert.That(knight.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.Material));
            Assert.That(knight.Behaviors[0].Material.BaseMaterialId, Is.EqualTo(102));
            Assert.That(knight.Behaviors[0].Material.SwapTable[0].MaterialId, Is.EqualTo(202));
            Assert.That(knight.Behaviors[1].SlotIndex, Is.EqualTo(0));
            Assert.That(knight.Behaviors[1].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(knight.Behaviors[1].AssetBinding.AssetId, Is.EqualTo(42));
            Assert.That(knight.Behaviors[1].AssetBinding.MaterialId, Is.EqualTo(101));
            Assert.That(knight.Behaviors[1].AssetBinding.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(knight.Behaviors[1].AssetBinding.Mobility, Is.EqualTo(VisualMobility.Static));
            Assert.That(knight.Behaviors[1].AssetBinding.LocalOffset, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(knight.Behaviors[1].AssetBinding.LocalScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }

        [Test]
        public void Load_CompilesSemanticParamKeysAndBehaviorSlots()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "semantic_actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Semantic" },
                        "command": { "kind": "SetParam", "paramKey": "semantic.health.ratio", "paramLane": "Float", "valueSource": "Fixed", "paramValue": 0.5 }
                      }
                    ],
                    "bindings": [
                      { "paramKey": "semantic.health.ratio", "source": "constant", "constantValue": 0.75 }
                    ],
                    "paramDefaults": [
                      { "paramKey": "semantic.health.ratio", "lane": "Float", "floatValue": 1.0 }
                    ],
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldHud",
                          "renderPath": "None",
                          "mobility": "Movable",
                          "materialParamKey": "semantic.health.ratio"
                        }
                      },
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "sizePx": 6.0,
                          "visibilityParamKey": "none"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            int semanticKey = PresenterParamKeyRegistry.Register("semantic.health.ratio");
            Assert.That(registry.TryGet(registry.GetId("semantic_actor"), out var definition), Is.True);
            Assert.That(definition.Rules[0].Command.ParamKey, Is.EqualTo(semanticKey));
            Assert.That(definition.Bindings[0].ParamKey, Is.EqualTo(semanticKey));
            Assert.That(definition.ParamDefaults[0].ParamKey, Is.EqualTo(semanticKey));
            Assert.That(definition.Behaviors[0].SlotIndex, Is.EqualTo(0));
            Assert.That(definition.Behaviors[0].AssetBinding.MaterialParamKey, Is.EqualTo(semanticKey));
            Assert.That(definition.Behaviors[1].SlotIndex, Is.EqualTo(2));
            Assert.That(definition.Behaviors[1].MinimapMarker.VisibilityParamKey, Is.EqualTo(-1));
        }

        [Test]
        public void Load_ExtensionCommandKind_ParsesRegisteredCommand()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "extension_actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Extension" },
                        "command": {
                          "kind": "ExampleMod.MarkCommand",
                          "route": "SingleRuntime",
                          "scopeTag": "extensionScope"
                        }
                      }
                    ]
                  }
                ]
                """);

            var commandKinds = new PerformerCommandKindRegistry();
            int commandKindId = commandKinds.Register(
                "ExampleMod.MarkCommand",
                new PerformerCommandExtensionDescriptor(
                    PerformerCommandRouteStrategy.SingleRuntime,
                    NoOpExtensionCommand));
            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry, commandKinds: commandKinds);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("extension_actor"), out var definition), Is.True);
            Assert.That(definition.Rules[0].Command.CommandKind, Is.EqualTo(PresenterCommandKind.Extension));
            Assert.That(definition.Rules[0].Command.CommandKindId, Is.EqualTo(commandKindId));
            Assert.That(definition.Rules[0].Command.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.SingleRuntime));
            Assert.That(definition.Rules[0].Command.ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("extensionScope")));
        }

        [Test]
        public void Load_ExtensionBehaviorKind_ParsesRegisteredBehavior()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "extension_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "ExampleMod.TickBehavior",
                        "execution": { "lane": "ContinuousTick" },
                        "activeByDefault": true
                      }
                    ]
                  }
                ]
                """);

            var behaviorKinds = new PerformerBehaviorKindRegistry();
            int behaviorKindId = behaviorKinds.Register(
                "ExampleMod.TickBehavior",
                new PerformerBehaviorExtensionDescriptor(
                    PerformerBehaviorExecutionLane.ContinuousTick,
                    NoOpExtensionBehavior));
            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry, behaviorKinds: behaviorKinds);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("extension_actor"), out var definition), Is.True);
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.Extension));
            Assert.That(definition.Behaviors[0].KindId, Is.EqualTo(behaviorKindId));
            Assert.That(definition.Behaviors[0].ExtensionLane, Is.EqualTo(PerformerBehaviorExecutionLane.ContinuousTick));
            Assert.That(definition.Behaviors[0].ActiveByDefault, Is.True);
        }

        [Test]
        public void Load_ResolvesGasSemanticEventKeysIntoPresenterRules()
        {
            WriteCatalog();
            int castAbilityId = AbilityIdRegistry.Register("Ability.Test.Cast");
            int hitEffectId = EffectTemplateIdRegistry.Register("Effect.Test.Hit");
            int persistentEffectId = EffectTemplateIdRegistry.Register("Effect.Test.Persistent");
            WritePresenters(
                """
                [
                  {
                    "id": "semantic_event_actor",
                    "rules": [
                      {
                        "event": { "kind": "CastCommitted", "key": "Ability.Test.Cast" },
                        "command": { "kind": "CreatePresenter", "definitionId": "semantic_event_actor", "scopeSource": "Fixed" }
                      },
                      {
                        "event": { "kind": "EffectApplied", "key": "Effect.Test.Hit" },
                        "command": { "kind": "CreatePresenter", "definitionId": "semantic_event_actor", "scopeSource": "Fixed" }
                      },
                      {
                        "event": { "kind": "EffectActivated", "key": "Effect.Test.Persistent" },
                        "command": { "kind": "CreatePresenter", "definitionId": "semantic_event_actor", "scopeSource": "Fixed" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveEffectTemplateId: EffectTemplateIdRegistry.GetId);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("semantic_event_actor"), out var definition), Is.True);
            Assert.That(definition.Rules.Length, Is.EqualTo(3));
            Assert.That(definition.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.CastCommitted));
            Assert.That(definition.Rules[0].Event.KeyId, Is.EqualTo(castAbilityId));
            Assert.That(definition.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.EffectApplied));
            Assert.That(definition.Rules[1].Event.KeyId, Is.EqualTo(hitEffectId));
            Assert.That(definition.Rules[2].Event.Kind, Is.EqualTo(PresentationEventKind.EffectActivated));
            Assert.That(definition.Rules[2].Event.KeyId, Is.EqualTo(persistentEffectId));
        }

        [TestCase(
            """
            { "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event requires an object with explicit field 'kind'.")]
        [TestCase(
            """
            { "event": {}, "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event.kind requires a non-empty enum string.")]
        [TestCase(
            """
            { "event": { "kind": "" }, "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event.kind requires a non-empty enum string.")]
        [TestCase(
            """
            { "event": { "kind": "GameplaySignal" }, "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event.kind has invalid value 'GameplaySignal'.")]
        [TestCase(
            """
            { "event": { "kind": "50" }, "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event.kind has invalid value '50'.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" }, "command": { "kind": "50" } }
            """,
            "rules[0].command.kind has invalid value '50'.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" } }
            """,
            "rules[0].command requires an object with explicit field 'kind'.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" }, "command": {} }
            """,
            "rules[0].command.kind must be a semantic string.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" }, "command": { "kind": "" } }
            """,
            "rules[0].command.kind must be a semantic string.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" }, "command": { "kind": "FireAndForget" } }
            """,
            "rules[0].command.kind has invalid value 'FireAndForget'.")]
        [TestCase(
            """
            { "event": { "kind": "None" }, "command": { "kind": "SetParam", "paramKey": "test.value" } }
            """,
            "rules[0].event.kind must not be 'None'.")]
        [TestCase(
            """
            { "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" }, "command": { "kind": "None" } }
            """,
            "rules[0].command.kind must not be 'None'.")]
        public void Load_RejectsRulesWithoutExplicitExecutableEventAndCommandKinds(string ruleJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_rule_actor",
                    "rules": [
                      {{ruleJson}}
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase(
            """
            { "kind": "CreatePresenter", "definitionId": "strict_actor", "scopeTag": "strictScope" }
            """,
            "rules[0].command.scopeSource")]
        [TestCase(
            """
            { "kind": "DestroyPresenterScope", "scopeTag": "strictScope" }
            """,
            "rules[0].command.scopeSource")]
        [TestCase(
            """
            { "kind": "DestroyScopedPresenter", "definitionId": "strict_actor", "scopeTag": "strictScope" }
            """,
            "rules[0].command.scopeSource")]
        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "valueSource": "Fixed", "paramValue": 1.0 }
            """,
            "rules[0].command.paramLane")]
        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "paramLane": "Float", "paramValue": 1.0 }
            """,
            "rules[0].command.valueSource")]
        public void Load_RejectsCommandsMissingRequiredExplicitConfigFields(string commandJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" },
                        "command": {{commandJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("Field must be explicit"));
        }

        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "paramLane": "Float", "valueSource": "Fixed" }
            """,
            "rules[0].command.paramValue")]
        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "paramLane": "Float", "valueSource": "Fixed", "ParamValue": 1.0 }
            """,
            "rules[0].command.paramValue")]
        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "paramLane": "Int", "valueSource": "Fixed" }
            """,
            "rules[0].command.intValue")]
        [TestCase(
            """
            { "kind": "SetParam", "paramKey": "strict.param", "paramLane": "Vector", "valueSource": "Fixed" }
            """,
            "rules[0].command.vectorValue")]
        public void Load_RejectsFixedSetParamMissingLanePayload(string commandJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Strict" },
                        "command": {{commandJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("requires an explicit"));
        }

        [TestCase(
            """
            { "paramKey": "strict.binding", "source": "graph" }
            """,
            "Presenter binding graph.sourceId")]
        [TestCase(
            """
            { "paramKey": "strict.binding", "source": "entityColor" }
            """,
            "Presenter binding entityColor.sourceId")]
        [TestCase(
            """
            { "paramKey": "strict.binding", "source": "constant" }
            """,
            "Presenter binding constant.constantValue")]
        [TestCase(
            """
            { "paramKey": "strict.binding", "source": "constant", "ConstantValue": 1.0 }
            """,
            "Presenter binding constant.constantValue")]
        public void Load_RejectsBindingsMissingRequiredSourcePayload(string bindingJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_binding_actor",
                    "bindings": [
                      {{bindingJson}}
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("requires an explicit"));
        }

        [Test]
        public void Load_RejectsAttributeSourcesInParamBindings()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "attribute_param_binding",
                    "bindings": [
                      { "paramKey": "legacy.health.ratio", "source": "attributeRatio", "attributeId": "Health" }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: key => key == "Health" ? 1 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("duplicates AttributeBinding behavior"));
            Assert.That(ex.Message, Does.Contain("attributeBinding.targetParamKey"));
        }

        [TestCase(
            """
            {
              "assetId": "cube",
              "renderPath": "StaticMesh",
              "mobility": "Static"
            }
            """,
            "AssetBinding.assetKind")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "mobility": "Static"
            }
            """,
            "AssetBinding.renderPath")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "renderPath": "StaticMesh"
            }
            """,
            "AssetBinding.mobility")]
        public void Load_RejectsAssetBindingsMissingRequiredExplicitConfigFields(string assetBindingJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_asset_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "assetBinding": {{assetBindingJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("Field must be explicit"));
        }

        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "renderPath": "StaticMesh",
              "mobility": "Static"
            }
            """,
            "Mesh assetId")]
        [TestCase(
            """
            {
              "assetKind": "WorldText",
              "renderPath": "None",
              "mobility": "Movable"
            }
            """,
            "WorldText assetId")]
        [TestCase(
            """
            {
              "assetKind": "WorldHud",
              "assetId": "unused.hud.asset",
              "renderPath": "None",
              "mobility": "Movable"
            }
            """,
            "WorldHud AssetBinding must not declare assetId")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "assetSwapTable": [
                { "paramValue": 0, "assetId": "cube" }
              ],
              "renderPath": "StaticMesh",
              "mobility": "Static"
            }
            """,
            "assetSwapTable requires explicit assetSwapParamKey")]
        public void Load_RejectsAssetBindingImplicitOrDeadAssetFields(string assetBindingJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_asset_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {{assetBindingJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0,
                resolveTextTokenId: key =>
                    string.Equals(key, "hud.combat.delta", StringComparison.Ordinal) ? 777 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Load_ParsesSurfaceAssetBindingRoutingAndMaterialCustomData()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "surface_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Surface",
                          "assetId": "surface.projector",
                          "materialId": "surface.grid",
                          "renderPath": "Surface",
                          "mobility": "Static",
                          "surfaceLayerKey": "terrain.rvt",
                          "sortId": 17,
                          "materialCustomData": [
                            {
                              "slot": 1,
                              "lane": "Vector",
                              "paramKey": "surface.flow",
                              "defaultVectorValue": [0.1, 0.2, 0.3, 0.4]
                            },
                            {
                              "slot": 0,
                              "lane": "Float",
                              "paramKey": "surface.heat",
                              "defaultFloatValue": 2.5
                            }
                          ]
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => string.Equals(key, "surface.grid", StringComparison.Ordinal) ? 88 : 0,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Surface && string.Equals(key, "surface.projector", StringComparison.Ordinal) ? 77 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("surface_actor"), out var definition), Is.True);
            AssetBindingConfig binding = definition.Behaviors[0].AssetBinding;
            Assert.That(binding.AssetKind, Is.EqualTo(AssetKind.Surface));
            Assert.That(binding.AssetId, Is.EqualTo(77));
            Assert.That(binding.MaterialId, Is.EqualTo(88));
            Assert.That(binding.RenderPath, Is.EqualTo(VisualRenderPath.Surface));
            Assert.That(binding.SurfaceLayerKey, Is.EqualTo("terrain.rvt"));
            Assert.That(binding.SortId, Is.EqualTo(17));
            Assert.That(binding.MaterialCustomData.Slots.Length, Is.EqualTo(2));
            Assert.That(binding.MaterialCustomData.Slots[0].Slot, Is.EqualTo(0));
            Assert.That(binding.MaterialCustomData.Slots[0].Lane, Is.EqualTo(MaterialCustomDataLane.Float));
            Assert.That(binding.MaterialCustomData.Slots[0].ParamKey, Is.EqualTo(PresenterParamKeyRegistry.Register("surface.heat")));
            Assert.That(binding.MaterialCustomData.Slots[0].DefaultFloatValue, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(binding.MaterialCustomData.Slots[1].Slot, Is.EqualTo(1));
            Assert.That(binding.MaterialCustomData.Slots[1].Lane, Is.EqualTo(MaterialCustomDataLane.Vector));
            Assert.That(binding.MaterialCustomData.Slots[1].ParamKey, Is.EqualTo(PresenterParamKeyRegistry.Register("surface.flow")));
            Assert.That(binding.MaterialCustomData.Slots[1].DefaultVectorValue, Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        }

        [TestCase(
            """
            {
              "assetKind": "Surface",
              "assetId": "surface.projector",
              "renderPath": "StaticMesh",
              "mobility": "Static",
              "surfaceLayerKey": "terrain.rvt"
            }
            """,
            "requires renderPath 'Surface'")]
        [TestCase(
            """
            {
              "assetKind": "Surface",
              "assetId": "surface.projector",
              "renderPath": "Surface",
              "mobility": "Static"
            }
            """,
            "requires non-empty surfaceLayerKey")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "renderPath": "StaticMesh",
              "mobility": "Static",
              "surfaceLayerKey": "terrain.rvt"
            }
            """,
            "surfaceLayerKey is only valid for Surface assets")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "renderPath": "StaticMesh",
              "mobility": "Static",
              "sortId": 3
            }
            """,
            "sortId is only valid for Surface assets")]
        public void Load_RejectsMisconfiguredSurfaceAssetBinding(string assetBindingJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "bad_surface_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {{assetBindingJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    (kind == AssetKind.Surface && string.Equals(key, "surface.projector", StringComparison.Ordinal)) ||
                    (kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal))
                        ? 42
                        : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase(
            """
            {
              "assetKind": "WorldHud",
              "renderPath": "None",
              "mobility": "Movable",
              "materialCustomData": [
                { "slot": 0, "lane": "Float", "defaultFloatValue": 1.0 }
              ]
            }
            """,
            "not supported by renderPath 'None'")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "renderPath": "StaticMesh",
              "mobility": "Static",
              "materialCustomData": [
                { "slot": 1, "lane": "Float", "defaultFloatValue": 1.0 }
              ]
            }
            """,
            "contiguous starting at 0")]
        [TestCase(
            """
            {
              "assetKind": "Mesh",
              "assetId": "cube",
              "renderPath": "StaticMesh",
              "mobility": "Static",
              "materialCustomData": [
                { "slot": 0, "lane": "Float", "defaultFloatValue": 1.0 },
                { "slot": 1, "lane": "Float", "defaultFloatValue": 1.0 },
                { "slot": 2, "lane": "Float", "defaultFloatValue": 1.0 },
                { "slot": 3, "lane": "Float", "defaultFloatValue": 1.0 },
                { "slot": 4, "lane": "Float", "defaultFloatValue": 1.0 }
              ]
            }
            """,
            "supports at most 4 slots")]
        public void Load_RejectsMisconfiguredMaterialCustomData(string assetBindingJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "bad_custom_data_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {{assetBindingJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase(
            """
            {
              "baseMaterialId": "knight_base",
              "swapTable": [
                { "paramValue": 0, "materialId": "knight_armor" }
              ]
            }
            """,
            "swapTable requires explicit materialSwapParamKey")]
        [TestCase(
            """
            {
              "baseMaterialId": "knight_base",
              "materialSwapParamKey": "test.material.state"
            }
            """,
            "materialSwapParamKey requires a non-empty swapTable")]
        public void Load_RejectsMaterialSwapPartialConfig(string materialJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_material_actor",
                    "behaviors": [
                      {
                        "slot": "material",
                        "kind": "Material",
                        "material": {{materialJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => key switch
                {
                    "knight_base" => 100,
                    "knight_armor" => 200,
                    _ => 0,
                });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase(
            """
            { "updatePolicy": "Once" }
            """,
            "Grounding.mode")]
        [TestCase(
            """
            { "mode": "SnapToGround" }
            """,
            "Grounding.updatePolicy")]
        public void Load_RejectsGroundingMissingRequiredExplicitConfigFields(string groundingJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_grounding_actor",
                    "behaviors": [
                      {
                        "slot": "grounding",
                        "kind": "Grounding",
                        "grounding": {{groundingJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("Field must be explicit"));
        }

        [Test]
        public void Load_RejectsImplicitEventWildcard_AndAcceptsExplicitWildcardKey()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "implicit_wildcard_actor",
                    "rules": [
                      {
                        "event": { "kind": "EntitySpawned" },
                        "command": { "kind": "CreatePresenter", "definitionId": "implicit_wildcard_actor" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("requires explicit key or keyId"));
            Assert.That(ex.Message, Does.Contain("key \"*\""));

            WritePresenters(
                """
                [
                  {
                    "id": "explicit_wildcard_actor",
                    "rules": [
                      {
                        "event": { "kind": "EntitySpawned", "key": "*" },
                        "command": { "kind": "CreatePresenter", "definitionId": "explicit_wildcard_actor", "scopeSource": "Fixed" }
                      }
                    ]
                  }
                ]
                """);
            registry = new PresenterDefinitionRegistry();
            (_, _, pipeline, catalog) = BuildPipeline();
            loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("explicit_wildcard_actor"), out var definition), Is.True);
            Assert.That(definition.Rules[0].Event.KeyId, Is.EqualTo(-1));
        }

        [Test]
        public void Load_ResolvesEntityCollectionEventsThroughCollectionKeyResolver()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "collection_highlight" },
                  {
                    "id": "collection_rules",
                    "rules": [
                      {
                        "event": { "kind": "EntityCollectionMemberAdded", "key": "collection.ability.aim.affected" },
                        "command": {
                          "kind": "CreatePresenter",
                          "definitionId": "collection_highlight",
                          "scopeSource": "EventPayloadA",
                          "ownerSource": "EventSource"
                        }
                      },
                      {
                        "event": { "kind": "EntityCollectionMemberRemoved", "key": "collection.ability.aim.affected" },
                        "command": {
                          "kind": "DestroyScopedPresenter",
                          "definitionId": "collection_highlight",
                          "scopeSource": "EventPayloadA",
                          "ownerSource": "EventSource"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveEntityCollectionKeyId: key => string.Equals(key, "collection.ability.aim.affected", StringComparison.Ordinal)
                    ? 322
                    : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("collection_rules"), out var definition), Is.True);
            Assert.That(definition.Rules.Length, Is.EqualTo(2));
            Assert.That(definition.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.EntityCollectionMemberAdded));
            Assert.That(definition.Rules[0].Event.KeyId, Is.EqualTo(322));
            Assert.That(definition.Rules[0].Command.OwnerSource, Is.EqualTo(PresenterCommandEntitySource.EventSource));
            Assert.That(definition.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.EntityCollectionMemberRemoved));
            Assert.That(definition.Rules[1].Event.KeyId, Is.EqualTo(322));
            Assert.That(definition.Rules[1].Command.OwnerSource, Is.EqualTo(PresenterCommandEntitySource.EventSource));
        }

        [Test]
        public void Load_CreatePresenterCommand_CanCarryInitialParamPayload()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "floating_text" },
                  {
                    "id": "combat_text_rules",
                    "rules": [
                      {
                        "event": { "kind": "EffectApplied", "key": "*" },
                        "command": {
                          "kind": "CreatePresenter",
                          "definitionId": "floating_text",
                          "scopeSource": "Fixed",
                          "paramKey": "worldText.value0",
                          "paramLane": "Float",
                          "valueSource": "EventMagnitude"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("combat_text_rules"), out var definition), Is.True);
            PresenterCommand command = definition.Rules[0].Command;
            Assert.That(command.CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
            Assert.That(command.HasParamPayload, Is.True);
            Assert.That(command.ParamKey, Is.EqualTo(WellKnownPresenterParamKeys.TextValue0));
            Assert.That(command.ParamLane, Is.EqualTo(ParamLane.Float));
            Assert.That(command.ValueSource, Is.EqualTo(PresenterCommandValueSource.EventMagnitude));
        }

        [Test]
        public void Load_RejectsNumericParamKeyAuthoring()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "numeric_param_actor",
                    "bindings": [
                      { "paramKey": 17, "source": "constant", "constantValue": 1.0 }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("bindings[0].paramKey"));
            Assert.That(ex.Message, Does.Contain("numeric authoring value 17"));
        }

        [Test]
        public void Load_RejectsNumericBehaviorSlotAuthoring()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "numeric_slot_actor",
                    "behaviors": [
                      {
                        "slot": 0,
                        "kind": "AssetBinding",
                        "assetBinding": {
                          "assetKind": "WorldHud"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("behavior[0].slot"));
            Assert.That(ex.Message, Does.Contain("numeric authoring value 0"));
        }

        [Test]
        public void Load_RejectsNumericDefinitionIdScopeTagAndEventKeyAuthoring()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "child" },
                  {
                    "id": "numeric_identifier_actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": 99 },
                        "command": { "kind": "CreatePresenter", "definitionId": "child", "scopeTag": "childScope" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException eventKeyEx = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(eventKeyEx.Message, Does.Contain("keyId must be a semantic string"));

            WritePresenters(
                """
                [
                  { "id": "child" },
                  {
                    "id": "numeric_identifier_actor",
                    "children": [
                      { "definitionId": 4, "scopeTag": "childScope" }
                    ]
                  }
                ]
                """);
            registry = new PresenterDefinitionRegistry();
            (_, _, pipeline, catalog) = BuildPipeline();
            loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException definitionEx = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(definitionEx.Message, Does.Contain("definitionId must be a semantic string"));

            WritePresenters(
                """
                [
                  { "id": "child" },
                  {
                    "id": "numeric_identifier_actor",
                    "children": [
                      { "definitionId": "child", "scopeTag": 101 }
                    ]
                  }
                ]
                """);
            registry = new PresenterDefinitionRegistry();
            (_, _, pipeline, catalog) = BuildPipeline();
            loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException scopeEx = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(scopeEx.Message, Does.Contain("scopeTag must be a semantic string"));
        }

        [Test]
        public void Load_ChildOverrides_ParsesTransformAndParams_AndRejectsLegacyParamOverrides()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "child_marker" },
                  {
                    "id": "root_with_overrides",
                    "children": [
                      {
                        "definitionId": "child_marker",
                        "scopeTag": "structure",
                        "overrides": {
                          "transform": {
                            "localPosition": [1.5, 0.25, -2],
                            "localRotation": [0, 90, 0],
                            "localScale": [2, 2, 2]
                          },
                          "params": [
                            { "paramKey": "marker.tint", "lane": "Float", "floatValue": 0.4 }
                          ]
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);
            loader.Load(catalog);

            PresenterDefinition root = registry.Get(registry.GetId("root_with_overrides"));
            Assert.That(root.Children.Length, Is.EqualTo(1));
            Assert.That(root.Children[0].TransformOverride.HasOverride, Is.True);
            Assert.That(root.Children[0].TransformOverride.LocalPosition.X, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(root.Children[0].TransformOverride.LocalScale.X, Is.EqualTo(2f).Within(0.001f));
            Assert.That(root.Children[0].ParamOverrides.Length, Is.EqualTo(1));
            Assert.That(root.Children[0].ParamOverrides[0].FloatValue, Is.EqualTo(0.4f).Within(0.001f));

            WritePresenters(
                """
                [
                  { "id": "child_marker" },
                  {
                    "id": "legacy_child",
                    "children": [
                      { "definitionId": "child_marker", "paramOverrides": [] }
                    ]
                  }
                ]
                """);
            registry = new PresenterDefinitionRegistry();
            (_, _, pipeline, catalog) = BuildPipeline();
            loader = new PresenterDefinitionConfigLoader(pipeline, registry);
            InvalidOperationException legacyEx = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(legacyEx.Message, Does.Contain("overrides.params"));
        }

        [Test]
        public void Load_RejectsDuplicateBehaviorSlots()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "duplicate_slot_actor",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "assetBinding": { "assetKind": "WorldHud", "renderPath": "None", "mobility": "Movable" }
                      },
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "assetBinding": { "assetKind": "WorldHud", "renderPath": "None", "mobility": "Movable" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("duplicate behavior slot 'body'"));
        }

        [Test]
        public void Load_RejectsNonCanonicalBehaviorSlotAliases()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_slot_alias",
                    "behaviors": [
                      {
                        "slot": "staticMinimap",
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "sizePx": 6.0
                        }
                      }
                    ]
                  },
                  {
                    "id": "canonical_slot",
                    "behaviors": [
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "sizePx": 6.0
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("staticMinimap"));
        }

        [Test]
        public void Load_RejectsGroundingInsideAssetBinding()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "bad_asset_grounding",
                    "behaviors": [
                      {
                        "slot": "body",
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
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "renderPath": "InstancedStaticMesh",
                          "mobility": "Static"
                        }
                      },
                      {
                        "slot": "grounding",
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "cube", StringComparison.Ordinal) ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("AssetBinding must not declare grounding"));
        }

        [Test]
        public void Load_ParsesMinimapMarkerBehaviorAsAuthoredCoreSignal()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "marker_actor",
                    "behaviors": [
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [0.18, 0.82, 1.0, 1.0],
                          "sizePx": 8.0,
                          "colorParamKey": "test.marker.color",
                          "sizeParamKey": "test.marker.size",
                          "visibilityParamKey": "test.marker.visibility",
                          "orientationMode": "ParamRadians",
                          "orientationParamKey": "test.marker.orientation",
                          "orientationOffsetRad": 0.25,
                          "orientationLengthPx": 15.0
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("marker_actor"), out var definition), Is.True);
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.MinimapMarker));
            int colorParamKey = PresenterParamKeyRegistry.Register("test.marker.color");
            int sizeParamKey = PresenterParamKeyRegistry.Register("test.marker.size");
            int visibilityParamKey = PresenterParamKeyRegistry.Register("test.marker.visibility");
            int orientationParamKey = PresenterParamKeyRegistry.Register("test.marker.orientation");
            Assert.That(definition.Behaviors[0].SlotIndex, Is.EqualTo(2));
            Assert.That(definition.Behaviors[0].ActiveByDefault, Is.True);
            Assert.That(definition.Behaviors[0].MinimapMarker.Shape, Is.EqualTo(MinimapMarkerShape.Circle));
            Assert.That(definition.Behaviors[0].MinimapMarker.Color, Is.EqualTo(new Vector4(0.18f, 0.82f, 1.0f, 1.0f)));
            Assert.That(definition.Behaviors[0].MinimapMarker.SizePx, Is.EqualTo(8f));
            Assert.That(definition.Behaviors[0].MinimapMarker.ColorParamKey, Is.EqualTo(colorParamKey));
            Assert.That(definition.Behaviors[0].MinimapMarker.SizeParamKey, Is.EqualTo(sizeParamKey));
            Assert.That(definition.Behaviors[0].MinimapMarker.VisibilityParamKey, Is.EqualTo(visibilityParamKey));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationMode, Is.EqualTo(MinimapMarkerOrientationMode.ParamRadians));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationParamKey, Is.EqualTo(orientationParamKey));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationOffsetRad, Is.EqualTo(0.25f));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationLengthPx, Is.EqualTo(15f));
        }

        [Test]
        public void Load_ParsesMinimapMarkerPresenterForwardOrientationWithoutParamKey()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "marker_actor",
                    "behaviors": [
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "activeByDefault": true,
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [0.18, 0.82, 1.0, 1.0],
                          "sizePx": 8.0,
                          "orientationMode": "PresenterForward",
                          "orientationOffsetRad": 0.25,
                          "orientationLengthPx": 15.0
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("marker_actor"), out var definition), Is.True);
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationMode, Is.EqualTo(MinimapMarkerOrientationMode.PresenterForward));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationParamKey, Is.EqualTo(-1));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationOffsetRad, Is.EqualTo(0.25f));
            Assert.That(definition.Behaviors[0].MinimapMarker.OrientationLengthPx, Is.EqualTo(15f));
        }

        [Test]
        public void Load_RejectsInvalidMinimapMarkerShapeWithoutDefaultingToAssetBinding()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "bad_marker",
                    "behaviors": [
                      {
                        "slot": "minimap",
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
                        "slot": "minimap",
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("Square"));
        }

        [Test]
        public void Load_RejectsInvalidMinimapMarkerOrientationWithoutDefaultingToAssetBinding()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "bad_orientation",
                    "behaviors": [
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [1.0, 0.0, 0.0, 1.0],
                          "sizePx": 8.0,
                          "orientationMode": "EntityYaw",
                          "orientationParamKey": "test.marker.orientation",
                          "orientationLengthPx": 12.0
                        }
                      }
                    ]
                  },
                  {
                    "id": "bad_missing_key",
                    "behaviors": [
                      {
                        "slot": "minimap",
                        "kind": "MinimapMarker",
                        "minimapMarker": {
                          "shape": "Circle",
                          "color": [1.0, 0.0, 0.0, 1.0],
                          "sizePx": 8.0,
                          "orientationMode": "ParamDegrees",
                          "orientationLengthPx": 12.0
                        }
                      }
                    ]
                  },
                  {
                    "id": "good_marker",
                    "behaviors": [
                      {
                        "slot": "minimap",
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("EntityYaw"));
        }

        [Test]
        public void Load_PreservesChildrenAsDeclarativeHierarchy_WithoutSyntheticRules()
        {
            WriteCatalog();
            WritePresenters(
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("root"), out var root), Is.True);
            Assert.That(root.Rules.Length, Is.EqualTo(0));
            Assert.That(root.Children.Length, Is.EqualTo(1));
            Assert.That(root.Children[0].DefinitionId, Is.EqualTo(registry.GetId("child_a")));
            Assert.That(root.Children[0].ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("structure")));
        }

        [Test]
        public void Load_ExpandsExtendsChain_AppendsRules_AndOverridesBehaviorsBySlot()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "base_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Base" },
                        "command": { "kind": "DestroyPresenterScope", "scopeTag": "base", "scopeSource": "Fixed" }
                      }
                    ],
                    "behaviors": [
                      {
                        "slot": "material",
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
                        "command": { "kind": "DestroyPresenterScope", "scopeTag": "child", "scopeSource": "Fixed" }
                      }
                    ],
                    "behaviors": [
                      {
                        "slot": "material",
                        "kind": "Material",
                        "material": { "baseMaterialId": "knight_armor" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => key == "knight_base" ? 100 : key == "knight_armor" ? 200 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("knight"), out var knight), Is.True);
            Assert.That(knight.Rules.Length, Is.EqualTo(2));
            Assert.That(knight.Rules[0].Event.KeyId, Is.EqualTo(TagRegistry.GetId("Event.Base")));
            Assert.That(knight.Rules[1].Event.KeyId, Is.EqualTo(TagRegistry.GetId("Event.Child")));
            Assert.That(knight.Behaviors.Length, Is.EqualTo(1));
            Assert.That(knight.Behaviors[0].SlotIndex, Is.EqualTo(5));
            Assert.That(knight.Behaviors[0].Material.BaseMaterialId, Is.EqualTo(200));
        }

        [Test]
        public void Load_ExpandsExtendsChain_DeepMergesSameKindBehaviorBySlot()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "base_marker",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "materialId": "base_mat",
                          "renderPath": "StaticMesh",
                          "mobility": "Static",
                          "localScale": [1, 1, 1]
                        },
                        "style": {
                          "color": [0.1, 0.2, 0.3, 1]
                        }
                      }
                    ]
                  },
                  {
                    "id": "child_marker",
                    "extends": "base_marker",
                    "behaviors": [
                      {
                        "slot": "body",
                        "style": {
                          "color": [0.9, 0.8, 0.7, 1]
                        },
                        "assetBinding": {
                          "localScale": [2, 2, 2]
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => key == "base_mat" ? 100 : 0,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && key == "cube" ? 42 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("child_marker"), out var child), Is.True);
            Assert.That(child.Behaviors.Length, Is.EqualTo(1));
            Assert.That(child.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(child.Behaviors[0].AssetBinding.AssetId, Is.EqualTo(42));
            Assert.That(child.Behaviors[0].AssetBinding.MaterialId, Is.EqualTo(100));
            Assert.That(child.Behaviors[0].AssetBinding.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(child.Behaviors[0].AssetBinding.Mobility, Is.EqualTo(VisualMobility.Static));
            Assert.That(child.Behaviors[0].AssetBinding.LocalScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
            Assert.That(child.DefaultColor, Is.EqualTo(new Vector4(0.9f, 0.8f, 0.7f, 1f)));
        }

        [Test]
        public void Load_ExpandsExtendsChain_ReplacesPayloadWhenChildRedeclaresBehaviorKind()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "base_marker",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "cube",
                          "materialId": "base_mat",
                          "renderPath": "StaticMesh",
                          "mobility": "Static",
                          "scaleParamKey": "base.scale",
                          "colorParamKey": "base.color",
                          "localScale": [1, 1, 1]
                        },
                        "style": {
                          "color": [0.1, 0.2, 0.3, 1]
                        }
                      }
                    ]
                  },
                  {
                    "id": "child_marker",
                    "extends": "base_marker",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "sphere",
                          "renderPath": "StaticMesh",
                          "mobility": "Movable",
                          "localScale": [0.5, 0.5, 0.5]
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMaterialId: key => key == "base_mat" ? 100 : 0,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && key == "cube" ? 42 :
                    kind == AssetKind.Mesh && key == "sphere" ? 43 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("child_marker"), out var child), Is.True);
            Assert.That(child.Behaviors.Length, Is.EqualTo(1));
            Assert.That(child.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(child.Behaviors[0].AssetBinding.AssetId, Is.EqualTo(43));
            Assert.That(child.Behaviors[0].AssetBinding.MaterialId, Is.EqualTo(0));
            Assert.That(child.Behaviors[0].AssetBinding.Mobility, Is.EqualTo(VisualMobility.Movable));
            Assert.That(child.Behaviors[0].AssetBinding.ScaleParamKey, Is.EqualTo(PresenterParamKeyRegistry.UnsetParamKey));
            Assert.That(child.Behaviors[0].AssetBinding.ColorParamKey, Is.EqualTo(PresenterParamKeyRegistry.UnsetParamKey));
            Assert.That(child.Behaviors[0].AssetBinding.LocalScale, Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)));
            Assert.That(child.DefaultColor, Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 1f)));
        }

        [Test]
        public void Load_RejectsLegacyInvalidFields()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "bad_presenter",
                    "visualKind": "WorldBar",
                    "entityScope": "AllWithAttributes",
                    "requiredTemplate": "moba_hero"
                  },
                  {
                    "id": "good_presenter",
                    "rules": [
                      {
                        "event": { "kind": "TagEffectiveChanged", "keyId": "Status.Working" },
                        "command": { "kind": "DestroyPresenterScope", "scopeTag": "working", "scopeSource": "Fixed" }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("entityScope"));
        }

        [Test]
        public void Load_RejectsLegacyWorldTextModeField()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "old_text",
                    "legacyWorldTextMode": "AttributeCurrent"
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.combat.delta" ? 777 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("legacyWorldTextMode"));
        }

        [Test]
        public void Load_WorldTextAssetId_ResolvesThroughTextTokenRegistry()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "floating_text",
                    "lifecycle": { "durationSeconds": 1.2 },
                    "anchor": { "offset": [0, 1, 0] },
                    "visibility": { "inline": "SourceIsAlive" },
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "WorldText",
                        "activeByDefault": true,
                        "worldText": {
                          "textToken": "hud.combat.delta",
                          "mode": "AttributeCurrent",
                          "valueParamKey": "worldText.value0",
                          "secondaryValueParamKey": "worldText.value1",
                          "fontSize": 18
                        },
                        "style": {
                          "color": [1, 0.2, 0.1, 1],
                          "alphaPolicy": "FadeOverLifetime"
                        },
                        "motion": {
                          "yDriftPerSecond": 0.8
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.combat.delta" ? 777 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("floating_text"), out var definition), Is.True);
            Assert.That(definition.DefaultLifetime, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(definition.PositionOffset, Is.EqualTo(new Vector3(0f, 1f, 0f)));
            Assert.That(definition.VisibilityCondition.Inline, Is.EqualTo(InlineConditionKind.SourceIsAlive));
            Assert.That(definition.DefaultFontSize, Is.EqualTo(18));
            Assert.That(definition.WorldTextMode, Is.EqualTo(WorldHudValueMode.AttributeCurrent));
            Assert.That(definition.DefaultColor, Is.EqualTo(new Vector4(1f, 0.2f, 0.1f, 1f)));
            Assert.That(definition.AlphaFadeOverLifetime, Is.True);
            Assert.That(definition.PositionYDriftPerSecond, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.WorldText));
            Assert.That(definition.Behaviors[0].AssetBinding.AssetKind, Is.EqualTo(AssetKind.WorldText));
            Assert.That(definition.Behaviors[0].AssetBinding.AssetId, Is.EqualTo(777));
            Assert.That(definition.Behaviors[0].AssetBinding.ScaleParamKey, Is.EqualTo(WellKnownPresenterParamKeys.TextValue0));
            Assert.That(definition.Behaviors[0].AssetBinding.MaterialParamKey, Is.EqualTo(WellKnownPresenterParamKeys.TextValue1));
        }

        [Test]
        public void Load_SurfaceSourceBehavior_ParsesIntoDefinitionSurface()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "road_surface",
                    "lifecycle": { "persistence": "Scoped" },
                    "behaviors": [
                      {
                        "slot": "surface",
                        "kind": "SurfaceSource",
                        "activeByDefault": true,
                        "surfaceSource": {
                          "kind": "SplineRibbon",
                          "profileId": "road_surface_profile",
                          "geometrySource": {
                            "controlPointSource": { "kind": "Constant", "id": "road.points" },
                            "widthSource": { "kind": "Constant", "id": "road.width" },
                            "segmentationPolicy": "Bezier12"
                          },
                          "chunkBake": {
                            "enabled": true,
                            "ownership": "PerChunk",
                            "chunkInfluencePolicy": "ExplicitPayloadChunks",
                            "rebakePolicy": "DirtyPayload",
                            "usageHint": "Static"
                          },
                          "materialSet": {
                            "primaryMaterialId": "mat.road"
                          },
                          "lodProfileId": "default_surface_lod",
                          "grounding": { "mode": "None" },
                          "boundsPolicy": "Auto"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("road_surface"), out var definition), Is.True);
            Assert.That(definition.Surface, Is.Not.Null);
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.SurfaceSource));
            Assert.That(definition.Behaviors[0].SlotIndex, Is.EqualTo(12));
            Assert.That(definition.Surface!.ProfileId, Is.EqualTo("road_surface_profile"));
            Assert.That(definition.Surface.MaterialSet.PrimaryMaterialId, Is.EqualTo("mat.road"));
        }

        [Test]
        public void Load_InstancedBatchBehavior_ParsesIntoDefinitionBinding()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "forest_patch",
                    "lifecycle": { "persistence": "Scoped" },
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "InstancedBatch",
                        "activeByDefault": true,
                        "instancedBatch": {
                          "batchAssetId": "forest.tree.cluster"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveInstancedBatchAssetId: key => key == "forest.tree.cluster" ? 42 : 0);

            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("forest_patch"), out var definition), Is.True);
            Assert.That(definition.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.InstancedBatch));
            Assert.That(definition.InstancedBatches.Length, Is.EqualTo(1));
            Assert.That(definition.InstancedBatches[0].BatchAssetId, Is.EqualTo(42));
            Assert.That(definition.InstancedBatches[0].SlotIndex, Is.EqualTo(0));
        }

        [TestCase("defaultFontSize", "18")]
        [TestCase("worldTextMode", "\"AttributeCurrent\"")]
        [TestCase("defaultLifetime", "1.2")]
        [TestCase("positionOffset", "[0, 1, 0]")]
        [TestCase("defaultColor", "[1, 1, 1, 1]")]
        [TestCase("instancedBatches", "[]")]
        [TestCase("surface", "{}")]
        [TestCase("requiredAttributes", "[]")]
        public void Load_RejectsRemovedTopLevelPresenterAuthoringFields(string fieldName, string valueJson)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "legacy_presenter",
                    "{{fieldName}}": {{valueJson}}
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(fieldName));
            Assert.That(ex.Message, Does.Contain("removed field"));
        }

        [TestCase("{}", "durationSeconds or persistence")]
        [TestCase(@"{ ""durationSeconds"": 0 }", "durationSeconds must be > 0")]
        [TestCase(@"{ ""durationSeconds"": -0.1 }", "durationSeconds must be > 0")]
        [TestCase(@"{ ""persistence"": ""Persistent"" }", "persistence must be 'Scoped'")]
        public void Load_RejectsAmbiguousLifecycleAuthoring(string lifecycleJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "bad_lifecycle",
                    "lifecycle": {{lifecycleJson}}
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Load_RejectsVisibilityGraphProgramId()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "graph_visibility",
                    "visibility": { "graphProgramId": 12 }
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("visibility.graphProgramId"));
            Assert.That(ex.Message, Does.Contain("not wired"));
        }

        [Test]
        public void Load_RejectsBehaviorActivationConditionUntilRuntimeConsumesIt()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "activation_condition",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "WorldText",
                        "activeByDefault": true,
                        "activationCondition": { "inline": "SourceIsAlive" },
                        "worldText": {
                          "textToken": "hud.combat.delta",
                          "mode": "AttributeCurrent"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.combat.delta" ? 777 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("activationCondition"));
            Assert.That(ex.Message, Does.Contain("not wired"));
        }

        [Test]
        public void Load_RejectsRemovedDefaultTextIdField()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "floating_text",
                    "defaultTextId": "hud.combat.delta"
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("defaultTextId"));
        }

        [Test]
        public void Load_RejectsDefinitionWithInheritanceCycle()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  { "id": "cycle_a", "extends": "cycle_b" },
                  { "id": "cycle_b", "extends": "cycle_a" },
                  { "id": "ok_root" }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("inheritance cycle"));
        }

        [TestCase("\" bad_id\"", "entry id")]
        [TestCase("\"bad_id \"", "entry id")]
        [TestCase("\"\"", "entry id")]
        public void Load_RejectsNonCanonicalDefinitionId(string idJson, string expectedContext)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  { "id": {{idJson}} }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain(expectedContext));
            Assert.That(ex.Message, Does.Contain("semantic string").Or.Contain("whitespace"));
        }

        [TestCase("\"\"", "non-empty")]
        [TestCase("\"   \"", "non-empty")]
        [TestCase("\"base_unit \"", "whitespace")]
        [TestCase("\" base_unit\"", "whitespace")]
        public void Load_RejectsNonCanonicalExtendsWhenFieldExists(string extendsJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  { "id": "base_unit" },
                  { "id": "child_unit", "extends": {{extendsJson}} }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("extends"));
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase("\"\"", "non-empty")]
        [TestCase("\"   \"", "non-empty")]
        [TestCase("\"AttributeCurrent \"", "invalid value")]
        [TestCase("\"attributeCurrent\"", "invalid value")]
        public void Load_RejectsNonCanonicalWorldTextBehaviorModeWhenFieldExists(string modeJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "floating_text",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "WorldText",
                        "activeByDefault": true,
                        "worldText": {
                          "textToken": "hud.combat.delta",
                          "mode": {{modeJson}}
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.combat.delta" ? 777 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("worldText.mode"));
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [TestCase("\"\"", "semantic string")]
        [TestCase("\"   \"", "semantic string")]
        [TestCase("\"AssetBinding \"", "whitespace")]
        [TestCase("\"assetBinding\"", "invalid value")]
        public void Load_RejectsNonCanonicalBehaviorKind(string kindJson, string expectedMessage)
        {
            WriteCatalog();
            WritePresenters($$"""
                [
                  {
                    "id": "strict_behavior",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": {{kindJson}}
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("behavior[0].kind"));
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Load_RejectsDefinitionUsingVisualKindAlias()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_aliases",
                    "visualKind": "Marker3D",
                    "bindings": [
                      { "paramKey": "legacy.text.token", "source": "textToken", "sourceKey": "hud.current_over_base" }
                    ],
                    "paramDefaults": [
                      { "paramKey": "legacy.value", "value": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": "tag",
                        "kind": "TagBinding",
                        "tagBinding": {
                          "tag": "Status.Working",
                          "targetParamKey": "legacy.tag.active"
                        }
                      }
                    ]
                  },
                  {
                    "id": "canonical",
                    "bindings": [
                      { "paramKey": "canonical.text.token", "source": "textToken", "textToken": "hud.current_over_base" }
                    ],
                    "paramDefaults": [
                      { "paramKey": "canonical.value", "lane": "Int", "intValue": 7 }
                    ],
                    "behaviors": [
                      {
                        "slot": "tag",
                        "kind": "TagBinding",
                        "tagBinding": {
                          "tagId": "Status.Working",
                          "targetParamKey": "canonical.tag.active"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.current_over_base" ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("visualKind"));
        }

        [Test]
        public void Load_RejectsTextTokenSourceKeyAlias()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_text_token_alias",
                    "bindings": [
                      { "paramKey": "legacy.text.token", "source": "textToken", "sourceKey": "hud.current_over_base" }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.current_over_base" ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("sourceKey"));
        }

        [Test]
        public void Load_RejectsParamDefaultValueAlias()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_param_default_alias",
                    "paramDefaults": [
                      { "paramKey": "legacy.value", "lane": "Int", "value": 7 }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("value"));
        }

        [Test]
        public void Load_RejectsTextTokenSourceKeyAliasEvenWhenCanonicalFieldExists()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_text_token_alias_with_canonical",
                    "bindings": [
                      {
                        "paramKey": "legacy.text.token",
                        "source": "textToken",
                        "textToken": "hud.current_over_base",
                        "sourceKey": "hud.current_over_base"
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => key == "hud.current_over_base" ? 42 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("sourceKey"));
        }

        [Test]
        public void Load_RejectsParamDefaultValueAliasEvenWhenCanonicalFieldExists()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_param_default_alias_with_canonical",
                    "paramDefaults": [
                      { "paramKey": "legacy.value", "lane": "Int", "intValue": 7, "value": 7 }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("value"));
        }

        [Test]
        public void Load_RejectsTagBindingTagAliasEvenWhenCanonicalFieldExists()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_tag_alias_with_canonical",
                    "behaviors": [
                      {
                        "slot": "tag",
                        "kind": "TagBinding",
                        "tagBinding": {
                          "tagId": "Status.Working",
                          "tag": "Status.Working",
                          "targetParamKey": "legacy.tag.active"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("tag"));
        }

        [Test]
        public void Load_RejectsAttributeNameAliasInAttributeBindingBehavior()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "legacy_attribute_name",
                    "behaviors": [
                      {
                        "slot": "attribute",
                        "kind": "AttributeBinding",
                        "attributeBinding": {
                          "targetParamKey": "legacy.health.ratio",
                          "mode": "AttributeRatio",
                          "attributeName": "Health"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: key => key == "Health" ? 1 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("attributeName"));
            Assert.That(ex.Message, Does.Contain("attributeId"));
        }

        [Test]
        public void Load_RejectsNonAttributeModeInAttributeBindingBehavior()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "invalid_attribute_mode",
                    "behaviors": [
                      {
                        "slot": "attribute",
                        "kind": "AttributeBinding",
                        "attributeBinding": {
                          "attributeId": "Health",
                          "targetParamKey": "legacy.health.ratio",
                          "mode": "Constant"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: key => key == "Health" ? 1 : 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("AttributeBinding.mode"));
            Assert.That(ex.Message, Does.Contain("AttributeRatio"));
        }

        [Test]
        public void Load_RejectsFailedDefinitionAndParentsThatReferenceThem()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "broken_child",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldText",
                          "assetId": "hud.missing.token",
                          "renderPath": "None",
                          "mobility": "Movable"
                        }
                      }
                    ]
                  },
                  {
                    "id": "root",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Spawn" },
                        "command": { "kind": "CreatePresenter", "definitionId": "broken_child", "scopeTag": "structure", "scopeSource": "Fixed" }
                      }
                    ]
                  },
                  {
                    "id": "ok_root"
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: _ => 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("hud.missing.token"));
        }

        [Test]
        public void Load_RemovesTransitiveParentsWhenReferencedDefinitionFailsLateValidation()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "bad_leaf",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "WorldText",
                          "assetId": "hud.missing.token",
                          "renderPath": "None",
                          "mobility": "Movable"
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
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: _ => 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("hud.missing.token"));
        }

        private static void NoOpExtensionCommand(in PerformerCommandExecutionContext context)
        {
        }

        private static void NoOpExtensionBehavior(in PerformerBehaviorExecutionContext context)
        {
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
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePresenters(string content)
        {
            WriteFile("Core", "Presentation/presenters.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }
    }
}
