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
using Ludots.Core.Knowledge;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public class PresenterEndToEndTests
    {
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PresenterCommandBuffer _commands;
        private PresenterDefinitionRegistry _defs;
        private PresenterEntityRuntime _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private SplineRibbonBuffer _splineRibbons;
        private PresentationRequestBuffer _requests;
        private SoundRequestBuffer _soundRequests;
        private PresentationOwnerChangeBuffer _ownerChanges;
        private PresentationRequestFlushSystem _flush;
        private GameplayPresentationProjectionSystem _projection;
        private PresentationEntityLifecycleSystem _entityLifecycle;
        private PresentationEntityFinalizeDestroySystem _finalizeDestroy;
        private PresenterRuleSystem _ruleSystem;
        private PresenterRuntimeSystem _runtimeSystem;
        private PresenterBehaviorSystem _behaviorSystem;
        private PresenterEmitSystem _emitSystem;
        private PresentationStableIdAllocator _stableIds;
        private int _healthAttrId;
        private Entity _viewer;
        private KnowledgeProjectionStore _knowledge;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer(64);
            _eventBus = new GameplayEventBus();
            _presEvents = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            _commands = new PresenterCommandBuffer();
            _defs = new PresenterDefinitionRegistry();
            _instances = new PresenterEntityRuntime(_world);
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();
            _splineRibbons = new SplineRibbonBuffer();
            _requests = new PresentationRequestBuffer();
            _soundRequests = new SoundRequestBuffer();
            _ownerChanges = new PresentationOwnerChangeBuffer(64);
            _stableIds = new PresentationStableIdAllocator();

            _healthAttrId = AttributeRegistry.Register("Health");
            _viewer = _world.Create();
            _knowledge = new KnowledgeProjectionStore();
            _globals[CoreServiceKeys.LocalPlayerEntity.Name] = _viewer;
            _globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(_knowledge);

            LoadCorePresenterDefinitions(_defs, _healthAttrId);
            int healthBarDefId = _defs.GetOrRegisterId(WellKnownPresenterKeys.EntityHealthBar);
            _defs.Register(WellKnownPresenterKeys.EntityHealthBar, CreateWorldBarDefinition(
                _healthAttrId,
                new Vector4(0f, 1f, 0f, 1f),
                new Vector3(0f, 1.5f, 0f),
                width: 50f,
                height: 8f,
                rules: new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = -1 },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = healthBarDefId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                        }
                    },
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntityDestroyed, KeyId = -1 },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.DestroyPresenterScope,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                        }
                    }
                }));

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);

            _projection = new GameplayPresentationProjectionSystem(_world, _eventBus, _presEvents, session, _gasEvents, _ownerChanges);
            _entityLifecycle = new PresentationEntityLifecycleSystem(
                _world,
                _presEvents,
                _instances,
                _defs,
                _stableIds);
            _finalizeDestroy = new PresentationEntityFinalizeDestroySystem(_world);
            _ruleSystem = new PresenterRuleSystem(_world, _presEvents, _commands, _defs, _instances, _programs, graphApi, _globals);
            _runtimeSystem = new PresenterRuntimeSystem(_world, _commands, _presEvents, new TransientMarkerBuffer(), _requests, _instances, _stableIds, _defs);
            _behaviorSystem = new PresenterBehaviorSystem(_world, _instances, _defs, _presEvents, _ownerChanges, _soundRequests);
            _emitSystem = new PresenterEmitSystem(_world, _instances, _defs, _requests, _globals);
            _flush = new PresentationRequestFlushSystem(
                _world,
                _requests,
                new MeshAssetRegistry(),
                new StableDrawCache(4096),
                _primitives,
                _overlays,
                _hud,
                _splineRibbons,
                new PrimitiveDrawBuffer(4096),
                new PresentationVisualProxyBuffer(4096),
                new SkinnedVisualBatchBuffer(1024));
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _behaviorSystem?.Dispose();
            _flush?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _finalizeDestroy?.Dispose();
            _entityLifecycle?.Dispose();
            _projection?.Dispose();
            _world?.Dispose();
        }

        private void TickPipeline(float dt)
        {
            _requests.Clear();
            _soundRequests.Clear();
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _splineRibbons.Clear();
            _entityLifecycle.Update(dt);
            _projection.Update(dt);
            _ruleSystem.Update(dt);
            _runtimeSystem.Update(dt);
            _behaviorSystem.Update(0f);
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
                _world.Add(entity, new EntityTemplateKeyRef { TemplateKeyId = templateKeyId });
            }

            GrantHudKnowledge(entity, includeHealthAttribute: hasAttributes);
            return entity;
        }

        private void GrantHudKnowledge(Entity entity, bool includeHealthAttribute)
        {
            KnowledgeIdMask256 attributeMask = includeHealthAttribute
                ? KnowledgeIdMask256.Empty.WithId(_healthAttrId)
                : KnowledgeIdMask256.Empty;
            _knowledge.Upsert(
                _viewer,
                entity,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    attributeMask,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    _viewer,
                    observedTick: 0,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 0));
        }

        private int CountActiveInstancesInScope(int scopeId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            _world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.ScopeId == scopeId)
                {
                    count++;
                }
            });
            return count;
        }

        private int CountHudBars()
        {
            int count = 0;
            var hudSpan = _hud.GetSpan();
            for (int i = 0; i < hudSpan.Length; i++)
            {
                if (hudSpan[i].Kind == WorldHudItemKind.Bar)
                {
                    count++;
                }
            }

            return count;
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
            var attacker = CreatePresentableEntity(new Vector3(1f, 0f, 1f));
            var target = CreatePresentableEntity(new Vector3(5f, 0f, 5f));
            _gasEvents.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = attacker,
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
                    Assert.That(span[i].Owner, Is.EqualTo(target));
                    Assert.That(span[i].Value0, Is.EqualTo(-30f).Within(0.001f));
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
            int scopeId = _world.Get<PresentationStableId>(entity).Value;

            TickPipeline(0.016f);
            Assert.That(CountActiveInstancesInScope(scopeId), Is.GreaterThan(0));
            Assert.That(CountHudBars(), Is.GreaterThan(0));

            _world.Add(entity, new PresentationDestroyPending());

            TickPipeline(0.016f);

            Assert.That(CountActiveInstancesInScope(scopeId), Is.EqualTo(0));
            Assert.That(CountHudBars(), Is.EqualTo(0));
        }

        [Test]
        public void DirectApi_CreateAndDestroyScope_Lifecycle()
        {
            int overlayDefId = _defs.Register("test.overlay", new PresenterDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = CreateMeshAssetBinding(assetId: 2, new Vector3(5f, 5f, 5f)),
                    }
                },
                DefaultColor = new Vector4(0.3f, 0.7f, 1f, 0.5f),
            });

            var owner = CreatePresentableEntity(new Vector3(5f, 0f, 5f));
            int scopeTag = 42;

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = overlayDefId,
                ScopeTag = scopeTag,
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickPipeline(0.016f);
            Assert.That(_primitives.GetSpan().Length, Is.GreaterThan(0));

            _commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                ScopeTag = scopeTag,
            });
            TickPipeline(0.016f);

            Assert.That(_primitives.GetSpan().Length, Is.EqualTo(0));
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
            _defs.Register("test.template.keyed.bar", CreateWorldBarDefinition(
                _healthAttrId,
                new Vector4(1f, 0f, 0f, 1f),
                new Vector3(0f, 2f, 0f),
                width: 40f,
                height: 6f,
                rules: new[]
                {
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = heroTemplateId },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = defId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                        }
                    },
                    new PresenterRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntityDestroyed, KeyId = heroTemplateId },
                        Condition = ConditionRef.AlwaysTrue,
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.DestroyPresenterScope,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                        }
                    }
                }));

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

        private static PresenterDefinition CreateWorldBarDefinition(
            int attributeId,
            Vector4 color,
            Vector3 positionOffset,
            float width,
            float height,
            PresenterRule[] rules)
        {
            return new PresenterDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = attributeId,
                            TargetParamKey = WellKnownPresenterParamKeys.BarFillRatio,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds = Array.Empty<ThresholdMapping>(),
                        }
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = CreateWorldHudAssetBinding(width, height, WellKnownPresenterParamKeys.BarFillRatio),
                    }
                },
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
                DefaultColor = color,
                PositionOffset = positionOffset,
                Rules = rules,
            };
        }

        private static AssetBindingConfig CreateWorldHudAssetBinding(float width, float height, int valueParamKey)
        {
            return new AssetBindingConfig
            {
                AssetKind = AssetKind.WorldHud,
                MaterialId = 0,
                RenderPath = VisualRenderPath.None,
                Mobility = VisualMobility.Movable,
                LocalOffset = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = new Vector3(width, height, 1f),
                ScaleParamKey = -1,
                ColorParamKey = -1,
                MaterialParamKey = valueParamKey,
                AssetIdParamKey = -1,
                AssetSwapParamKey = -1,
                VisibilityParamKey = -1,
            };
        }

        private static AssetBindingConfig CreateMeshAssetBinding(int assetId, Vector3 scale)
        {
            return new AssetBindingConfig
            {
                AssetKind = AssetKind.Mesh,
                AssetId = assetId,
                MaterialId = 0,
                RenderPath = VisualRenderPath.StaticMesh,
                Mobility = VisualMobility.Movable,
                LocalOffset = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = scale,
                ScaleParamKey = -1,
                ColorParamKey = -1,
                MaterialParamKey = -1,
                AssetIdParamKey = -1,
                AssetSwapParamKey = -1,
                VisibilityParamKey = -1,
            };
        }

        private static void LoadCorePresenterDefinitions(PresenterDefinitionRegistry defs, int healthAttrId)
        {
            string repoRoot = FindRepoRoot();
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
                defs,
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
}
