using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// End-to-end tests for the unified Performer pipeline.
    /// Wires Bridge → RuleSystem → RuntimeSystem → EmitSystem with real BuiltinPerformerDefinitions.
    /// Verifies the full path from GAS gameplay events to draw buffer output.
    /// </summary>
    [TestFixture]
    public class PerformerEndToEndTests
    {
        // ── Shared infrastructure ──
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PresentationCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private PerformerInstanceBuffer _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;

        // ── Output buffers ──
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;

        // ── Systems (full pipeline) ──
        private PresentationBridgeSystem _bridge;
        private PerformerRuleSystem _ruleSystem;
        private PerformerRuntimeSystem _runtimeSystem;
        private PerformerEmitSystem _emitSystem;

        private int _healthAttrId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer();
            _eventBus = new GameplayEventBus();
            _presEvents = new PresentationEventStream();
            _commands = new PresentationCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _instances = new PerformerInstanceBuffer();
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();

            // Attribute registry — register "Health" (idempotent if already registered)
            _healthAttrId = AttributeRegistry.Register("Health");

            // Register built-in definitions (CastCommitted marker, CastFailed marker, FloatingCombatText, EntityHealthBar)
            BuiltinPerformerDefinitions.Register(_defs);

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);

            _bridge = new PresentationBridgeSystem(_world, _eventBus, _presEvents, session, _gasEvents);
            _ruleSystem = new PerformerRuleSystem(_world, _presEvents, _commands, _defs, _programs, graphApi, _globals);
            _runtimeSystem = new PerformerRuntimeSystem(_world, new PrefabRegistry(), _commands, _primitives, new TransientMarkerBuffer(), _instances);
            _emitSystem = new PerformerEmitSystem(_world, _instances, _defs, _overlays, _primitives, _hud, _programs, graphApi, _globals);
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _bridge?.Dispose();
            _world?.Dispose();
        }

        /// <summary>Drives the full pipeline once: Bridge → Rule → Runtime → Emit.</summary>
        private void TickPipeline(float dt)
        {
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _bridge.Update(dt);
            _ruleSystem.Update(dt);
            _runtimeSystem.Update(dt);
            _emitSystem.Update(dt);
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-1  EffectApplied → FloatingCombatText (WorldText + drift + fade)
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void EffectApplied_ProducesFloatingCombatText_InWorldHud()
        {
            // Arrange
            var target = _world.Create(new VisualTransform { Position = new Vector3(5, 0, 5) });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -30f,
                AttributeId = _healthAttrId,
                EffectTemplateId = 1,
            });

            // Act
            TickPipeline(0.016f);

            // Assert — a WorldText item should appear
            var hudSpan = _hud.GetSpan();
            Assert.That(hudSpan.Length, Is.GreaterThanOrEqualTo(1), "FloatingCombatText should emit at least one WorldText item");
            bool foundText = false;
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Text)
                {
                    foundText = true;
                    break;
                }
            }
            Assert.That(foundText, Is.True, "Expected a WorldHudItemKind.Text from FloatingCombatText");
        }

        [Test]
        public void FloatingCombatText_DriftsUpward_OverTime()
        {
            // Arrange
            var target = _world.Create(new VisualTransform { Position = new Vector3(0, 0, 0) });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -10f,
                EffectTemplateId = 1,
            });
            TickPipeline(0.016f); // creates the instance

            // Record initial Y
            float firstY = GetFirstHudTextPosition().Y;

            // Act — tick again for 0.5 seconds
            TickPipeline(0.5f);
            float secondY = GetFirstHudTextPosition().Y;

            // Assert — text should have drifted upward
            Assert.That(secondY, Is.GreaterThan(firstY), "FloatingCombatText should drift upward over time");
        }

        [Test]
        public void FloatingCombatText_FadesAlpha_OverTime()
        {
            // Arrange
            var target = _world.Create(new VisualTransform { Position = Vector3.Zero });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -5f,
                EffectTemplateId = 1,
            });
            TickPipeline(0.016f);
            float initialAlpha = GetFirstHudTextColor().W;

            // Act — tick to ~50% of lifetime (1.2s)
            TickPipeline(0.6f);
            float midAlpha = GetFirstHudTextColor().W;

            // Assert
            Assert.That(midAlpha, Is.LessThan(initialAlpha), "Alpha should decrease over time");
            Assert.That(midAlpha, Is.GreaterThan(0f), "Alpha should not be zero at 50% lifetime");
        }

        [Test]
        public void FloatingCombatText_DisappearsAfterLifetime()
        {
            // Arrange
            var target = _world.Create(new VisualTransform { Position = Vector3.Zero });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -5f,
                EffectTemplateId = 1,
            });
            TickPipeline(0.016f);
            Assert.That(_hud.GetSpan().Length, Is.GreaterThan(0), "Precondition: text should exist");

            // Act — tick well past the 1.2s lifetime
            TickPipeline(1.3f);

            // Assert — no more output
            var span = _hud.GetSpan();
            int textCount = 0;
            for (int i = 0; i < span.Length; i++)
                if (span[i].Kind == WorldHudItemKind.Text) textCount++;
            Assert.That(textCount, Is.EqualTo(0), "FloatingCombatText should expire after DefaultLifetime");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-2  CastCommitted → Marker3D (PrimitiveDrawBuffer)
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void CastCommitted_ProducesMarker3D_InPrimitiveBuffer()
        {
            // Arrange
            var actor = _world.Create(new VisualTransform { Position = new Vector3(3, 0, 3) });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                Target = actor,
                AbilitySlot = 0,
                AbilityId = 100,
            });

            // Act
            TickPipeline(0.016f);

            // Assert
            var drawSpan = _primitives.GetSpan();
            Assert.That(drawSpan.Length, Is.GreaterThanOrEqualTo(1), "CastCommitted should emit a Marker3D");
        }

        [Test]
        public void CastCommitted_Marker_ExpiresAfterLifetime()
        {
            // Arrange
            var actor = _world.Create(new VisualTransform { Position = Vector3.Zero });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                Target = actor,
                AbilityId = 1,
            });
            TickPipeline(0.016f);
            Assert.That(_primitives.GetSpan().Length, Is.GreaterThan(0), "Precondition: marker should exist");

            // Act — tick past 0.22s lifetime
            TickPipeline(0.3f);

            // Assert — marker expired
            Assert.That(_primitives.GetSpan().Length, Is.EqualTo(0), "Marker should expire after DefaultLifetime");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-3  CastFailed → small grey Marker3D
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void CastFailed_ProducesSmallGreyMarker()
        {
            // Arrange
            var actor = _world.Create(new VisualTransform { Position = new Vector3(1, 0, 1) });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                AbilityId = 5,
                FailReason = AbilityCastFailReason.OnCooldown,
            });

            // Act
            TickPipeline(0.016f);

            // Assert
            var drawSpan = _primitives.GetSpan();
            Assert.That(drawSpan.Length, Is.GreaterThanOrEqualTo(1), "CastFailed should emit a Marker3D");
            // CastFailed marker scale should be smaller than CastCommitted
            Assert.That(drawSpan[0].Scale.X, Is.LessThanOrEqualTo(0.3f), "CastFailed marker should be small");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-4  Entity-scoped HealthBar — attribute-driven fill ratio
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void EntityScoped_HealthBar_EmitsForEntityWithAttributes()
        {
            // Arrange — entity with VisualTransform + AttributeBuffer
            var attrBuf = new AttributeBuffer();
            attrBuf.SetBase(_healthAttrId, 100f);
            attrBuf.SetCurrent(_healthAttrId, 100f);
            var entity = _world.Create(new VisualTransform { Position = new Vector3(10, 0, 10) }, attrBuf);

            // Act
            TickPipeline(0.016f);

            // Assert — at least one Bar item in HUD
            var hudSpan = _hud.GetSpan();
            bool foundBar = false;
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    foundBar = true;
                    break;
                }
            }
            Assert.That(foundBar, Is.True, "Entity with AttributeBuffer should get an entity-scoped health bar");
        }

        [Test]
        public void EntityScoped_HealthBar_SkipsEntityWithoutAttributes()
        {
            // Arrange — entity with VisualTransform but NO AttributeBuffer
            _world.Create(new VisualTransform { Position = Vector3.Zero });

            // Act
            TickPipeline(0.016f);

            // Assert — no bar
            var hudSpan = _hud.GetSpan();
            int barCount = 0;
            for (int i = 0; i < hudSpan.Length; i++)
                if (hudSpan[i].Kind == WorldHudItemKind.Bar) barCount++;
            Assert.That(barCount, Is.EqualTo(0), "Entity without AttributeBuffer should not get a health bar");
        }

        [Test]
        public void EntityScoped_HealthBar_CullInvisible_NoOutput()
        {
            // Arrange — entity with CullState.IsVisible = false
            var attrBuf = new AttributeBuffer();
            attrBuf.SetBase(_healthAttrId, 100f);
            attrBuf.SetCurrent(_healthAttrId, 100f);
            _world.Create(
                new VisualTransform { Position = Vector3.Zero },
                attrBuf,
                new CullState { IsVisible = false });

            // Act
            TickPipeline(0.016f);

            // Assert
            var hudSpan = _hud.GetSpan();
            int barCount = 0;
            for (int i = 0; i < hudSpan.Length; i++)
                if (hudSpan[i].Kind == WorldHudItemKind.Bar) barCount++;
            Assert.That(barCount, Is.EqualTo(0), "Culled entity should not produce a health bar");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-5  Direct API — PresentationCommandBuffer create/destroy scope
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void DirectApi_CreateAndDestroyScope_Lifecycle()
        {
            // Arrange — register a simple GroundOverlay definition
            var def = new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.GroundOverlay,
                MeshOrShapeId = 0,
                DefaultScale = 5f,
                DefaultColor = new Vector4(0.3f, 0.7f, 1f, 0.5f),
            };
            _defs.Register(1, def);

            var owner = _world.Create(new VisualTransform { Position = new Vector3(5, 0, 5) });
            int scopeId = 42;

            // Act 1 — create via direct API
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = 1,
                IdB = scopeId,
                Source = owner,
            });
            TickPipeline(0.016f);

            // Assert 1 — overlay emitted
            Assert.That(_overlays.GetSpan().Length, Is.GreaterThan(0), "Direct API CreatePerformer should emit a GroundOverlay");

            // Act 2 — destroy scope
            _commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = scopeId,
            });
            TickPipeline(0.016f);

            // Assert 2 — no more overlay
            Assert.That(_overlays.GetSpan().Length, Is.EqualTo(0), "DestroyPerformerScope should remove all performers in scope");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-6  Multiple GAS events in one frame — all produce output
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void MultipleGasEvents_OneFrame_AllProduceOutput()
        {
            // Arrange — one cast + one effect applied
            var actor = _world.Create(new VisualTransform { Position = new Vector3(1, 0, 1) });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                Target = actor,
                AbilityId = 1,
            });
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = actor,
                Target = actor,
                Delta = -20f,
                EffectTemplateId = 2,
            });

            // Act
            TickPipeline(0.016f);

            // Assert — marker AND floating text
            Assert.That(_primitives.GetSpan().Length, Is.GreaterThan(0), "CastCommitted marker should be present");
            bool foundText = false;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
                if (hudSpan[i].Kind == WorldHudItemKind.Text) { foundText = true; break; }
            Assert.That(foundText, Is.True, "EffectApplied floating text should be present");
        }

        // ═══════════════════════════════════════════════════════════════
        // E2E-7  EffectApplied on dead entity — no crash, no output
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void EffectApplied_DeadEntity_NoCrash()
        {
            // Arrange — create and immediately destroy entity
            var entity = _world.Create(new VisualTransform { Position = Vector3.Zero });
            _world.Destroy(entity);
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = entity,
                Target = entity,
                Delta = -10f,
                EffectTemplateId = 1,
            });

            // Act — should not throw
            Assert.DoesNotThrow(() => TickPipeline(0.016f));
        }

        // ═══════════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════════

        private Vector3 GetFirstHudTextPosition()
        {
            var span = _hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
                if (span[i].Kind == WorldHudItemKind.Text) return span[i].WorldPosition;
            Assert.Fail("No WorldText found in HUD buffer");
            return default;
        }

        private Vector4 GetFirstHudTextColor()
        {
            var span = _hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
                if (span[i].Kind == WorldHudItemKind.Text) return span[i].Color0;
            Assert.Fail("No WorldText found in HUD buffer");
            return default;
        }
    }
}
