using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Perform;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public class PerformerEndToEndTests
    {
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PerformCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private PerformerInstanceBuffer _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private RoadSplineBuffer _roadSplines;
        private PresentationRequestBuffer _requests;
        private PresentationRequestFlushSystem _flush;
        private PresentationBridgeSystem _bridge;
        private PresentationEntityLifecycleSystem _entityLifecycle;
        private PresentationEntityFinalizeDestroySystem _finalizeDestroy;
        private PerformerRuleSystem _ruleSystem;
        private PerformerRuntimeSystem _runtimeSystem;
        private PerformerEmitSystem _emitSystem;
        private PresentationStableIdAllocator _stableIds;
        private int _healthAttrId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer();
            _eventBus = new GameplayEventBus();
            _presEvents = new PresentationEventStream();
            _commands = new PerformCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _instances = new PerformerInstanceBuffer();
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();
            _roadSplines = new RoadSplineBuffer();
            _requests = new PresentationRequestBuffer();
            _stableIds = new PresentationStableIdAllocator();

            _healthAttrId = AttributeRegistry.Register("Health");

            BuiltinPerformerDefinitions.Register(
                _defs,
                new MeshAssetRegistry(),
                key => string.Equals(key, WellKnownHudTextKeys.CombatDelta, StringComparison.Ordinal) ? 1 : 0);
            int healthBarDefId = _defs.GetOrRegisterId(WellKnownPerformerKeys.EntityHealthBar);
            _defs.Register(WellKnownPerformerKeys.EntityHealthBar, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
                DefaultColor = new Vector4(0f, 1f, 0f, 1f),
                DefaultScale = 1f,
                PositionOffset = new Vector3(0f, 1.5f, 0f),
                Bindings = new[]
                {
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarFillRatio, Value = ValueRef.FromAttributeRatio(_healthAttrId) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarWidth, Value = ValueRef.FromConstant(50f) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarHeight, Value = ValueRef.FromConstant(8f) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarBackgroundR, Value = ValueRef.FromConstant(0.2f) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarBackgroundG, Value = ValueRef.FromConstant(0f) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarBackgroundB, Value = ValueRef.FromConstant(0f) },
                    new PerformerParamBinding { ParamKey = WellKnownPerformerParamKeys.BarBackgroundA, Value = ValueRef.FromConstant(0.85f) },
                },
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = -1 },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = healthBarDefId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    },
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntityDestroyed, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.DestroyPerformerScope,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    }
                },
            });

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);

            _bridge = new PresentationBridgeSystem(_world, _eventBus, _presEvents, session, _gasEvents);
            _entityLifecycle = new PresentationEntityLifecycleSystem(_world, _presEvents);
            _finalizeDestroy = new PresentationEntityFinalizeDestroySystem(_world);
            _ruleSystem = new PerformerRuleSystem(_world, _presEvents, _commands, _defs, _programs, graphApi, _globals);
            _runtimeSystem = new PerformerRuntimeSystem(_world, new PrefabRegistry(), _commands, _presEvents, new TransientMarkerBuffer(), _requests, _instances, _stableIds, _defs);
            _emitSystem = new PerformerEmitSystem(_world, _instances, _defs, _requests, _programs, graphApi, _globals);
            _flush = new PresentationRequestFlushSystem(_world, _requests, new PrefabRegistry(), new MeshAssetRegistry(), _primitives, _overlays, _hud, _roadSplines);
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _flush?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _finalizeDestroy?.Dispose();
            _entityLifecycle?.Dispose();
            _bridge?.Dispose();
            _world?.Dispose();
        }

        private void TickPipeline(float dt)
        {
            _requests.Clear();
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _roadSplines.Clear();
            _entityLifecycle.Update(dt);
            _bridge.Update(dt);
            _ruleSystem.Update(dt);
            _runtimeSystem.Update(dt);
            _emitSystem.Update(dt);
            _flush.Update(dt);
            _finalizeDestroy.Update(dt);
        }

        private Entity CreatePresentableEntity(Vector3 position, AttributeBuffer attributeBuffer = default, bool hasAttributes = false, bool isVisible = true, int templateKeyId = 0)
        {
            var entity = _world.Create(new VisualTransform { Position = position });
            _world.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });

            if (hasAttributes)
            {
                _world.Add(entity, attributeBuffer);
            }

            if (!isVisible)
            {
                _world.Add(entity, new CullState { IsVisible = false });
            }

            if (templateKeyId > 0)
            {
                _world.Add(entity, new EntityTemplateKeyCm { TemplateKeyId = templateKeyId });
            }

            return entity;
        }

        private Vector3 GetFirstHudTextPosition()
        {
            var span = _hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == WorldHudItemKind.Text)
                {
                    return span[i].WorldPosition;
                }
            }

            Assert.Fail("No WorldText found in HUD buffer");
            return default;
        }

        private Vector4 GetFirstHudTextColor()
        {
            var span = _hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == WorldHudItemKind.Text)
                {
                    return span[i].Color0;
                }
            }

            Assert.Fail("No WorldText found in HUD buffer");
            return default;
        }

        [Test]
        public void EffectApplied_ProducesFloatingCombatText_InWorldHud()
        {
            var target = CreatePresentableEntity(new Vector3(5f, 0f, 5f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -30f,
                AttributeId = _healthAttrId,
                EffectTemplateId = 1,
            });

            TickPipeline(0.016f);

            bool foundText = false;
            var span = _hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == WorldHudItemKind.Text)
                {
                    foundText = true;
                    break;
                }
            }

            Assert.That(foundText, Is.True);
        }

        [Test]
        public void FloatingCombatText_DriftsUpward_AndFadesOut()
        {
            var target = CreatePresentableEntity(new Vector3(3f, 0f, 3f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -20f,
                AttributeId = _healthAttrId,
                EffectTemplateId = 2,
            });

            TickPipeline(0.016f);
            Vector3 startPos = GetFirstHudTextPosition();
            Vector4 startColor = GetFirstHudTextColor();

            TickPipeline(0.5f);
            Vector3 movedPos = GetFirstHudTextPosition();
            Vector4 movedColor = GetFirstHudTextColor();

            Assert.That(movedPos.Y, Is.GreaterThan(startPos.Y));
            Assert.That(movedColor.W, Is.LessThan(startColor.W));
        }

        [Test]
        public void FloatingCombatText_ExpiresAfterLifetime()
        {
            var target = CreatePresentableEntity(new Vector3(3f, 0f, 3f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = target,
                Target = target,
                Delta = -10f,
                AttributeId = _healthAttrId,
                EffectTemplateId = 3,
            });

            TickPipeline(0.016f);
            Assert.That(_hud.GetSpan().Length, Is.GreaterThan(0));

            TickPipeline(1.3f);
            Assert.That(_hud.GetSpan().Length, Is.EqualTo(0));
        }

        [Test]
        public void CastCommitted_ProducesMarker3D()
        {
            var actor = CreatePresentableEntity(new Vector3(1f, 0f, 1f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                Target = actor,
                AbilityId = 7,
                AbilitySlot = 1,
            });

            TickPipeline(0.016f);

            Assert.That(_primitives.GetSpan().Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CastFailed_ProducesMarker3D()
        {
            var actor = CreatePresentableEntity(new Vector3(2f, 0f, 2f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                Target = actor,
                AbilityId = 9,
                AbilitySlot = 1,
                FailReason = AbilityCastFailReason.OnCooldown,
            });

            TickPipeline(0.016f);

            var drawSpan = _primitives.GetSpan();
            Assert.That(drawSpan.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(drawSpan[0].Scale.X, Is.LessThanOrEqualTo(0.3f));
        }

        [Test]
        public void LifecycleHealthBar_EmitsForEntityWithAttributes()
        {
            var attrBuf = new AttributeBuffer();
            attrBuf.SetBase(_healthAttrId, 100f);
            attrBuf.SetCurrent(_healthAttrId, 100f);
            CreatePresentableEntity(new Vector3(10f, 0f, 10f), attrBuf, hasAttributes: true);

            Assert.That(_presEvents.Count, Is.EqualTo(0));
            TickPipeline(0.016f);
            Assert.That(_commands.Count, Is.GreaterThanOrEqualTo(0));

            bool foundBar = false;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    foundBar = true;
                    break;
                }
            }

            Assert.That(foundBar, Is.True);
        }

        [Test]
        public void LifecycleHealthBar_SkipsEntityWithoutAttributes()
        {
            CreatePresentableEntity(Vector3.Zero);

            TickPipeline(0.016f);

            int barCount = 0;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                }
            }

            Assert.That(barCount, Is.EqualTo(0));
        }

        [Test]
        public void LifecycleHealthBar_CullInvisible_NoOutput()
        {
            var attrBuf = new AttributeBuffer();
            attrBuf.SetBase(_healthAttrId, 100f);
            attrBuf.SetCurrent(_healthAttrId, 100f);
            CreatePresentableEntity(Vector3.Zero, attrBuf, hasAttributes: true, isVisible: false);

            TickPipeline(0.016f);

            int barCount = 0;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                }
            }

            Assert.That(barCount, Is.EqualTo(0));
        }

        [Test]
        public void LifecycleHealthBar_PendingDestroy_ReleasesBeforeEmit()
        {
            var attrBuf = new AttributeBuffer();
            attrBuf.SetBase(_healthAttrId, 100f);
            attrBuf.SetCurrent(_healthAttrId, 50f);
            var entity = CreatePresentableEntity(new Vector3(4f, 0f, 4f), attrBuf, hasAttributes: true);

            TickPipeline(0.016f);
            Assert.That(_instances.ActiveCount, Is.EqualTo(1));

            ref var lifecycle = ref _world.Get<PresentationLifecycleState>(entity);
            lifecycle.PendingDestroy = true;

            TickPipeline(0.016f);

            Assert.That(_instances.ActiveCount, Is.EqualTo(0));
            int barCount = 0;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                }
            }

            Assert.That(barCount, Is.EqualTo(0));
        }

        [Test]
        public void DirectApi_CreateAndDestroyScope_Lifecycle()
        {
            int overlayDefId = _defs.Register("test.overlay", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.GroundOverlay,
                MeshOrShapeId = 0,
                DefaultScale = 5f,
                DefaultColor = new Vector4(0.3f, 0.7f, 1f, 0.5f),
            });

            var owner = CreatePresentableEntity(new Vector3(5f, 0f, 5f));
            int scopeId = 42;

            _commands.TryAdd(new PerformCommand
            {
                CommandKind = PresentationCommandKind.CreatePerformer,
                PerformerDefinitionId = overlayDefId,
                ScopeId = scopeId,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickPipeline(0.016f);
            Assert.That(_overlays.GetSpan().Length, Is.GreaterThan(0));

            _commands.TryAdd(new PerformCommand
            {
                CommandKind = PresentationCommandKind.DestroyPerformerScope,
                PerformerDefinitionId = scopeId,
                ScopeId = scopeId,
            });
            TickPipeline(0.016f);

            Assert.That(_overlays.GetSpan().Length, Is.EqualTo(0));
        }

        [Test]
        public void MultipleGasEvents_OneFrame_AllProduceOutput()
        {
            var actor = CreatePresentableEntity(new Vector3(1f, 0f, 1f));
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
                AttributeId = _healthAttrId,
                EffectTemplateId = 2,
            });

            TickPipeline(0.016f);

            Assert.That(_primitives.GetSpan().Length, Is.GreaterThan(0));

            bool foundText = false;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Text)
                {
                    foundText = true;
                    break;
                }
            }

            Assert.That(foundText, Is.True);
        }

        [Test]
        public void EffectApplied_DeadEntity_NoCrash()
        {
            var entity = CreatePresentableEntity(Vector3.Zero);
            _world.Destroy(entity);
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = entity,
                Target = entity,
                Delta = -10f,
                AttributeId = _healthAttrId,
                EffectTemplateId = 1,
            });

            Assert.DoesNotThrow(() => TickPipeline(0.016f));
        }

        [Test]
        public void LifecycleTemplateKeyFilter_OnlyMatchingTemplateEmits()
        {
            int heroTemplateId = 42;
            int defId = _defs.GetOrRegisterId("test.template.keyed.bar");
            _defs.Register("test.template.keyed.bar", new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                DefaultColor = new Vector4(1f, 0f, 0f, 1f),
                PositionOffset = new Vector3(0f, 2f, 0f),
                Bindings = new[]
                {
                    new PerformerParamBinding
                    {
                        ParamKey = WellKnownPerformerParamKeys.BarFillRatio,
                        Value = ValueRef.FromAttributeRatio(_healthAttrId)
                    }
                },
                Rules = new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = heroTemplateId },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.CreatePerformer,
                            PerformerDefinitionId = defId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    },
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntityDestroyed, KeyId = heroTemplateId },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PerformerCommand
                        {
                            CommandKind = PresentationCommandKind.DestroyPerformerScope,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    }
                }
            });

            var heroAttr = new AttributeBuffer();
            heroAttr.SetBase(_healthAttrId, 200f);
            heroAttr.SetCurrent(_healthAttrId, 200f);
            CreatePresentableEntity(new Vector3(1f, 0f, 1f), heroAttr, hasAttributes: true, templateKeyId: heroTemplateId);

            var minionAttr = new AttributeBuffer();
            minionAttr.SetBase(_healthAttrId, 50f);
            minionAttr.SetCurrent(_healthAttrId, 50f);
            CreatePresentableEntity(new Vector3(5f, 0f, 5f), minionAttr, hasAttributes: true, templateKeyId: 99);

            TickPipeline(0.016f);

            int totalBars = 0;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    totalBars++;
                }
            }

            Assert.That(totalBars, Is.EqualTo(3));
        }
    }
}
