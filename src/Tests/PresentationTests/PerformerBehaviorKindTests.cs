using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerBehaviorKindTests
    {
        [Test]
        public void BehaviorKindContract_ArchitectureExposesEightKinds()
        {
            BehaviorKind[] values = (BehaviorKind[])Enum.GetValues(typeof(BehaviorKind));
            Assert.That(values.Length, Is.EqualTo(8), "BehaviorKind SSOT is the architecture enum, which defines 8 kinds.");
            Assert.That(values, Does.Contain(BehaviorKind.AssetBinding));
            Assert.That(values, Does.Contain(BehaviorKind.AttributeBinding));
            Assert.That(values, Does.Contain(BehaviorKind.TagBinding));
            Assert.That(values, Does.Contain(BehaviorKind.Animator));
            Assert.That(values, Does.Contain(BehaviorKind.Attachment));
            Assert.That(values, Does.Contain(BehaviorKind.Sound));
            Assert.That(values, Does.Contain(BehaviorKind.Material));
            Assert.That(values, Does.Contain(BehaviorKind.Spline));
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
        }

        [Test]
        public void AttributeBinding_MapsAttributeRatioAndThreshold()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 50f);
            Entity owner = world.Create(attributes);

            var instances = new PerformerInstanceBuffer(capacity: 2);
            var definitions = new PerformerDefinitionRegistry();
            Assert.That(instances.TryAllocate(1, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7001, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;

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

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(handle, 100), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(handle, 101), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TagBinding_HandlesTagOffAndInvertLogic()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PerformerInstanceBuffer(capacity: 2);
            var definitions = new PerformerDefinitionRegistry();
            Assert.That(instances.TryAllocate(1, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7002, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 0b11u;

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

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(handle, 110), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, 111), Is.EqualTo(1));

            world.Add(owner, default(GameplayTagContainer));
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(owner);
            tags.AddTag(workingTagId);

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(handle, 110), Is.EqualTo(1));
            Assert.That(instances.ResolveInt(handle, 111), Is.EqualTo(0));
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

            var instances = new PerformerInstanceBuffer(capacity: 2);
            Assert.That(instances.TryAllocate(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7003, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;
            instances.SetParam(handle, 120, ParamLane.Float, 1f, 0, default);
            var animatorStates = new PerformerAnimatorStateBuffer(2);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            system.Update(0.016f);

            Assert.That(animatorStates.GetPackedState(handle).GetPrimaryStateIndex(), Is.EqualTo(2));
            Assert.That(instances.ResolveInt(handle, 121), Is.EqualTo(1));
        }

        [Test]
        public void Attachment_UsesBoneTransformProviderAndBoneSpaceOffset()
        {
            using var world = World.Create();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            Entity owner = world.Create();

            int defId = definitions.Register("behavior.attachment", new PerformerDefinition
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
                            BoneId = 17,
                            Offset = new Vector3(0f, 0.5f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            });

            Assert.That(instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7100, -1, out int parentHandle), Is.True);
            Assert.That(instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7101, parentHandle, out int childHandle), Is.True);
            instances.Get(childHandle).BehaviorActiveMask = 1u;
            instances.Get(parentHandle).StableId = 7100;

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

            ref PerformerInstance child = ref instances.Get(childHandle);
            Assert.That(child.TransformSource, Is.EqualTo(TransformSource.BoneAttached));
            Assert.That(child.WorldPosition, Is.EqualTo(new Vector3(3f, 4.5f, 5f)));
            Assert.That(child.WorldScale, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void Sound_EmitsPlayAndStopRequests()
        {
            using var world = World.Create();
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7004, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;

            using var system = new PerformerBehaviorSystem(world, instances, definitions, events, soundRequests);
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));

            soundRequests.Clear();
            instances.Get(handle).BehaviorActiveMask = 0u;
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
        }

        [Test]
        public void Material_MapsSwapTableToMaterialParam()
        {
            using var world = World.Create();
            var instances = new PerformerInstanceBuffer(capacity: 2);
            var definitions = new PerformerDefinitionRegistry();
            Assert.That(instances.TryAllocate(1, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7005, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;
            instances.SetParam(handle, 130, ParamLane.Float, 1f, 0, default);

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
            Assert.That(instances.ResolveInt(handle, 130), Is.EqualTo(9002));
        }

        [Test]
        public void Spline_Patrol_AdvancesProgressAndSetsSplineDrivenTransform()
        {
            using var world = World.Create();
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 7006, -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;
            instances.SetParam(handle, 141, ParamLane.Float, 2f, 0, default);

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                new SoundRequestBuffer());

            system.Update(0.25f);

            ref PerformerInstance instance = ref instances.Get(handle);
            Assert.That(instances.ResolveFloat(handle, 140), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instance.TransformSource, Is.EqualTo(TransformSource.SplineDriven));
            Assert.That(instance.WorldPosition.X, Is.EqualTo(0.5f).Within(0.001f));
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
