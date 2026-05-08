using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerGroundingAndGlobalEventTests
    {
        [Test]
        public void ResolveTransform_InheritParent_ComposesOffsetRotationScale()
        {
            var parent = new PerformerTransformSnapshot
            {
                WorldPosition = new Vector3(10f, 2f, 20f),
                WorldRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f),
                WorldScale = new Vector3(2f, 2f, 2f),
                TransformSource = TransformSource.EntityTransform,
            };

            var child = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.InheritParent,
            };

            var asset = new AssetBindingConfig
            {
                LocalOffset = new Vector3(1f, 0f, 0f),
                LocalRotation = Quaternion.Identity,
                LocalScale = new Vector3(0.5f, 0.5f, 0.5f),
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                child,
                parent,
                hasParent: true,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position.X, Is.EqualTo(10f).Within(0.001f));
            Assert.That(resolved.Position.Z, Is.EqualTo(18f).Within(0.001f));
            Assert.That(resolved.Scale, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void ResolveTransform_EntityTransform_ComposesOwnerTransformAndAssetLocalTransform()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.EntityTransform,
                WorldPosition = new Vector3(-5f, -5f, -5f),
                WorldRotation = Quaternion.Identity,
            };

            var ownerTransform = new VisualTransform
            {
                Position = new Vector3(10f, 2f, 20f),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f),
                Scale = new Vector3(9f, 9f, 9f),
            };

            var asset = new AssetBindingConfig
            {
                LocalOffset = new Vector3(1f, 0f, 0f),
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.25f),
                LocalScale = new Vector3(2f, 3f, 4f),
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform,
                hasOwnerTransform: true,
                asset);

            Assert.That(resolved.Position.X, Is.EqualTo(10f).Within(0.001f));
            Assert.That(resolved.Position.Y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(resolved.Position.Z, Is.EqualTo(11f).Within(0.001f));
            Assert.That(resolved.Scale, Is.EqualTo(new Vector3(18f, 27f, 36f)));
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, resolved.Rotation);
            Assert.That(MathF.Abs(forward.X), Is.GreaterThan(0.6f));
        }

        [Test]
        public void ResolveTransform_SplineDriven_UsesInstanceTransformAndAssetLocalTransform()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.SplineDriven,
                WorldPosition = new Vector3(3f, 4f, 5f),
                WorldRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f),
            };

            var asset = new AssetBindingConfig
            {
                LocalOffset = new Vector3(0f, 1f, 2f),
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f),
                LocalScale = new Vector3(1.5f, 2f, 2.5f),
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(resolved.Position.Y, Is.EqualTo(5f).Within(0.001f));
            Assert.That(resolved.Position.Z, Is.EqualTo(7f).Within(0.001f));
            Assert.That(resolved.Scale, Is.EqualTo(new Vector3(1.5f, 2f, 2.5f)));
        }

        [Test]
        public void ResolveTransform_WorldFixed_DoesNotApplyGrounding()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.WorldFixed,
                WorldPosition = new Vector3(3f, 9f, 4f),
                WorldRotation = Quaternion.Identity,
            };

            var asset = new AssetBindingConfig
            {
                LocalScale = Vector3.One,
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position.Y, Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void ResolveTransform_GroundingNone_DoesNotTouchHeightmap()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.WorldFixed,
                WorldPosition = new Vector3(7f, 8f, 9f),
                WorldRotation = Quaternion.Identity,
            };

            var asset = new AssetBindingConfig
            {
                LocalScale = Vector3.One,
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position, Is.EqualTo(new Vector3(7f, 8f, 9f)));
        }

        [Test]
        public void DefinitionMetadata_SplitsBootstrapAndEveryFrameGroundingLanes()
        {
            var definitions = new PerformerDefinitionRegistry();

            int defId = definitions.Register("grounding.lane.split", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            UpdatePolicy = GroundingUpdatePolicy.Once,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.AlignToSurface,
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 2,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.None,
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                ],
            });

            PerformerDefinition definition = definitions.Get(defId);

            Assert.That(ReadInternalIntArray(definition, "BootstrapGroundingBehaviorIndices"), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(ReadInternalIntArray(definition, "TickBehaviorIndices"), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void ResolveBatch_AppliesGroundingPerMode_AndSkipsGroundingNone()
        {
            Vector3[] positions =
            [
                new Vector3(1f, 2f, 3f),
                new Vector3(4f, 5f, 6f),
                new Vector3(7f, 8f, 9f),
            ];
            GroundingMode[] modes =
            [
                GroundingMode.None,
                GroundingMode.SnapToGround,
                GroundingMode.AlignToSurface,
            ];
            float[] offsets = [0f, 1.25f, 0.5f];

            Quaternion[] rotations =
            [
                Quaternion.Identity,
                Quaternion.Identity,
                Quaternion.Identity,
            ];

            PerformerGroundingUtility.ResolveBatch(positions, rotations, modes, offsets, new StubHeightmap(heightCm: 250f));

            Assert.That(positions[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(positions[1].Y, Is.EqualTo(3.75f).Within(0.001f));
            Assert.That(positions[2].Y, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void ResolveBatch_AlignToSurface_RotatesUpAxisToGroundNormal()
        {
            Vector3[] positions = [new Vector3(1f, 0f, 2f)];
            Quaternion[] rotations = [Quaternion.Identity];
            GroundingMode[] modes = [GroundingMode.AlignToSurface];
            float[] offsets = [0f];
            Vector3 surfaceNormal = Vector3.Normalize(new Vector3(0f, 1f, 1f));

            PerformerGroundingUtility.ResolveBatch(positions, rotations, modes, offsets, new StubHeightmap(heightCm: 120f, normal: surfaceNormal));

            Vector3 up = Vector3.Transform(Vector3.UnitY, rotations[0]);
            Assert.That(positions[0].Y, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(Vector3.Dot(Vector3.Normalize(up), surfaceNormal), Is.GreaterThan(0.999f));
        }

        [Test]
        public void ResolveTransform_BoneAttached_SkipsGrounding()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.BoneAttached,
                WorldPosition = new Vector3(5f, 6f, 7f),
                WorldRotation = Quaternion.Identity,
                WorldScale = Vector3.One,
            };

            var asset = new AssetBindingConfig
            {
                LocalScale = new Vector3(2f, 3f, 4f),
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position, Is.EqualTo(instance.WorldPosition));
            Assert.That(resolved.Scale, Is.EqualTo(instance.WorldScale));
        }

        [Test]
        public void ResolveTransform_AttachedToParent_SkipsGrounding()
        {
            var instance = new PerformerTransformSnapshot
            {
                TransformSource = TransformSource.AttachedToParent,
                WorldPosition = new Vector3(2f, 3f, 4f),
                WorldRotation = Quaternion.Identity,
                WorldScale = new Vector3(1.5f, 1.5f, 1.5f),
            };

            var asset = new AssetBindingConfig
            {
                LocalScale = Vector3.One,
            };

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                default,
                hasParent: false,
                ownerTransform: default,
                hasOwnerTransform: false,
                asset);

            Assert.That(resolved.Position, Is.EqualTo(instance.WorldPosition));
            Assert.That(resolved.Scale, Is.EqualTo(instance.WorldScale));
        }

        [Test]
        public void RuntimeCreate_GroundedStaticMesh_KeepsBootstrapAndEventDrivenStaticEmit()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream();
            var runtime = new PerformerEntityRuntime(world);
            var stableIds = new PresentationStableIdAllocator();
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create(new VisualTransform
            {
                Position = new Vector3(10f, 0f, 20f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            });

            int defId = definitions.Register("grounded.static.mesh", new PerformerDefinition
            {
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
                            AssetId = 77,
                            MaterialId = 11,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalOffset = new Vector3(2f, 0f, -3f),
                            LocalScale = new Vector3(1.5f, 1f, 1.5f),
                            ScaleParamKey = -1,
                            ColorParamKey = -1,
                            MaterialParamKey = -1,
                            AssetSwapParamKey = -1,
                            VisibilityParamKey = -1,
                        },
                    },
                ],
            });

            using var runtimeSystem = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                runtime,
                stableIds,
                definitions);

            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                Source = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            }), Is.True);

            runtimeSystem.Update(0.016f);

            Entity performer = Entity.Null;
            world.Query(new QueryDescription().WithAll<PerformerState>(), (Entity entity, ref PerformerState state) =>
            {
                if (state.DefId == defId)
                {
                    performer = entity;
                }
            });

            Assert.That(performer, Is.Not.EqualTo(Entity.Null));
            Assert.That(world.Has<PerformerBootstrapPending>(performer), Is.True, "grounding/local transform still require one-shot bootstrap.");
            Assert.That(world.Has<PerfStaticStableVisual>(performer), Is.True, "grounded static mesh should still use event-driven stable emit after bootstrap.");
            Assert.That(world.Get<PerformerEmitCache>(performer).StaticDirty, Is.EqualTo((byte)1));
        }

        [Test]
        public void RuntimeCreate_StaticOnceGrounding_DoesNotEnterTickDrivenGrounding()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream();
            var runtime = new PerformerEntityRuntime(world);
            var stableIds = new PresentationStableIdAllocator();
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 2f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new PresentationStaticTransform());

            int defId = definitions.Register("static.once.grounded.mesh", new PerformerDefinition
            {
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
                            AssetId = 77,
                            MaterialId = 11,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.One,
                            ScaleParamKey = -1,
                            ColorParamKey = -1,
                            MaterialParamKey = -1,
                            AssetSwapParamKey = -1,
                            VisibilityParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
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

            using var runtimeSystem = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                runtime,
                stableIds,
                definitions);

            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                Source = owner,
                ScopeTag = 1,
                AnchorKind = PresentationAnchorKind.Entity,
            }), Is.True);

            runtimeSystem.Update(0.016f);

            Entity performer = Entity.Null;
            world.Query(new QueryDescription().WithAll<PerformerState>(), (Entity entity, ref PerformerState state) =>
            {
                if (state.DefId == defId)
                {
                    performer = entity;
                }
            });

            Assert.That(performer, Is.Not.EqualTo(Entity.Null));
            Assert.That(world.Has<PerformerBootstrapPending>(performer), Is.True);
            Assert.That(world.Has<PerfHasGrounding>(performer), Is.False, "Once grounding must stay out of the per-frame grounding query.");
            Assert.That(world.Has<PerfStaticStableVisual>(performer), Is.True, "Static mesh with one-shot grounding remains eligible for event-driven stable emit.");
        }

        [Test]
        public void BehaviorSystem_OwnerAttributeChangeBuffer_UpdatesOwnerScopedPerformerOnly()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var ownerChanges = new PresentationOwnerChangeBuffer();
            var sounds = new SoundRequestBuffer();
            int durabilityId = 7;
            Entity owner = world.Create(new AttributeBuffer());
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(owner);
            attributes.SetBase(durabilityId, 100f);
            attributes.SetCurrent(durabilityId, 50f);

            int defId = definitions.Register("owner.attr.bound.visual", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = durabilityId,
                            TargetParamKey = 101,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping
                                {
                                    Threshold = 0.5f,
                                    OutputParamKey = 104,
                                    OutputValue = 2f,
                                },
                            ],
                        },
                    },
                ],
            });
            Entity performer = runtime.Create(
                defId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero,
                stableId: 11,
                parent: Entity.Null,
                definitions.Get(defId));

            ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, durabilityId));

            using var system = new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                events,
                ownerChanges,
                sounds);

            system.Update(0.016f);

            Assert.That(world.Get<PerformerFloatParams>(performer).TryGet(101, out float ratio), Is.True);
            Assert.That(ratio, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(world.Get<PerformerIntParams>(performer).TryGet(104, out int state), Is.True);
            Assert.That(state, Is.EqualTo(2));
        }

        [Test]
        public void BehaviorSystem_BootstrapAttachmentThenOnceGrounding_ComposesBeforeSamplingHeight()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var sounds = new SoundRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new PresentationStaticTransform());

            int parentDefId = definitions.Register("static.parent", new PerformerDefinition());
            int childDefId = definitions.Register("static.child.attached.once.grounded", new PerformerDefinition
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
                            Offset = new Vector3(2f, 0f, 3f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            Offset = 0.5f,
                            UpdatePolicy = GroundingUpdatePolicy.Once,
                        },
                    },
                ],
            });
            Entity parent = runtime.Create(
                parentDefId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                world.Get<VisualTransform>(owner).Position,
                stableId: 11,
                parent: Entity.Null,
                definitions.Get(parentDefId));
            Entity child = runtime.Create(
                childDefId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                world.Get<VisualTransform>(owner).Position,
                stableId: 12,
                parent,
                definitions.Get(childDefId));
            world.Add(child, new PerformerBootstrapPending());

            using var system = new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                events,
                sounds,
                new StubHeightmap(heightCm: 250f));

            system.Update(0.016f);

            Vector3 position = world.Get<PerformerWorldPosition>(child).Value;
            Assert.That(position.X, Is.EqualTo(12f).Within(0.001f));
            Assert.That(position.Z, Is.EqualTo(23f).Within(0.001f));
            Assert.That(position.Y, Is.EqualTo(3f).Within(0.001f));
            Assert.That(world.Has<PerformerBootstrapPending>(child), Is.False);
            Assert.That(world.Has<PerfHasGrounding>(child), Is.False);
        }

        [Test]
        public void BehaviorSystem_SkipsDuplicateEntityBackedSnapToGround_WhenOwnerVisualHeightAlreadySampled()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var sounds = new SoundRequestBuffer();
            Entity frame = world.Create(new PresentationFrameState { FrameId = 7 }, new PresentationFrameStateTag());
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 2.5f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new VisualHeightmapSampleState { FrameId = 7, Sampled = 1 });

            int defId = definitions.Register("entity.grounded.root", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            Offset = 0f,
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                ],
            });

            Entity performer = runtime.Create(
                defId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                worldPosition: world.Get<VisualTransform>(owner).Position,
                stableId: 11,
                parent: Entity.Null,
                definitions.Get(defId));
            if (world.Has<PerformerBootstrapPending>(performer))
            {
                world.Remove<PerformerBootstrapPending>(performer);
            }
            Assert.That(world.Has<PerfHasGrounding>(performer), Is.False,
                "Entity-backed zero-offset snap-to-ground should reuse the owner's VisualHeightmapSampleState instead of entering the performer tick query.");

            using var system = new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                events,
                sounds,
                new ThrowingHeightmap());

            Assert.DoesNotThrow(() => system.Update(0.016f));
            Assert.That(world.Get<PerformerWorldPosition>(performer).Value.Y, Is.EqualTo(2.5f).Within(0.001f));
            world.Destroy(frame);
        }

        [Test]
        public void BehaviorSystem_DoesNotSkipSnapToGround_WhenOffsetRequiresPerformerGrounding()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var sounds = new SoundRequestBuffer();
            world.Create(new PresentationFrameState { FrameId = 7 }, new PresentationFrameStateTag());
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 2.5f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new VisualHeightmapSampleState { FrameId = 7, Sampled = 1 });

            int defId = definitions.Register("entity.grounded.offset.root", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            Offset = 1.25f,
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                ],
            });

            Entity performer = runtime.Create(
                defId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                worldPosition: world.Get<VisualTransform>(owner).Position,
                stableId: 11,
                parent: Entity.Null,
                definitions.Get(defId));
            if (world.Has<PerformerBootstrapPending>(performer))
            {
                world.Remove<PerformerBootstrapPending>(performer);
            }
            Assert.That(world.Has<PerfHasGrounding>(performer), Is.True,
                "Offset grounding still needs performer-side sampling.");

            using var system = new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                events,
                sounds,
                new StubHeightmap(heightCm: 250f));

            system.Update(0.016f);

            Assert.That(world.Get<PerformerWorldPosition>(performer).Value.Y, Is.EqualTo(3.75f).Within(0.001f));
        }

        [Test]
        public void BehaviorSystem_EveryFrameGroundingWithoutHeightmap_FallsBackToOffset()
        {
            using var world = World.Create();
            var runtime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var sounds = new SoundRequestBuffer();
            Entity owner = world.Create(
                new VisualTransform
                {
                    Position = new Vector3(10f, 9f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            int defId = definitions.Register("entity.grounded.no.heightmap", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Grounding,
                        ActiveByDefault = true,
                        Grounding = new GroundingConfig
                        {
                            Mode = GroundingMode.SnapToGround,
                            Offset = 1.25f,
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                ],
            });

            Entity performer = runtime.Create(
                defId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                worldPosition: world.Get<VisualTransform>(owner).Position,
                stableId: 11,
                parent: Entity.Null,
                definitions.Get(defId));
            if (world.Has<PerformerBootstrapPending>(performer))
            {
                world.Remove<PerformerBootstrapPending>(performer);
            }

            using var system = new PerformerBehaviorSystem(
                world,
                runtime,
                definitions,
                events,
                sounds,
                heightmap: null);

            Assert.DoesNotThrow(() => system.Update(0.016f));

            Assert.That(world.Has<PerfHasGrounding>(performer), Is.True);
            Assert.That(world.Get<PerformerWorldPosition>(performer).Value.Y, Is.EqualTo(1.25f).Within(0.001f));
        }

        [Test]
        public void TerrainHeightSync_StaticWithoutHeightmap_ProjectsOnceToZeroAndClearsPending()
        {
            using var world = World.Create();
            world.Create(new PresentationFrameState { Enabled = true, InterpolationAlpha = 1f, FrameId = 1 }, new PresentationFrameStateTag());
            Entity entity = world.Create(
                WorldPositionCm.FromCmFloat(100f, 200f),
                new PreviousWorldPositionCm { Value = WorldPositionCm.FromCmFloat(100f, 200f).Value },
                new VisualTransform
                {
                    Position = new Vector3(1f, 9f, 2f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new PresentationStaticTransform(),
                new PresentationStaticHeightPending());

            using var system = new TerrainHeightSyncSystem(world, new Dictionary<string, object>());

            Assert.DoesNotThrow(() => system.Update(0.016f));
            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(world.Has<PresentationStaticHeightPending>(entity), Is.False);

            world.Get<VisualTransform>(entity).Position.Y = 9f;
            Assert.DoesNotThrow(() => system.Update(0.016f));
            Assert.That(world.Get<VisualTransform>(entity).Position.Y, Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void GlobalEventBridgeSystem_BridgesQueuedEventsIntoPresentationStream()
        {
            using var world = World.Create();
            var globals = new GlobalPresentationEventBuffer();
            var stream = new PresentationEventStream();
            var session = new GameSession();
            using var system = new GlobalEventBridgeSystem(world, globals, stream, session);

            globals.AddDayNight(keyId: 7, phase01: 0.75f);
            globals.AddRegionChanged(regionId: 42, previousRegionId: 11);
            globals.AddWeather(weatherId: 9, intensity: 0.4f);

            system.Update(0.016f);

            ReadOnlySpan<PresentationEvent> span = stream.GetSpan();
            Assert.That(span.Length, Is.EqualTo(3));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationEventKind.GlobalDayNight));
            Assert.That(span[0].KeyId, Is.EqualTo(7));
            Assert.That(span[0].Magnitude, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(span[1].Kind, Is.EqualTo(PresentationEventKind.GlobalRegionChanged));
            Assert.That(span[1].KeyId, Is.EqualTo(42));
            Assert.That(span[1].PayloadA, Is.EqualTo(11));
            Assert.That(span[2].Kind, Is.EqualTo(PresentationEventKind.GlobalWeather));
            Assert.That(span[2].KeyId, Is.EqualTo(9));
            Assert.That(span[2].Magnitude, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(globals.Count, Is.EqualTo(0));
        }

        private sealed class StubHeightmap : IVisualHeightmap
        {
            private readonly float _heightCm;
            private readonly Vector3 _normal;

            public StubHeightmap(float heightCm, Vector3? normal = null)
            {
                _heightCm = heightCm;
                _normal = normal ?? Vector3.UnitY;
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
                    normal: _normal);
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
                    outNormalX[i] = _normal.X;
                    outNormalY[i] = _normal.Y;
                    outNormalZ[i] = _normal.Z;
                    outLayerIndex[i] = layerIndex;
                    outHitMask[i] = 1;
                }

                return true;
            }
        }

        private sealed class ThrowingHeightmap : IVisualHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                throw new AssertionException("GroundingMode.None should not sample heights.");
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                throw new AssertionException("GroundingMode.None should not batch sample heights.");
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                throw new AssertionException("GroundingMode.None should not raycast heightmap.");
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
                throw new AssertionException("GroundingMode.None should not raycast heightmap.");
            }
        }

        private static int[] ReadInternalIntArray(PerformerDefinition definition, string fieldName)
        {
            FieldInfo? field = typeof(PerformerDefinition).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing internal performer metadata field '{fieldName}'.");
            return (int[])field!.GetValue(definition)!;
        }
    }
}
