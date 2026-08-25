using Ludots.Tests.TestCommon;
using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterWave3Wave4Tests
    {
        [Test]
        public void PresenterRuntimeSystem_DestroyRoot_EmitsDestroyedForWholeSubtree()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var stableIds = new PresentationStableIdAllocator();
            var definitions = new PresenterDefinitionRegistry();
            int rootDef = definitions.Register("root", new PresenterDefinition { Behaviors = Array.Empty<BehaviorSlot>() });
            int childDef = definitions.Register("child", new PresenterDefinition { Behaviors = Array.Empty<BehaviorSlot>() });
            Entity owner = world.Create();

            using var runtime = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                stableIds,
                definitions);

            var createRoot = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = rootDef,
                Source = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createRoot), Is.True);
            runtime.Update(0.016f);
            Entity rootEntity = events.GetSpan()[0].PresenterEntity;
            events.Clear();

            var createChild = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = childDef,
                ParentEntity = rootEntity,
                Source = owner,
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createChild), Is.True);
            runtime.Update(0.016f);
            Entity childEntity = events.GetSpan()[0].PresenterEntity;
            events.Clear();

            var destroyRoot = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenter,
                PresenterEntity = rootEntity,
            };
            Assert.That(commands.TryAdd(in destroyRoot), Is.True);
            runtime.Update(0.016f);

            ReadOnlySpan<PresentationEvent> destroyed = events.GetSpan();
            Assert.That(destroyed.Length, Is.EqualTo(2));
            Assert.That(destroyed[0].Kind, Is.EqualTo(PresentationEventKind.PresenterDestroyed));
            Assert.That(destroyed[0].PresenterEntity, Is.EqualTo(childEntity));
            Assert.That(destroyed[1].PresenterEntity, Is.EqualTo(rootEntity));
            Assert.That(world.IsAlive(rootEntity), Is.False);
            Assert.That(world.IsAlive(childEntity), Is.False);
        }

        [Test]
        public void PresenterRuntimeSystem_CreateWithInactiveParent_Throws()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("child", new PresenterDefinition());
            using var runtime = new PresenterRuntimeSystem(
                world,
                commands,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                new PresenterEntityRuntime(world),
                new PresentationStableIdAllocator(),
                definitions);

            // Create a fake dead entity to use as parent
            Entity fakeParent = world.Create();
            world.Destroy(fakeParent);

            var createWithInactiveParent = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ParentEntity = fakeParent,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createWithInactiveParent), Is.True);

            Assert.That(
                () => runtime.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("inactive parent"));
        }

        [Test]
        public void PresenterRuntimeSystem_ActivateDeactivateAndSetParam_UpdateBehaviorMaskAndBlackboard()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("actor", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot { SlotIndex = 0, Kind = BehaviorKind.Sound, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 1, Kind = BehaviorKind.Material, ActiveByDefault = false },
                ],
            });

            using var runtime = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var create = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in create), Is.True);
            runtime.Update(0.016f);

            Entity presenter = events.GetSpan()[0].PresenterEntity;
            Assert.That(world.IsAlive(presenter), Is.True);
            Assert.That(world.Get<PresenterState>(presenter).BehaviorActiveMask, Is.EqualTo(1u));

            var activate = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.ActivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 1,
            };
            Assert.That(commands.TryAdd(in activate), Is.True);
            var setParam = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SetParam,
                PresenterEntity = presenter,
                ParamKey = 55,
                ParamLane = ParamLane.Int,
                IntValue = 7,
            };
            Assert.That(commands.TryAdd(in setParam), Is.True);
            var deactivate = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 0,
            };
            Assert.That(commands.TryAdd(in deactivate), Is.True);

            runtime.Update(0.016f);

            Assert.That(world.Get<PresenterState>(presenter).BehaviorActiveMask, Is.EqualTo(1u << 1));
            Assert.That(instances.ResolveInt(presenter, 55), Is.EqualTo(7));
        }

        [Test]
        public void PresenterRuntimeSystem_DestroyPresenterScope_RejectsNonPositiveScopeTags()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("scoped", new PresenterDefinition());

            using var runtime = new PresenterRuntimeSystem(
                world,
                commands,
                events,
                new TransientMarkerBuffer(),
                new PresentationRequestBuffer(),
                instances,
                new PresentationStableIdAllocator(),
                definitions);

            var create = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 42,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in create), Is.True);
            runtime.Update(0.016f);
            events.Clear();

            var createUnscoped = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 0,
                Source = world.Create(),
                AnchorKind = PresentationAnchorKind.Entity,
            };
            Assert.That(commands.TryAdd(in createUnscoped), Is.True);
            runtime.Update(0.016f);
            events.Clear();

            var destroyWithZeroScope = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                PresenterDefinitionId = 42,
                ScopeTag = 0,
            };
            Assert.That(commands.TryAdd(in destroyWithZeroScope), Is.True);

            Assert.That(
                () => runtime.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("positive scopeTag"));
            Assert.That(events.Count, Is.EqualTo(0));
        }

        [Test]
        public void PresenterRuleSystem_GlobalRegionChanged_BroadcastsToMatchingDefinitionInstances()
        {
            using var world = World.Create();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var commands = new PresenterCommandBuffer();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("region_actor", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.SetParam,
                            ParamKey = 300,
                            ParamLane = ParamLane.Int,
                            ValueSource = PresenterCommandValueSource.EventKeyId,
                        },
                    },
                ],
            });
            int otherDefId = definitions.Register("region_actor.other", new PresenterDefinition());
            instances.BindDefinitions(definitions);

            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            Entity otherOwner = world.Create();
            Entity presenterA = instances.Create(defId, ownerA, scopeId: 1);
            Entity presenterB = instances.Create(defId, ownerB, scopeId: 1);
            Entity presenterOther = instances.Create(otherDefId, otherOwner, scopeId: 1);

            using var system = new PresenterRuleSystem(
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

            ReadOnlySpan<PresenterCommand> emitted = commands.GetSpan();
            Assert.That(emitted.Length, Is.EqualTo(2));
            Assert.That(emitted[0].PresenterEntity, Is.EqualTo(presenterA));
            Assert.That(emitted[0].IntValue, Is.EqualTo(42));
            Assert.That(emitted[0].ValueSource, Is.EqualTo(PresenterCommandValueSource.Fixed));
            Assert.That(emitted[1].PresenterEntity, Is.EqualTo(presenterB));
            Assert.That(emitted[1].IntValue, Is.EqualTo(42));
            Assert.That(events.Count, Is.EqualTo(0));
        }

        [Test]
        public void PresenterRuleSystem_ValueSourceEventKeyId_WritesFloatAndIntBlackboardLanes()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("region_params", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.SetParam,
                            ParamKey = 300,
                            ParamLane = ParamLane.Int,
                            ValueSource = PresenterCommandValueSource.EventKeyId,
                        },
                    },
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.SetParam,
                            ParamKey = 301,
                            ParamLane = ParamLane.Float,
                            ValueSource = PresenterCommandValueSource.EventKeyId,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);
            Entity owner = world.Create();
            Entity presenter = instances.Create(defId, owner, scopeId: 1);

            using var rules = new PresenterRuleSystem(
                world,
                events,
                commands,
                definitions,
                instances,
                new GraphProgramRegistry(),
                new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null),
                new Dictionary<string, object>());
            using var runtime = new PresenterRuntimeSystem(
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

            Assert.That(instances.ResolveInt(presenter, 300, -1), Is.EqualTo(17));
            Assert.That(instances.ResolveFloat(presenter, 301, -1f), Is.EqualTo(17f).Within(0.001f));
        }

        [Test]
        public void PresenterRuleSystem_Throws_WhenCommandBufferOverflows()
        {
            using var world = World.Create();
            var commands = new PresenterCommandBuffer(capacity: 1);
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            definitions.Register("command_buffer_limit_probe", new PresenterDefinition
            {
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.GlobalRegionChanged, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.SetParam,
                            ParamKey = 300,
                            ParamLane = ParamLane.Int,
                            ValueSource = PresenterCommandValueSource.EventKeyId,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);

            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            instances.Create(1, ownerA, scopeId: 1);
            instances.Create(1, ownerB, scopeId: 2);

            using var rules = new PresenterRuleSystem(
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
                KeyId = 17,
                Source = Entity.Null,
                Target = Entity.Null,
            }), Is.True);

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => rules.Update(0.016f));
            Assert.That(ex!.Message, Does.Contain("PresenterCommandBuffer overflowed"));
            Assert.That(commands.DroppedSinceClear, Is.EqualTo(1));
        }

        [Test]
        public void PresenterBehaviorSystem_AttributeTagMaterialAndSound_WriteBlackboardAndRequests()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(3, 100f);
            attributes.SetCurrent(3, 25f);
            var tags = default(GameplayTagContainer);
            tags.AddTag(5);
            Entity owner = world.Create(attributes, tags);

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("behavior", new PresenterDefinition
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
            instances.BindDefinitions(definitions);
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 700, Entity.Null, definitions.Get(defId));
            world.Add(presenter, new PresenterBootstrapPending());

            var soundRequests = new SoundRequestBuffer();
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                soundRequests);

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 100), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(instances.ResolveFloat(presenter, 101), Is.EqualTo(2f).Within(0.001f));
            Assert.That(instances.ResolveInt(presenter, 101), Is.EqualTo(99));
            Assert.That(instances.ResolveInt(presenter, 102), Is.EqualTo(1));
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(77));
        }

        [Test]
        public void PresenterEntityRuntime_SyncCullVisibility_SkipsOwnerCulledChildren()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Culled });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int parentDef = definitions.Register("culled.parent", new PresenterDefinition());
            int childDef = definitions.Register("culled.child", new PresenterDefinition());
            instances.BindDefinitions(definitions);

            Entity parentPresenter = instances.Create(parentDef, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 701, Entity.Null, default);
            Entity childPresenter = instances.Create(childDef, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 702, parentPresenter, default);

            instances.SyncCullVisibility();

            Assert.That(world.Get<PresenterCullState>(parentPresenter).OwnerCullVisible, Is.False);
            Assert.That(world.Get<PresenterCullState>(childPresenter).OwnerCullVisible, Is.False);

            int activeCount = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity e, ref PresenterState s) => { activeCount++; });
            Assert.That(activeCount, Is.EqualTo(2));
        }

        [Test]
        public void PresenterEntityRuntime_SyncCullVisibility_ChangedOwners_OnlyTouchesTargetOwnerHierarchy()
        {
            using var world = World.Create();
            Entity ownerA = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity ownerB = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int rootDefA = definitions.Register("owner.a.root", new PresenterDefinition());
            int childDefA = definitions.Register("owner.a.child", new PresenterDefinition());
            int rootDefB = definitions.Register("owner.b.root", new PresenterDefinition());
            instances.BindDefinitions(definitions);

            Entity ownerARoot = instances.Create(rootDefA, ownerA, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 711, Entity.Null, definitions.Get(rootDefA));
            Entity ownerAChild = instances.Create(childDefA, ownerA, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 712, ownerARoot, definitions.Get(childDefA));
            Entity ownerBRoot = instances.Create(rootDefB, ownerB, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 713, Entity.Null, definitions.Get(rootDefB));

            instances.SyncCullVisibility();

            world.Get<CullState>(ownerA).IsVisible = false;
            world.Get<CullState>(ownerA).LOD = LODLevel.Culled;
            instances.SyncCullVisibility([ownerA]);

            Assert.That(world.Get<PresenterCullState>(ownerARoot).OwnerCullVisible, Is.False);
            Assert.That(world.Get<PresenterCullState>(ownerARoot).LOD, Is.EqualTo(LODLevel.Culled));
            Assert.That(world.Get<PresenterCullState>(ownerAChild).OwnerCullVisible, Is.False);
            Assert.That(world.Get<PresenterCullState>(ownerAChild).LOD, Is.EqualTo(LODLevel.Culled));
            Assert.That(world.Get<PresenterCullState>(ownerBRoot).OwnerCullVisible, Is.True);
            Assert.That(world.Get<PresenterCullState>(ownerBRoot).LOD, Is.EqualTo(LODLevel.High));
        }

        [Test]
        public void PresenterBehaviorSystem_OwnerCulled_StillUpdatesDirtySyncState()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(3, 100f);
            attributes.SetCurrent(3, 25f);
            Entity owner = world.Create(attributes, new CullState { IsVisible = false, LOD = LODLevel.Culled });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("culled.behavior", new PresenterDefinition
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
                            AttributeId = 3,
                            TargetParamKey = 100,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 703, Entity.Null, definitions.Get(defId));
            world.Add(presenter, new PresenterBootstrapPending());
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SyncCullVisibility();

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(presenter, 100, -1f), Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void PresenterBehaviorSystem_TagBinding_WritesZeroWhenTagMissing_AndInvertsWhenConfigured()
        {
            using var world = World.Create();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("tags", new PresenterDefinition
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
            instances.BindDefinitions(definitions);
            Entity owner = world.Create();
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 701, Entity.Null, definitions.Get(defId));
            world.Add(presenter, new PresenterBootstrapPending());

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(instances.ResolveInt(presenter, 201), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(presenter, 202), Is.EqualTo(1));
        }

        [Test]
        public void PresenterBehaviorSystem_SoundStop_EmitsWhenBehaviorBecomesInactive()
        {
            using var world = World.Create();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var soundRequests = new SoundRequestBuffer();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("sound_actor", new PresenterDefinition
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
            instances.BindDefinitions(definitions);
            Entity owner = world.Create();
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 777, Entity.Null, definitions.Get(defId));

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                new PresentationOwnerChangeBuffer(8),
                soundRequests);

            system.Update(0.016f);
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));

            soundRequests.Clear();
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 0u;
            system.Update(0.016f);

            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.Stop));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(88));
        }

        [Test]
        public void PresenterBehaviorSystem_SoundStop_EmitsWhenPresenterDestroyed()
        {
            using var world = World.Create();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var soundRequests = new SoundRequestBuffer();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("sound_actor", new PresenterDefinition
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
            instances.BindDefinitions(definitions);

            Entity owner = world.Create();
            Entity presenter = instances.CreateHierarchy(definitions, defId, owner, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 778, Entity.Null, definitions.Get(defId));

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                new PresentationOwnerChangeBuffer(8),
                soundRequests);

            system.Update(0.016f);
            soundRequests.Clear();
            instances.Destroy(presenter);
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.PresenterDestroyed,
                KeyId = defId,
                Source = owner,
                Target = owner,
                PresenterEntity = presenter,
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
                        new AnimatorStateDefinition { PackedStateIndex = 5, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 9, DurationSeconds = 0.4f, PlaybackSpeed = 1f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 12,
                            Threshold = 0f,
                            DurationSeconds = 0f,
                            ConsumeTrigger = true,
                        },
                    ],
                });

            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("animated", new PresenterDefinition
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
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, world.Create(), 0);
            ref var presenterState = ref world.Get<PresenterState>(presenter);
            presenterState.BehaviorActiveMask = 1u;
            instances.SetParam(presenter, 12, ParamLane.Int, 0f, 1, default);

            var animatorStates = new PresenterAnimatorStateBuffer(4);
            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);

            system.Update(0.1f);

            Assert.That(instances.ResolveInt(presenter, 12), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(presenter, 20), Is.EqualTo(1));
            Assert.That(animatorStates.GetPackedState(presenter).GetPrimaryStateIndex(), Is.EqualTo(9));
        }

        [Test]
        public void PresenterEmitSystem_AssetBinding_EmitsVisualSoundSplineHudAndText()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity viewer = world.Create();
            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
            projectionStore.Upsert(
                viewer,
                owner,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(projectionStore),
            };
            ClientLocalSeatTestBindings.BindSoleSeat(globals, viewer, 1, "seat.0");
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var soundRequests = new SoundRequestBuffer();
            var animatorStates = new PresenterAnimatorStateBuffer(16);

            int meshDef = RegisterAssetDefinition(definitions, "mesh", AssetKind.Mesh, assetId: 10, materialId: 20, slot: 0);
            int skinnedDef = RegisterAssetDefinition(definitions, "skinned", AssetKind.SkinnedMesh, assetId: 11, materialId: 21, slot: 0, renderPath: VisualRenderPath.SkinnedMesh);
            int soundDef = RegisterAssetDefinition(definitions, "sound", AssetKind.Sound, assetId: 30, materialId: 0, slot: 0);
            int splineDef = RegisterAssetDefinition(definitions, "spline", AssetKind.Spline, assetId: 40, materialId: 0, slot: 0);
            int hudDef = RegisterAssetDefinition(definitions, "hud", AssetKind.WorldHud, assetId: 0, materialId: 0, slot: 0);
            int textDef = RegisterAssetDefinition(definitions, "text", AssetKind.WorldText, assetId: 50, materialId: 0, slot: 0);
            instances.BindDefinitions(definitions);

            Entity meshPresenter = AllocateActive(instances, world, meshDef, owner, 100);
            Entity skinnedPresenter = AllocateActive(instances, world, skinnedDef, owner, 101);
            Entity soundPresenter = AllocateActive(instances, world, soundDef, owner, 102);
            Entity splinePresenter = AllocateActive(instances, world, splineDef, owner, 103);
            Entity hudPresenter = AllocateActive(instances, world, hudDef, owner, 104);
            Entity textPresenter = AllocateActive(instances, world, textDef, owner, 105);
            animatorStates.Ensure(skinnedPresenter, controllerId: 7);
            animatorStates.GetPackedState(skinnedPresenter).SetPrimaryStateIndex(3);
            instances.SetParam(meshPresenter, 900, ParamLane.Vector, 0f, 0, new Vector4(0.1f, 0.2f, 0.3f, 1f));
            instances.SetParam(hudPresenter, 901, ParamLane.Float, 0.75f, 0, default);

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                globals,
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
                else if (request.Kind == PresentationRequestKind.SplineRibbon)
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
        public void PresenterEmitSystem_AssetBinding_EmitsDecalAndVfxAsVisualProxies()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PresenterAnimatorStateBuffer(8);

            int decalDef = RegisterAssetDefinition(definitions, "decal", AssetKind.Decal, assetId: 61, materialId: 71, slot: 0);
            int vfxDef = RegisterAssetDefinition(definitions, "vfx", AssetKind.VFX, assetId: 62, materialId: 72, slot: 0);
            instances.BindDefinitions(definitions);
            AllocateActive(instances, world, decalDef, owner, 201);
            AllocateActive(instances, world, vfxDef, owner, 202);

            using var system = new PresenterEmitSystem(
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

        [Test]
        public void PresenterEmitSystem_OwnerCulled_SkipsAssetProjection()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Culled });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int meshDef = RegisterAssetDefinition(definitions, "culled.mesh", AssetKind.Mesh, assetId: 10, materialId: 20, slot: 0);
            instances.BindDefinitions(definitions);
            Entity presenter = AllocateActive(instances, world, meshDef, owner, 704);
            world.Get<PresenterState>(presenter).AnchorKind = PresentationAnchorKind.Entity;
            instances.SyncCullVisibility();

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(0));
            Assert.That(world.Get<PresenterState>(presenter).Elapsed, Is.EqualTo(0.016f).Within(0.001f));
        }

        [Test]
        public void PresenterEmitSystem_OwnerCulled_DoesNotDestroyLifetimeInstances()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Culled });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int meshDef = definitions.Register("culled.transient", new PresenterDefinition
            {
                DefaultLifetime = 0.01f,
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
                            AssetId = 10,
                            MaterialId = 20,
                            LocalScale = Vector3.One,
                        },
                    },
                ],
            });
            Entity presenter = instances.Create(meshDef, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 705, Entity.Null, new PresenterDefinition { DefaultLifetime = 0.01f });
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SyncCullVisibility();

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            Assert.That(world.IsAlive(presenter), Is.True,
                "销毁只经 TimerSet → TimerExpired → Rule → DestroyPresenter 链；EmitSystem 不再有 lifetime 销毁分支");
            Assert.That(requests.Count, Is.EqualTo(0));
        }

        private static int RegisterAssetDefinition(
            PresenterDefinitionRegistry definitions,
            string key,
            AssetKind assetKind,
            int assetId,
            int materialId,
            int slot,
            VisualRenderPath renderPath = VisualRenderPath.None)
        {
            return definitions.Register(key, new PresenterDefinition
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
                            RenderPath = renderPath == VisualRenderPath.None && assetKind is AssetKind.Mesh or AssetKind.Decal or AssetKind.VFX
                                ? VisualRenderPath.StaticMesh
                                : renderPath,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            ColorParamKey = assetKind == AssetKind.Mesh ? 900 : -1,
                            MaterialParamKey = assetKind == AssetKind.WorldHud ? 901 : -1,
                        },
                    },
                ],
            });
        }

        private static Entity AllocateActive(PresenterEntityRuntime instances, World world, int defId, Entity owner, int stableId)
        {
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, stableId, Entity.Null, default);
            ref var state = ref world.Get<PresenterState>(presenter);
            state.BehaviorActiveMask = 1u;
            ref var scale = ref world.Get<PresenterWorldScale>(presenter);
            scale.Value = Vector3.One;
            return presenter;
        }
    }
}
