using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public class EventFilterTests
    {
        [Test]
        public void Matches_ExactKindAndKey_ReturnsTrue()
        {
            var filter = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = 42 };
            var evt = new PresentationEvent { Kind = PresentationEventKind.CastCommitted, KeyId = 42 };
            Assert.That(filter.Matches(in evt), Is.True);
        }

        [Test]
        public void Matches_WrongKind_ReturnsFalse()
        {
            var filter = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 };
            var evt = new PresentationEvent { Kind = PresentationEventKind.EffectApplied, KeyId = 10 };
            Assert.That(filter.Matches(in evt), Is.False);
        }

        [Test]
        public void Matches_WildcardKeyId_MatchesAnyKey()
        {
            var filter = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = -1 };
            var evt = new PresentationEvent { Kind = PresentationEventKind.EffectApplied, KeyId = 999 };
            Assert.That(filter.Matches(in evt), Is.True);
        }

        [Test]
        public void Matches_WrongKey_ReturnsFalse()
        {
            var filter = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = 1 };
            var evt = new PresentationEvent { Kind = PresentationEventKind.CastCommitted, KeyId = 2 };
            Assert.That(filter.Matches(in evt), Is.False);
        }
    }

    [TestFixture]
    public class PerformerInstanceBufferTests
    {
        private PerformerInstanceBuffer _buf;

        [SetUp]
        public void Setup()
        {
            _buf = new PerformerInstanceBuffer(16);
        }

        [Test]
        public void TryAllocate_ReturnsHandle_AndInstanceIsActive()
        {
            var world = World.Create();
            var entity = world.Create();
            Assert.That(_buf.TryAllocate(100, entity, 0, out int handle), Is.True);
            Assert.That(_buf.IsActive(handle), Is.True);
            world.Dispose();
        }

        [Test]
        public void TryAllocate_InitializesT4TransformAndTreeDefaults()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(100, entity, 7, out int handle), Is.True);

            ref readonly PerformerInstance instance = ref _buf.Get(handle);
            Assert.That(instance.ScopeId, Is.EqualTo(7));
            Assert.That(instance.WorldRotation, Is.EqualTo(Quaternion.Identity));
            Assert.That(instance.WorldScale, Is.EqualTo(Vector3.One));
            Assert.That(instance.TransformSource, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(instance.ParentHandle, Is.EqualTo(-1));
            Assert.That(instance.FirstChildHandle, Is.EqualTo(-1));
            Assert.That(instance.NextSiblingHandle, Is.EqualTo(-1));
            Assert.That(instance.BehaviorActiveMask, Is.EqualTo(0u));
        }

        [Test]
        public void TryAllocate_WorldAnchor_InitializesWorldFixedTransformSource()
        {
            using var world = World.Create();

            Assert.That(
                _buf.TryAllocate(100, Entity.Null, 11, PresentationAnchorKind.WorldPosition, new Vector3(1f, 2f, 3f), 321, out int handle),
                Is.True);

            ref readonly PerformerInstance instance = ref _buf.Get(handle);
            Assert.That(instance.AnchorKind, Is.EqualTo(PresentationAnchorKind.WorldPosition));
            Assert.That(instance.WorldPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(instance.TransformSource, Is.EqualTo(TransformSource.WorldFixed));
            Assert.That(instance.StableId, Is.EqualTo(321));
        }

        [Test]
        public void TryAllocate_WithParent_LinksIntoTreeAndBlackboardParentChain()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(100, entity, 7, out int parentHandle), Is.True);
            Assert.That(
                _buf.TryAllocate(101, entity, 7, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentHandle, out int childHandle),
                Is.True);

            ref readonly PerformerInstance parent = ref _buf.Get(parentHandle);
            ref readonly PerformerInstance child = ref _buf.Get(childHandle);

            Assert.That(parent.FirstChildHandle, Is.EqualTo(childHandle));
            Assert.That(child.ParentHandle, Is.EqualTo(parentHandle));
            Assert.That(child.NextSiblingHandle, Is.EqualTo(-1));
            Assert.That(_buf.GetParentHandle(childHandle), Is.EqualTo(parentHandle));
        }

        [Test]
        public void Release_MakesInactive()
        {
            var world = World.Create();
            var entity = world.Create();
            _buf.TryAllocate(100, entity, 0, out int handle);
            _buf.Release(handle);
            Assert.That(_buf.IsActive(handle), Is.False);
            world.Dispose();
        }

        [Test]
        public void ReleaseScope_ReleasesAllInScope()
        {
            var world = World.Create();
            var e1 = world.Create();
            var e2 = world.Create();
            _buf.TryAllocate(1, e1, 42, out int h1);
            _buf.TryAllocate(2, e2, 42, out int h2);
            _buf.TryAllocate(3, e1, 99, out int h3); // different scope

            _buf.ReleaseScope(42);
            Assert.That(_buf.IsActive(h1), Is.False);
            Assert.That(_buf.IsActive(h2), Is.False);
            Assert.That(_buf.IsActive(h3), Is.True);
            world.Dispose();
        }

        [Test]
        public void Release_RecursivelyReleasesChildSubtree()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(1, entity, 5, out int parentHandle), Is.True);
            Assert.That(_buf.TryAllocate(2, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentHandle, out int childHandle), Is.True);
            Assert.That(_buf.TryAllocate(3, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, childHandle, out int grandChildHandle), Is.True);

            Assert.That(_buf.Release(parentHandle), Is.True);

            Assert.That(_buf.IsActive(parentHandle), Is.False);
            Assert.That(_buf.IsActive(childHandle), Is.False);
            Assert.That(_buf.IsActive(grandChildHandle), Is.False);
            Assert.That(_buf.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void ReleaseScope_RecursivelyReleasesScopedRootsAndChildren()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(1, entity, 42, out int rootHandle), Is.True);
            Assert.That(_buf.TryAllocate(2, entity, 99, PresentationAnchorKind.Entity, Vector3.Zero, 0, rootHandle, out int childHandle), Is.True);
            Assert.That(_buf.TryAllocate(3, entity, 77, out int unrelatedHandle), Is.True);

            int released = _buf.ReleaseScope(42);

            Assert.That(released, Is.EqualTo(2));
            Assert.That(_buf.IsActive(rootHandle), Is.False);
            Assert.That(_buf.IsActive(childHandle), Is.False);
            Assert.That(_buf.IsActive(unrelatedHandle), Is.True);
        }

        [Test]
        public void ParamOverride_IsRetrievable()
        {
            var world = World.Create();
            var entity = world.Create();
            _buf.TryAllocate(1, entity, 0, out int handle);
            _buf.SetParamOverride(handle, 5, 3.14f);
            Assert.That(_buf.TryGetParamOverride(handle, 5, out float val), Is.True);
            Assert.That(val, Is.EqualTo(3.14f).Within(0.001f));
            world.Dispose();
        }

        [Test]
        public void ParamOverride_MissingKey_ReturnsFalse()
        {
            var world = World.Create();
            var entity = world.Create();
            _buf.TryAllocate(1, entity, 0, out int handle);
            Assert.That(_buf.TryGetParamOverride(handle, 99, out _), Is.False);
            world.Dispose();
        }

        [Test]
        public void ResolveFloat_InheritsFromParentBlackboard()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(1, entity, 0, out int parentHandle), Is.True);
            Assert.That(_buf.TryAllocate(2, entity, 0, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentHandle, out int childHandle), Is.True);
            _buf.SetParam(parentHandle, 15, ParamLane.Float, 9.5f, 0, Vector4.Zero);

            Assert.That(_buf.ResolveFloat(childHandle, 15, -1f), Is.EqualTo(9.5f).Within(0.001f));
            Assert.That(_buf.TryGetParamOverride(childHandle, 15, out _), Is.False);
        }

        [Test]
        public void SetParam_WritesAllBlackboardLanes()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            Assert.That(_buf.TryAllocate(1, entity, 0, out int handle), Is.True);
            _buf.SetParam(handle, 1, ParamLane.Float, 2.5f, 0, Vector4.Zero);
            _buf.SetParam(handle, 2, ParamLane.Int, 0f, 7, Vector4.Zero);
            _buf.SetParam(handle, 3, ParamLane.Vector, 0f, 0, new Vector4(1f, 2f, 3f, 4f));

            Assert.That(_buf.ResolveFloat(handle, 1, -1f), Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(_buf.ResolveInt(handle, 2, -1), Is.EqualTo(7));
            Assert.That(_buf.ResolveVector(handle, 3, Vector4.Zero), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
        }
    }

    [TestFixture]
    public class PerformerRuleSystemTests
    {
        private World _world;
        private PresentationEventStream _events;
        private PerformerCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private GraphProgramRegistry _programs;
        private PerformerRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream();
            _commands = new PerformerCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null);
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, instances: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        private void TickAndFlush(float dt)
        {
            _system.Update(dt);
        }

        [Test]
        public void MatchingEvent_ProducesCommand()
        {
            var def = new PerformerDefinition
            {
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = 1,
                            ScopeTag = -1,
                        }
                    }
                }
            };
            _defs.Register("test_1", def);

            var actor = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                KeyId = 5,
                Source = actor,
                Target = actor,
            });

            TickAndFlush(0.016f);

            var cmds = _commands.GetSpan();
            Assert.That(cmds.Length, Is.EqualTo(1));
            Assert.That(cmds[0].CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
            Assert.That(cmds[0].PerformerDefinitionId, Is.EqualTo(1));
        }

        [Test]
        public void NonMatchingEvent_ProducesNoCommand()
        {
            var def = new PerformerDefinition
            {
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = 1,
                            ScopeTag = -1,
                        }
                    }
                }
            };
            _defs.Register("test_1", def);

            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EffectApplied, // wrong kind
                KeyId = 5,
            });

            TickAndFlush(0.016f);

            var cmds = _commands.GetSpan();
            Assert.That(cmds.Length, Is.EqualTo(0));
        }

        [Test]
        public void EventsClearedAfterUpdate()
        {
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                KeyId = 1,
            });
            TickAndFlush(0.016f);

            Assert.That(_events.Count, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class PerformerRuntimeSystemTests
    {
        private World _world;
        private PerformerCommandBuffer _commands;
        private PresentationEventStream _events;
        private PerformerInstanceBuffer _instances;
        private PerformerDefinitionRegistry _definitions;
        private PerformerRuntimeSystem _system;
        private PresentationRequestBuffer _requests;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _commands = new PerformerCommandBuffer();
            _events = new PresentationEventStream();
            _instances = new PerformerInstanceBuffer();
            _definitions = new PerformerDefinitionRegistry();
            var markers = new TransientMarkerBuffer();
            _requests = new PresentationRequestBuffer();
            _system = new PerformerRuntimeSystem(_world, _commands, _events, markers, _requests, _instances, new Ludots.Core.Presentation.PresentationStableIdAllocator(), _definitions);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        private void TickAndFlush(float dt)
        {
            _system.Update(dt);
        }

        [Test]
        public void CreatePerformerCommand_AllocatesInstance()
        {
            var owner = _world.Create();
            _definitions.Register("test.runtime.basic", new PerformerDefinition
            {
                DefaultLifetime = 1f,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = _definitions.GetId("test.runtime.basic"),
                ScopeTag = 5,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.IsActive(0), Is.True);
        }

        [Test]
        public void DestroyPerformerScopeCommand_ReleasesInstances()
        {
            var owner = _world.Create();
            _definitions.Register("test.runtime.scope", new PerformerDefinition
            {
                DefaultLifetime = -1f,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = _definitions.GetId("test.runtime.scope"),
                ScopeTag = 7,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformerScope,
                PerformerDefinitionId = 7,
                ScopeTag = 7,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.IsActive(0), Is.False);
        }

        [Test]
        public void CreatePerformerCommand_ReleasesDeadOwnerInstancesBeforeAllocating()
        {
            _definitions.Register("test.runtime.dead_owner", new PerformerDefinition
            {
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.dead_owner");
            var firstOwner = _world.Create();
            Assert.That(_instances.TryAllocate(defId, firstOwner, 1, out int staleHandle), Is.True);
            _world.Destroy(firstOwner);

            var secondOwner = _world.Create();
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 2,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = secondOwner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
            ref readonly var remaining = ref _instances.Get(staleHandle);
            Assert.That(remaining.Owner, Is.EqualTo(secondOwner));
            Assert.That(remaining.ScopeId, Is.EqualTo(2));
        }

        [Test]
        public void CreatePerformerCommand_DedupesPersistentScopedInstance()
        {
            _definitions.Register("test.runtime.persistent_scope", new PerformerDefinition
            {
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.persistent_scope");
            var owner = _world.Create();

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 77,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 77,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void CreatePerformerCommand_WithParentHandle_LinksChildAndInheritsDefaults()
        {
            var owner = _world.Create();
            int parentDefId = _definitions.Register("test.runtime.parent", new PerformerDefinition
            {
                DefaultLifetime = -1f,
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 100, Lane = ParamLane.Int, IntValue = 3 }
                }
            });
            int childDefId = _definitions.Register("test.runtime.child", new PerformerDefinition
            {
                DefaultLifetime = -1f,
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 200, Lane = ParamLane.Float, FloatValue = 2.25f }
                }
            });

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = parentDefId,
                ScopeTag = 10,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = childDefId,
                PerformerHandle = -1,
                ParentHandle = 0,
                ScopeTag = 20,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(2));
            ref readonly PerformerInstance parent = ref _instances.Get(0);
            ref readonly PerformerInstance child = ref _instances.Get(1);
            Assert.That(parent.FirstChildHandle, Is.EqualTo(1));
            Assert.That(child.ParentHandle, Is.EqualTo(0));
            Assert.That(_instances.ResolveFloat(1, 200, -1f), Is.EqualTo(2.25f).Within(0.001f));
            Assert.That(_instances.ResolveInt(0, 100, -1), Is.EqualTo(3));
        }

        [Test]
        public void SetParamCommand_WritesRequestedLaneToBlackboard()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.param_lanes", new PerformerDefinition
            {
                DefaultLifetime = -1f,
            });

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 1,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerHandle = 0,
                ParamKey = 10,
                ParamLane = ParamLane.Float,
                ParamValue = 4.5f,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerHandle = 0,
                ParamKey = 11,
                ParamLane = ParamLane.Int,
                IntValue = 9,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerHandle = 0,
                ParamKey = 12,
                ParamLane = ParamLane.Vector,
                VectorValue = new Vector4(8f, 7f, 6f, 5f),
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ResolveFloat(0, 10, -1f), Is.EqualTo(4.5f).Within(0.001f));
            Assert.That(_instances.ResolveInt(0, 11, -1), Is.EqualTo(9));
            Assert.That(_instances.ResolveVector(0, 12, Vector4.Zero), Is.EqualTo(new Vector4(8f, 7f, 6f, 5f)));
        }

        [Test]
        public void CreatePerformerDefinition_WithActiveByDefaultBehaviors_SeedsBehaviorMask()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.behaviors", new PerformerDefinition
            {
                DefaultLifetime = -1f,
                Behaviors = new[]
                {
                    new BehaviorSlot { SlotIndex = 0, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 2, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 5, ActiveByDefault = false },
                }
            });

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                ScopeTag = 3,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.Get(0).BehaviorActiveMask, Is.EqualTo((1u << 0) | (1u << 2)));
        }
    }

    [TestFixture]
    public class PresentationBridgeGasTests
    {
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private PresentationEventStream _stream;
        private PresentationBridgeSystem _bridge;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer();
            _stream = new PresentationEventStream();
            var eventBus = new GameplayEventBus();
            var session = new GameSession();
            _bridge = new PresentationBridgeSystem(_world, eventBus, _stream, session, _gasEvents);
        }

        [TearDown]
        public void TearDown()
        {
            _bridge?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void EffectApplied_BridgedToStream()
        {
            var actor = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = actor,
                Delta = -25f,
                AttributeId = 1,
                EffectTemplateId = 10,
            });

            _bridge.Update(0.016f);

            var span = _stream.GetSpan();
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(1));
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind != PresentationEventKind.EffectApplied)
                {
                    continue;
                }

                Assert.That(span[i].Magnitude, Is.EqualTo(-25f));
                Assert.That(span[i].PayloadA, Is.EqualTo(1));
                Assert.That(span[i].KeyId, Is.EqualTo(10));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "EffectApplied event not bridged");
        }

        [Test]
        public void CastCommitted_BridgedToStream()
        {
            var actor = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                AbilitySlot = 2,
                AbilityId = 42,
            });

            _bridge.Update(0.016f);

            var span = _stream.GetSpan();
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind != PresentationEventKind.CastCommitted)
                {
                    continue;
                }

                Assert.That(span[i].PayloadA, Is.EqualTo(2));
                Assert.That(span[i].KeyId, Is.EqualTo(42));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "CastCommitted event not bridged");
        }

        [Test]
        public void CastFailed_BridgedToStream()
        {
            var actor = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                AbilitySlot = 1,
                AbilityId = 5,
                FailReason = AbilityCastFailReason.OnCooldown,
            });

            _bridge.Update(0.016f);

            var span = _stream.GetSpan();
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind != PresentationEventKind.CastFailed)
                {
                    continue;
                }

                Assert.That(span[i].PayloadB, Is.EqualTo((int)AbilityCastFailReason.OnCooldown));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "CastFailed event not bridged");
        }
    }

    [TestFixture]
    public class CorePerformerDefinitionTests
    {
        [Test]
        public void LoadFromJson_AllCoreBuiltinIds_Present()
        {
            var registry = new PerformerDefinitionRegistry();
            LoadCorePerformerDefinitions(registry);

            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.CastCommittedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.CastFailedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.FloatingCombatText), out _), Is.True);
            Assert.That(registry.GetId(WellKnownPerformerKeys.EntityHealthBar), Is.EqualTo(0));
        }

        [Test]
        public void FloatingCombatText_HasYDriftAndAlphaFade()
        {
            var registry = new PerformerDefinitionRegistry();
            LoadCorePerformerDefinitions(registry);
            registry.TryGet(registry.GetId(WellKnownPerformerKeys.FloatingCombatText), out var def);

            Assert.That(def.PositionYDriftPerSecond, Is.GreaterThan(0f));
            Assert.That(def.AlphaFadeOverLifetime, Is.True);
            Assert.That(def.DefaultLifetime, Is.GreaterThan(0f));
        }

        [Test]
        public void EntityHealthBar_IsConfigDefined_NotBuiltin()
        {
            var registry = new PerformerDefinitionRegistry();
            LoadCorePerformerDefinitions(registry);

            Assert.That(registry.GetId(WellKnownPerformerKeys.EntityHealthBar), Is.GreaterThan(0));
        }

        private static void LoadCorePerformerDefinitions(PerformerDefinitionRegistry registry)
        {
            string repoRoot = FindRepoRoot();
            int healthAttrId = AttributeRegistry.Register("Health");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            vfs.Mount("LudotsCoreMod", Path.Combine(repoRoot, "mods", "LudotsCoreMod"));
            modLoader.LoadedModIds.Add("LudotsCoreMod");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var meshes = new MeshAssetRegistry();
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);

            new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: name => string.Equals(name, "Health", StringComparison.Ordinal) ? healthAttrId : 0,
                resolveMeshId: meshes.GetId,
                resolveTextTokenId: textCatalog.GetTokenId,
                resolveBehaviorAssetId: (kind, key) => kind switch
                {
                    AssetKind.Mesh => meshes.GetId(key),
                    AssetKind.WorldText => textCatalog.GetTokenId(key),
                    _ => 0,
                }).Load(catalog);
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }

    [TestFixture]
    public class PerformerLifecycleRuleFilterTests
    {
        private World _world;
        private PresentationEventStream _events;
        private PerformerCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private GraphProgramRegistry _programs;
        private PerformerRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream();
            _commands = new PerformerCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null);
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, instances: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void LifecycleRule_KeyFilter_SkipsMismatch()
        {
            _defs.Register("test.lifecycle.mismatch", new PerformerDefinition
            {
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = 77,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    }
                }
            });

            var owner = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntitySpawned,
                KeyId = 11,
                Source = owner,
                PayloadA = 123,
            });

            _system.Update(0.016f);

            Assert.That(_commands.GetSpan().Length, Is.EqualTo(0));
        }

        [Test]
        public void LifecycleRule_KeyFilter_EmitsScopeFromEventPayload()
        {
            _defs.Register("test.lifecycle.match", new PerformerDefinition
            {
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = 77,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    }
                }
            });

            var owner = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntitySpawned,
                KeyId = 10,
                Source = owner,
                PayloadA = 456,
            });

            _system.Update(0.016f);

            var span = _commands.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
            Assert.That(span[0].PerformerDefinitionId, Is.EqualTo(77));
            Assert.That(span[0].ScopeTag, Is.EqualTo(456));
            Assert.That(span[0].Source, Is.EqualTo(owner));
        }

        [Test]
        public void PerformerCreatedEvent_PromotesPayloadHandle_ToChildParentHandle()
        {
            _defs.Register("test.lifecycle.child", new PerformerDefinition
            {
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.PerformerCreated, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = 99,
                            ParentHandle = -1,
                            ScopeTag = 55,
                        }
                    }
                }
            });

            var owner = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.PerformerCreated,
                KeyId = 1,
                Source = owner,
                Target = owner,
                PayloadA = 23,
                PayloadB = 77,
            });

            _system.Update(0.016f);

            var span = _commands.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].PerformerDefinitionId, Is.EqualTo(99));
            Assert.That(span[0].ParentHandle, Is.EqualTo(23));
            Assert.That(span[0].ScopeTag, Is.EqualTo(55));
        }
    }

    [TestFixture]
    public class WellKnownPerformerParamKeysTests
    {
        [Test]
        public void BarConstants_MatchEmitSystemConventions()
        {
            // These values must stay aligned with the canonical world-bar param mapping.
            Assert.That(WellKnownPerformerParamKeys.BarFillRatio, Is.EqualTo(0));
            Assert.That(WellKnownPerformerParamKeys.BarWidth, Is.EqualTo(1));
            Assert.That(WellKnownPerformerParamKeys.BarHeight, Is.EqualTo(2));
            Assert.That(WellKnownPerformerParamKeys.BarForegroundR, Is.EqualTo(4));
            Assert.That(WellKnownPerformerParamKeys.BarForegroundG, Is.EqualTo(5));
            Assert.That(WellKnownPerformerParamKeys.BarForegroundB, Is.EqualTo(6));
            Assert.That(WellKnownPerformerParamKeys.BarForegroundA, Is.EqualTo(7));
            Assert.That(WellKnownPerformerParamKeys.BarBackgroundR, Is.EqualTo(8));
            Assert.That(WellKnownPerformerParamKeys.BarBackgroundG, Is.EqualTo(9));
            Assert.That(WellKnownPerformerParamKeys.BarBackgroundB, Is.EqualTo(10));
            Assert.That(WellKnownPerformerParamKeys.BarBackgroundA, Is.EqualTo(11));
        }

        [Test]
        public void TextConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPerformerParamKeys.TextValue0, Is.EqualTo(0));
            Assert.That(WellKnownPerformerParamKeys.TextValue1, Is.EqualTo(1));
            Assert.That(WellKnownPerformerParamKeys.TextFontSize, Is.EqualTo(3));
            Assert.That(WellKnownPerformerParamKeys.TextColorR, Is.EqualTo(4));
            Assert.That(WellKnownPerformerParamKeys.TextTokenId, Is.EqualTo(15));
            Assert.That(WellKnownPerformerParamKeys.TextValueMode, Is.EqualTo(16));
        }

        [Test]
        public void OverlayConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPerformerParamKeys.OverlayRadius, Is.EqualTo(0));
            Assert.That(WellKnownPerformerParamKeys.OverlayInnerRadius, Is.EqualTo(1));
            Assert.That(WellKnownPerformerParamKeys.OverlayAngle, Is.EqualTo(2));
            Assert.That(WellKnownPerformerParamKeys.OverlayRotation, Is.EqualTo(3));
            Assert.That(WellKnownPerformerParamKeys.OverlayBorderWidth, Is.EqualTo(12));
            Assert.That(WellKnownPerformerParamKeys.OverlayLength, Is.EqualTo(13));
            Assert.That(WellKnownPerformerParamKeys.OverlayWidth, Is.EqualTo(14));
        }

        [Test]
        public void MarkerConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPerformerParamKeys.MarkerScale, Is.EqualTo(0));
            Assert.That(WellKnownPerformerParamKeys.MarkerScaleX, Is.EqualTo(1));
            Assert.That(WellKnownPerformerParamKeys.MarkerScaleY, Is.EqualTo(2));
            Assert.That(WellKnownPerformerParamKeys.MarkerScaleZ, Is.EqualTo(3));
            Assert.That(WellKnownPerformerParamKeys.MarkerColorR, Is.EqualTo(4));
            Assert.That(WellKnownPerformerParamKeys.MarkerColorG, Is.EqualTo(5));
            Assert.That(WellKnownPerformerParamKeys.MarkerColorB, Is.EqualTo(6));
            Assert.That(WellKnownPerformerParamKeys.MarkerColorA, Is.EqualTo(7));
        }
    }
}
