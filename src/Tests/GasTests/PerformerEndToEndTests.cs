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
        private PerformerCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private PerformerEntityRuntime _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private RoadSplineBuffer _roadSplines;
        private PresentationRequestBuffer _requests;
        private SoundRequestBuffer _soundRequests;
        private PresentationRequestFlushSystem _flush;
        private PresentationBridgeSystem _bridge;
        private PresentationEntityLifecycleSystem _entityLifecycle;
        private PresentationEntityFinalizeDestroySystem _finalizeDestroy;
        private PerformerRuleSystem _ruleSystem;
        private PerformerRuntimeSystem _runtimeSystem;
        private PerformerBehaviorSystem _behaviorSystem;
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
            _commands = new PerformerCommandBuffer();
            _defs = new PerformerDefinitionRegistry();
            _instances = new PerformerEntityRuntime(_world);
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer();
            _hud = new WorldHudBatchBuffer();
            _overlays = new GroundOverlayBuffer();
            _roadSplines = new RoadSplineBuffer();
            _requests = new PresentationRequestBuffer();
            _soundRequests = new SoundRequestBuffer();
            _stableIds = new PresentationStableIdAllocator();

            _healthAttrId = AttributeRegistry.Register("Health");

            LoadCorePerformerDefinitions(_defs, _healthAttrId);
            int healthBarDefId = _defs.GetOrRegisterId(WellKnownPerformerKeys.EntityHealthBar);
            _defs.Register(WellKnownPerformerKeys.EntityHealthBar, CreateWorldBarDefinition(
                _healthAttrId,
                new Vector4(0f, 1f, 0f, 1f),
                new Vector3(0f, 1.5f, 0f),
                width: 50f,
                height: 8f,
                rules: new[]
                {
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = -1 },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
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
                            CommandKind = PerformerCommandKind.DestroyPerformerScope,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        }
                    }
                }));

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);

            _bridge = new PresentationBridgeSystem(_world, _eventBus, _presEvents, session, _gasEvents);
            _entityLifecycle = new PresentationEntityLifecycleSystem(_world, _presEvents);
            _finalizeDestroy = new PresentationEntityFinalizeDestroySystem(_world);
            _ruleSystem = new PerformerRuleSystem(_world, _presEvents, _commands, _defs, _instances, _programs, graphApi, _globals);
            _runtimeSystem = new PerformerRuntimeSystem(_world, _commands, _presEvents, new TransientMarkerBuffer(), _requests, _instances, _stableIds, _defs);
            _behaviorSystem = new PerformerBehaviorSystem(_world, _instances, _defs, _presEvents, _soundRequests);
            _emitSystem = new PerformerEmitSystem(_world, _instances, _defs, _requests, _globals);
            _flush = new PresentationRequestFlushSystem(
                _world,
                _requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                new StableDrawCache(4096),
                _primitives,
                _overlays,
                _hud,
                _roadSplines,
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
            _bridge?.Dispose();
            _world?.Dispose();
        }

        private void TickPipeline(float dt)
        {
            _requests.Clear();
            _soundRequests.Clear();
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _roadSplines.Clear();
            _entityLifecycle.Update(dt);
            _bridge.Update(dt);
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
                _world.Add(entity, new EntityTemplateKeyCm { TemplateKeyId = templateKeyId });
            }

            return entity;
        }

        private int CountActiveInstancesInScope(int scopeId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
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
            int overlayDefId = _defs.Register("test.overlay", new PerformerDefinition
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

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = overlayDefId,
                ScopeTag = scopeTag,
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
            });
            TickPipeline(0.016f);
            Assert.That(_primitives.GetSpan().Length, Is.GreaterThan(0));

            _commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformerScope,
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
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned, KeyId = heroTemplateId },
                        Condition = new ConditionRef { Inline = InlineConditionKind.SourceHasAttributes },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
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
                            CommandKind = PerformerCommandKind.DestroyPerformerScope,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
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

        private static PerformerDefinition CreateWorldBarDefinition(
            int attributeId,
            Vector4 color,
            Vector3 positionOffset,
            float width,
            float height,
            PerformerRule[] rules)
        {
            return new PerformerDefinition
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
                            TargetParamKey = WellKnownPerformerParamKeys.BarFillRatio,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds = Array.Empty<ThresholdMapping>(),
                        }
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = CreateWorldHudAssetBinding(width, height, WellKnownPerformerParamKeys.BarFillRatio),
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
                AssetSwapParamKey = -1,
                VisibilityParamKey = -1,
                Grounding = GroundingMode.None,
                GroundingOffset = 0f,
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
                AssetSwapParamKey = -1,
                VisibilityParamKey = -1,
                Grounding = GroundingMode.None,
                GroundingOffset = 0f,
            };
        }

        private static void LoadCorePerformerDefinitions(PerformerDefinitionRegistry defs, int healthAttrId)
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
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);

            new PerformerDefinitionConfigLoader(
                pipeline,
                defs,
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
}
