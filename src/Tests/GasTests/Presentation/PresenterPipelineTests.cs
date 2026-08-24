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
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

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
    public class PresenterEntityRuntimeTests
    {
        private World _world;
        private PresenterEntityRuntime _buf;
        private PresenterDefinitionRegistry _defs;
        private int _primaryDefId;
        private int _secondaryDefId;
        private int _tertiaryDefId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _buf = new PresenterEntityRuntime(_world);
            _defs = new PresenterDefinitionRegistry();
            _primaryDefId = _defs.Register("test.entity_runtime.primary", new PresenterDefinition());
            _secondaryDefId = _defs.Register("test.entity_runtime.secondary", new PresenterDefinition());
            _tertiaryDefId = _defs.Register("test.entity_runtime.tertiary", new PresenterDefinition());
            _buf.BindDefinitions(_defs);
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
            var presenter = _buf.Create(_primaryDefId, entity, 0);
            Assert.That(_world.IsAlive(presenter), Is.True);
        }

        [Test]
        public void Create_InitializesStateAndTransformDefaults()
        {
            Entity entity = _world.Create();

            var presenter = _buf.Create(_primaryDefId, entity, 7);

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            Assert.That(state.ScopeId, Is.EqualTo(7));
            Assert.That(_world.Get<PresenterWorldRotation>(presenter).Value, Is.EqualTo(Quaternion.Identity));
            Assert.That(_world.Get<PresenterWorldScale>(presenter).Value, Is.EqualTo(Vector3.One));
            Assert.That(_world.Get<PresenterTransformSource>(presenter).Value, Is.EqualTo(TransformSource.EntityTransform));
            Assert.That(state.BehaviorActiveMask, Is.EqualTo(0u));
        }

        [Test]
        public void Create_WorldAnchor_InitializesWorldFixedTransformSource()
        {
            var presenter = _buf.Create(_primaryDefId, Entity.Null, 11, PresentationAnchorKind.WorldPosition, new Vector3(1f, 2f, 3f), 321, Entity.Null, default);

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            Assert.That(state.AnchorKind, Is.EqualTo(PresentationAnchorKind.WorldPosition));
            Assert.That(_world.Get<PresenterWorldPosition>(presenter).Value, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(_world.Get<PresenterTransformSource>(presenter).Value, Is.EqualTo(TransformSource.WorldFixed));
            Assert.That(state.StableId, Is.EqualTo(321));
        }

        [Test]
        public void Create_WithParent_LinksIntoTree()
        {
            Entity entity = _world.Create();

            var parentPresenter = _buf.Create(_primaryDefId, entity, 7);
            var childPresenter = _buf.Create(_secondaryDefId, entity, 7, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPresenter, default);

            ref readonly PresenterChildren parentChildren = ref _world.Get<PresenterChildren>(parentPresenter);
            ref readonly PresenterParent childParent = ref _world.Get<PresenterParent>(childPresenter);

            Assert.That(parentChildren.Count, Is.GreaterThan(0));
            Assert.That(childParent.Parent, Is.EqualTo(parentPresenter));
        }

        [Test]
        public void CreateHierarchy_RecursivelyBuildsChildrenAndAppliesDefaults()
        {
            Entity owner = _world.Create(new VisualTransform());
            var definitions = new PresenterDefinitionRegistry();
            int childId = definitions.Register("test.child", new PresenterDefinition
            {
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 7, Lane = ParamLane.Int, IntValue = 99 }
                }
            });
            int rootId = definitions.Register("test.root", new PresenterDefinition
            {
                Children = new[]
                {
                    new ChildPresenterRef
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

            ref readonly PresenterChildren children = ref _world.Get<PresenterChildren>(root);
            Assert.That(children.Count, Is.EqualTo(1));
            Entity child = children.Get(0);
            Assert.That(_world.IsAlive(child), Is.True);
            Assert.That(_world.Get<PresenterParent>(child).Parent, Is.EqualTo(root));
            Assert.That(_world.Get<PresenterState>(child).ScopeId, Is.EqualTo(42));
            Assert.That(_buf.ResolveInt(child, 7, -1), Is.EqualTo(99));
        }

        [Test]
        public void Destroy_MakesEntityDead()
        {
            var entity = _world.Create();
            var presenter = _buf.Create(_primaryDefId, entity, 0);
            _buf.Destroy(presenter);
            Assert.That(_world.IsAlive(presenter), Is.False);
        }

        [Test]
        public void DestroyScope_DestroysAllInScope()
        {
            var e1 = _world.Create();
            var e2 = _world.Create();
            var p1 = _buf.Create(_primaryDefId, e1, 42);
            var p2 = _buf.Create(_secondaryDefId, e2, 42);
            var p3 = _buf.Create(_tertiaryDefId, e1, 99); // different scope

            _buf.DestroyScope(42);
            Assert.That(_world.IsAlive(p1), Is.False);
            Assert.That(_world.IsAlive(p2), Is.False);
            Assert.That(_world.IsAlive(p3), Is.True);
        }

        [Test]
        public void Destroy_RecursivelyDestroysChildSubtree()
        {
            Entity entity = _world.Create();

            var parentPresenter = _buf.Create(_primaryDefId, entity, 5);
            var childPresenter = _buf.Create(_secondaryDefId, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPresenter, default);
            var grandChildPresenter = _buf.Create(_tertiaryDefId, entity, 5, PresentationAnchorKind.Entity, Vector3.Zero, 0, childPresenter, default);

            _buf.Destroy(parentPresenter);

            Assert.That(_world.IsAlive(parentPresenter), Is.False);
            Assert.That(_world.IsAlive(childPresenter), Is.False);
            Assert.That(_world.IsAlive(grandChildPresenter), Is.False);
            Assert.That(_buf.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void DestroyScope_RecursivelyDestroysScopedRootsAndChildren()
        {
            Entity entity = _world.Create();

            var rootPresenter = _buf.Create(_primaryDefId, entity, 42);
            var childPresenter = _buf.Create(_secondaryDefId, entity, 99, PresentationAnchorKind.Entity, Vector3.Zero, 0, rootPresenter, default);
            var unrelatedPresenter = _buf.Create(_tertiaryDefId, entity, 77);

            int destroyed = _buf.DestroyScope(42);

            Assert.That(destroyed, Is.GreaterThanOrEqualTo(1));
            Assert.That(_world.IsAlive(rootPresenter), Is.False);
            Assert.That(_world.IsAlive(childPresenter), Is.False);
            Assert.That(_world.IsAlive(unrelatedPresenter), Is.True);
        }

        [Test]
        public void SetParam_Float_IsResolvable()
        {
            var entity = _world.Create();
            var presenter = _buf.Create(_primaryDefId, entity, 0);
            _buf.SetParam(presenter, 5, ParamLane.Float, 3.14f, 0, default);
            Assert.That(_buf.ResolveFloat(presenter, 5, -1f), Is.EqualTo(3.14f).Within(0.001f));
        }

        [Test]
        public void ResolveFloat_InheritsFromParent()
        {
            Entity entity = _world.Create();

            var parentPresenter = _buf.Create(_primaryDefId, entity, 0);
            var childPresenter = _buf.Create(_secondaryDefId, entity, 0, PresentationAnchorKind.Entity, Vector3.Zero, 0, parentPresenter, default);
            _buf.SetParam(parentPresenter, 15, ParamLane.Float, 9.5f, 0, Vector4.Zero);

            Assert.That(_buf.ResolveFloat(childPresenter, 15, -1f), Is.EqualTo(9.5f).Within(0.001f));
        }

        [Test]
        public void SetParam_WritesAllLanes()
        {
            Entity entity = _world.Create();

            var presenter = _buf.Create(_primaryDefId, entity, 0);
            _buf.SetParam(presenter, 1, ParamLane.Float, 2.5f, 0, Vector4.Zero);
            _buf.SetParam(presenter, 2, ParamLane.Int, 0f, 7, Vector4.Zero);
            _buf.SetParam(presenter, 3, ParamLane.Vector, 0f, 0, new Vector4(1f, 2f, 3f, 4f));

            Assert.That(_buf.ResolveFloat(presenter, 1, -1f), Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(_buf.ResolveInt(presenter, 2, -1), Is.EqualTo(7));
            Assert.That(_buf.ResolveVector(presenter, 3, Vector4.Zero), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
        }
    }

    [TestFixture]
    public class PresenterRuleSystemTests
    {
        private World _world;
        private PresentationEventStream _events;
        private PresenterCommandBuffer _commands;
        private PresenterDefinitionRegistry _defs;
        private GraphProgramRegistry _programs;
        private PresenterRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            _commands = new PresenterCommandBuffer();
            _defs = new PresenterDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null);
            _system = new PresenterRuleSystem(_world, _events, _commands, _defs, runtime: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
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
            var def = new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 1,
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
            Assert.That(cmds[0].CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
            Assert.That(cmds[0].PresenterDefinitionId, Is.EqualTo(1));
        }

        [Test]
        public void NonMatchingEvent_ProducesNoCommand()
        {
            var def = new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 1,
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

        [Test]
        public void UnsupportedInlineCondition_Throws()
        {
            _defs.Register("test.rule.unsupported_inline", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = new ConditionRef { Inline = (InlineConditionKind)byte.MaxValue },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 1,
                            ScopeTag = 1,
                        },
                    },
                },
            });

            var actor = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                Source = actor,
                Target = actor,
            });

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => TickAndFlush(0.016f));
            Assert.That(ex!.Message, Does.Contain("Unsupported presenter rule inline condition"));
        }

        [Test]
        public void MissingConditionGraphProgram_Throws()
        {
            _defs.Register("test.rule.missing_condition_graph", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = new ConditionRef { GraphProgramId = 9001 },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 1,
                            ScopeTag = 1,
                        },
                    },
                },
            });

            var actor = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                Source = actor,
                Target = actor,
            });

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => TickAndFlush(0.016f));
            Assert.That(ex!.Message, Does.Contain("unknown graphProgramId=9001"));
        }

        [Test]
        public void MissingParamGraphProgram_Throws()
        {
            _defs.Register("test.rule.missing_param_graph", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.SetParam,
                            ParamKey = 11,
                            ParamLane = ParamLane.Float,
                            ValueSource = PresenterCommandValueSource.Fixed,
                            ParamGraphProgramId = 9002,
                        },
                    },
                },
            });

            var actor = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                Source = actor,
                Target = actor,
            });

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => TickAndFlush(0.016f));
            Assert.That(ex!.Message, Does.Contain("paramGraphProgramId=9002"));
        }
    }

    [TestFixture]
    public class PresenterRuntimeSystemTests
    {
        private World _world;
        private PresenterCommandBuffer _commands;
        private PresentationEventStream _events;
        private PresenterEntityRuntime _instances;
        private PresenterDefinitionRegistry _definitions;
        private PresenterRuntimeSystem _system;
        private PresentationRequestBuffer _requests;
        private TransientMarkerBuffer _markers;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _commands = new PresenterCommandBuffer();
            _events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            _instances = new PresenterEntityRuntime(_world);
            _definitions = new PresenterDefinitionRegistry();
            _markers = new TransientMarkerBuffer();
            _requests = new PresentationRequestBuffer();
            _system = new PresenterRuntimeSystem(_world, _commands, _events, _markers, _requests, _instances, new Ludots.Core.Presentation.PresentationStableIdAllocator(), _definitions);
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
        public void CreatePresenterCommand_AllocatesInstance()
        {
            var owner = _world.Create();
            _definitions.Register("test.runtime.basic", new PresenterDefinition
            {
                DefaultLifetime = 1f,
            });
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = _definitions.GetId("test.runtime.basic"),
                ScopeTag = 5,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyPresenterScopeCommand_ReleasesInstances()
        {
            var owner = _world.Create();
            _definitions.Register("test.runtime.scope", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = _definitions.GetId("test.runtime.scope"),
                ScopeTag = 7,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                PresenterDefinitionId = 7,
                ScopeTag = 7,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void EntityDestroyedEvent_ReleasesOwnerWithFivePresenters()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.owner_many", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });
            for (int i = 0; i < 5; i++)
            {
                Entity presenter = _instances.Create(defId, owner, scopeId: i + 1);
                Assert.That(presenter, Is.Not.EqualTo(Entity.Null));
            }

            Assert.That(_instances.ActiveCount, Is.EqualTo(5));
            Assert.That(_events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntityDestroyed,
                Source = owner,
                Target = owner,
            }), Is.True);

            Assert.DoesNotThrow(() => TickAndFlush(0.016f));

            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Update_WhenPaused_DoesNotTickTransientMarkers()
        {
            Assert.That(_markers.TryAddMesh(
                meshAssetId: 1,
                position: Vector3.Zero,
                scale: Vector3.One,
                color: Vector4.One,
                lifetimeSeconds: 0.25f), Is.True);

            Assert.DoesNotThrow(() => TickAndFlush(0f));

            Assert.That(_markers.Count, Is.EqualTo(1));
            Assert.That(_requests.Count, Is.EqualTo(0));
        }

        [Test]
        public void Update_WhenNegativeDeltaTime_Throws()
        {
            Assert.That(
                () => TickAndFlush(-0.01f),
                Throws.InvalidOperationException.With.Message.Contains("PresenterRuntimeSystem dt"));
        }

        [Test]
        public void CreatePresenterCommand_ReleasesDeadOwnerInstancesBeforeAllocating()
        {
            _definitions.Register("test.runtime.dead_owner", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.dead_owner");
            var firstOwner = _world.Create();
            var staleEntity = _instances.Create(defId, firstOwner, 1);
            Assert.That(staleEntity, Is.Not.EqualTo(Entity.Null));
            _world.Destroy(firstOwner);

            var secondOwner = _world.Create();
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 2,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = secondOwner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void CreatePresenterCommand_DedupesPersistentScopedInstance()
        {
            _definitions.Register("test.runtime.persistent_scope", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.persistent_scope");
            var owner = _world.Create();

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 77,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 77,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });

            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyScopedPresenterCommand_DestroysUniqueScopedWorldPositionInstanceWithoutEventPosition()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.world_scope_destroy", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 91,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.WorldPosition,
                Position = new Vector3(3f, 0.03f, 0f),
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyScopedPresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 91,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void CreatePresenterCommand_AppliesInitialParamPayloadToCreatedInstance()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.initial_param", new PresenterDefinition
            {
                DefaultLifetime = 1f,
            });

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 9,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
                HasParamPayload = true,
                ParamKey = WellKnownPresenterParamKeys.TextValue0,
                ParamLane = ParamLane.Float,
                ParamValue = -33f,
            });

            TickAndFlush(0.016f);

            Entity presenter = Entity.Null;
            var query = new QueryDescription().WithAll<PresenterState>();
            _world.Query(in query, (Entity e, ref PresenterState s) => { presenter = e; });
            Assert.That(presenter, Is.Not.EqualTo(Entity.Null));
            Assert.That(_instances.ResolveFloat(presenter, WellKnownPresenterParamKeys.TextValue0, 0f), Is.EqualTo(-33f).Within(0.001f));
        }

        [Test]
        public void CreatePresenterCommand_WithParentHandle_LinksChildAndInheritsDefaults()
        {
            var owner = _world.Create();
            int parentDefId = _definitions.Register("test.runtime.parent", new PresenterDefinition
            {
                DefaultLifetime = -1f,
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 100, Lane = ParamLane.Int, IntValue = 3 }
                }
            });
            int childDefId = _definitions.Register("test.runtime.child", new PresenterDefinition
            {
                DefaultLifetime = -1f,
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = 200, Lane = ParamLane.Float, FloatValue = 2.25f }
                }
            });

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = parentDefId,
                ScopeTag = 10,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = childDefId,
                PresenterEntity = Entity.Null,
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
            int defId = _definitions.Register("test.runtime.param_lanes", new PresenterDefinition
            {
                DefaultLifetime = -1f,
            });

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 1,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);
            _commands.Clear();

            var query = new QueryDescription().WithAll<PresenterState>();
            Entity presenter = Entity.Null;
            _world.Query(in query, (Entity e, ref PresenterState s) => { presenter = e; });
            Assert.That(presenter, Is.Not.EqualTo(Entity.Null));

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SetParam,
                PresenterEntity = presenter,
                ParamKey = 10,
                ParamLane = ParamLane.Float,
                ParamValue = 4.5f,
            });
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SetParam,
                PresenterEntity = presenter,
                ParamKey = 11,
                ParamLane = ParamLane.Int,
                IntValue = 9,
            });
            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SetParam,
                PresenterEntity = presenter,
                ParamKey = 12,
                ParamLane = ParamLane.Vector,
                VectorValue = new Vector4(8f, 7f, 6f, 5f),
            });
            TickAndFlush(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, 10, -1f), Is.EqualTo(4.5f).Within(0.001f));
            Assert.That(_instances.ResolveInt(presenter, 11, -1), Is.EqualTo(9));
            Assert.That(_instances.ResolveVector(presenter, 12, Vector4.Zero), Is.EqualTo(new Vector4(8f, 7f, 6f, 5f)));
        }

        [Test]
        public void CreatePresenterDefinition_WithActiveByDefaultBehaviors_SeedsBehaviorMask()
        {
            var owner = _world.Create();
            int defId = _definitions.Register("test.runtime.behaviors", new PresenterDefinition
            {
                DefaultLifetime = -1f,
                Behaviors = new[]
                {
                    new BehaviorSlot { SlotIndex = 0, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 2, ActiveByDefault = true },
                    new BehaviorSlot { SlotIndex = 5, ActiveByDefault = false },
                }
            });

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 3,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickAndFlush(0.016f);

            var query = new QueryDescription().WithAll<PresenterState>();
            Entity presenter = Entity.Null;
            _world.Query(in query, (Entity e, ref PresenterState s) => { presenter = e; });
            Assert.That(presenter, Is.Not.EqualTo(Entity.Null));
            Assert.That(_world.Get<PresenterState>(presenter).BehaviorActiveMask, Is.EqualTo((1u << 0) | (1u << 2)));
        }
    }

    [TestFixture]
    public class GameplayPresentationProjectionGasTests
    {
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private PresentationEventStream _stream;
        private GameplayPresentationProjectionSystem _projection;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer(8);
            _stream = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var eventBus = new GameplayEventBus();
            var session = new GameSession();
            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            _projection = new GameplayPresentationProjectionSystem(_world, eventBus, _stream, session, _gasEvents, ownerChanges);
        }

        [TearDown]
        public void TearDown()
        {
            _projection?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void EffectApplied_ProjectedToStream()
        {
            var attacker = _world.Create();
            var defender = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = attacker,
                Target = defender,
                Delta = -25f,
                AttributeId = 1,
                EffectTemplateId = 10,
            });

            _projection.Update(0.016f);

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
                Assert.That(span[i].Source, Is.EqualTo(defender));
                Assert.That(span[i].Target, Is.EqualTo(attacker));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "EffectApplied event not projected");
            Assert.That(_gasEvents.Count, Is.EqualTo(0), "Gas presentation events should be cleared only after projection consumes them.");
        }

        [Test]
        public void EffectActivated_ProjectedToStream_ReceiverSide()
        {
            var attacker = _world.Create();
            var defender = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectActivated,
                Actor = attacker,
                Target = defender,
                Delta = 1f,
                AttributeId = 3,
                EffectTemplateId = 12,
            });

            _projection.Update(0.016f);

            var span = _stream.GetSpan();
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(1));
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind != PresentationEventKind.EffectActivated)
                {
                    continue;
                }

                Assert.That(span[i].Magnitude, Is.EqualTo(1f));
                Assert.That(span[i].PayloadA, Is.EqualTo(3));
                Assert.That(span[i].KeyId, Is.EqualTo(12));
                Assert.That(span[i].Source, Is.EqualTo(defender));
                Assert.That(span[i].Target, Is.EqualTo(attacker));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "EffectActivated event not projected receiver-side");
        }

        [Test]
        public void CastCommitted_ProjectedToStream()
        {
            var actor = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                AbilitySlot = 2,
                AbilityId = 42,
            });

            _projection.Update(0.016f);

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

            Assert.That(found, Is.True, "CastCommitted event not projected");
        }

        [Test]
        public void CastFailed_ProjectedToStream()
        {
            var actor = _world.Create();
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                AbilitySlot = 1,
                AbilityId = 5,
                FailReason = AbilityCastFailReason.TimedLockout,
            });

            _projection.Update(0.016f);

            var span = _stream.GetSpan();
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind != PresentationEventKind.CastFailed)
                {
                    continue;
                }

                Assert.That(span[i].PayloadB, Is.EqualTo((int)AbilityCastFailReason.TimedLockout));
                found = true;
                break;
            }

            Assert.That(found, Is.True, "CastFailed event not projected");
        }
    }

    [TestFixture]
    public class CorePresenterDefinitionTests
    {
        [Test]
        public void LoadFromJson_AllCoreBuiltinIds_Present()
        {
            var registry = new PresenterDefinitionRegistry();
            LoadCorePresenterDefinitions(registry);

            Assert.That(registry.TryGet(registry.GetId(WellKnownPresenterKeys.CastCommittedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPresenterKeys.CastFailedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPresenterKeys.FloatingCombatText), out _), Is.True);
        }

        [Test]
        public void FloatingCombatText_HasYDriftAndAlphaFade()
        {
            var registry = new PresenterDefinitionRegistry();
            LoadCorePresenterDefinitions(registry);
            registry.TryGet(registry.GetId(WellKnownPresenterKeys.FloatingCombatText), out var def);

            Assert.That(def.PositionYDriftPerSecond, Is.GreaterThan(0f));
            Assert.That(def.AlphaFadeOverLifetime, Is.True);
            Assert.That(def.DefaultLifetime, Is.GreaterThan(0f));
        }

        [Test]
        public void EntityHealthBar_IsConfigDefined_NotBuiltin()
        {
            var registry = new PresenterDefinitionRegistry();
            LoadCorePresenterDefinitions(registry);

            Assert.That(registry.GetId(WellKnownPresenterKeys.EntityHealthBar), Is.GreaterThan(0));
        }

        private static void LoadCorePresenterDefinitions(PresenterDefinitionRegistry registry)
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
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);
            var materialAssets = new PresentationMaterialRegistry();
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            var templateRegistry = new DataRegistry<EntityTemplate>(pipeline);
            templateRegistry.Load("Entities/templates.json", catalog);
            var templateKeys = new EntityTemplateKeyRegistry();
            foreach (EntityTemplate template in templateRegistry.GetAll())
            {
                templateKeys.Register(template.Id);
            }

            var animatorControllers = new AnimatorControllerRegistry();
            new AnimatorControllerConfigLoader(pipeline, animatorControllers).Load(catalog);
            var animationClips = new AnimationClipRegistry();
            new AnimationClipConfigLoader(pipeline, animationClips).Load(catalog);
            var animationProfiles = new AnimationProfileRegistry();
            new AnimationProfileConfigLoader(pipeline, animationProfiles, animatorControllers, animationClips).Load(catalog);

            new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: name => string.Equals(name, "Health", StringComparison.Ordinal) ? healthAttrId : 0,
                resolveMeshId: meshes.GetId,
                resolveTextTokenId: textCatalog.GetTokenId,
                resolveEntityTemplateKey: templateKeys.GetId,
                resolveMaterialId: materialAssets.GetId,
                resolveAnimatorControllerId: animatorControllers.GetId,
                resolveAnimationProfileId: animationProfiles.GetId,
                resolveBehaviorAssetId: (kind, key) => kind switch
                {
                    AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Spline or AssetKind.Sound => meshes.GetId(key),
                    AssetKind.WorldText => textCatalog.GetTokenId(key),
                    AssetKind.GroundOverlay => Enum.TryParse<GroundOverlayShape>(key, ignoreCase: false, out var shape) ? (int)shape : 0,
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
    public class PresenterLifecycleRuleFilterTests
    {
        private World _world;
        private PresentationEventStream _events;
        private PresenterCommandBuffer _commands;
        private PresenterDefinitionRegistry _defs;
        private GraphProgramRegistry _programs;
        private PresenterRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            _commands = new PresenterCommandBuffer();
            _defs = new PresenterDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null);
            _system = new PresenterRuleSystem(_world, _events, _commands, _defs, runtime: null, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
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
            _defs.Register("test.lifecycle.mismatch", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Condition = new ConditionRef { Inline = InlineConditionKind.EventMagnitudePositive },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 77,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
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
            _defs.Register("test.lifecycle.match", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Condition = new ConditionRef { Inline = InlineConditionKind.EventMagnitudePositive },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 77,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
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
                Magnitude = 1f,
            });

            _system.Update(0.016f);

            var span = _commands.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
            Assert.That(span[0].PresenterDefinitionId, Is.EqualTo(77));
            Assert.That(span[0].ScopeTag, Is.EqualTo(456));
            Assert.That(span[0].Source, Is.EqualTo(owner));
        }

        [Test]
        public void PresenterCreatedEvent_PromotesPayloadEntity_ToChildParentEntity()
        {
            _defs.Register("test.lifecycle.child", new PresenterDefinition
            {
                Rules = new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.PresenterCreated, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = 99,
                            ParentEntity = Entity.Null,
                            ScopeTag = 55,
                        }
                    }
                }
            });

            var owner = _world.Create();
            var parentPresenter = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.PresenterCreated,
                KeyId = 1,
                Source = owner,
                Target = owner,
                PayloadA = 23,
                PayloadB = 77,
                PresenterEntity = parentPresenter,
            });

            _system.Update(0.016f);

            var span = _commands.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].PresenterDefinitionId, Is.EqualTo(99));
            Assert.That(span[0].ParentEntity, Is.EqualTo(parentPresenter));
            Assert.That(span[0].ScopeTag, Is.EqualTo(55));
        }
    }

    [TestFixture]
    public class WellKnownPresenterParamKeysTests
    {
        [Test]
        public void BarConstants_MatchEmitSystemConventions()
        {
            // These values must stay aligned with the canonical world-bar param mapping.
            Assert.That(WellKnownPresenterParamKeys.BarFillRatio, Is.EqualTo(0));
            Assert.That(WellKnownPresenterParamKeys.BarWidth, Is.EqualTo(1));
            Assert.That(WellKnownPresenterParamKeys.BarHeight, Is.EqualTo(2));
            Assert.That(WellKnownPresenterParamKeys.BarForegroundR, Is.EqualTo(4));
            Assert.That(WellKnownPresenterParamKeys.BarForegroundG, Is.EqualTo(5));
            Assert.That(WellKnownPresenterParamKeys.BarForegroundB, Is.EqualTo(6));
            Assert.That(WellKnownPresenterParamKeys.BarForegroundA, Is.EqualTo(7));
            Assert.That(WellKnownPresenterParamKeys.BarBackgroundR, Is.EqualTo(8));
            Assert.That(WellKnownPresenterParamKeys.BarBackgroundG, Is.EqualTo(9));
            Assert.That(WellKnownPresenterParamKeys.BarBackgroundB, Is.EqualTo(10));
            Assert.That(WellKnownPresenterParamKeys.BarBackgroundA, Is.EqualTo(11));
        }

        [Test]
        public void TextConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPresenterParamKeys.TextValue0, Is.EqualTo(0));
            Assert.That(WellKnownPresenterParamKeys.TextValue1, Is.EqualTo(1));
            Assert.That(WellKnownPresenterParamKeys.TextFontSize, Is.EqualTo(3));
            Assert.That(WellKnownPresenterParamKeys.TextColorR, Is.EqualTo(4));
            Assert.That(WellKnownPresenterParamKeys.TextTokenId, Is.EqualTo(15));
        }

        [Test]
        public void OverlayConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPresenterParamKeys.OverlayRadius, Is.EqualTo(0));
            Assert.That(WellKnownPresenterParamKeys.OverlayInnerRadius, Is.EqualTo(1));
            Assert.That(WellKnownPresenterParamKeys.OverlayAngle, Is.EqualTo(2));
            Assert.That(WellKnownPresenterParamKeys.OverlayRotation, Is.EqualTo(3));
            Assert.That(WellKnownPresenterParamKeys.OverlayBorderWidth, Is.EqualTo(12));
            Assert.That(WellKnownPresenterParamKeys.OverlayLength, Is.EqualTo(13));
            Assert.That(WellKnownPresenterParamKeys.OverlayWidth, Is.EqualTo(14));
        }

        [Test]
        public void MarkerConstants_MatchEmitSystemConventions()
        {
            Assert.That(WellKnownPresenterParamKeys.MarkerScale, Is.EqualTo(0));
            Assert.That(WellKnownPresenterParamKeys.MarkerScaleX, Is.EqualTo(1));
            Assert.That(WellKnownPresenterParamKeys.MarkerScaleY, Is.EqualTo(2));
            Assert.That(WellKnownPresenterParamKeys.MarkerScaleZ, Is.EqualTo(3));
            Assert.That(WellKnownPresenterParamKeys.MarkerColorR, Is.EqualTo(4));
            Assert.That(WellKnownPresenterParamKeys.MarkerColorG, Is.EqualTo(5));
            Assert.That(WellKnownPresenterParamKeys.MarkerColorB, Is.EqualTo(6));
            Assert.That(WellKnownPresenterParamKeys.MarkerColorA, Is.EqualTo(7));
        }
    }
}
