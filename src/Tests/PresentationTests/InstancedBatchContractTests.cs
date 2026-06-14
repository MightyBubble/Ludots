using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class InstancedBatchContractTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void Load_RegistersBatchWithStableAddressesAndProgressivePolicy()
        {
            string root = CreateTempCoreRoot();
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.large",
                    "renderPath": "HierarchicalInstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "progressiveSubmission": { "maxInstancesPerFlush": 2 },
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "materialId": "material.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [
                          { "positionCm": { "x": 1, "y": 2, "z": 3 } },
                          { "positionCm": [4, 5, 6], "rotation": [0, 0, 0, 1], "scale": [1, 2, 1] }
                        ]
                      }
                    ]
                  }
                ]
                """);

            var loader = BuildLoader(root, out InstancedBatchAssetRegistry registry);
            loader.Load(BuildCatalog());

            int id = registry.GetId("batch.large");
            Assert.That(registry.TryGet(id, out InstancedBatchAsset asset), Is.True);
            Assert.That(asset.RenderPath, Is.EqualTo(VisualRenderPath.HierarchicalInstancedStaticMesh));
            Assert.That(asset.Groups.Length, Is.EqualTo(1));
            Assert.That(asset.Groups[0].BucketId, Is.EqualTo("bucket.a"));
            Assert.That(asset.CustomDataChannels[0].Lane, Is.EqualTo(MaterialCustomDataLane.Float));
            Assert.That(asset.ProgressiveSubmission.MaxInstancesPerFlush, Is.EqualTo(2));
            Assert.That(asset.AddressTable.TryResolve("group.a", "bucket.a", "span.a", out InstancedBatchAddress address), Is.True);
            Assert.That(address.IsValid, Is.True);
            Assert.That(asset.Groups[0].Address.Equals(address), Is.True);
        }

        [TestCase("unknown mesh asset 'missing.mesh'", "{ \"id\": \"group.a\", \"meshAssetId\": \"missing.mesh\", \"bucketId\": \"bucket.a\", \"instanceSpanId\": \"span.a\", \"transforms\": [ { \"positionCm\": [1,2,3] } ] }")]
        [TestCase("unknown material asset 'missing.material'", "{ \"id\": \"group.a\", \"meshAssetId\": \"mesh.unit\", \"materialId\": \"missing.material\", \"bucketId\": \"bucket.a\", \"instanceSpanId\": \"span.a\", \"transforms\": [ { \"positionCm\": [1,2,3] } ] }")]
        [TestCase("non-empty groups array", "")]
        [TestCase("bucketId must be a string", "{ \"id\": \"group.a\", \"meshAssetId\": \"mesh.unit\", \"instanceSpanId\": \"span.a\", \"transforms\": [ { \"positionCm\": [1,2,3] } ] }")]
        [TestCase("requires exactly 3 numeric array entries", "{ \"id\": \"group.a\", \"meshAssetId\": \"mesh.unit\", \"bucketId\": \"bucket.a\", \"instanceSpanId\": \"span.a\", \"transforms\": [ { \"positionCm\": [1,2] } ] }")]
        public void Load_RejectsMalformedBatchData(string expectedMessage, string groupJson)
        {
            string root = CreateTempCoreRoot();
            string groups = groupJson.Length == 0 ? "[]" : $"[ {groupJson} ]";
            WritePresentationFile(root, "instanced_batches.json",
                $$"""
                [
                  {
                    "id": "batch.bad",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": {{groups}}
                  }
                ]
                """);

            var loader = BuildLoader(root, out _);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(BuildCatalog()))!;
            Assert.That(ex.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Load_RejectsUnsupportedInstancedBatchSchemaFields()
        {
            AttributeRegistry.Register("attr.health");

            AssertUnsupportedField(
                "unknownTop",
                """
                [
                  {
                    "id": "batch.strict",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "unknownTop": true,
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ]
                  }
                ]
                """);
            AssertUnsupportedField(
                "unknownGroup",
                """
                [
                  {
                    "id": "batch.strict",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "unknownGroup": true,
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ]
                  }
                ]
                """);
            AssertUnsupportedField(
                "unknownSource",
                """
                [
                  {
                    "id": "batch.strict",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-source",
                        "source": { "kind": "Attribute", "key": "attr.health", "unknownSource": true },
                        "target": { "operation": "SetVisibility", "group": "group.a", "bucket": "bucket.a", "span": "span.a" }
                      }
                    ]
                  }
                ]
                """);
            AssertUnsupportedField(
                "unknownTarget",
                """
                [
                  {
                    "id": "batch.strict",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-target",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "SetVisibility",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "unknownTarget": true
                        }
                      }
                    ]
                  }
                ]
                """);
            AssertUnsupportedField(
                "unknownMapping",
                """
                [
                  {
                    "id": "batch.strict",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-mapping",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": { "operation": "SetVisibility", "group": "group.a", "bucket": "bucket.a", "span": "span.a" },
                        "mapping": { "kind": "Constant", "constantValue": 1, "unknownMapping": true }
                      }
                    ]
                  }
                ]
                """);

            static void AssertUnsupportedField(string fieldName, string json)
            {
                string root = CreateTempCoreRoot();
                WritePresentationFile(root, "instanced_batches.json", json);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => BuildLoader(root, out _).Load(BuildCatalog()))!;
                Assert.That(ex.Message, Does.Contain($"unsupported field '{fieldName}'"));
            }
        }

        [Test]
        public void Load_RejectsPayloadFieldsOnWrongOperationTarget()
        {
            AttributeRegistry.Register("attr.health");

            AssertInvalidTarget(
                "presentationStateId is only valid for SetPresentationState",
                """
                [
                  {
                    "id": "batch.invalid.target",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-state-payload",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "SetVisibility",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "presentationStateId": 7
                        }
                      }
                    ]
                  }
                ]
                """);
            AssertInvalidTarget(
                "effectAssetId is only valid for AttachEffect, UpdateEffect, or RemoveEffect",
                """
                [
                  {
                    "id": "batch.invalid.target",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-effect-payload",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0,
                          "effectAssetId": "effect.batch.spark"
                        }
                      }
                    ]
                  }
                ]
                """);
            AssertInvalidTarget(
                "customDataSlot is only valid for WriteCustomData",
                """
                [
                  {
                    "id": "batch.invalid.target",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-custom-data-slot",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "SetPresentationState",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0,
                          "presentationStateId": 7
                        }
                      }
                    ]
                  }
                ]
                """);

            static void AssertInvalidTarget(string expectedMessage, string json)
            {
                string root = CreateTempCoreRoot();
                WritePresentationFile(root, "instanced_batches.json", json);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => BuildLoader(root, out _).Load(BuildCatalog()))!;
                Assert.That(ex.Message, Does.Contain(expectedMessage));
            }
        }

        [Test]
        public void Load_CompilesBehaviorTargetAndRejectsInvalidSelectorsOrSlots()
        {
            string root = CreateTempCoreRoot();
            int attributeId = AttributeRegistry.Register("attr.health");
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.behavior",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "health-to-custom-data",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0
                        },
                        "mapping": { "kind": "Linear", "inputMin": 0, "inputMax": 100, "outputMin": 0, "outputMax": 1 }
                      }
                    ]
                  }
                ]
                """);

            var loader = BuildLoader(root, out InstancedBatchAssetRegistry registry);
            loader.Load(BuildCatalog());

            Assert.That(registry.TryGet(registry.GetId("batch.behavior"), out InstancedBatchAsset asset), Is.True);
            Assert.That(asset.Behaviors.Length, Is.EqualTo(1));
            Assert.That(asset.Behaviors[0].SourceKeyId, Is.EqualTo(attributeId));
            Assert.That(asset.Behaviors[0].HasCompiledAddress, Is.True);
            Assert.That(asset.Behaviors[0].Address.Bucket.Value, Is.EqualTo(1));

            string invalidRoot = CreateTempCoreRoot();
            WritePresentationFile(invalidRoot, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.invalid",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-selector",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.missing",
                          "span": "span.a",
                          "customDataSlot": 0
                        }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException selectorEx = Assert.Throws<InvalidOperationException>(
                () => BuildLoader(invalidRoot, out _).Load(BuildCatalog()))!;
            Assert.That(selectorEx.Message, Does.Contain("bucket='bucket.missing'"));

            string slotRoot = CreateTempCoreRoot();
            WritePresentationFile(slotRoot, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.invalid.slot",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "bad-slot",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 1
                        }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException slotEx = Assert.Throws<InvalidOperationException>(
                () => BuildLoader(slotRoot, out _).Load(BuildCatalog()))!;
            Assert.That(slotEx.Message, Does.Contain("undeclared customDataSlot 1"));
        }

        [Test]
        public void Load_ParsesEventVisibilityStateAndEffectBehaviorPayloads()
        {
            string root = CreateTempCoreRoot();
            int tagId = TagRegistry.Register("event.visibility");
            int effectId = EffectTemplateIdRegistry.Register("effect.spark");
            int abilityId = AbilityIdRegistry.Register("ability.cast");
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.events",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "event-to-visibility",
                        "source": { "kind": "PresentationEvent", "eventKind": "GameplayEvent", "key": "event.visibility" },
                        "target": { "operation": "SetVisibility", "group": "group.a", "bucket": "bucket.a", "span": "span.a" },
                        "mapping": { "kind": "Constant", "constantValue": 1 }
                      },
                      {
                        "id": "effect-attach",
                        "source": { "kind": "GasEvent", "eventKind": "EffectApplied", "key": "effect.spark" },
                        "target": { "operation": "AttachEffect", "group": "group.a", "bucket": "bucket.a", "span": "span.a", "effectAssetId": "effect.batch.spark" }
                      },
                      {
                        "id": "cast-state",
                        "source": { "kind": "GasEvent", "eventKind": "CastCommitted", "key": "ability.cast" },
                        "target": { "operation": "SetPresentationState", "group": "group.a", "bucket": "bucket.a", "span": "span.a", "presentationStateId": 7 }
                      }
                    ]
                  }
                ]
                """);

            var loader = BuildLoader(root, out InstancedBatchAssetRegistry registry);
            loader.Load(BuildCatalog());

            Assert.That(registry.TryGet(registry.GetId("batch.events"), out InstancedBatchAsset asset), Is.True);
            Assert.That(asset.Behaviors[0].SourceEventKind, Is.EqualTo(PresentationEventKind.GameplayEvent));
            Assert.That(asset.Behaviors[0].SourceKeyId, Is.EqualTo(tagId));
            Assert.That(asset.Behaviors[1].SourceEventKind, Is.EqualTo(PresentationEventKind.EffectApplied));
            Assert.That(asset.Behaviors[1].SourceKeyId, Is.EqualTo(effectId));
            Assert.That(asset.Behaviors[1].TargetPayloadId, Is.GreaterThan(0));
            Assert.That(asset.Behaviors[2].SourceEventKind, Is.EqualTo(PresentationEventKind.CastCommitted));
            Assert.That(asset.Behaviors[2].SourceKeyId, Is.EqualTo(abilityId));
            Assert.That(asset.Behaviors[2].TargetPayloadId, Is.EqualTo(7));
        }

        [Test]
        public void Load_RejectsUnknownEffectAssetBehaviorPayload()
        {
            string root = CreateTempCoreRoot();
            EffectTemplateIdRegistry.Register("effect.spark");
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.bad.effect",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "missing-effect-asset",
                        "source": { "kind": "GasEvent", "eventKind": "EffectApplied", "key": "effect.spark" },
                        "target": {
                          "operation": "AttachEffect",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "effectAssetId": "effect.missing"
                        }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => BuildLoader(root, out _).Load(BuildCatalog()))!;
            Assert.That(ex.Message, Does.Contain("unknown effect asset 'effect.missing'"));
        }

        [Test]
        public void Load_SortsBehaviorsByOrderAndCarriesLifecycleDeclarations()
        {
            string root = CreateTempCoreRoot();
            AttributeRegistry.Register("attr.health");
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.ordered",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "later",
                        "order": 20,
                        "coalescing": "None",
                        "lifecycle": "Transient",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0
                        }
                      },
                      {
                        "id": "earlier",
                        "order": 10,
                        "coalescing": "LastWriteWins",
                        "lifecycle": "UntilOwnerDestroyed",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0
                        }
                      }
                    ]
                  }
                ]
                """);

            var loader = BuildLoader(root, out InstancedBatchAssetRegistry registry);
            loader.Load(BuildCatalog());

            Assert.That(registry.TryGet(registry.GetId("batch.ordered"), out InstancedBatchAsset asset), Is.True);
            Assert.That(asset.Behaviors[0].Key, Is.EqualTo("earlier"));
            Assert.That(asset.Behaviors[0].Order, Is.EqualTo(10));
            Assert.That(asset.Behaviors[0].Coalescing, Is.EqualTo(InstancedBatchCoalescingMode.LastWriteWins));
            Assert.That(asset.Behaviors[0].Lifecycle, Is.EqualTo(InstancedBatchLifecycleMode.UntilOwnerDestroyed));
            Assert.That(asset.Behaviors[1].Key, Is.EqualTo("later"));
            Assert.That(asset.Behaviors[1].Coalescing, Is.EqualTo(InstancedBatchCoalescingMode.None));
            Assert.That(asset.Behaviors[1].Lifecycle, Is.EqualTo(InstancedBatchLifecycleMode.Transient));
        }

        [Test]
        public void AddressTable_RejectsDuplicateAndCrossGroupSelectors()
        {
            var inputs = new[]
            {
                new InstancedBatchAddressGroupInput("group.a", "bucket.a", "span.a"),
                new InstancedBatchAddressGroupInput("group.b", "bucket.b", "span.b"),
            };

            InstancedBatchAddressTable table = InstancedBatchAddressTable.Build(7, "owner.alpha", inputs);

            Assert.That(table.TryResolve("group.a", "bucket.a", "span.a", out InstancedBatchAddress first), Is.True);
            Assert.That(first.Group.Value, Is.EqualTo(1));
            Assert.That(table.TryResolve("group.a", "bucket.b", "span.a", out _), Is.False);
            Assert.That(
                () => InstancedBatchAddressTable.Build(
                    7,
                    "owner.alpha",
                    new[]
                    {
                        new InstancedBatchAddressGroupInput("group.a", "bucket.a", "span.a"),
                        new InstancedBatchAddressGroupInput("group.a", "bucket.b", "span.b"),
                    }),
                Throws.InvalidOperationException.With.Message.Contains("duplicated"));
        }

        [Test]
        public void SubmissionRuntime_SplitsAndResumesDeterministically()
        {
            var runtime = new InstancedBatchSubmissionRuntime();

            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 5, out int start, out int count, budget: 2), Is.True);
            Assert.That((start, count), Is.EqualTo((0, 2)));
            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 5, out start, out count, budget: 2), Is.True);
            Assert.That((start, count), Is.EqualTo((2, 2)));
            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 5, out start, out count, budget: 2), Is.True);
            Assert.That((start, count), Is.EqualTo((4, 1)));
            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 5, out _, out _, budget: 2), Is.False);
        }

        [Test]
        public void SubmissionRuntime_SubmitsSmallBatchInOneFinalChunk()
        {
            var runtime = new InstancedBatchSubmissionRuntime();

            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 3, out int start, out int count, budget: 0), Is.True);
            Assert.That((start, count), Is.EqualTo((0, 3)));
            Assert.That(runtime.ShouldSubmit(default, 10, 20, 0, 3, out _, out _, budget: 0), Is.False);
        }

        [Test]
        public void CapabilityValidator_RejectsUnsupportedRenderPathAndOperation()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            requests.Add(new InstancedBatchRequest(
                InstancedBatchRequestKind.CreateOrUpdate,
                1,
                100,
                default,
                default,
                new InstancedBatchAddress(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1)),
                VisualRenderPath.HierarchicalInstancedStaticMesh,
                10,
                20,
                0,
                1,
                finalChunk: true));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.WriteCustomData,
                1,
                100,
                default,
                default,
                new InstancedBatchAddress(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1)),
                0,
                new Vector4(1f, 0f, 0f, 0f)));

            var capabilities = new PresentationAdapterCapabilities(PresentationVisualCapabilities.InstancedStaticMeshBatch);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InstancedBatchCapabilityValidator.Validate(requests, operations, capabilities))!;
            Assert.That(ex.Message, Does.Contain("HierarchicalInstancedStaticMesh"));
        }

        [Test]
        public void CapabilityValidator_AcceptsSupportedRequestAndOperationPayloads()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            InstancedBatchAddress address = new(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));
            requests.Add(new InstancedBatchRequest(
                InstancedBatchRequestKind.CreateOrUpdate,
                1,
                100,
                default,
                default,
                address,
                VisualRenderPath.InstancedStaticMesh,
                10,
                20,
                0,
                1,
                finalChunk: true));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.AttachEffect,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero,
                payloadId: 77,
                state: 1));

            var capabilities = new PresentationAdapterCapabilities(
                PresentationVisualCapabilities.InstancedStaticMeshBatch |
                PresentationVisualCapabilities.InstancedBatchEffect);

            Assert.DoesNotThrow(() => InstancedBatchCapabilityValidator.Validate(requests, operations, capabilities));
        }

        [Test]
        public void CapabilityValidator_RejectsUnsupportedOperation()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            InstancedBatchAddress address = new(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.Refresh,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero));

            var capabilities = new PresentationAdapterCapabilities(PresentationVisualCapabilities.InstancedStaticMeshBatch);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InstancedBatchCapabilityValidator.Validate(requests, operations, capabilities))!;
            Assert.That(ex.Message, Does.Contain("operation 'Refresh'"));
        }

        [Test]
        public void OperationBuffer_CoalescesLastWriteWinsDeterministically()
        {
            var operations = new InstancedBatchOperationBuffer();
            InstancedBatchAddress address = new(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.SetVisibility,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero,
                state: 0,
                coalescing: InstancedBatchCoalescingMode.LastWriteWins));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.SetVisibility,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.One,
                state: 1,
                coalescing: InstancedBatchCoalescingMode.LastWriteWins));

            ReadOnlySpan<InstancedBatchOperation> span = operations.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].State, Is.EqualTo(1));
            Assert.That(span[0].Value.X, Is.EqualTo(1f));

            operations.Clear();
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.SetPresentationState,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero,
                payloadId: 7,
                coalescing: InstancedBatchCoalescingMode.LastWriteWins));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.SetPresentationState,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero,
                payloadId: 8,
                coalescing: InstancedBatchCoalescingMode.LastWriteWins));

            span = operations.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].PayloadId, Is.EqualTo(8));

            AssertEffectPayloadsDoNotCoalesce(InstancedBatchOperationKind.AttachEffect);
            AssertEffectPayloadsDoNotCoalesce(InstancedBatchOperationKind.UpdateEffect);
            AssertEffectPayloadsDoNotCoalesce(InstancedBatchOperationKind.RemoveEffect);

            void AssertEffectPayloadsDoNotCoalesce(InstancedBatchOperationKind kind)
            {
                operations.Clear();
                operations.Add(new InstancedBatchOperation(
                    kind,
                    1,
                    100,
                    default,
                    default,
                    address,
                    -1,
                    Vector4.Zero,
                    payloadId: 7,
                    coalescing: InstancedBatchCoalescingMode.LastWriteWins));
                operations.Add(new InstancedBatchOperation(
                    kind,
                    1,
                    100,
                    default,
                    default,
                    address,
                    -1,
                    Vector4.Zero,
                    payloadId: 8,
                    coalescing: InstancedBatchCoalescingMode.LastWriteWins));

                ReadOnlySpan<InstancedBatchOperation> effectSpan = operations.GetSpan();
                Assert.That(effectSpan.Length, Is.EqualTo(2));
                Assert.That(effectSpan[0].PayloadId, Is.EqualTo(7));
                Assert.That(effectSpan[1].PayloadId, Is.EqualTo(8));
            }
        }

        [Test]
        public void BehaviorSystem_EmitsAttributeAndEventOperationsWithCompiledAddresses()
        {
            string root = CreateTempCoreRoot();
            int attributeId = AttributeRegistry.Register("attr.health");
            int eventId = TagRegistry.Register("event.visibility");
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.runtime",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "customDataChannels": [
                      { "key": "visual.amount", "slot": 0, "type": "Float" }
                    ],
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "health-to-custom-data",
                        "source": { "kind": "Attribute", "key": "attr.health" },
                        "target": {
                          "operation": "WriteCustomData",
                          "group": "group.a",
                          "bucket": "bucket.a",
                          "span": "span.a",
                          "customDataSlot": 0
                        },
                        "mapping": { "kind": "Linear", "inputMin": 0, "inputMax": 100, "outputMin": 0, "outputMax": 1 }
                      },
                      {
                        "id": "event-to-visibility",
                        "source": { "kind": "PresentationEvent", "eventKind": "GameplayEvent", "key": "event.visibility" },
                        "target": { "operation": "SetVisibility", "group": "group.a", "bucket": "bucket.a", "span": "span.a" },
                        "mapping": { "kind": "Constant", "constantValue": 1 }
                      }
                    ]
                  }
                ]
                """);
            var loader = BuildLoader(root, out InstancedBatchAssetRegistry batches);
            loader.Load(BuildCatalog());

            using var world = World.Create();
            var definitions = new PerformerDefinitionRegistry();
            int batchId = batches.GetId("batch.runtime");
            int defId = definitions.Register("performer.batch", new PerformerDefinition
            {
                InstancedBatches = new[] { new InstancedBatchBinding(batchId) },
            });
            var runtime = new PerformerEntityRuntime(world);
            var owner = world.Create(new AttributeBuffer());
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(owner);
            attributes.SetCurrent(attributeId, 50f);
            Entity performer = runtime.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 500, Entity.Null, definitions.Get(defId));
            var operations = new InstancedBatchOperationBuffer();
            var events = new PresentationEventStream(8);
            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, attributeId)), Is.True);
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = eventId,
                Source = owner,
                Magnitude = 1f,
            }), Is.True);

            using var system = new InstancedBatchBehaviorSystem(world, definitions, runtime, batches, operations, events, ownerChanges);
            system.Update(0.016f);

            ReadOnlySpan<InstancedBatchOperation> span = operations.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2));
            Assert.That(span[0].Kind, Is.EqualTo(InstancedBatchOperationKind.WriteCustomData));
            Assert.That(span[0].Performer, Is.EqualTo(performer));
            Assert.That(span[0].Address.IsValid, Is.True);
            Assert.That(span[0].Value.X, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(span[1].Kind, Is.EqualTo(InstancedBatchOperationKind.SetVisibility));
            Assert.That(span[1].State, Is.EqualTo(1));
        }

        [TestCase(PresentationEventKind.PerformerCreated)]
        [TestCase(PresentationEventKind.PerformerDestroyed)]
        public void BehaviorSystem_MatchesLifecyclePresentationEventWildcardBindings(PresentationEventKind eventKind)
        {
            string root = CreateTempCoreRoot();
            WritePresentationFile(root, "instanced_batches.json",
                $$"""
                [
                  {
                    "id": "batch.lifecycle",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [ { "positionCm": [1,2,3] } ]
                      }
                    ],
                    "behaviors": [
                      {
                        "id": "any-lifecycle-event",
                        "source": { "kind": "PresentationEvent", "eventKind": "{{eventKind}}", "key": "*" },
                        "target": { "operation": "SetVisibility", "group": "group.a", "bucket": "bucket.a", "span": "span.a" },
                        "mapping": { "kind": "Constant", "constantValue": 1 }
                      }
                    ]
                  }
                ]
                """);
            var loader = BuildLoader(root, out InstancedBatchAssetRegistry batches);
            loader.Load(BuildCatalog());
            Assert.That(batches.TryGet(batches.GetId("batch.lifecycle"), out InstancedBatchAsset asset), Is.True);
            Assert.That(asset.Behaviors[0].SourceKeyId, Is.EqualTo(-1));

            using var world = World.Create();
            var definitions = new PerformerDefinitionRegistry();
            int batchId = batches.GetId("batch.lifecycle");
            int defId = definitions.Register("performer.lifecycle", new PerformerDefinition
            {
                InstancedBatches = new[] { new InstancedBatchBinding(batchId) },
            });
            var runtime = new PerformerEntityRuntime(world);
            Entity owner = world.Create();
            Entity performer = runtime.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 700, Entity.Null, definitions.Get(defId));
            var operations = new InstancedBatchOperationBuffer();
            var events = new PresentationEventStream(8);
            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = eventKind,
                KeyId = defId,
                Source = owner,
                PerformerEntity = performer,
                Magnitude = 700f,
            }), Is.True);

            using var system = new InstancedBatchBehaviorSystem(world, definitions, runtime, batches, operations, events, ownerChanges);
            system.Update(0.016f);

            ReadOnlySpan<InstancedBatchOperation> span = operations.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(InstancedBatchOperationKind.SetVisibility));
            Assert.That(span[0].PerformerStableId, Is.EqualTo(700));
            Assert.That(span[0].Address.IsValid, Is.True);
        }

        [Test]
        public void PerformerLoader_ParsesInstancedBatchReferencesAndRejectsUnknownAssets()
        {
            string root = CreateTempCoreRoot();
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            WritePresentationFile(root, "performers.json",
                """
                [
                  {
                    "id": "performer.batch",
                    "instancedBatches": [
                      { "batchAssetId": "batch.runtime" }
                    ]
                  }
                ]
                """);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Presentation/performers.json", ConfigMergePolicy.ArrayById, "id"));
            var definitions = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                definitions,
                resolveInstancedBatchAssetId: key => key == "batch.runtime" ? 42 : 0);

            loader.Load(catalog);

            Assert.That(definitions.TryGet(definitions.GetId("performer.batch"), out PerformerDefinition definition), Is.True);
            Assert.That(definition.InstancedBatches.Length, Is.EqualTo(1));
            Assert.That(definition.InstancedBatches[0].BatchAssetId, Is.EqualTo(42));

            string badRoot = CreateTempCoreRoot();
            WritePresentationFile(badRoot, "performers.json",
                """
                [
                  {
                    "id": "performer.bad",
                    "instancedBatches": [
                      { "batchAssetId": "batch.missing" }
                    ]
                  }
                ]
                """);
            var badVfs = new VirtualFileSystem();
            badVfs.Mount("Core", badRoot);
            var badPipeline = new ConfigPipeline(badVfs, modLoader: null!);
            var badDefinitions = new PerformerDefinitionRegistry();
            var badLoader = new PerformerDefinitionConfigLoader(
                badPipeline,
                badDefinitions,
                resolveInstancedBatchAssetId: _ => 0);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => badLoader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("unknown instanced batch asset 'batch.missing'"));
        }

        [Test]
        public void EmissionSystem_EmitsCreateChunksAndDeterministicRemovals()
        {
            string root = CreateTempCoreRoot();
            WritePresentationFile(root, "instanced_batches.json",
                """
                [
                  {
                    "id": "batch.emit",
                    "renderPath": "InstancedStaticMesh",
                    "ownerStableId": "owner.alpha",
                    "progressiveSubmission": { "maxInstancesPerFlush": 1 },
                    "groups": [
                      {
                        "id": "group.a",
                        "meshAssetId": "mesh.unit",
                        "bucketId": "bucket.a",
                        "instanceSpanId": "span.a",
                        "transforms": [
                          { "positionCm": [1,2,3] },
                          { "positionCm": [4,5,6] }
                        ]
                      }
                    ]
                  }
                ]
                """);
            var loader = BuildLoader(root, out InstancedBatchAssetRegistry batches);
            loader.Load(BuildCatalog());
            int batchId = batches.GetId("batch.emit");

            using var world = World.Create();
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("performer.batch", new PerformerDefinition
            {
                InstancedBatches = new[] { new InstancedBatchBinding(batchId) },
            });
            Entity owner = world.Create();
            Entity performer = world.Create(new PerformerState
            {
                DefId = defId,
                StableId = 900,
                OwnerEntity = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            });
            var requests = new InstancedBatchRequestBuffer();
            var events = new PresentationEventStream(8);
            var runtime = new InstancedBatchSubmissionRuntime();
            using var system = new InstancedBatchEmissionSystem(world, definitions, batches, requests, runtime, events);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(InstancedBatchRequestKind.CreateOrUpdate));
            Assert.That(requests.GetSpan()[0].InstanceStart, Is.EqualTo(0));
            Assert.That(requests.GetSpan()[0].InstanceCount, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].FinalChunk, Is.False);

            requests.Clear();
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].InstanceStart, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].FinalChunk, Is.True);

            requests.Clear();
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.PerformerDestroyed,
                KeyId = defId,
                Source = owner,
                PerformerEntity = performer,
                Magnitude = 900,
            }), Is.True);
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(InstancedBatchRequestKind.Remove));
            Assert.That(requests.GetSpan()[0].PerformerStableId, Is.EqualTo(900));
        }

        [Test]
        public void RequestAndOperationBuffers_ClearTransientFrameData()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            InstancedBatchAddress address = new(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1));
            requests.Add(new InstancedBatchRequest(
                InstancedBatchRequestKind.Remove,
                1,
                100,
                default,
                default,
                address,
                VisualRenderPath.InstancedStaticMesh,
                10,
                0,
                0,
                0,
                finalChunk: true));
            operations.Add(new InstancedBatchOperation(
                InstancedBatchOperationKind.SetVisibility,
                1,
                100,
                default,
                default,
                address,
                -1,
                Vector4.Zero,
                state: 0));

            requests.Clear();
            operations.Clear();

            Assert.That(requests.Count, Is.EqualTo(0));
            Assert.That(operations.Count, Is.EqualTo(0));
        }

        private static InstancedBatchAssetConfigLoader BuildLoader(string root, out InstancedBatchAssetRegistry registry)
        {
            var pipeline = BuildCorePipeline(root);
            var meshes = new MeshAssetRegistry();
            var materials = new PresentationMaterialRegistry();
            registry = new InstancedBatchAssetRegistry();

            meshes.Register("mesh.unit", MeshAssetDescriptor.Model(0));
            meshes.Register("effect.batch.spark", MeshAssetDescriptor.Billboard(0));

            materials.Register("material.unit", MaterialAssetDomain.Surface, Array.Empty<string>(), MaterialAssetFlags.None);
            return new InstancedBatchAssetConfigLoader(
                pipeline,
                registry,
                meshes,
                materials,
                AttributeRegistry.GetId,
                ResolveGasEventKey,
                ResolvePresentationEventKey);
        }

        private static int ResolveGasEventKey(PresentationEventKind eventKind, string key)
        {
            return eventKind == PresentationEventKind.EffectApplied
                ? EffectTemplateIdRegistry.GetId(key)
                : AbilityIdRegistry.GetId(key);
        }

        private static int ResolvePresentationEventKey(PresentationEventKind eventKind, string key)
        {
            return eventKind switch
            {
                PresentationEventKind.GameplayEvent => TagRegistry.GetId(key),
                PresentationEventKind.TagEffectiveChanged => TagRegistry.GetId(key),
                PresentationEventKind.EffectApplied => EffectTemplateIdRegistry.GetId(key),
                PresentationEventKind.CastCommitted => AbilityIdRegistry.GetId(key),
                PresentationEventKind.CastFailed => AbilityIdRegistry.GetId(key),
                PresentationEventKind.PerformerCreated => key == "*" ? -1 : 0,
                PresentationEventKind.PerformerDestroyed => key == "*" ? -1 : 0,
                _ => 0,
            };
        }

        private static string CreateTempCoreRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_InstancedBatchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
            return root;
        }

        private static void WritePresentationFile(string root, string fileName, string content)
        {
            File.WriteAllText(Path.Combine(root, "Configs", "Presentation", fileName), content);
        }

        private static ConfigPipeline BuildCorePipeline(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            return new ConfigPipeline(vfs, modLoader: null!);
        }

        private static ConfigCatalog BuildCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Presentation/instanced_batches.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }
    }
}
