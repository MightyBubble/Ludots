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
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
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
            int defId = _defs.GetOrRegisterId("test_1");
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
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = defId,
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

            _system.Update(0.016f);

            var cmds = _commands.GetSpan();
            Assert.That(cmds.Length, Is.EqualTo(1));
            Assert.That(cmds[0].Kind, Is.EqualTo(PresentationCommandKind.CreatePerformer));
            Assert.That(cmds[0].IdA, Is.EqualTo(defId));
        }

        [Test]
        public void NonMatchingEvent_ProducesNoCommand()
        {
            int defId = _defs.GetOrRegisterId("test_1");
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
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = defId,
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
        private PerformerDefinitionRegistry _definitions;
        private PerformerRuntimeSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _commands = new PresentationCommandBuffer();
            _instances = new PerformerInstanceBuffer();
            _definitions = new PerformerDefinitionRegistry();
            var prefabs = new Ludots.Core.Presentation.Assets.PrefabRegistry();
            var draw = new PrimitiveDrawBuffer();
            var markers = new TransientMarkerBuffer();
            _system = new PerformerRuntimeSystem(_world, prefabs, _commands, draw, markers, _instances, new Ludots.Core.Presentation.PresentationStableIdAllocator(), _definitions);
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
            _definitions.Register("test.runtime.basic", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                DefaultLifetime = 1f,
            });
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = _definitions.GetId("test.runtime.basic"),
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
            _definitions.Register("test.runtime.scope", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                DefaultLifetime = -1f,
            });
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = _definitions.GetId("test.runtime.scope"),
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

        [Test]
        public void CreatePerformerCommand_ReleasesDeadOwnerInstancesBeforeAllocating()
        {
            _definitions.Register("test.runtime.dead_owner", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.dead_owner");
            var firstOwner = _world.Create();
            Assert.That(_instances.TryAllocate(defId, firstOwner, 1, out _), Is.True);
            _world.Destroy(firstOwner);

            var secondOwner = _world.Create();
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = defId,
                IdB = 2,
                Source = secondOwner,
            });

            _system.Update(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
            bool hasFirstOwnerInstance = false;
            bool hasSecondOwnerInstance = false;
            _instances.ProcessActive(0f, (int _, ref PerformerInstance instance) =>
            {
                hasFirstOwnerInstance |= instance.Owner == firstOwner;
                hasSecondOwnerInstance |= instance.Owner == secondOwner;
            });
            Assert.That(hasFirstOwnerInstance, Is.False);
            Assert.That(hasSecondOwnerInstance, Is.True);
        }

        [Test]
        public void CreatePerformerCommand_DedupesPersistentScopedInstance()
        {
            _definitions.Register("test.runtime.persistent_scope", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                DefaultLifetime = -1f,
            });
            int defId = _definitions.GetId("test.runtime.persistent_scope");
            var owner = _world.Create();

            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = defId,
                IdB = 77,
                Source = owner,
            });
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = defId,
                IdB = 77,
                Source = owner,
            });

            _system.Update(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(1));
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
        private RoadSplineBuffer _roadSplines;
        private PerformerEmitSystem _system;
        private System.Collections.Generic.Dictionary<string, object> _globals;
        private RenderDebugState _renderDebug;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _instances = new PerformerInstanceBuffer();
            _defs = new PerformerDefinitionRegistry();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();
            _roadSplines = new RoadSplineBuffer();
            var programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, null, null, null);
            _globals = new System.Collections.Generic.Dictionary<string, object>();
            _renderDebug = new RenderDebugState();
            _globals[CoreServiceKeys.RenderDebugState.Name] = _renderDebug;
            _system = new PerformerEmitSystem(_world, _instances, _defs, _overlays, _primitives, _hud, programs, api, _globals, roadSplines: _roadSplines);
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
            var entity = _world.Create(
                new PresentationStableId { Value = 1001 },
                new VisualTransform { Position = new Vector3(1, 2, 3) });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultColor = new Vector4(1, 0, 0, 1),
                DefaultScale = 0.5f,
                DefaultLifetime = 1f,
            };
            int defId = _defs.Register("test_50", def);
            _instances.TryAllocate(defId, entity, 0, out _);

            _system.Update(0.016f);

            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].MeshAssetId, Is.EqualTo(2));
            Assert.That(span[0].Scale.X, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void InstanceScoped_Marker3D_CanInheritOwnerRotation()
        {
            Quaternion ownerRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.75f);
            var entity = _world.Create(
                new PresentationStableId { Value = 1002 },
                new VisualTransform { Position = new Vector3(1, 2, 3), Rotation = ownerRotation });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultScale = 1f,
                Bindings = new[]
                {
                    new PerformerParamBinding
                    {
                        ParamKey = WellKnownPerformerParamKeys.MarkerUseOwnerRotation,
                        Value = ValueRef.FromConstant(1f)
                    }
                }
            };
            int defId = _defs.Register("test_marker_owner_rotation", def);
            _instances.TryAllocate(defId, entity, 0, out _);

            _system.Update(0.016f);

            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            float similarity = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(span[0].Rotation), Quaternion.Normalize(ownerRotation)));
            Assert.That(similarity, Is.GreaterThanOrEqualTo(0.9999f));
        }

        [Test]
        public void WorldAnchored_InstanceScoped_Marker3D_EmitsAtWorldAnchor()
        {
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultColor = new Vector4(0f, 1f, 0f, 1f),
                DefaultScale = 1f,
            };

            int defId = _defs.Register("test_world_anchor", def);
            _instances.TryAllocate(defId, default, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 0.5f, 9f), 123, out _);

            _system.Update(0.016f);

            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].StableId, Is.EqualTo(123));
            Assert.That(span[0].Position.X, Is.EqualTo(7f).Within(0.01f));
            Assert.That(span[0].Position.Z, Is.EqualTo(9f).Within(0.01f));
        }

        [Test]
        public void InstanceScoped_AutoExpires_AfterLifetime()
        {
            var entity = _world.Create(
                new PresentationStableId { Value = 1003 },
                new VisualTransform { Position = Vector3.Zero });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 1,
                DefaultLifetime = 0.1f,
            };
            int defId = _defs.Register("test_60", def);
            _instances.TryAllocate(defId, entity, 0, out int handle);

            // Tick past lifetime
            _system.Update(0.05f);
            Assert.That(_instances.IsActive(handle), Is.True);

            _system.Update(0.06f); // total elapsed > 0.1
            Assert.That(_instances.IsActive(handle), Is.False);
        }

        [Test]
        public void InstanceScoped_AlphaFade_ReducesAlpha()
        {
            var entity = _world.Create(
                new PresentationStableId { Value = 1004 },
                new VisualTransform { Position = Vector3.Zero });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultColor = new Vector4(1, 1, 1, 1),
                DefaultScale = 1f,
                DefaultLifetime = 1f,
                AlphaFadeOverLifetime = true,
            };
            int defId = _defs.Register("test_70", def);
            _instances.TryAllocate(defId, entity, 0, out _);

            // Tick to 50% of lifetime
            _system.Update(0.5f);
            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Color.W, Is.LessThan(1f));
            Assert.That(span[0].Color.W, Is.GreaterThan(0f));
        }

        [Test]
        public void InstanceScoped_GroundOverlay_FacingRadiansBinding_UsesOwnerFacingAngle()
        {
            var entity = _world.Create(
                new VisualTransform { Position = Vector3.Zero },
                new FacingDirection { AngleRad = MathF.PI * 0.5f });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.GroundOverlay,
                MeshOrShapeId = (int)GroundOverlayShape.Line,
                DefaultScale = 1f,
                Bindings = new[]
                {
                    new PerformerParamBinding
                    {
                        ParamKey = WellKnownPerformerParamKeys.OverlayRotation,
                        Value = ValueRef.FromFacingRadians()
                    }
                }
            };
            int defId = _defs.Register("test_overlay_facing_radians", def);
            _instances.TryAllocate(defId, entity, 0, out _);

            _system.Update(0.016f);

            var span = _overlays.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Rotation, Is.EqualTo(MathF.PI * 0.5f).Within(0.0001f));
        }

        [Test]
        public void InstanceScoped_YDrift_OffsetsPosition()
        {
            var entity = _world.Create(
                new PresentationStableId { Value = 1005 },
                new VisualTransform { Position = new Vector3(0, 0, 0) });
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.Marker3D,
                MeshOrShapeId = 2,
                DefaultScale = 1f,
                DefaultLifetime = 2f,
                PositionYDriftPerSecond = 1f, // 1 meter per second
            };
            int defId = _defs.Register("test_80", def);
            _instances.TryAllocate(defId, entity, 0, out _);

            _system.Update(1f); // 1 second → Y should be ~1.0
            var span = _primitives.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Position.Y, Is.GreaterThan(0.5f));
        }

        [Test]
        public void InstanceScoped_WorldBar_EmitsForAllocatedEntities()
        {
            // Create two entities with VisualTransform + AttributeBuffer
            var e1 = _world.Create(new VisualTransform { Position = new Vector3(1, 0, 0) }, new AttributeBuffer());
            var e2 = _world.Create(new VisualTransform { Position = new Vector3(2, 0, 0) }, new AttributeBuffer());
            // One entity without AttributeBuffer — should NOT get a bar
            var e3 = _world.Create(new VisualTransform { Position = new Vector3(3, 0, 0) });

            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                DefaultColor = new Vector4(0, 1, 0, 1),
                PositionOffset = new Vector3(0, 0.5f, 0),
            };
            int defId = _defs.Register("test_90", def);
            _instances.TryAllocate(defId, e1, 1001, out _);
            _instances.TryAllocate(defId, e2, 1002, out _);

            _system.Update(0.016f);

            var span = _hud.GetSpan();
            Assert.That(span.Length, Is.EqualTo(2)); // e1 and e2 only
            Assert.That(_world.IsAlive(e3), Is.True);
        }

        [Test]
        public void InstanceScoped_CullState_HidesInvisibleEntities()
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
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
            };
            int defId = _defs.Register("test_91", def);
            _instances.TryAllocate(defId, visible, 1001, out _);
            _instances.TryAllocate(defId, hidden, 1002, out _);

            _system.Update(0.016f);

            var span = _hud.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1)); // only visible entity
        }

        [Test]
        public void InstanceScoped_RoadSpline_EmitsControlPointsAndColorsWithoutParamCollisions()
        {
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.RoadSpline,
                DefaultColor = new Vector4(0.25f, 0.35f, 0.45f, 0.9f),
                DefaultScale = 0.75f,
                Bindings = new[]
                {
                    new PerformerParamBinding { ParamKey = 0, Value = ValueRef.FromConstant(2f) },
                    new PerformerParamBinding { ParamKey = 1, Value = ValueRef.FromConstant(0.1f) },
                    new PerformerParamBinding { ParamKey = 2, Value = ValueRef.FromConstant(3f) },
                    new PerformerParamBinding { ParamKey = 3, Value = ValueRef.FromConstant(4f) },
                    new PerformerParamBinding { ParamKey = 4, Value = ValueRef.FromConstant(0.2f) },
                    new PerformerParamBinding { ParamKey = 5, Value = ValueRef.FromConstant(6f) },
                    new PerformerParamBinding { ParamKey = 6, Value = ValueRef.FromConstant(8f) },
                    new PerformerParamBinding { ParamKey = 7, Value = ValueRef.FromConstant(0.3f) },
                    new PerformerParamBinding { ParamKey = 8, Value = ValueRef.FromConstant(12f) },
                    new PerformerParamBinding { ParamKey = 12, Value = ValueRef.FromConstant(1.25f) },
                    new PerformerParamBinding { ParamKey = 13, Value = ValueRef.FromConstant(0.15f) },
                    new PerformerParamBinding { ParamKey = 14, Value = ValueRef.FromConstant(3f) },
                    new PerformerParamBinding { ParamKey = 20, Value = ValueRef.FromConstant(0.9f) },
                    new PerformerParamBinding { ParamKey = 21, Value = ValueRef.FromConstant(0.8f) },
                    new PerformerParamBinding { ParamKey = 22, Value = ValueRef.FromConstant(0.2f) },
                    new PerformerParamBinding { ParamKey = 23, Value = ValueRef.FromConstant(0.7f) },
                    new PerformerParamBinding { ParamKey = 24, Value = ValueRef.FromConstant(0.1f) },
                    new PerformerParamBinding { ParamKey = 25, Value = ValueRef.FromConstant(0.2f) },
                    new PerformerParamBinding { ParamKey = 26, Value = ValueRef.FromConstant(0.3f) },
                    new PerformerParamBinding { ParamKey = 27, Value = ValueRef.FromConstant(0.4f) },
                }
            };

            int defId = _defs.Register("test_road_spline", def);
            Assert.That(
                _instances.TryAllocate(defId, owner: default, scopeId: 0, PresentationAnchorKind.WorldPosition, new Vector3(10f, 0.5f, 20f), stableId: 701, out _),
                Is.True);

            _system.Update(0.016f);

            Assert.That(_roadSplines.Count, Is.EqualTo(1));
            Assert.That(_roadSplines.StableIds[0], Is.EqualTo(701));
            Assert.That(_roadSplines.P0X[0], Is.EqualTo(10f).Within(0.001f));
            Assert.That(_roadSplines.P1X[0], Is.EqualTo(12f).Within(0.001f));
            Assert.That(_roadSplines.P1Z[0], Is.EqualTo(23f).Within(0.001f));
            Assert.That(_roadSplines.P2X[0], Is.EqualTo(14f).Within(0.001f));
            Assert.That(_roadSplines.P2Z[0], Is.EqualTo(26f).Within(0.001f));
            Assert.That(_roadSplines.P3X[0], Is.EqualTo(18f).Within(0.001f));
            Assert.That(_roadSplines.P3Z[0], Is.EqualTo(32f).Within(0.001f));
            Assert.That(_roadSplines.Width[0], Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(_roadSplines.BorderWidth[0], Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(_roadSplines.FillR[0], Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(_roadSplines.FillG[0], Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(_roadSplines.FillB[0], Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(_roadSplines.FillA[0], Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(_roadSplines.BorderR[0], Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(_roadSplines.BorderG[0], Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(_roadSplines.BorderB[0], Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(_roadSplines.BorderA[0], Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(_roadSplines.Style[0], Is.EqualTo((byte)3));
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
            var meshes = new MeshAssetRegistry();
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(
                registry,
                meshes,
                key => string.Equals(key, WellKnownHudTextKeys.CombatDelta, StringComparison.Ordinal) ? 1 : 0);

            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.CastCommittedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.CastFailedMarker), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.FloatingCombatText), out _), Is.True);
            Assert.That(registry.TryGet(registry.GetId(WellKnownPerformerKeys.EntityHealthBar), out _), Is.True);
        }

        [Test]
        public void FloatingCombatText_HasYDriftAndAlphaFade()
        {
            var meshes = new MeshAssetRegistry();
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(
                registry,
                meshes,
                key => string.Equals(key, WellKnownHudTextKeys.CombatDelta, StringComparison.Ordinal) ? 1 : 0);
            registry.TryGet(registry.GetId(WellKnownPerformerKeys.FloatingCombatText), out var def);

            Assert.That(def.PositionYDriftPerSecond, Is.GreaterThan(0f));
            Assert.That(def.AlphaFadeOverLifetime, Is.True);
            Assert.That(def.DefaultLifetime, Is.GreaterThan(0f));
        }

        [Test]
        public void EntityHealthBar_UsesEntityLifecycleRules()
        {
            var meshes = new MeshAssetRegistry();
            var registry = new PerformerDefinitionRegistry();
            BuiltinPerformerDefinitions.Register(
                registry,
                meshes,
                key => string.Equals(key, WellKnownHudTextKeys.CombatDelta, StringComparison.Ordinal) ? 1 : 0);
            registry.TryGet(registry.GetId(WellKnownPerformerKeys.EntityHealthBar), out var def);

            Assert.That(def.VisualKind, Is.EqualTo(PerformerVisualKind.WorldBar));
            Assert.That(def.Rules.Length, Is.EqualTo(2));
            Assert.That(def.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.EntitySpawned));
            Assert.That(def.Rules[0].Command.CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
            Assert.That(def.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.EntityDestroyed));
            Assert.That(def.Rules[1].Command.CommandKind, Is.EqualTo(PerformerCommandKind.DestroyPerformerScope));
        }
    }

    [TestFixture]
    public class PerformerLifecycleRuleTemplateFilterTests
    {
        private World _world;
        private PerformerDefinitionRegistry _defs;
        private PresentationEventStream _events;
        private PresentationCommandBuffer _commands;
        private PerformerRuleSystem _system;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _defs = new PerformerDefinitionRegistry();
            _events = new PresentationEventStream();
            _commands = new PresentationCommandBuffer();
            var programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(_world, null, null, null);
            var globals = new System.Collections.Generic.Dictionary<string, object>();
            _system = new PerformerRuleSystem(_world, _events, _commands, _defs, programs, api, globals);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void EntitySpawnedRule_WithExactTemplateKey_SkipsMismatch()
        {
            int defId = _defs.GetOrRegisterId("test_tmpl_mismatch");
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = defId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        },
                    },
                },
            };
            _defs.Register("test_tmpl_mismatch", def);
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntitySpawned,
                KeyId = 5,
                PayloadA = 5001,
            });

            _system.Update(0.016f);

            Assert.That(_commands.GetSpan().Length, Is.EqualTo(0));
        }

        [Test]
        public void EntitySpawnedRule_WithExactTemplateKey_IncludesMatch()
        {
            int defId = _defs.GetOrRegisterId("test_tmpl_match");
            _defs.Register("test_tmpl_match", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = 10 },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = defId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        },
                    },
                },
            });
            _events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntitySpawned,
                KeyId = 10,
                PayloadA = 1001,
            });

            _system.Update(0.016f);

            var commands = _commands.GetSpan();
            Assert.That(commands.Length, Is.EqualTo(1));
            Assert.That(commands[0].Kind, Is.EqualTo(PresentationCommandKind.CreatePerformer));
            Assert.That(commands[0].IdA, Is.EqualTo(defId));
            Assert.That(commands[0].IdB, Is.EqualTo(1001));
        }
    }

    [TestFixture]
    public class WellKnownPerformerParamKeysTests
    {
        [Test]
        public void BarConstants_MatchEmitSystemConventions()
        {
            // These values must match the hardcoded keys in PerformerEmitSystem.EmitWorldBar
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
            Assert.That(WellKnownPerformerParamKeys.MarkerUseOwnerRotation, Is.EqualTo(8));
        }
    }
}
