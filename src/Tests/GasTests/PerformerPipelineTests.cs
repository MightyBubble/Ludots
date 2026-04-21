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
    public class PerformerEntityRuntimeTests
    {
        private World _world;
        private PerformerEntityRuntime _buf;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _buf = new PerformerEntityRuntime(_world);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void Create_ReturnsEntity_AndIsAlive()
        {
            var entity = _world.Create();
            var performer = _buf.Create(100, entity, 0);
            Assert.That(_world.IsAlive(performer), Is.True);
        }

        [Test]
        public void Create_InitializesStateAndTransformDefaults()
        {
            Entity entity = _world.Create();

            var performer = _buf.Create(100, entity, 7);

            ref readonly PerformerState state = ref _world.Get<PerformerState>(performer);
            Assert.That(state.ScopeId, Is.EqualTo(7));
            Assert.That(_world.Get<PerformerWorldRotation>(performer).Value, Is.EqualTo(Quaternion.Identity));
            Assert.That(_world.Get<PerformerWorldScale>(performer).Value, Is.EqualTo(Vector3.One));
            Assert.That(_world.Get<PerformerTransformSource>(performer).Value, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(state.BehaviorActiveMask, Is.EqualTo(0u));
        }

        [Test]
        public void Create_WorldAnchor_InitializesWorldFixedTransformSource()
        {
            var performer = _buf.Create(100, Entity.Null, 11, PresentationAnchorKind.WorldPosition, new Vector3(1f, 2f, 3f), 321, Entity.Null, default);

            ref readonly PerformerState state = ref _world.Get<PerformerState>(performer);
            Assert.That(state.AnchorKind, Is.EqualTo(PresentationAnchorKind.WorldPosition));
            Assert.That(_world.Get<PerformerWorldPosition>(performer).Value, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(_world.Get<PerformerTransformSource>(performer).Value, Is.EqualTo(TransformSource.WorldFixed));
            Assert.That(state.StableId, Is.EqualTo(321));
        }

        [Test]
        public void Create_WithParent_LinksIntoTree()
        {
            Entity entity = _world.Create();

            var parentPerformer = _buf.Create(100, entity, 7);
            var childPerformer = _buf.Create(101, entity, 7, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPerformer, default);

            ref readonly PerformerChildren parentChildren = ref _world.Get<PerformerChildren>(parentPerformer);
            ref readonly PerformerParent childParent = ref _world.Get<PerformerParent>(childPerformer);

            Assert.That(parentChildren.Count, Is.GreaterThan(0));
            Assert.That(childParent.Parent, Is.EqualTo(parentPerformer));
        }

        [Test]
        public void CreateHierarchy_RecursivelyBuildsChildrenAndAppliesDefaults()
        {
            Entity owner = _world.Create(new VisualTransform());
            var definitions = new PerformerDefinitionRegistry();
            int childId = definitions.Register("test.child", new PerformerDefinition
            {
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 7, Lane = ParamLane.Int, IntValue = 99 }
                }
            });
            int rootId = definitions.Register("test.root", new PerformerDefinition
            {
                Children = new[]
                {
                    new ChildPerformerRef
                    {
                        DefinitionId = childId,
                        ScopeTag = 42
                    }
                }
            });

            Entity root = _buf.CreateHierarchy(
                definitions,
                rootId,
                owner,
                11,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                500,
                Entity.Null,
                definitions.Get(rootId));

            ref readonly PerformerChildren children = ref _world.Get<PerformerChildren>(root);
            Assert.That(children.Count, Is.EqualTo(1));
            Entity child = children.Get(0);
            Assert.That(_world.IsAlive(child), Is.True);
            Assert.That(_world.Get<PerformerParent>(child).Parent, Is.EqualTo(root));
            Assert.That(_world.Get<PerformerState>(child).ScopeId, Is.EqualTo(42));
            Assert.That(_buf.ResolveInt(child, 7, -1), Is.EqualTo(99));
        }

        [Test]
        public void Destroy_MakesEntityDead()
        {
            var entity = _world.Create();
            var performer = _buf.Create(100, entity, 0);
            _buf.Destroy(performer);
            Assert.That(_world.IsAlive(performer), Is.False);
        }

        [Test]
        public void DestroyScope_DestroysAllInScope()
        {
            var e1 = _world.Create();
            var e2 = _world.Create();
            var p1 = _buf.Create(1, e1, 42);
            var p2 = _buf.Create(2, e2, 42);
            var p3 = _buf.Create(3, e1, 99); // different scope

            _buf.DestroyScope(42);
            Assert.That(_world.IsAlive(p1), Is.False);
            Assert.That(_world.IsAlive(p2), Is.False);
            Assert.That(_world.IsAlive(p3), Is.True);
        }

        [Test]
        public void Destroy_RecursivelyDestroysChildSubtree()
        {
            Entity entity = _world.Create();

            var parentPerformer = _buf.Create(1, entity, 5);
            var childPerformer = _buf.Create(2, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPerformer, default);
            var grandChildPerformer = _buf.Create(3, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, childPerformer, default);

            _buf.Destroy(parentPerformer);

            Assert.That(_world.IsAlive(parentPerformer), Is.False);
            Assert.That(_world.IsAlive(childPerformer), Is.False);
            Assert.That(_world.IsAlive(grandChildPerformer), Is.False);
            Assert.That(_buf.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void DestroyScope_RecursivelyDestroysScopedRootsAndChildren()
        {
            Entity entity = _world.Create();

            var rootPerformer = _buf.Create(1, entity, 42);
            var childPerformer = _buf.Create(2, entity, 99, PresentationAnchorKind.Entity, Vector3.Zero, 0, rootPerformer, default);
            var unrelatedPerformer = _buf.Create(3, entity, 77);

            int destroyed = _buf.DestroyScope(42);

            Assert.That(destroyed, Is.GreaterThanOrEqualTo(1));
            Assert.That(_world.IsAlive(rootPerformer), Is.False);
            Assert.That(_world.IsAlive(childPerformer), Is.False);
            Assert.That(_world.IsAlive(unrelatedPerformer), Is.True);
        }

        [Test]
        public void SetParam_Float_IsResolvable()
        {
            var entity = _world.Create();
            var performer = _buf.Create(1, entity, 0);
            _buf.SetParam(performer, 5, ParamLane.Float, 3.14f, 0, default);
            Assert.That(_buf.ResolveFloat(performer, 5, -1f), Is.EqualTo(3.14f).Within(0.001f));
        }

        [Test]
        public void ResolveFloat_InheritsFromParent()
        {
            Entity entity = _world.Create();

            var parentPerformer = _buf.Create(1, entity, 0);
            var childPerformer = _buf.Create(2, entity, 0, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPerformer, default);
            _buf.SetParam(parentPerformer, 15, ParamLane.Float, 9.5f, 0, Vector4.Zero);

            Assert.That(_buf.ResolveFloat(childPerformer, 15, -1f), Is.EqualTo(9.5f).Within(0.001f));
        }

        [Test]
        public void SetParam_WritesAllLanes()
        {
            Entity entity = _world.Create();

            var performer = _buf.Create(1, entity, 0);
            _buf.SetParam(performer, 1, ParamLane.Float, 2.5f, 0, Vector4.Zero);
            _buf.SetParam(performer, 2, ParamLane.Int, 0f, 7, Vector4.Zero);
            _buf.SetParam(performer, 3, ParamLane.Vector, 0f, 0, new Vector4(1f, 2f, 3f, 4f));

            Assert.That(_buf.ResolveFloat(performer, 1, -1f), Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(_buf.ResolveInt(performer, 2, -1), Is.EqualTo(7));
            Assert.That(_buf.ResolveVector(performer, 3, Vector4.Zero), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
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
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, runtime: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
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
        private PerformerEntityRuntime _instances;
        private PerformerDefinitionRegistry _definitions;
        private PerformerRuntimeSystem _system;
        private PresentationRequestBuffer _requests;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _commands = new PerformerCommandBuffer();
            _events = new PresentationEventStream();
            _instances = new PerformerEntityRuntime(_world);
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

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
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

            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
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
            var staleEntity = _instances.Create(defId, firstOwner, 1);
            Assert.That(staleEntity, Is.Not.EqualTo(Entity.Null));
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
                PerformerEntity = Entity.Null,
                ParentEntity = Entity.Null,
                ScopeTag = 20,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(2));
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
                PerformerEntity = Entity.Null,
                ParamKey = 10,
                ParamLane = ParamLane.Float,
                ParamValue = 4.5f,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerEntity = Entity.Null,
                ParamKey = 11,
                ParamLane = ParamLane.Int,
                IntValue = 9,
            });
            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.SetParam,
                PerformerEntity = Entity.Null,
                ParamKey = 12,
                ParamLane = ParamLane.Vector,
                VectorValue = new Vector4(8f, 7f, 6f, 5f),
            });
            TickAndFlush(0.016f);

            var query = new QueryDescription().WithAll<PerformerState>();
            Entity performer = Entity.Null;
            _world.Query(in query, (Entity e, ref PerformerState s) => { performer = e; });
            Assert.That(performer, Is.Not.EqualTo(Entity.Null));

            Assert.That(_instances.ResolveFloat(performer, 10, -1f), Is.EqualTo(4.5f).Within(0.001f));
            Assert.That(_instances.ResolveInt(performer, 11, -1), Is.EqualTo(9));
            Assert.That(_instances.ResolveVector(performer, 12, Vector4.Zero), Is.EqualTo(new Vector4(8f, 7f, 6f, 5f)));
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

            var query = new QueryDescription().WithAll<PerformerState>();
            Entity performer = Entity.Null;
            _world.Query(in query, (Entity e, ref PerformerState s) => { performer = e; });
            Assert.That(performer, Is.Not.EqualTo(Entity.Null));
            Assert.That(_world.Get<PerformerState>(performer).BehaviorActiveMask, Is.EqualTo((1u << 0) | (1u << 2)));
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
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, runtime: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
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
        public void PerformerCreatedEvent_PromotesPayloadEntity_ToChildParentEntity()
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
                            ParentEntity = Entity.Null,
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
            Assert.That(span[0].ParentEntity.Id, Is.EqualTo(23));
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
