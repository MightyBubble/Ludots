using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using Arch.Core;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterTransformSingleEntryTests
    {
        [Test]
        public void Emit_MeshSurfaceAndSkinnedShareResolvedRootTransform_AnchorAppliedOnce()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("single.entry.multi.output", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 31,
                            MaterialId = 41,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = new Vector3(1f, 0f, 0f),
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.SkinnedMesh,
                            AssetId = 32,
                            MaterialId = 42,
                            RenderPath = VisualRenderPath.SkinnedMesh,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = new Vector3(-1f, 0f, 0f),
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 12,
                        Kind = BehaviorKind.SurfaceSource,
                        ActiveByDefault = true,
                        SurfaceSource = new SurfaceAuthoringBlock(),
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var events = new PresentationEventStream(64);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7001, parent: Entity.Null,
                definitions.Get(defId));

            Assert.That(
                world.Get<PresenterWorldPosition>(presenter).Value,
                Is.EqualTo(new Vector3(10f, 2f, 20f)),
                "Root initialization applies anchor.offset exactly once.");

            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());
            emitSystem.Update(0.016f);

            PresentationVisualProxy mesh = default;
            PresentationVisualProxy skinned = default;
            SurfaceSourceRequest surface = default;
            bool meshEmitted = false;
            bool skinnedEmitted = false;
            bool surfaceEmitted = false;
            foreach (ref readonly PresentationRequestOp op in requests.Ops)
            {
                switch (op.Channel)
                {
                    case PresentationRequestChannel.VisualProxy:
                    {
                        ref readonly VisualProxyChannelItem item = ref requests.VisualProxyAt(op.Slot);
                        if (item.VisualProxy.MeshAssetId == 31)
                        {
                            mesh = item.VisualProxy;
                            meshEmitted = true;
                        }
                        else if (item.VisualProxy.MeshAssetId == 32)
                        {
                            skinned = item.VisualProxy;
                            skinnedEmitted = true;
                        }

                        break;
                    }
                    case PresentationRequestChannel.SurfaceSource:
                        surface = requests.SurfaceSourceAt(op.Slot).Item;
                        surfaceEmitted = true;
                        break;
                }
            }

            Assert.That(meshEmitted, Is.True, "Mesh output must emit.");
            Assert.That(mesh.Position, Is.EqualTo(new Vector3(11f, 2f, 20f)), "Mesh consumes the shared root once and applies its localOffset once.");
            Assert.That(skinnedEmitted, Is.True, "Skinned output must emit.");
            Assert.That(skinned.Position, Is.EqualTo(new Vector3(9f, 2f, 20f)), "Skinned output shares the same resolved root.");
            Assert.That(surfaceEmitted, Is.True, "Surface output must emit.");
            Assert.That(surface.AnchorPosition, Is.EqualTo(new Vector3(10f, 2f, 20f)), "Surface anchor equals the resolved root; the anchor offset is not added a second time at emit.");
        }

        [Test]
        public void Emit_RepeatedEmit_AnchorOffsetDoesNotAccumulate()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("single.entry.repeat.emit", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 51,
                            MaterialId = 61,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7002, parent: Entity.Null,
                definitions.Get(defId));

            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());
            emitSystem.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.VisualProxyAt(0).VisualProxy.Position, Is.EqualTo(new Vector3(10f, 2f, 20f)));

            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(2), "Dirty re-emit must emit again.");
            Assert.That(
                requests.VisualProxyAt(1).VisualProxy.Position,
                Is.EqualTo(new Vector3(10f, 2f, 20f)),
                "Repeated emit must not stack the anchor offset a second time.");

            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);
            Assert.That(requests.VisualProxyAt(2).VisualProxy.Position, Is.EqualTo(new Vector3(10f, 2f, 20f)));
        }

        [Test]
        public void TransformSync_MovingOwner_TracksAnchorOffsetWithoutDrift()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("single.entry.owner.sync", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 52,
                            MaterialId = 62,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7003, parent: Entity.Null,
                definitions.Get(defId));

            using var syncSystem = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());

            world.Get<VisualTransform>(owner).Position = new Vector3(30f, 0f, 40f);
            syncSystem.Update(0.016f);
            Assert.That(
                world.Get<PresenterWorldPosition>(presenter).Value,
                Is.EqualTo(new Vector3(30f, 2f, 40f)),
                "Sync must preserve the anchor offset on the resolved root.");

            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);
            Assert.That(requests.VisualProxyAt(0).VisualProxy.Position, Is.EqualTo(new Vector3(30f, 2f, 40f)));

            world.Get<VisualTransform>(owner).Position = new Vector3(50f, 0f, 60f);
            syncSystem.Update(0.016f);
            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);
            Assert.That(requests.VisualProxyAt(1).VisualProxy.Position, Is.EqualTo(new Vector3(50f, 2f, 60f)), "Anchor offset must never accumulate across sync cycles.");
        }

        [Test]
        public void Emit_AttachedChild_ConsumesAttachmentOnceAndLocalOffsetOnce()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int parentDefId = definitions.Register("single.entry.parent", new PresenterDefinition());
            int childDefId = definitions.Register("single.entry.attached.child", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 3f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 53,
                            MaterialId = 63,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = new Vector3(1f, 0f, 0f),
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var events = new PresentationEventStream(64);
            var sounds = new SoundRequestBuffer();
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity parent = runtime.CreateHierarchy(
                definitions, parentDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7004, parent: Entity.Null,
                definitions.Get(parentDefId));
            Entity child = runtime.CreateHierarchy(
                definitions, childDefId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7005, parent,
                definitions.Get(childDefId));

            using var behaviorSystem = new PresenterBehaviorSystem(
                world, runtime, definitions, events, new PresentationOwnerChangeBuffer(8), sounds);
            behaviorSystem.Update(0.016f);

            Assert.That(
                world.Get<PresenterWorldPosition>(child).Value,
                Is.EqualTo(new Vector3(10f, 3f, 20f)),
                "The parent attachment positions the child root exactly once.");

            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());
            emitSystem.Update(0.016f);

            foreach (ref readonly PresentationRequestOp op in requests.Ops)
            {
                if (op.Channel != PresentationRequestChannel.VisualProxy)
                {
                    continue;
                }

                ref readonly VisualProxyChannelItem item = ref requests.VisualProxyAt(op.Slot);
                if (item.VisualProxy.MeshAssetId == 53)
                {
                    Assert.That(
                        item.VisualProxy.Position,
                        Is.EqualTo(new Vector3(11f, 3f, 20f)),
                        "Asset localOffset composes once on top of the attached root.");
                    return;
                }
            }

            Assert.Fail("Attached child mesh output must emit.");
        }

        [Test]
        public void Grounding_SnapsResolvedRoot_AndKeepsLocalOffsetRelative()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("single.entry.grounded", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 54,
                            MaterialId = 64,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalOffset = new Vector3(0f, 1f, 0f),
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 3,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            Offset = 0f,
                            UpdatePolicy = GroundingUpdatePolicy.Once,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var events = new PresentationEventStream(64);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 7006, parent: Entity.Null,
                definitions.Get(defId));
            world.Add(presenter, new PresenterBootstrapPending());

            using var behaviorSystem = new PresenterBehaviorSystem(
                world, runtime, definitions, events, new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(), new FlatHeightmap(heightCm: 250f));
            behaviorSystem.Update(0.016f);

            Assert.That(
                world.Get<PresenterWorldPosition>(presenter).Value.Y,
                Is.EqualTo(2.5f).Within(0.001f),
                "Grounding snaps the resolved root (owner + anchor offset); the asset local offset is not baked into the snapped root.");

            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());
            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);

            foreach (ref readonly PresentationRequestOp op in requests.Ops)
            {
                if (op.Channel != PresentationRequestChannel.VisualProxy)
                {
                    continue;
                }

                ref readonly VisualProxyChannelItem item = ref requests.VisualProxyAt(op.Slot);
                if (item.VisualProxy.MeshAssetId == 54)
                {
                    Assert.That(item.VisualProxy.Position.Y, Is.EqualTo(3.5f).Within(0.001f));
                    return;
                }
            }

            Assert.Fail("Grounded mesh output must emit.");
        }

        [Test]
        public void WorldFixedAnchor_RootStaysStableAcrossBootstrapAndEmit()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("single.entry.world.fixed", new PresenterDefinition
            {
                PositionOffset = new Vector3(0f, 2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 55,
                            MaterialId = 65,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            var events = new PresentationEventStream(64);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create();

            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.WorldPosition,
                worldPosition: new Vector3(5f, 0f, 7f), stableId: 7007, parent: Entity.Null,
                definitions.Get(defId));

            Assert.That(
                world.Get<PresenterWorldPosition>(presenter).Value,
                Is.EqualTo(new Vector3(5f, 2f, 7f)),
                "World-fixed initialization applies the anchor offset exactly once.");

            world.Add(presenter, new PresenterBootstrapPending());
            using var behaviorSystem = new PresenterBehaviorSystem(
                world, runtime, definitions, events, new PresentationOwnerChangeBuffer(8), new SoundRequestBuffer());
            behaviorSystem.Update(0.016f);
            behaviorSystem.Update(0.016f);

            Assert.That(
                world.Get<PresenterWorldPosition>(presenter).Value,
                Is.EqualTo(new Vector3(5f, 2f, 7f)),
                "Repeated bootstrap resolution must not move a world-fixed root.");

            using var emitSystem = new PresenterEmitSystem(
                world, runtime, definitions, requests, new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(4), new SoundRequestBuffer());
            emitSystem.Update(0.016f);
            runtime.MarkTransformDrivenEmitDirty(presenter);
            emitSystem.Update(0.016f);

            Assert.That(requests.VisualProxyAt(0).VisualProxy.Position, Is.EqualTo(new Vector3(5f, 2f, 7f)));
            Assert.That(requests.VisualProxyAt(1).VisualProxy.Position, Is.EqualTo(new Vector3(5f, 2f, 7f)));
        }

        [Test]
        public void LocalOffsetConsumption_DoubleConsume_ProducesDiagnostic()
        {
            var asset = new AssetBindingConfig { LocalOffset = new Vector3(1f, 0f, 0f) };
            uint consumedMask = 0u;
            PresenterLocalOffsetConsumption.MarkSlotConsumed(3, in asset, 9001, ref consumedMask);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => PresenterLocalOffsetConsumption.MarkSlotConsumed(3, in asset, 9001, ref consumedMask));
            Assert.That(ex.Message, Does.Contain("9001"));
            Assert.That(ex.Message, Does.Contain("slot 3"));
            Assert.That(ex.Message, Does.Contain("localOffset"));

            PresenterLocalOffsetConsumption.MarkSlotConsumed(4, in asset, 9001, ref consumedMask);
            uint zeroOffsetMask = 0u;
            PresenterLocalOffsetConsumption.MarkSlotConsumed(
                3, new AssetBindingConfig { LocalOffset = Vector3.Zero }, 9001, ref zeroOffsetMask);
        }

        [Test]
        public void Load_RejectsAnchorOffsetCombinedWithAttachmentBehavior()
        {
            var loaderHarness = new LoaderHarness();
            loaderHarness.WriteCatalog();
            loaderHarness.WritePresenters(
                """
                [
                  {
                    "id": "anchor_plus_attachment",
                    "lifecycle": { "durationSeconds": 5 },
                    "anchor": { "offset": [0, 1.5, 0] },
                    "behaviors": [
                      {
                        "slot": "attachment",
                        "kind": "Attachment",
                        "activeByDefault": true,
                        "attachment": {
                          "target": "Parent",
                          "offset": [0, 0.5, 0],
                          "rotationOffset": [0, 0, 0, 1],
                          "inheritScale": true
                        }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loaderHarness.Load());
            Assert.That(ex.Message, Does.Contain("anchor_plus_attachment"));
            Assert.That(ex.Message, Does.Contain("anchor.offset"));
            Assert.That(ex.Message, Does.Contain("Attachment"));
        }

        [Test]
        public void Load_RejectsChildAnchorOffsetCombinedWithInstanceTransformOverride()
        {
            var loaderHarness = new LoaderHarness();
            loaderHarness.WriteCatalog();
            loaderHarness.WritePresenters(
                """
                [
                  {
                    "id": "offset_child",
                    "lifecycle": { "durationSeconds": 5 },
                    "anchor": { "offset": [0, 1, 0] }
                  },
                  {
                    "id": "override_parent",
                    "lifecycle": { "durationSeconds": 5 },
                    "children": [
                      {
                        "definitionId": "offset_child",
                        "overrides": {
                          "transform": { "localPosition": [1, 0, 0] }
                        }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loaderHarness.Load());
            Assert.That(ex.Message, Does.Contain("children[0]"));
            Assert.That(ex.Message, Does.Contain("offset_child"));
        }

        [Test]
        public void Load_AcceptsAnchorOffsetWithoutAttachment_AndAttachmentWithoutAnchorOffset()
        {
            var loaderHarness = new LoaderHarness();
            loaderHarness.WriteCatalog();
            loaderHarness.WritePresenters(
                """
                [
                  {
                    "id": "anchor_only",
                    "lifecycle": { "durationSeconds": 5 },
                    "anchor": { "offset": [0, 1.5, 0] }
                  },
                  {
                    "id": "attachment_only",
                    "lifecycle": { "durationSeconds": 5 },
                    "behaviors": [
                      {
                        "slot": "attachment",
                        "kind": "Attachment",
                        "activeByDefault": true,
                        "attachment": {
                          "target": "Parent",
                          "offset": [0, 0.5, 0],
                          "rotationOffset": [0, 0, 0, 1],
                          "inheritScale": true
                        }
                      }
                    ]
                  }
                ]
                """);

            loaderHarness.Load();
            Assert.That(loaderHarness.Registry.GetId("anchor_only"), Is.GreaterThan(0));
            Assert.That(loaderHarness.Registry.GetId("attachment_only"), Is.GreaterThan(0));
        }

        private sealed class LoaderHarness : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterTransformSingleEntry", Guid.NewGuid().ToString("N"));

            public PresenterDefinitionRegistry Registry { get; } = new();

            public void WriteCatalog()
            {
                WriteFile(
                    "Core",
                    "config_catalog.json",
                    @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            }

            public void WritePresenters(string content)
            {
                WriteFile("Core", "Presentation/presenters.json", content);
            }

            public void Load()
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", Path.Combine(_root, "Core"));
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);
                new PresenterDefinitionConfigLoader(pipeline, Registry).Load(catalog);
            }

            private void WriteFile(string modId, string relativePath, string content)
            {
                string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch
                {
                    // Ignore temp cleanup failures in test teardown.
                }
            }
        }

        private sealed class FlatHeightmap : IVisualHeightmap
        {
            private readonly float _heightCm;

            public FlatHeightmap(float heightCm)
            {
                _heightCm = heightCm;
            }

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                heightCm = _heightCm;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                for (int i = 0; i < outHeightCm.Length; i++)
                {
                    outHeightCm[i] = _heightCm;
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                hit = new VisualGroundHit(
                    ray.Origin.X * 100f,
                    ray.Origin.Z * 100f,
                    _heightCm,
                    layerIndex,
                    distanceMeters: 0f,
                    normal: Vector3.UnitY);
                return true;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = 0)
            {
                for (int i = 0; i < outHeightCm.Length; i++)
                {
                    outWorldXCm[i] = originXMeters[i] * 100f;
                    outWorldYCm[i] = originZMeters[i] * 100f;
                    outHeightCm[i] = _heightCm;
                    outDistanceMeters[i] = 0f;
                    outNormalX[i] = 0f;
                    outNormalY[i] = 1f;
                    outNormalZ[i] = 0f;
                    outLayerIndex[i] = layerIndex;
                    outHitMask[i] = 1;
                }

                return true;
            }
        }
    }
}
