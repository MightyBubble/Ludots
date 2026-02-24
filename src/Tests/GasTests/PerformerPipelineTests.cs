using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
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
    }

    [TestFixture]
    public class PerformerRuleSystemTests
    {
        private World _world;
        private PresentationEventStream _events;
        private PresentationCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private GraphProgramRegistry _programs;
        private PerformerRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream();
            _commands = new PresentationCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null);
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, _programs, api, new System.Collections.Generic.Dictionary<string, object>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void MatchingEvent_ProducesCommand()
        {
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = 1,
                            ScopeId = -1,
                        }
                    }
                }
            };
            _defs.Register(1, def);

            var actor = _world.Create();
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.CastCommitted,
                KeyId = 5,
                Source = actor,
                Target = actor,
            });

            _system.Update(0.016f);

            var cmds = _commands.GetSpan();
            Assert.That(cmds.Length, Is.EqualTo(1));
            Assert.That(cmds[0].Kind, Is.EqualTo(PresentationCommandKind.CreatePerformer));
            Assert.That(cmds[0].IdA, Is.EqualTo(1)); // PerformerDefinitionId
        }

        [Test]
        public void NonMatchingEvent_ProducesNoCommand()
        {
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = 1,
                            ScopeId = -1,
                        }
                    }
                }
            };
            _defs.Register(1, def);

            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EffectApplied, // wrong kind
                KeyId = 5,
            });

            _system.Update(0.016f);

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
            _system.Update(0.016f);

            Assert.That(_events.Count, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class PerformerRuntimeSystemTests
    {
        private World _world;
        private PresentationCommandBuffer _commands;
        private PerformerInstanceBuffer _instances;
        private PerformerRuntimeSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _commands = new PresentationCommandBuffer();
            _instances = new PerformerInstanceBuffer();
            var prefabs = new Ludots.Core.Presentation.Assets.PrefabRegistry();
            var draw = new PrimitiveDrawBuffer();
            var markers = new TransientMarkerBuffer();
            _system = new PerformerRuntimeSystem(_world, prefabs, _commands, draw, markers, _instances);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void CreatePerformerCommand_AllocatesInstance()
        {
            var owner = _world.Create();
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = 100, // defId
                IdB = 5,   // scopeId
                Source = owner,
            });

            _system.Update(0.016f);

            Assert.That(_instances.IsActive(0), Is.True);
        }

        [Test]
        public void DestroyPerformerScopeCommand_ReleasesInstances()
        {
            var owner = _world.Create();
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = 100,
                IdB = 7,
                Source = owner,
            });
            _system.Update(0.016f);
            _commands.Clear();

            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = 7,
            });
            _system.Update(0.016f);

            Assert.That(_instances.IsActive(0), Is.False);
        }
    }

    [TestFixture]
    public class PerformerEmitSystemTests
    {
        private World _world;
        private PerformerInstanceBuffer _instances;
        private PerformerDefinitionRegistry _defs;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private PerformerEmitSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _instances = new PerformerInstanceBuffer();
            _defs = new PerformerDefinitionRegistry();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();
            var programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, null, null, null);
            var globals = new System.Collections.Generic.Dictionary<string, object>();
            _system = new PerformerEmitSystem(_world, _instances, _defs, _overlays, _primitives, _hud, programs, api, globals);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void InstanceScoped_Marker3D_EmitsToPrimitiveBuffer()
        {
            var entity = _world.Create(new VisualTransform { Position = new Vector3(1, 2, 3) });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultColor = new Vector4(1, 0, 0, 1),
                DefaultScale = 0.5f,
                DefaultLifetime = 1f,
            };
            _defs.Register(50, def);
            _instances.TryAllocate(50, entity, 0, out _);

            _system.Update(0.016f);

            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].MeshAssetId, Is.EqualTo(2));
            Assert.That(span[0].Scale.X, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void InstanceScoped_AutoExpires_AfterLifetime()
        {
            var entity = _world.Create(new VisualTransform { Position = Vector3.Zero });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 1,
                DefaultLifetime = 0.1f,
            };
            _defs.Register(60, def);
            _instances.TryAllocate(60, entity, 0, out int handle);

            // Tick past lifetime
            _system.Update(0.05f);
            Assert.That(_instances.IsActive(handle), Is.True);

            _system.Update(0.06f); // total elapsed > 0.1
            Assert.That(_instances.IsActive(handle), Is.False);
        }

        [Test]
        public void InstanceScoped_AlphaFade_ReducesAlpha()
        {
            var entity = _world.Create(new VisualTransform { Position = Vector3.Zero });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultColor = new Vector4(1, 1, 1, 1),
                DefaultScale = 1f,
                DefaultLifetime = 1f,
                AlphaFadeOverLifetime = true,
            };
            _defs.Register(70, def);
            _instances.TryAllocate(70, entity, 0, out _);

            // Tick to 50% of lifetime
            _system.Update(0.5f);
            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Color.W, Is.LessThan(1f));
            Assert.That(span[0].Color.W, Is.GreaterThan(0f));
        }

        [Test]
        public void InstanceScoped_YDrift_OffsetsPosition()
        {
            var entity = _world.Create(new VisualTransform { Position = new Vector3(0, 0, 0) });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultScale = 1f,
                DefaultLifetime = 2f,
                PositionYDriftPerSecond = 1f, // 1 meter per second
            };
            _defs.Register(80, def);
            _instances.TryAllocate(80, entity, 0, out _);

            _system.Update(1f); // 1 second → Y should be ~1.0
            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Position.Y, Is.GreaterThan(0.5f));
        }

        [Test]
        public void EntityScoped_WorldBar_EmitsForMatchingEntities()
        {
            // Create two entities with VisualTransform + AttributeBuffer
            var e1 = _world.Create(new VisualTransform { Position = new Vector3(1, 0, 0) }, new AttributeBuffer());
            var e2 = _world.Create(new VisualTransform { Position = new Vector3(2, 0, 0) }, new AttributeBuffer());
            // One entity without AttributeBuffer — should NOT get a bar
            var e3 = _world.Create(new VisualTransform { Position = new Vector3(3, 0, 0) });

            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                EntityScope = EntityScopeFilter.AllWithAttributes,
                DefaultColor = new Vector4(0, 1, 0, 1),
                PositionOffset = new Vector3(0, 0.5f, 0),
            };
            _defs.Register(90, def);

            _system.Update(0.016f);

            var span = _hud.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2)); // e1 and e2 only
        }

        [Test]
        public void EntityScoped_CullState_HidesInvisibleEntities()
        {
            var visible = _world.Create(
                new VisualTransform { Position = Vector3.Zero },
                new AttributeBuffer(),
                new CullState { IsVisible = true });
            var hidden = _world.Create(
                new VisualTransform { Position = Vector3.One },
                new AttributeBuffer(),
                new CullState { IsVisible = false });

            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                EntityScope = EntityScopeFilter.AllWithAttributes,
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
            };
            _defs.Register(91, def);

            _system.Update(0.016f);

            var span = _hud.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1)); // only visible entity
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
            // Find the EffectApplied event
            bool found = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == PresentationEventKind.EffectApplied)
                {
                    Assert.That(span[i].Magnitude, Is.EqualTo(-25f));
                    Assert.That(span[i].PayloadA, Is.EqualTo(1)); // attributeId
                    Assert.That(span[i].KeyId, Is.EqualTo(10));   // effectTemplateId
                    found = true;
                    break;
                }
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
                if (span[i].Kind == PresentationEventKind.CastCommitted)
                {
                    Assert.That(span[i].PayloadA, Is.EqualTo(2)); // slot
                    Assert.That(span[i].KeyId, Is.EqualTo(42));   // abilityId
                    found = true;
                    break;
                }
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
                if (span[i].Kind == PresentationEventKind.CastFailed)
                {
                    Assert.That(span[i].PayloadB, Is.EqualTo((int)AbilityCastFailReason.OnCooldown));
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True, "CastFailed event not bridged");
        }
    }

    [TestFixture]
    public class BuiltinPerformerDefinitionTests
    {
        [Test]
        public void Register_AllBuiltinIds_Present()
        {
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(registry);

            Assert.That(registry.TryGet(BuiltinPerformerIds.CastCommittedMarker, out _), Is.True);
            Assert.That(registry.TryGet(BuiltinPerformerIds.CastFailedMarker, out _), Is.True);
            Assert.That(registry.TryGet(BuiltinPerformerIds.FloatingCombatText, out _), Is.True);
            Assert.That(registry.TryGet(BuiltinPerformerIds.EntityHealthBar, out _), Is.True);
        }

        [Test]
        public void FloatingCombatText_HasYDriftAndAlphaFade()
        {
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(registry);
            registry.TryGet(BuiltinPerformerIds.FloatingCombatText, out var def);

            Assert.That(def.PositionYDriftPerSecond, Is.GreaterThan(0f));
            Assert.That(def.AlphaFadeOverLifetime, Is.True);
            Assert.That(def.DefaultLifetime, Is.GreaterThan(0f));
        }

        [Test]
        public void EntityHealthBar_IsEntityScoped()
        {
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(registry);
            registry.TryGet(BuiltinPerformerIds.EntityHealthBar, out var def);

            Assert.That(def.EntityScope, Is.EqualTo(EntityScopeFilter.AllWithAttributes));
            Assert.That(def.VisualKind, Is.EqualTo(PerformerVisualKind.WorldBar));
        }
    }
}
