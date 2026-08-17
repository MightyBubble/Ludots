using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Tests.TestCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using Arch.Core.Extensions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterBehaviorKindTests
    {
        [Test]
        public void BehaviorKindContract_ArchitectureExposesCoreKinds()
        {
            BehaviorKind[] values = (BehaviorKind[])Enum.GetValues(typeof(BehaviorKind));
            Assert.That(values.Length, Is.EqualTo(15), "BehaviorKind SSOT is the architecture enum.");
            Assert.That(values, Does.Contain(BehaviorKind.None));
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
            Assert.That(values, Does.Contain(BehaviorKind.WorldText));
            Assert.That(values, Does.Contain(BehaviorKind.SurfaceSource));
            Assert.That(values, Does.Contain(BehaviorKind.InstancedBatch));
            Assert.That(values, Does.Contain(BehaviorKind.Extension));
        }

        [Test]
        public void BehaviorKindContract_ArchitecturePreservesExplicitEnumValues()
        {
            Assert.That((byte)BehaviorKind.None, Is.EqualTo(0));
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
            Assert.That((byte)BehaviorKind.WorldText, Is.EqualTo(11));
            Assert.That((byte)BehaviorKind.SurfaceSource, Is.EqualTo(12));
            Assert.That((byte)BehaviorKind.InstancedBatch, Is.EqualTo(13));
            Assert.That((byte)BehaviorKind.Extension, Is.EqualTo(255));
        }

        [Test]
        public void ExtensionCommand_DispatchesRegisteredHandler()
        {
            _extensionCommandCalls = 0;
            using var world = World.Create();
            var commands = new PresenterCommandBuffer(8);
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var commandKinds = new PerformerCommandKindRegistry();
            int commandKindId = commandKinds.Register(
                "ExampleMod.MarkCommand",
                new PerformerCommandExtensionDescriptor(
                    PerformerCommandRouteStrategy.SingleRuntime,
                    CountExtensionCommand));

            Assert.That(commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.Extension,
                CommandKindId = commandKindId,
                RouteStrategy = PerformerCommandRouteStrategy.SingleRuntime,
            }), Is.True);

            using var system = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                new PresentationStableIdAllocator(),
                definitions,
                extensionCommands: commandKinds);

            system.Update(0.016f);

            Assert.That(_extensionCommandCalls, Is.EqualTo(1));
            Assert.That(commands.Count, Is.EqualTo(0));
        }

        [Test]
        public void ExtensionBehavior_DispatchesRegisteredHandler()
        {
            _extensionBehaviorCalls = 0;
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var behaviorKinds = new PerformerBehaviorKindRegistry();
            int behaviorKindId = behaviorKinds.Register(
                "ExampleMod.TickBehavior",
                new PerformerBehaviorExtensionDescriptor(
                    PerformerBehaviorExecutionLane.ContinuousTick,
                    CountExtensionBehavior));
            int defId = definitions.Register("behavior.extension", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Extension,
                        KindId = behaviorKindId,
                        ExtensionLane = PerformerBehaviorExecutionLane.ContinuousTick,
                        ActiveByDefault = true,
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7008, Entity.Null, definitions.Get(defId));
            world.Add(performer, new PresenterBootstrapPending());
            world.Get<PresenterState>(performer).BehaviorActiveMask = 1u;

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(),
                extensionBehaviors: behaviorKinds);

            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(_extensionBehaviorCalls, Is.EqualTo(1));
        }

        [Test]
        public void ExtensionCommand_ProgrammaticDefinitionRejectsModIdWithoutExtensionKind()
        {
            var definitions = new PresenterDefinitionRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() => definitions.Register(
                "command.invalid-extension-kind",
                new PresenterDefinition
                {
                    Rules =
                    [
                        new PresenterRule
                        {
                            Command = new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.SetParam,
                                CommandKindId = PerformerCommandKindRegistry.FirstModCommandKindId,
                            },
                        },
                    ],
                }));

            Assert.That(ex!.Message, Does.Contain("does not match builtin command kind"));
        }

        [Test]
        public void ExtensionBehavior_ProgrammaticDefinitionRejectsModIdWithoutExtensionKind()
        {
            var definitions = new PresenterDefinitionRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() => definitions.Register(
                "behavior.invalid-extension-kind",
                new PresenterDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.AssetBinding,
                            KindId = PerformerBehaviorKindRegistry.FirstModBehaviorKindId,
                        },
                    ],
                }));

            Assert.That(ex!.Message, Does.Contain("does not match builtin kind"));
        }

        [Test]
        public void AttributeBinding_MapsAttributeRatioAndThreshold()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 50f);
            Entity owner = world.Create(attributes);

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.attribute", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7001, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 100), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 101), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void OwnerAttributeChangeBuffer_OnlyUpdatesMatchingAttributeBindingBehaviors()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 25f);
            attributes.SetBase(8, 200f);
            attributes.SetCurrent(8, 100f);
            Entity owner = world.Create(attributes);

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.owner.attr.fastpath", new PresenterDefinition
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
                            TargetParamKey = 200,
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
                            TargetParamKey = 201,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 2,
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
                        SlotIndex = 3,
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

            Assert.That(definitions.TryGet(defId, out PresenterDefinition definition), Is.True);
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7103, Entity.Null, definition);
            world.Add(presenter, new PresenterBootstrapPending());

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveFloat(presenter, 200), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 201), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 210), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 211), Is.EqualTo(0.5f).Within(0.001f));

            ref AttributeBuffer updated = ref world.Get<AttributeBuffer>(owner);
            updated.SetCurrent(7, 80f);
            updated.SetCurrent(8, 150f);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, 8)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 200), Is.EqualTo(0.25f).Within(0.001f), "attribute 7 binding must stay untouched when only attribute 8 changed.");
            Assert.That(instances.ResolveFloat(presenter, 201), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 210), Is.EqualTo(0.25f).Within(0.001f), "attribute behavior for attr 7 must not be rescanned.");
            Assert.That(instances.ResolveFloat(presenter, 211), Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void AttributeBindingAndParamBindings_ResolveAttributeEntityColorAndFacingIntoBlackboard()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(7, 100f);
            attributes.SetCurrent(7, 25f);
            Entity owner = world.Create(
                attributes,
                new FacingDirection { AngleRad = MathF.PI * 0.5f },
                new Ludots.Core.Gameplay.Components.Team { Id = 2 });

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.bindings", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = 201,
                        Value = ValueRef.FromEntityColor(0),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = 202,
                        Value = ValueRef.FromEntityColor(1),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = 205,
                        Value = ValueRef.FromEntityColorVector(),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = 203,
                        Value = ValueRef.FromFacingRadians(),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = 204,
                        Value = ValueRef.FromFacingDegrees(),
                    },
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
                            TargetParamKey = 200,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7102, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 200), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 201), Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 202), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(instances.ResolveVector(presenter, 205, Vector4.Zero), Is.EqualTo(new Vector4(0.9f, 0.2f, 0.2f, 1f)));
            Assert.That(instances.ResolveFloat(presenter, 203), Is.EqualTo(MathF.PI * 0.5f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 204), Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void TagBinding_HandlesTagOffAndInvertLogic()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();

            int workingTagId = TagRegistry.Register("working");
            int defId = definitions.Register("behavior.tag", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7002, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 0b11u;

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(presenter, 110), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(presenter, 111), Is.EqualTo(1));

            world.Add(owner, default(GameplayTagContainer));
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(owner);
            tags.AddTag(workingTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, workingTagId, stateValue: 1)), Is.True);

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(presenter, 110), Is.EqualTo(1));
            Assert.That(instances.ResolveInt(presenter, 111), Is.EqualTo(0));
        }

        [Test]
        public void OwnerTagChangeBuffer_OnlyUpdatesMatchingTagBinding()
        {
            using var world = World.Create();
            int workingTagId = TagRegistry.Register("working.fastpath");
            int alertTagId = TagRegistry.Register("alert.fastpath");
            Entity owner = world.Create(default(GameplayTagContainer));

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.owner.tag.fastpath", new PresenterDefinition
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

            Assert.That(definitions.TryGet(defId, out PresenterDefinition definition), Is.True);
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7104, Entity.Null, definition);
            world.Add(presenter, new PresenterBootstrapPending());

            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                ownerChanges,
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(presenter, 310), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(presenter, 311), Is.EqualTo(0));

            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(owner);
            tags.AddTag(workingTagId);
            tags.AddTag(alertTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, alertTagId, stateValue: 1)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(presenter, 310), Is.EqualTo(0), "tag binding for unrelated tag must stay untouched.");
            Assert.That(instances.ResolveInt(presenter, 311), Is.EqualTo(1));

            tags.RemoveTag(alertTagId);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, alertTagId, stateValue: 0)), Is.True);

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(presenter, 310), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(presenter, 311), Is.EqualTo(0));
        }

        [Test]
        public void TickBehaviorMarkers_TrackActiveBehaviorMask()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.tick.markers", new PresenterDefinition
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

            Assert.That(definitions.TryGet(defId, out PresenterDefinition definition), Is.True);
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9001, Entity.Null, definition);

            Assert.That(world.Has<PerfHasAttachment>(presenter), Is.True);
            Assert.That(world.Has<PerfHasSound>(presenter), Is.False);
            Assert.That(world.Has<PerfHasSpline>(presenter), Is.False);

            var runtime = new PresenterRuntimeSystem(
                world,
                new PresenterCommandBuffer(8),
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var commands = new PresenterCommandBuffer(8);
            commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.ActivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 0,
            });
            commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.ActivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 1,
            });

            using var system = new PresenterRuntimeSystem(
                world,
                commands,
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            system.Update(0.016f);

            Assert.That(world.Has<PerfHasAttachment>(presenter), Is.True);
            Assert.That(world.Has<PerfHasSound>(presenter), Is.True);
            Assert.That(world.Has<PerfHasSpline>(presenter), Is.True);

            var deactivateCommands = new PresenterCommandBuffer(8);
            deactivateCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 0,
            });
            deactivateCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 1,
            });
            deactivateCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 2,
            });

            using var deactivateSystem = new PresenterRuntimeSystem(
                world,
                deactivateCommands,
                new PresentationEventStream(8),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            deactivateSystem.Update(0.016f);

            Assert.That(world.Has<PerfHasAttachment>(presenter), Is.False);
            Assert.That(world.Has<PerfHasSound>(presenter), Is.False);
            Assert.That(world.Has<PerfHasSpline>(presenter), Is.False);
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

            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.animator", new PresenterDefinition
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

            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7003, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SetParam(presenter, 120, ParamLane.Float, 1f, 0, default);
            var animatorStates = new PresenterAnimatorStateBuffer(2);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            system.Update(0.016f);

            Assert.That(animatorStates.GetPackedState(presenter).GetPrimaryStateIndex(), Is.EqualTo(2));
            Assert.That(instances.ResolveInt(presenter, 121), Is.EqualTo(1));
        }

        [Test]
        public void Attachment_UsesBoneTransformProviderAndBoneSpaceOffset()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create();

            var definition = new PresenterDefinition
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

            Entity parentPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7100, Entity.Null, definition);
            Entity childPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7101, parentPresenter, definition);
            world.Get<PresenterState>(childPresenter).BehaviorActiveMask = 1u;
            world.Get<PresenterState>(parentPresenter).StableId = 7100;

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(),
                heightmap: null,
                boneTransformProvider: new StubBoneTransformProvider(
                    expectedStableId: 7100,
                    expectedBoneId: 17,
                    position: new Vector3(3f, 4f, 5f),
                    rotation: Quaternion.Identity,
                    scale: new Vector3(2f, 2f, 2f)));

            system.Update(0.016f);

            ref var child = ref world.Get<PresenterTransformSource>(childPresenter);
            ref var childPos = ref world.Get<PresenterWorldPosition>(childPresenter);
            ref var childScale = ref world.Get<PresenterWorldScale>(childPresenter);
            Assert.That(child.Value, Is.EqualTo(TransformSource.BoneAttached));
            Assert.That(childPos.Value, Is.EqualTo(new Vector3(3f, 4.5f, 5f)));
            Assert.That(childScale.Value, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void Attachment_TargetParent_UsesParentTransformWithoutBoneProvider()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create();

            var definition = new PresenterDefinition
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

            Entity parentPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7200, Entity.Null, definition);
            Entity childPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7201, parentPresenter, definition);
            world.Get<PresenterState>(childPresenter).BehaviorActiveMask = 1u;
            ref var parentPos = ref world.Get<PresenterWorldPosition>(parentPresenter);
            parentPos.Value = new Vector3(10f, 4f, 6f);
            ref var parentRot = ref world.Get<PresenterWorldRotation>(parentPresenter);
            parentRot.Value = Quaternion.Identity;
            ref var parentScale = ref world.Get<PresenterWorldScale>(parentPresenter);
            parentScale.Value = new Vector3(3f, 3f, 3f);

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.016f);

            ref var childTransform = ref world.Get<PresenterTransformSource>(childPresenter);
            ref var childPos = ref world.Get<PresenterWorldPosition>(childPresenter);
            ref var childScale = ref world.Get<PresenterWorldScale>(childPresenter);
            Assert.That(childTransform.Value, Is.EqualTo(TransformSource.AttachedToParent));
            Assert.That(childPos.Value, Is.EqualTo(new Vector3(10f, 6f, 6f)));
            Assert.That(childScale.Value, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void Attachment_TargetParent_WorldHudChildUpdatesRetainedPositionWhenParentMoves()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var worldHud = new WorldHudBatchBuffer(8);
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity viewer = world.Create();
            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            UpsertPresenterKnowledge(projectionStore, viewer, owner);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };
            ClientLocalSeatTestBindings.BindSoleSeat(globals, viewer, 1);

            int parentDefId = definitions.Register("behavior.attachment.hud.parent", new PresenterDefinition
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
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            int hudDefId = definitions.Register("behavior.attachment.hud.child", new PresenterDefinition
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
                            AssetKind = AssetKind.WorldHud,
                            Mobility = VisualMobility.Movable,
                            LocalScale = new Vector3(64f, 8f, 1f),
                            MaterialParamKey = 100,
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
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
            });
            instances.BindDefinitions(definitions);

            Entity parent = instances.Create(parentDefId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9101, Entity.Null, default);
            Entity hud = instances.Create(hudDefId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9102, parent, default);
            world.Get<PresenterState>(parent).BehaviorActiveMask = 1u;
            world.Get<PresenterState>(hud).BehaviorActiveMask = 0b11u;
            world.Get<PresenterCullState>(parent).OwnerCullVisible = true;
            world.Get<PresenterCullState>(hud).OwnerCullVisible = true;
            world.Get<PresenterWorldPosition>(parent).Value = new Vector3(10f, 0f, 20f);
            world.Get<PresenterWorldScale>(parent).Value = Vector3.One;
            instances.SetParam(hud, 100, ParamLane.Float, 1f, 0, default);
            instances.SyncTickBehaviorMarkers(hud, definitions.Get(hudDefId), 0b11u);
            instances.SyncEmitWorkMarkers(hud, definitions.Get(hudDefId), 0b11u);

            using var behavior = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());
            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                globals,
                worldHudBuffer: worldHud);

            behavior.Update(0.016f);
            emit.Update(0.016f);

            int stableId = HudItemIdentity.ComposeStableId(9102, WorldHudItemKind.Bar, hudDefId);
            Assert.That(worldHud.TryGetByStableId(stableId, out WorldHudItem first), Is.True);
            Assert.That(first.WorldPosition, Is.EqualTo(new Vector3(10f, 2f, 20f)));

            world.Get<PresenterWorldPosition>(parent).Value = new Vector3(30f, 0f, 40f);
            behavior.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(worldHud.TryGetByStableId(stableId, out WorldHudItem moved), Is.True);
            Assert.That(moved.WorldPosition, Is.EqualTo(new Vector3(30f, 2f, 40f)));
        }

        [Test]
        public void Attachment_TargetParent_WorldHudChildUsesOwnerPayloadTransformSync()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new PresentationOwnerHasPresenterPayload());

            int hudDefId = definitions.Register("behavior.attachment.ownerpayload.hud", new PresenterDefinition
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
                            AssetKind = AssetKind.WorldHud,
                            Mobility = VisualMobility.Movable,
                            LocalScale = new Vector3(64f, 8f, 1f),
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
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
            });
            int parentDefId = definitions.Register("behavior.attachment.ownerpayload.parent", new PresenterDefinition
            {
                Children = [new ChildPresenterRef { DefinitionId = hudDefId, ScopeTag = 1 }],
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
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);

            Span<Entity> created = stackalloc Entity[1];
            int count = instances.CreateEntityAnchoredRootBatch(
                definitions,
                parentDefId,
                new Entity[] { owner },
                new[] { 1 },
                new[] { 9101 },
                new[] { world.Get<VisualTransform>(owner) },
                new[] { world.Get<CullState>(owner) },
                definitions.Get(parentDefId),
                created,
                allocateStableId: () => 9102);

            Assert.That(count, Is.EqualTo(1));
            Entity parent = created[0];
            Entity hud = world.Get<PresenterChildren>(parent).Get(0);
            ref readonly PresentationOwnerHasPresenterPayload payload = ref world.Get<PresentationOwnerHasPresenterPayload>(owner);
            Assert.That(payload.Count, Is.EqualTo(2));
            Assert.That(payload.RootCount, Is.EqualTo(1));
            Assert.That(payload.SingleRootPresenter, Is.EqualTo(parent));
            Assert.That(payload.SingleRootTransformSync, Is.EqualTo(1));
            Assert.That(world.Has<PerfOwnerPayloadTransformSync>(parent), Is.True);
            Assert.That(world.Has<PerfHasAttachmentTick>(hud), Is.False);
            Assert.That(world.Has<PerfOwnerPayloadAttachedTransformSync>(hud), Is.True);

            using var sync = new PresenterEntityTransformSyncSystem(world, instances, definitions);
            world.Get<VisualTransform>(owner).Position = new Vector3(30f, 0f, 40f);
            world.Get<WorldPositionCm>(owner).Value = WorldPositionCm.FromCm(3000, 4000).Value;
            sync.Update(0.016f);

            Assert.That(world.Get<PresenterWorldPosition>(parent).Value, Is.EqualTo(new Vector3(30f, 0f, 40f)));
            Assert.That(world.Get<PresenterWorldPosition>(hud).Value, Is.EqualTo(new Vector3(30f, 2f, 40f)));
            Assert.That(world.Get<PresenterTransformSource>(hud).Value, Is.EqualTo(TransformSource.AttachedToParent));
        }

        [Test]
        public void EntityAnchoredRootBatch_AppliesPerRootParamOverrides_BeforeChildrenResolveParentParams()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int slopeParamKey = PresenterParamKeyRegistry.Register("test.static.wall.slope");

            Entity ownerA = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity ownerB = world.Create(
                WorldPositionCm.FromCm(3000, 4000),
                new VisualTransform
                {
                    Position = new Vector3(30f, 0f, 40f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            int childDefId = definitions.Register("batch.param.child", new PresenterDefinition
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
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Static,
                            AssetIdParamKey = -1,
                            MaterialCustomData = new MaterialCustomDataBinding
                            {
                                Slots =
                                [
                                    new MaterialCustomDataSlotBinding
                                    {
                                        Slot = 0,
                                        Lane = MaterialCustomDataLane.Float,
                                        ParamKey = slopeParamKey,
                                    },
                                ],
                            },
                        },
                    },
                ],
            });

            int rootDefId = definitions.Register("batch.param.root", new PresenterDefinition
            {
                ParamDefaults =
                [
                    new ParamDefault
                    {
                        ParamKey = slopeParamKey,
                        Lane = ParamLane.Float,
                        FloatValue = 0f,
                    },
                ],
                Children = [new ChildPresenterRef { DefinitionId = childDefId, ScopeTag = 1 }],
            });
            instances.BindDefinitions(definitions);

            Entity[] owners = [ownerA, ownerB];
            int[] scopes = [11, 12];
            int[] stableIds = [101, 102];
            VisualTransform[] transforms = [world.Get<VisualTransform>(ownerA), world.Get<VisualTransform>(ownerB)];
            CullState[] culls = [world.Get<CullState>(ownerA), world.Get<CullState>(ownerB)];
            ParamDefault[][] overrides =
            [
                [new ParamDefault { ParamKey = slopeParamKey, Lane = ParamLane.Float, FloatValue = -0.25f }],
                [new ParamDefault { ParamKey = slopeParamKey, Lane = ParamLane.Float, FloatValue = 0.75f }],
            ];
            Entity[] created = new Entity[2];

            int count = instances.CreateEntityAnchoredRootBatch(
                definitions,
                rootDefId,
                owners,
                scopes,
                stableIds,
                transforms,
                culls,
                definitions.Get(rootDefId),
                created,
                rootParamOverrides: overrides);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(instances.TryResolveFloat(created[0], slopeParamKey, out float rootAValue), Is.True);
            Assert.That(instances.TryResolveFloat(created[1], slopeParamKey, out float rootBValue), Is.True);
            Assert.That(rootAValue, Is.EqualTo(-0.25f).Within(0.0001f));
            Assert.That(rootBValue, Is.EqualTo(0.75f).Within(0.0001f));

            Entity childA = world.Get<PresenterChildren>(created[0]).Get(0);
            Entity childB = world.Get<PresenterChildren>(created[1]).Get(0);
            Assert.That(instances.TryResolveFloat(childA, slopeParamKey, out float childAValue), Is.True);
            Assert.That(instances.TryResolveFloat(childB, slopeParamKey, out float childBValue), Is.True);
            Assert.That(childAValue, Is.EqualTo(-0.25f).Within(0.0001f));
            Assert.That(childBValue, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void Attachment_TargetParent_ChildFollowsEntityAnchoredParentTransformSync()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create(new VisualTransform
            {
                Position = new Vector3(10f, 0f, 20f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            });

            int parentDefId = definitions.Register("behavior.attachment.transformsync.parent", new PresenterDefinition
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
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });
            int markerDefId = definitions.Register("behavior.attachment.transformsync.marker", new PresenterDefinition
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
                            AssetId = 2,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 0.5f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);

            Entity parent = instances.CreateHierarchy(
                definitions,
                parentDefId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                stableId: 9101,
                Entity.Null,
                definitions.Get(parentDefId));
            Entity marker = instances.CreateHierarchy(
                definitions,
                markerDefId,
                owner,
                scopeId: 2,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                stableId: 9102,
                parent,
                definitions.Get(markerDefId));

            Assert.That(world.Has<PerfTransformSyncTick>(parent), Is.True);
            Assert.That(world.Has<PerfOwnerPayloadTransformSync>(parent), Is.False);
            world.Get<VisualTransform>(owner).Position = new Vector3(30f, 0f, 40f);

            using var sync = new PresenterEntityTransformSyncSystem(world, instances, definitions);
            sync.Update(0.016f);

            Assert.That(world.Get<PresenterWorldPosition>(parent).Value, Is.EqualTo(new Vector3(30f, 0f, 40f)));
            Assert.That(world.Get<PresenterWorldPosition>(marker).Value, Is.EqualTo(new Vector3(30f, 0.5f, 40f)));
            Assert.That(world.Get<PresenterTransformSource>(marker).Value, Is.EqualTo(TransformSource.AttachedToParent));
        }

        [Test]
        public void Attachment_TargetParent_DynamicScopedMarkerReemitsMovedMesh()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            Entity owner = world.Create(new VisualTransform
            {
                Position = new Vector3(10f, 0f, 20f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            });

            int parentDefId = definitions.Register("behavior.attachment.dynamic.parent", new PresenterDefinition
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
                            AssetKind = AssetKind.SkinnedMesh,
                            AssetId = 1,
                            RenderPath = VisualRenderPath.GpuSkinnedInstance,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
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
                            UpdatePolicy = GroundingUpdatePolicy.EveryFrame,
                        },
                    },
                ],
            });
            int markerDefId = definitions.Register("behavior.attachment.dynamic.marker", new PresenterDefinition
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
                            AssetId = 2,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Parent,
                            Offset = new Vector3(0f, 0.5f, 0f),
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            });
            using var runtimeSystem = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions);
            using var transformSync = new PresenterEntityTransformSyncSystem(world, instances, definitions);
            using var behavior = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());
            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>());

            Assert.That(commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = parentDefId,
                ScopeTag = 100,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            }), Is.True);
            runtimeSystem.Update(0.016f);
            behavior.Update(0.016f);
            transformSync.Update(0.016f);

            Assert.That(world.TryGet(owner, out PresentationOwnerHasPresenterPayload payload), Is.True);
            Entity parent = payload.SingleRootPresenter;
            Assert.That(commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = markerDefId,
                ScopeTag = 200,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
                ParentEntity = parent,
            }), Is.True);
            runtimeSystem.Update(0.016f);
            behavior.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(instances.TryGetActiveScopedInstance(
                markerDefId,
                owner,
                200,
                PresentationAnchorKind.Entity,
                default,
                out Entity marker), Is.True);
            Assert.That(world.Get<PresenterParent>(marker).Parent, Is.EqualTo(parent));

            requests.Clear();
            world.Get<VisualTransform>(owner).Position = new Vector3(30f, 0f, 40f);
            transformSync.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(world.Get<PresenterWorldPosition>(marker).Value, Is.EqualTo(new Vector3(30f, 0.5f, 40f)));
            Assert.That(TryFindVisualProxyRequest(requests, owner, markerDefId, out PresentationVisualProxy proxy), Is.True);
            Assert.That(proxy.Position, Is.EqualTo(new Vector3(30f, 0.5f, 40f)));
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

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior.entity_transform_scale", new PresenterDefinition
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
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7201, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.016f);

            ref var transformSource = ref world.Get<PresenterTransformSource>(presenter);
            ref var worldScale = ref world.Get<PresenterWorldScale>(presenter);
            ref var worldPos = ref world.Get<PresenterWorldPosition>(presenter);
            Assert.That(transformSource.Value, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(worldScale.Value, Is.EqualTo(new Vector3(1f, 6f, 1f)));
            Assert.That(worldPos.Value, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        }

        [Test]
        public void Sound_EmitsPlayAndStopRequests()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var soundRequests = new SoundRequestBuffer();
            Entity owner = world.Create();

            int defId = definitions.Register("behavior.sound", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7004, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var system = new PresenterBehaviorSystem(world, instances, definitions, events, new PresentationOwnerChangeBuffer(8), soundRequests);
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));

            soundRequests.Clear();
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 0u;
            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
        }

        [Test]
        public void Material_MapsSwapTableToMaterialParam()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();

            int defId = definitions.Register("behavior.material", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7005, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SetParam(presenter, 130, ParamLane.Float, 1f, 0, default);

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.016f);
            Assert.That(instances.ResolveInt(presenter, 130), Is.EqualTo(9002));
        }

        [Test]
        public void Material_ThrowsWhenSwapParamDoesNotResolve()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();

            int defId = definitions.Register("behavior.material.missing_source", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7006, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f))!;
            Assert.That(ex.Message, Does.Contain("materialSwapParamKey 130 did not resolve"));
        }

        [Test]
        public void Material_ThrowsWhenSwapParamHasNoMatchingTableEntry()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();

            int defId = definitions.Register("behavior.material.missing_entry", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, world.Create(), 0, PresentationAnchorKind.Entity, Vector3.Zero, 7007, Entity.Null, default);
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SetParam(presenter, 130, ParamLane.Float, 2f, 0, default);

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f))!;
            Assert.That(ex.Message, Does.Contain("materialSwapParamKey 130 resolved value 2"));
            Assert.That(ex.Message, Does.Contain("no matching swapTable entry"));
        }

        [Test]
        public void Spline_Patrol_AdvancesProgressAndSetsSplineDrivenTransform()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create();

            int defId = definitions.Register("behavior.spline", new PresenterDefinition
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

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 7006, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SetParam(presenter, 140, ParamLane.Float, 0f, 0, default);
            instances.SetParam(presenter, 141, ParamLane.Float, 2f, 0, default);

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.25f);

            ref var transformSource = ref world.Get<PresenterTransformSource>(presenter);
            ref var worldPos = ref world.Get<PresenterWorldPosition>(presenter);
            Assert.That(instances.ResolveFloat(presenter, 140), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(transformSource.Value, Is.EqualTo(TransformSource.SplineDriven));
            Assert.That(worldPos.Value.X, Is.EqualTo(0.5f).Within(0.001f));
        }

        private static int _extensionCommandCalls;
        private static int _extensionBehaviorCalls;

        private static void CountExtensionCommand(in PerformerCommandExecutionContext context)
        {
            Assert.That(context.Command.CommandKindId, Is.GreaterThanOrEqualTo(PerformerCommandKindRegistry.FirstModCommandKindId));
            _extensionCommandCalls++;
        }

        private static void CountExtensionBehavior(in PerformerBehaviorExecutionContext context)
        {
            Assert.That(context.Behavior.KindId, Is.GreaterThanOrEqualTo(PerformerBehaviorKindRegistry.FirstModBehaviorKindId));
            Assert.That(context.Behavior.Lane, Is.EqualTo(PerformerBehaviorExecutionLane.ContinuousTick));
            _extensionBehaviorCalls++;
        }

        private static bool TryFindVisualProxyRequest(
            PresentationRequestBuffer requests,
            Entity owner,
            int definitionId,
            out PresentationVisualProxy proxy)
        {
            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationRequest request = ref span[i];
                if (request.Kind != PresentationRequestKind.VisualProxy ||
                    request.Owner != owner ||
                    request.VisualProxy.TemplateId != definitionId)
                {
                    continue;
                }

                proxy = request.VisualProxy;
                return true;
            }

            proxy = default;
            return false;
        }

        private static void UpsertPresenterKnowledge(
            KnowledgeProjectionStore store,
            Entity viewer,
            Entity target,
            KnowledgeIdMask256 attributeMask = default)
        {
            store.Upsert(
                viewer,
                target,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    attributeMask,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));
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

            public bool TryGetBoneWorldTransform(int presenterStableId, int boneId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
            {
                position = _position;
                rotation = _rotation;
                scale = _scale;
                return presenterStableId == _expectedStableId && boneId == _expectedBoneId;
            }
        }
    }
}
