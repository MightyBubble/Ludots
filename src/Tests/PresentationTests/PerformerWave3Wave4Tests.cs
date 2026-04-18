using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerWave3Wave4Tests
    {
        [Test]
        public void PerformerRuntimeSystem_DestroyRoot_EmitsDestroyedForWholeSubtree()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream();
            var instances = new PerformerInstanceBuffer(capacity: 8);
            var stableIds = new PresentationStableIdAllocator();
            var definitions = new PerformerDefinitionRegistry();
            int rootDef = definitions.Register("root", new PerformerDefinition { Behaviors = Array.Empty<BehaviorSlot>() });
            int childDef = definitions.Register("child", new PerformerDefinition { Behaviors = Array.Empty<BehaviorSlot>() });
            Entity owner = world.Create();

            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                stableIds,
                definitions);

            var createRoot = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = rootDef,
                Source = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createRoot), Is.True);
            runtime.Update(0.016f);
            int rootHandle = events.GetSpan()[0].PayloadA;
            events.Clear();

            var createChild = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = childDef,
                ParentHandle = rootHandle,
                Source = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createChild), Is.True);
            runtime.Update(0.016f);
            int childHandle = events.GetSpan()[0].PayloadA;
            events.Clear();

            var destroyRoot = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformer,
                PerformerHandle = rootHandle,
            };
            Assert.That(commands.TryAdd(in destroyRoot), Is.True);
            runtime.Update(0.016f);

            ReadOnlySpan<PresentationEvent> destroyed = events.GetSpan();
            Assert.That(destroyed.Length, Is.EqualTo(2));
            Assert.That(destroyed[0].Kind, Is.EqualTo(PresentationEventKind.PerformerDestroyed));
            Assert.That(destroyed[0].PayloadA, Is.EqualTo(childHandle));
            Assert.That(destroyed[1].PayloadA, Is.EqualTo(rootHandle));
            Assert.That(instances.IsActive(rootHandle), Is.False);
            Assert.That(instances.IsActive(childHandle), Is.False);
        }

        [Test]
        public void PerformerRuntimeSystem_CreateWithInactiveParent_Throws()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("child", new PerformerDefinition());
            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                new PresentationEventStream(),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                new PerformerInstanceBuffer(capacity: 4),
                new PresentationStableIdAllocator(),
                definitions);

            var createWithInactiveParent = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ParentHandle = 2,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createWithInactiveParent), Is.True);

            Assert.That(
                () => runtime.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("inactive parentHandle"));
        }

        [Test]
        public void PerformerRuntimeSystem_ActivateDeactivateAndSetParam_UpdateBehaviorMaskAndBlackboard()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("actor", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot { SlotIndex = 0, Kind = BehaviorKind.Sound, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 1, Kind = BehaviorKind.Material, ActiveByDefault = false },
                ],
            });

            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                new PresentationEventStream(),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var create = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in create), Is.True);
            runtime.Update(0.016f);

            int handle = 0;
            Assert.That(instances.IsActive(handle), Is.True);
            Assert.That(instances.Get(handle).BehaviorActiveMask, Is.EqualTo(1u));

            var activate = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.ActivateBehavior,
                PerformerHandle = handle,
                TargetBehaviorSlot = 1,
            };
            Assert.That(commands.TryAdd(in activate), Is.True);
            var setParam = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerHandle = handle,
                ParamKey = 55,
                ParamLane = ParamLane.Int,
                IntValue = 7,
            };
            Assert.That(commands.TryAdd(in setParam), Is.True);
            var deactivate = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DeactivateBehavior,
                PerformerHandle = handle,
                TargetBehaviorSlot = 0,
            };
            Assert.That(commands.TryAdd(in deactivate), Is.True);

            runtime.Update(0.016f);

            Assert.That(instances.Get(handle).BehaviorActiveMask, Is.EqualTo(1u << 1));
            Assert.That(instances.ResolveInt(handle, 55), Is.EqualTo(7));
        }

        [Test]
        public void PerformerRuntimeSystem_DestroyPerformerScope_RejectsNonPositiveScopeTags()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("scoped", new PerformerDefinition());

            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var create = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 42,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in create), Is.True);
            runtime.Update(0.016f);
            events.Clear();

            var createUnscoped = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 0,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createUnscoped), Is.True);
            runtime.Update(0.016f);
            events.Clear();

            var destroyWithZeroScope = new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformerScope,
                PerformerDefinitionId = 42,
                ScopeTag = 0,
            };
            Assert.That(commands.TryAdd(in destroyWithZeroScope), Is.True);

            Assert.That(
                () => runtime.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("positive scopeTag"));
            Assert.That(events.Count, Is.EqualTo(0));
        }

        [Test]
        public void PerformerRuleSystem_GlobalRegionChanged_BroadcastsToMatchingDefinitionInstances()
        {
            using var world = World.Create();
            var events = new PresentationEventStream();
            var commands = new PerformerCommandBuffer();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("region_actor", new PerformerDefinition
            {
                Rules =
                [
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.SetParam,
                            ParamKey = 300,
                            ParamLane = ParamLane.Int,
                            ValueSource = PerformerCommandValueSource.EventKeyId,
                        },
                    },
                ],
            });

            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            Entity otherOwner = world.Create();
            Assert.That(instances.TryAllocate(defId, ownerA, scopeId: 1, out int handleA), Is.True);
            Assert.That(instances.TryAllocate(defId, ownerB, scopeId: 1, out int handleB), Is.True);
            Assert.That(instances.TryAllocate(defId + 100, otherOwner, scopeId: 1, out _), Is.True);

            using var system = new PerformerRuleSystem(
                world,
                events,
                commands,
                definitions,
                instances,
                new GraphProgramRegistry(),
                new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null),
                new Dictionary<string, object>());

            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GlobalRegionChanged,
                KeyId = 42,
                Source = Entity.Null,
                Target = Entity.Null,
            }), Is.True);

            system.Update(0.016f);

            ReadOnlySpan<PerformerCommand> emitted = commands.GetSpan();
            Assert.That(emitted.Length, Is.EqualTo(2));
            Assert.That(emitted[0].PerformerHandle, Is.EqualTo(handleA));
            Assert.That(emitted[0].IntValue, Is.EqualTo(42));
            Assert.That(emitted[0].ValueSource, Is.EqualTo(PerformerCommandValueSource.Fixed));
            Assert.That(emitted[1].PerformerHandle, Is.EqualTo(handleB));
            Assert.That(emitted[1].IntValue, Is.EqualTo(42));
            Assert.That(events.Count, Is.EqualTo(0));
        }

        [Test]
        public void PerformerRuleSystem_ValueSourceEventKeyId_WritesFloatAndIntBlackboardLanes()
        {
            using var world = World.Create();
            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("region_params", new PerformerDefinition
            {
                Rules =
                [
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.SetParam,
                            ParamKey = 300,
                            ParamLane = ParamLane.Int,
                            ValueSource = PerformerCommandValueSource.EventKeyId,
                        },
                    },
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.SetParam,
                            ParamKey = 301,
                            ParamLane = ParamLane.Float,
                            ValueSource = PerformerCommandValueSource.EventKeyId,
                        },
                    },
                ],
            });
            Entity owner = world.Create();
            Assert.That(instances.TryAllocate(defId, owner, scopeId: 1, out int handle), Is.True);

            using var rules = new PerformerRuleSystem(
                world,
                events,
                commands,
                definitions,
                instances,
                new GraphProgramRegistry(),
                new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null),
                new Dictionary<string, object>());
            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GlobalRegionChanged,
                KeyId = 17,
                Source = Entity.Null,
                Target = Entity.Null,
            }), Is.True);

            rules.Update(0.016f);
            runtime.Update(0.016f);

            Assert.That(instances.ResolveInt(handle, 300, -1), Is.EqualTo(17));
            Assert.That(instances.ResolveFloat(handle, 301, -1f), Is.EqualTo(17f).Within(0.001f));
        }

        [Test]
        public void PerformerBehaviorSystem_AttributeTagMaterialAndSound_WriteBlackboardAndRequests()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(3, 100f);
            attributes.SetCurrent(3, 25f);
            var tags = default(GameplayTagContainer);
            tags.AddTag(5);
            Entity owner = world.Create(attributes, tags);

            var instances = new PerformerInstanceBuffer(capacity: 4);
            Assert.That(instances.TryAllocate(1, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 700, parentHandle: -1, out int handle), Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 0b1111u;

            var definitions = new PerformerDefinitionRegistry();
            definitions.Register("behavior", new PerformerDefinition
            {
                Id = 1,
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 3,
                            TargetParamKey = 100,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping { Threshold = 0.5f, OutputParamKey = 101, OutputValue = 2f }
                            ],
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig { TagId = 5, TargetParamKey = 102 },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 2,
                        Kind = BehaviorKind.Material,
                        ActiveByDefault = true,
                        Material = new MaterialConfig
                        {
                            BaseMaterialId = 20,
                            MaterialSwapParamKey = 101,
                            SwapTable =
                            [
                                new MaterialSwapEntry { ParamValue = 2f, MaterialId = 99 }
                            ],
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 3,
                        Kind = BehaviorKind.Sound,
                        ActiveByDefault = true,
                        Sound = new SoundConfig { SoundAssetId = 77, Loop = true, Volume = 0.5f },
                    },
                ],
            });

            var soundRequests = new SoundRequestBuffer();
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                soundRequests);

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(handle, 100), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(handle, 101), Is.EqualTo(2f).Within(0.001f));
            Assert.That(instances.ResolveInt(handle, 101), Is.EqualTo(99));
            Assert.That(instances.ResolveInt(handle, 102), Is.EqualTo(1));
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(77));
        }

        [Test]
        public void PerformerBehaviorSystem_TagBinding_WritesZeroWhenTagMissing_AndInvertsWhenConfigured()
        {
            using var world = World.Create();
            var events = new PresentationEventStream();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            Entity owner = world.Create();
            Assert.That(instances.TryAllocate(1, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 701, parentHandle: -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 0b11u;

            var definitions = new PerformerDefinitionRegistry();
            definitions.Register("tags", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig { TagId = 9, TargetParamKey = 201 },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.TagBinding,
                        ActiveByDefault = true,
                        TagBinding = new TagBindingConfig { TagId = 9, TargetParamKey = 202, InvertLogic = true },
                    },
                ],
            });

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(handle, 201), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, 202), Is.EqualTo(1));
        }

        [Test]
        public void PerformerBehaviorSystem_SoundStop_EmitsWhenBehaviorBecomesInactive()
        {
            using var world = World.Create();
            var events = new PresentationEventStream();
            var soundRequests = new SoundRequestBuffer();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            Entity owner = world.Create();
            Assert.That(instances.TryAllocate(1, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 777, parentHandle: -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;

            var definitions = new PerformerDefinitionRegistry();
            definitions.Register("sound_actor", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Sound,
                        ActiveByDefault = true,
                        Sound = new SoundConfig { SoundAssetId = 88, Loop = true, Volume = 1f },
                    },
                ],
            });

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                soundRequests);

            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));

            soundRequests.Clear();
            instances.Get(handle).BehaviorActiveMask = 0u;
            system.Update(0.016f);

            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(88));
        }

        [Test]
        public void PerformerBehaviorSystem_SoundStop_EmitsWhenPerformerDestroyed()
        {
            using var world = World.Create();
            var events = new PresentationEventStream();
            var soundRequests = new SoundRequestBuffer();
            var instances = new PerformerInstanceBuffer(capacity: 4);
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("sound_actor", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Sound,
                        ActiveByDefault = true,
                        Sound = new SoundConfig { SoundAssetId = 89, Loop = true, Volume = 1f },
                    },
                ],
            });

            Entity owner = world.Create();
            Assert.That(instances.TryAllocate(defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 778, parentHandle: -1, out int handle), Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                soundRequests);

            system.Update(0.016f);
            soundRequests.Clear();
            Assert.That(instances.Release(handle), Is.True);
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.PerformerDestroyed,
                KeyId = defId,
                Source = owner,
                Target = owner,
                PayloadA = handle,
                Magnitude = 778,
            }), Is.True);

            system.Update(0.016f);

            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(89));
        }

        [Test]
        public void AnimatorRuntimeSystem_ReadsBlackboardTrigger_ConsumesIt_AndWritesStateParam()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "hero.attack",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 5, DurationSeconds = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 9, DurationSeconds = 0.4f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 12,
                            ConsumeTrigger = true,
                        },
                    ],
                });

            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register("animated", new PerformerDefinition
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
                            StateParamKey = 20,
                            SpeedParamKey = -1,
                        },
                    },
                ],
            });
            var instances = new PerformerInstanceBuffer(capacity: 4);
            Assert.That(instances.TryAllocate(defId, world.Create(), 0, out int handle), Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instances.SetParam(handle, 12, ParamLane.Int, 0f, 1, default);

            var animatorStates = new PerformerAnimatorStateBuffer(4);
            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);

            system.Update(0.1f);

            Assert.That(instances.ResolveInt(handle, 12), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, 20), Is.EqualTo(1));
            Assert.That(animatorStates.GetPackedState(handle).GetPrimaryStateIndex(), Is.EqualTo(9));
        }

        [Test]
        public void PerformerEmitSystem_AssetBinding_EmitsVisualSoundSplineHudAndText()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PerformerInstanceBuffer(capacity: 16);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var soundRequests = new SoundRequestBuffer();
            var animatorStates = new PerformerAnimatorStateBuffer(16);

            int meshDef = RegisterAssetDefinition(definitions, "mesh", AssetKind.Mesh, assetId: 10, materialId: 20, slot: 0);
            int skinnedDef = RegisterAssetDefinition(definitions, "skinned", AssetKind.SkinnedMesh, assetId: 11, materialId: 21, slot: 0, renderPath: VisualRenderPath.SkinnedMesh);
            int soundDef = RegisterAssetDefinition(definitions, "sound", AssetKind.Sound, assetId: 30, materialId: 0, slot: 0);
            int splineDef = RegisterAssetDefinition(definitions, "spline", AssetKind.Spline, assetId: 40, materialId: 0, slot: 0);
            int hudDef = RegisterAssetDefinition(definitions, "hud", AssetKind.WorldHud, assetId: 0, materialId: 0, slot: 0);
            int textDef = RegisterAssetDefinition(definitions, "text", AssetKind.WorldText, assetId: 50, materialId: 0, slot: 0);

            int meshHandle = AllocateActive(instances, meshDef, owner, 100);
            int skinnedHandle = AllocateActive(instances, skinnedDef, owner, 101);
            int soundHandle = AllocateActive(instances, soundDef, owner, 102);
            int splineHandle = AllocateActive(instances, splineDef, owner, 103);
            int hudHandle = AllocateActive(instances, hudDef, owner, 104);
            int textHandle = AllocateActive(instances, textDef, owner, 105);
            animatorStates.Ensure(skinnedHandle, controllerId: 7);
            animatorStates.GetPackedState(skinnedHandle).SetPrimaryStateIndex(3);
            instances.SetParam(meshHandle, 900, ParamLane.Vector, 0f, 0, new Vector4(0.1f, 0.2f, 0.3f, 1f));
            instances.SetParam(hudHandle, 901, ParamLane.Float, 0.75f, 0, default);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                soundRequests);

            system.Update(0.016f);

            int visualCount = 0;
            int splineCount = 0;
            int hudCount = 0;
            int textCount = 0;
            foreach (ref readonly PresentationRequest request in requests.GetSpan())
            {
                if (request.Kind == PresentationRequestKind.VisualProxy)
                {
                    visualCount++;
                    if (request.VisualProxy.MeshAssetId == 11)
                    {
                        Assert.That(request.VisualProxy.Animator.GetControllerId(), Is.EqualTo(7));
                    }
                }
                else if (request.Kind == PresentationRequestKind.RoadSpline)
                {
                    splineCount++;
                }
                else if (request.Kind == PresentationRequestKind.WorldHud)
                {
                    if (request.WorldHud.Kind == Ludots.Core.Presentation.Hud.WorldHudItemKind.Bar)
                    {
                        hudCount++;
                    }
                    else if (request.WorldHud.Kind == Ludots.Core.Presentation.Hud.WorldHudItemKind.Text)
                    {
                        textCount++;
                    }
                }
            }

            Assert.That(visualCount, Is.EqualTo(2));
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(30));
            Assert.That(splineCount, Is.EqualTo(1));
            Assert.That(hudCount, Is.EqualTo(1));
            Assert.That(textCount, Is.EqualTo(1));
        }

        [Test]
        public void PerformerEmitSystem_AssetBinding_EmitsDecalAndVfxAsVisualProxies()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PerformerInstanceBuffer(capacity: 8);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PerformerAnimatorStateBuffer(8);

            int decalDef = RegisterAssetDefinition(definitions, "decal", AssetKind.Decal, assetId: 61, materialId: 71, slot: 0);
            int vfxDef = RegisterAssetDefinition(definitions, "vfx", AssetKind.VFX, assetId: 62, materialId: 72, slot: 0);
            AllocateActive(instances, decalDef, owner, 201);
            AllocateActive(instances, vfxDef, owner, 202);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                soundRequests: null!);

            system.Update(0.016f);

            int visualCount = 0;
            foreach (ref readonly PresentationRequest request in requests.GetSpan())
            {
                if (request.Kind != PresentationRequestKind.VisualProxy)
                {
                    continue;
                }

                visualCount++;
                Assert.That(request.VisualProxy.MeshAssetId is 61 or 62, Is.True);
            }

            Assert.That(visualCount, Is.EqualTo(2));
        }

        private static int RegisterAssetDefinition(
            PerformerDefinitionRegistry definitions,
            string key,
            AssetKind assetKind,
            int assetId,
            int materialId,
            int slot,
            VisualRenderPath renderPath = VisualRenderPath.None)
        {
            return definitions.Register(key, new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = slot,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = assetId,
                            MaterialId = materialId,
                            RenderPath = renderPath,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            ColorParamKey = assetKind == AssetKind.Mesh ? 900 : -1,
                            MaterialParamKey = assetKind == AssetKind.WorldHud ? 901 : -1,
                        },
                    },
                ],
            });
        }

        private static int AllocateActive(PerformerInstanceBuffer instances, int defId, Entity owner, int stableId)
        {
            Assert.That(instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, stableId, -1, out int handle), Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldScale = Vector3.One;
            return handle;
        }
    }
}
