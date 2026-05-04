using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation;
using Arch.Core.Extensions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerBehaviorKindTests
    {
        [Test]
        public void BehaviorKindContract_ArchitectureExposesCoreKinds()
        {
            BehaviorKind[] values = (BehaviorKind[])Enum.GetValues(typeof(BehaviorKind));
            Assert.That(values.Length, Is.EqualTo(10), "BehaviorKind SSOT is the architecture enum.");
            Assert.That(values, Does.Contain(BehaviorKind.AssetBinding));
            Assert.That(values, Does.Contain(BehaviorKind.AttributeBinding));
            Assert.That(values, Does.Contain(BehaviorKind.TagBinding));
            Assert.That(values, Does.Contain(BehaviorKind.Animator));
            Assert.That(values, Does.Contain(BehaviorKind.Attachment));
            Assert.That(values, Does.Contain(BehaviorKind.Sound));
            Assert.That(values, Does.Contain(BehaviorKind.Material));
            Assert.That(values, Does.Contain(BehaviorKind.Spline));
            Assert.That(values, Does.Contain(BehaviorKind.Grounding));
            Assert.That(values, Does.Contain(BehaviorKind.MinimapMarker));
        }

        [Test]
        public void BehaviorKindContract_ArchitecturePreservesExplicitEnumValues()
        {
            Assert.That((byte)BehaviorKind.AssetBinding, Is.EqualTo(1));
            Assert.That((byte)BehaviorKind.AttributeBinding, Is.EqualTo(2));
            Assert.That((byte)BehaviorKind.TagBinding, Is.EqualTo(3));
            Assert.That((byte)BehaviorKind.Animator, Is.EqualTo(4));
            Assert.That((byte)BehaviorKind.Attachment, Is.EqualTo(5));
            Assert.That((byte)BehaviorKind.Sound, Is.EqualTo(6));
            Assert.That((byte)BehaviorKind.Material, Is.EqualTo(7));
            Assert.That((byte)BehaviorKind.Spline, Is.EqualTo(8));
            Assert.That((byte)BehaviorKind.Grounding, Is.EqualTo(9));
            Assert.That((byte)BehaviorKind.MinimapMarker, Is.EqualTo(10));
        }

        [Test]
        public void AttributeBinding_MapsAttributeRatioAndThreshold()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 50f);
            Entity owner = world.Create(attributes);

            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity performer = instances.Create(1, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7001, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            definitions.Register("behavior.attribute", new PerformerDefinition
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
                            AttributeId = 7,
                            TargetParamKey = 100,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping { Threshold = 0.66f, OutputParamKey = 101, OutputValue = 1f },
                            ],
                        },
                    },
                ],
            });

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(performer, 100), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 101), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void OwnerAttributeChangeBuffer_OnlyUpdatesMatchingAttributeWork()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 25f);
            attributes.SetBase(8, 200f);
            attributes.SetCurrent(8, 100f);
            Entity owner = world.Create(attributes);

            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.owner.attr.fastpath", new PerformerDefinition
            {
                Bindings =
                [
                    new PerformerParamBinding { ParamKey = 200, Value = ValueRef.FromAttributeRatio(7) },
                    new PerformerParamBinding { ParamKey = 201, Value = ValueRef.FromAttributeRatio(8) },
                ],
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 7,
                            TargetParamKey = 210,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 8,
                            TargetParamKey = 211,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                ],
            });

            Assert.That(definitions.TryGet(defId, out PerformerDefinition definition), Is.True);
            Entity performer = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7103, Entity.Null, definition);
            world.Add(performer, new PerformerBootstrapPending());

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveFloat(performer, 200), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 201), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 210), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 211), Is.EqualTo(0.5f).Within(0.001f));

            ref AttributeBuffer updated = ref world.Get<AttributeBuffer>(owner);
            updated.SetCurrent(7, 80f);
            updated.SetCurrent(8, 150f);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, 8)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(performer, 200), Is.EqualTo(0.25f).Within(0.001f), "attribute 7 binding must stay untouched when only attribute 8 changed.");
            Assert.That(instances.ResolveFloat(performer, 201), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 210), Is.EqualTo(0.25f).Within(0.001f), "attribute behavior for attr 7 must not be rescanned.");
            Assert.That(instances.ResolveFloat(performer, 211), Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void PerformerBindings_ResolveAttributeRatioEntityColorAndFacingIntoBlackboard()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 25f);
            Entity owner = world.Create(
                attributes,
                new FacingDirection { AngleRad = MathF.PI * 0.5f },
                new Ludots.Core.Gameplay.Components.Team { Id = 2 });

            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.bindings", new PerformerDefinition
            {
                Bindings =
                [
                    new PerformerParamBinding
                    {
                        ParamKey = 200,
                        Value = ValueRef.FromAttributeRatio(7),
                    },
                    new PerformerParamBinding
                    {
                        ParamKey = 201,
                        Value = ValueRef.FromEntityColor(0),
                    },
                    new PerformerParamBinding
                    {
                        ParamKey = 202,
                        Value = ValueRef.FromEntityColor(1),
                    },
                    new PerformerParamBinding
                    {
                        ParamKey = 205,
                        Value = ValueRef.FromEntityColorVector(),
                    },
                    new PerformerParamBinding
                    {
                        ParamKey = 203,
                        Value = ValueRef.FromFacingRadians(),
                    },
                    new PerformerParamBinding
                    {
                        ParamKey = 204,
                        Value = ValueRef.FromFacingDegrees(),
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7102, Entity.Null, default);

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(performer, 200), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 201), Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 202), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(instances.ResolveVector(performer, 205, Vector4.Zero), Is.EqualTo(new Vector4(0.9f, 0.2f, 0.2f, 1f)));
            Assert.That(instances.ResolveFloat(performer, 203), Is.EqualTo(MathF.PI * 0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(performer, 204), Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void TagBinding_HandlesTagOffAndInvertLogic()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity performer = instances.Create(1, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7002, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 0b11u;

            int workingTagId = TagRegistry.Register("working");
            definitions.Register("behavior.tag", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig
                        {
                            TagId = workingTagId,
                            TargetParamKey = 110,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig
                        {
                            TagId = workingTagId,
                            TargetParamKey = 111,
                            InvertLogic = true,
                        },
                    },
                ],
            });

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(performer, 110), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(performer, 111), Is.EqualTo(1));

            world.Add(owner, default(GameplayTagContainer));
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(owner);
            tags.AddTag(workingTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, workingTagId, stateValue: 1)), Is.True);

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(performer, 110), Is.EqualTo(1));
            Assert.That(instances.ResolveInt(performer, 111), Is.EqualTo(0));
        }

        [Test]
        public void OwnerTagChangeBuffer_OnlyUpdatesMatchingTagBinding()
        {
            using var world = World.Create();
            int workingTagId = TagRegistry.Register("working.fastpath");
            int alertTagId = TagRegistry.Register("alert.fastpath");
            Entity owner = world.Create(default(GameplayTagContainer));

            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.owner.tag.fastpath", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig
                        {
                            TagId = workingTagId,
                            TargetParamKey = 310,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig
                        {
                            TagId = alertTagId,
                            TargetParamKey = 311,
                        },
                    },
                ],
            });

            Assert.That(definitions.TryGet(defId, out PerformerDefinition definition), Is.True);
            Entity performer = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7104, Entity.Null, definition);
            world.Add(performer, new PerformerBootstrapPending());

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(performer, 310), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(performer, 311), Is.EqualTo(0));

            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(owner);
            tags.AddTag(workingTagId);
            tags.AddTag(alertTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, alertTagId, stateValue: 1)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(performer, 310), Is.EqualTo(0), "tag binding for unrelated tag must stay untouched.");
            Assert.That(instances.ResolveInt(performer, 311), Is.EqualTo(1));

            tags.RemoveTag(alertTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, alertTagId, stateValue: 0)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(performer, 310), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(performer, 311), Is.EqualTo(0));
        }

        [Test]
        public void TickBehaviorMarkers_TrackActiveBehaviorMask()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.tick.markers", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Sound,
                        ActiveByDefault = false,
                        Sound = new SoundConfig { SoundAssetId = 99, Loop = true, Volume = 1f },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Spline,
                        ActiveByDefault = false,
                        Spline = new SplineConfig { Usage = SplineUsage.Patrol, ProgressParamKey = 100, SpeedParamKey = 101, Loop = true },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 2,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig { Target = AttachmentTarget.Parent },
                    },
                ],
            });

            Assert.That(definitions.TryGet(defId, out PerformerDefinition definition), Is.True);
            Entity performer = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9001, Entity.Null, definition);

            Assert.That(world.Has<PerfHasAttachment>(performer), Is.True);
            Assert.That(world.Has<PerfHasSound>(performer), Is.False);
            Assert.That(world.Has<PerfHasSpline>(performer), Is.False);

            var runtime = new PerformerRuntimeSystem(
                world,
                new PerformerCommandBuffer(8),
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var commands = new PerformerCommandBuffer(8);
            commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.ActivateBehavior,
                PerformerEntity = performer,
                TargetBehaviorSlot = 0,
            });
            commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.ActivateBehavior,
                PerformerEntity = performer,
                TargetBehaviorSlot = 1,
            });

            using var system = new PerformerRuntimeSystem(
                world,
                commands,
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            system.Update(0.016f);

            Assert.That(world.Has<PerfHasAttachment>(performer), Is.True);
            Assert.That(world.Has<PerfHasSound>(performer), Is.True);
            Assert.That(world.Has<PerfHasSpline>(performer), Is.True);

            var deactivateCommands = new PerformerCommandBuffer(8);
            deactivateCommands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DeactivateBehavior,
                PerformerEntity = performer,
                TargetBehaviorSlot = 0,
            });
            deactivateCommands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DeactivateBehavior,
                PerformerEntity = performer,
                TargetBehaviorSlot = 1,
            });
            deactivateCommands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DeactivateBehavior,
                PerformerEntity = performer,
                TargetBehaviorSlot = 2,
            });

            using var deactivateSystem = new PerformerRuntimeSystem(
                world,
                deactivateCommands,
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            deactivateSystem.Update(0.016f);

            Assert.That(world.Has<PerfHasAttachment>(performer), Is.False);
            Assert.That(world.Has<PerfHasSound>(performer), Is.False);
            Assert.That(world.Has<PerfHasSpline>(performer), Is.False);
        }

        [Test]
        public void Animator_ReadsBlackboardAndWritesRuntimeState()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register("worker.anim", new AnimatorControllerDefinition
            {
                DefaultStateIndex = 0,
                States =
                [
                    new AnimatorStateDefinition { PackedStateIndex = 1, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                    new AnimatorStateDefinition { PackedStateIndex = 2, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                ],
                Transitions =
                [
                    new AnimatorTransitionDefinition
                    {
                        FromStateIndex = 0,
                        ToStateIndex = 1,
                        ConditionKind = AnimatorConditionKind.FloatGreaterOrEqual,
                        ParameterIndex = 120,
                        Threshold = 0.5f,
                        DurationSeconds = 0f,
                    },
                ],
            });

            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.animator", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Animator,
                        ActiveByDefault = true,
                        Animator = new AnimatorConfig
                        {
                            AnimatorControllerId = controllerId,
                            StateParamKey = 121,
                            SpeedParamKey = 120,
                        },
                    },
                ],
            });

            var instances = new PerformerEntityRuntime(world);
            Entity performer = instances.Create(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7003, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, 120, ParamLane.Float, 1f, 0, default);
            var animatorStates = new PerformerAnimatorStateBuffer(2);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            system.Update(0.016f);

            Assert.That(animatorStates.GetPackedState(performer).GetPrimaryStateIndex(), Is.EqualTo(2));
            Assert.That(instances.ResolveInt(performer, 121), Is.EqualTo(1));
        }

        [Test]
        public void Attachment_UsesBoneTransformProviderAndBoneSpaceOffset()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create();

            var definition = new PerformerDefinition
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
                            Target = AttachmentTarget.Bone,
                            BoneId = 17,
                            Offset = new Vector3(0f, 0.5f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            };
            int defId = definitions.Register("behavior.attachment", definition);

            Entity parentPerformer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7100, Entity.Null, definition);
            Entity childPerformer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7101, parentPerformer, definition);
            world.Get<PerformerState>(childPerformer).BehaviorActiveMask = 1u;
            world.Get<PerformerState>(parentPerformer).StableId = 7100;

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer(),
                heightmap: null,
                boneTransformProvider: new StubBoneTransformProvider(
                    expectedStableId: 7100,
                    expectedBoneId: 17,
                    position: new Vector3(3f, 4f, 5f),
                    rotation: Quaternion.Identity,
                    scale: new Vector3(2f, 2f, 2f)));

            system.Update(0.016f);

            ref var child = ref world.Get<PerformerTransformSource>(childPerformer);
            ref var childPos = ref world.Get<PerformerWorldPosition>(childPerformer);
            ref var childScale = ref world.Get<PerformerWorldScale>(childPerformer);
            Assert.That(child.Value, Is.EqualTo(TransformSource.BoneAttached));
            Assert.That(childPos.Value, Is.EqualTo(new Vector3(3f, 4.5f, 5f)));
            Assert.That(childScale.Value, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void Attachment_TargetParent_UsesParentTransformWithoutBoneProvider()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create();

            var definition = new PerformerDefinition
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
                            Offset = new Vector3(0f, 2f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            };
            int defId = definitions.Register("behavior.attachment.parent", definition);

            Entity parentPerformer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7200, Entity.Null, definition);
            Entity childPerformer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7201, parentPerformer, definition);
            world.Get<PerformerState>(childPerformer).BehaviorActiveMask = 1u;
            ref var parentPos = ref world.Get<PerformerWorldPosition>(parentPerformer);
            parentPos.Value = new Vector3(10f, 4f, 6f);
            ref var parentRot = ref world.Get<PerformerWorldRotation>(parentPerformer);
            parentRot.Value = Quaternion.Identity;
            ref var parentScale = ref world.Get<PerformerWorldScale>(parentPerformer);
            parentScale.Value = new Vector3(3f, 3f, 3f);

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.016f);

            ref var childTransform = ref world.Get<PerformerTransformSource>(childPerformer);
            ref var childPos = ref world.Get<PerformerWorldPosition>(childPerformer);
            ref var childScale = ref world.Get<PerformerWorldScale>(childPerformer);
            Assert.That(childTransform.Value, Is.EqualTo(TransformSource.AttachedToParent));
            Assert.That(childPos.Value, Is.EqualTo(new Vector3(10f, 6f, 6f)));
            Assert.That(childScale.Value, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void EntityTransform_InheritsOwnerScaleAndAppliesLocalScale()
        {
            using var world = World.Create();
            Entity owner = world.Create(new VisualTransform
            {
                Position = new Vector3(2f, 3f, 4f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(2f, 3f, 4f),
            });

            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("behavior.entity_transform_scale", new PerformerDefinition
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
                            AssetId = 1,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = new Vector3(0.5f, 2f, 0.25f),
                        },
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7201, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.016f);

            ref var transformSource = ref world.Get<PerformerTransformSource>(performer);
            ref var worldScale = ref world.Get<PerformerWorldScale>(performer);
            ref var worldPos = ref world.Get<PerformerWorldPosition>(performer);
            Assert.That(transformSource.Value, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(worldScale.Value, Is.EqualTo(new Vector3(1f, 6f, 1f)));
            Assert.That(worldPos.Value, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        }

        [Test]
        public void Sound_EmitsPlayAndStopRequests()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream();
            var soundRequests = new SoundRequestBuffer();
            Entity owner = world.Create();

            int defId = definitions.Register("behavior.sound", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Sound,
                        ActiveByDefault = true,
                        Sound = new SoundConfig
                        {
                            SoundAssetId = 8101,
                            Loop = true,
                            Volume = 0.75f,
                        },
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7004, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var system = new PerformerBehaviorSystem(world, instances, definitions, events, soundRequests);
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));

            soundRequests.Clear();
            world.Get<PerformerState>(performer).BehaviorActiveMask = 0u;
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
        }

        [Test]
        public void Material_MapsSwapTableToMaterialParam()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity performer = instances.Create(1, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7005, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, 130, ParamLane.Float, 1f, 0, default);

            definitions.Register("behavior.material", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Material,
                        ActiveByDefault = true,
                        Material = new MaterialConfig
                        {
                            BaseMaterialId = 9001,
                            MaterialSwapParamKey = 130,
                            SwapTable =
                            [
                                new MaterialSwapEntry { ParamValue = 1f, MaterialId = 9002 },
                            ],
                        },
                    },
                ],
            });

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(performer, 130), Is.EqualTo(9002));
        }

        [Test]
        public void Spline_Patrol_AdvancesProgressAndSetsSplineDrivenTransform()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create();

            int defId = definitions.Register("behavior.spline", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Spline,
                        ActiveByDefault = true,
                        Spline = new SplineConfig
                        {
                            SplineAssetId = 10001,
                            Usage = SplineUsage.Patrol,
                            ProgressParamKey = 140,
                            SpeedParamKey = 141,
                            Loop = true,
                        },
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 7006, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, 141, ParamLane.Float, 2f, 0, default);

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.25f);

            ref var transformSource = ref world.Get<PerformerTransformSource>(performer);
            ref var worldPos = ref world.Get<PerformerWorldPosition>(performer);
            Assert.That(instances.ResolveFloat(performer, 140), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(transformSource.Value, Is.EqualTo(TransformSource.SplineDriven));
            Assert.That(worldPos.Value.X, Is.EqualTo(0.5f).Within(0.001f));
        }

        private sealed class StubBoneTransformProvider : IBoneTransformProvider
        {
            private readonly int _expectedStableId;
            private readonly int _expectedBoneId;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;

            public StubBoneTransformProvider(int expectedStableId, int expectedBoneId, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                _expectedStableId = expectedStableId;
                _expectedBoneId = expectedBoneId;
                _position = position;
                _rotation = rotation;
                _scale = scale;
            }

            public bool TryGetBoneWorldTransform(int performerStableId, int boneId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
            {
                position = _position;
                rotation = _rotation;
                scale = _scale;
                return performerStableId == _expectedStableId && boneId == _expectedBoneId;
            }
        }
    }
}
