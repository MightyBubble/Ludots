using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class PerformerVisualPlusRetainedHudEmitTests
{
    [Test]
    public void ParentPerformer_VisualPlusBarPlusText_EmitsOneRetainedBarAndText_WithoutChildren()
    {
        using World world = World.Create();
        TeamManager.Clear();
        try
        {
            var definitions = new PerformerDefinitionRegistry();
            int healthRatioKey = PerformerParamKeyRegistry.Register("test.health.ratio");
            int healthCurrentKey = PerformerParamKeyRegistry.Register("test.health.current");
            int healthBaseKey = PerformerParamKeyRegistry.Register("test.health.base");
            int barColorKey = PerformerParamKeyRegistry.Register("test.health.barColor");
            int textColorKey = PerformerParamKeyRegistry.Register("test.health.textColor");
            const int textTokenId = 77;

            int definitionId = definitions.Register(
                "webgpu.visual-plus-retained-hud",
                new PerformerDefinition
                {
                    DefaultColor = new Vector4(0.2f, 0.4f, 0.8f, 1f),
                    WorldTextMode = WorldHudValueMode.AttributeCurrentOverBase,
                    DefaultFontSize = 11,
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.SkinnedMesh,
                                AssetId = 17,
                                MaterialId = 29,
                                RenderPath = VisualRenderPath.GpuSkinnedInstance,
                                Mobility = VisualMobility.Movable,
                                LocalScale = new Vector3(0.5f, 0.5f, 0.5f),
                            },
                        },
                        new BehaviorSlot
                        {
                            SlotIndex = 1,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.WorldHud,
                                MaterialParamKey = healthRatioKey,
                                ColorParamKey = barColorKey,
                                Mobility = VisualMobility.Movable,
                                LocalOffset = new Vector3(0f, 1.25f, 0f),
                                LocalScale = new Vector3(42f, 5f, 1f),
                            },
                        },
                        new BehaviorSlot
                        {
                            SlotIndex = 2,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.WorldText,
                                AssetId = textTokenId,
                                ScaleParamKey = healthCurrentKey,
                                MaterialParamKey = healthBaseKey,
                                ColorParamKey = textColorKey,
                                Mobility = VisualMobility.Movable,
                                LocalOffset = new Vector3(0f, 1.42f, 0f),
                                LocalScale = Vector3.One,
                            },
                        },
                    ],
                    ParamDefaults =
                    [
                        new ParamDefault { ParamKey = healthRatioKey, Lane = ParamLane.Float, FloatValue = 1f },
                        new ParamDefault { ParamKey = healthCurrentKey, Lane = ParamLane.Float, FloatValue = 100f },
                        new ParamDefault { ParamKey = healthBaseKey, Lane = ParamLane.Float, FloatValue = 100f },
                        new ParamDefault
                        {
                            ParamKey = barColorKey,
                            Lane = ParamLane.Vector,
                            VectorValue = new Vector4(0.12f, 0.92f, 0.30f, 0.96f),
                        },
                        new ParamDefault
                        {
                            ParamKey = textColorKey,
                            Lane = ParamLane.Vector,
                            VectorValue = new Vector4(1f, 0.98f, 0.92f, 1f),
                        },
                    ],
                });

            Assert.That(definitions.TryGet(definitionId, out PerformerDefinition definition), Is.True);
            Assert.That(definition.Children, Is.Empty);

            Entity audience = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 });
            Entity owner = world.Create(
                new PresentationStableId { Value = 101 },
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 },
                new VisualTransform
                {
                    Position = new Vector3(3f, 2f, 5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 8);
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            projectionStore.Upsert(
                audience,
                owner,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    audience,
                    observedTick: 0,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = audience,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };

            var runtime = new PerformerEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity performer = runtime.Create(
                definitionId,
                owner,
                scopeId: 1,
                PresentationAnchorKind.Entity,
                new Vector3(3f, 2f, 5f),
                stableId: 1001,
                Entity.Null,
                definition);
            runtime.SetParamDefault(definition, performer);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 0b111u;

            var worldHud = new WorldHudBatchBuffer(64);
            var requests = new PresentationRequestBuffer();
            var skinnedBatch = new SkinnedVisualBatchBuffer(4);
            var timings = new PresentationTimingDiagnostics();
            using var emit = new PerformerEmitSystem(
                world,
                runtime,
                definitions,
                requests,
                globals,
                timingDiagnostics: timings,
                skinnedVisualBatchBuffer: skinnedBatch,
                worldHudBuffer: worldHud);

            emit.Update(0.016f);

            Assert.That(timings.PerformerEmitSingleVisualFastCountLastFrame, Is.EqualTo(1));
            Assert.That(skinnedBatch.Count, Is.EqualTo(1));
            Assert.That(worldHud.Count, Is.EqualTo(2));

            int barStableId = HudItemIdentity.ComposeStableId(1001, WorldHudItemKind.Bar, definitionId);
            int textStableId = HudItemIdentity.ComposeStableId(1001, WorldHudItemKind.Text, definitionId);
            Assert.That(worldHud.TryGetByStableId(barStableId, out WorldHudItem bar), Is.True);
            Assert.That(worldHud.TryGetByStableId(textStableId, out WorldHudItem text), Is.True);
            Assert.That(bar.WorldPosition.Y, Is.EqualTo(3.25f).Within(0.0001f));
            Assert.That(text.WorldPosition.Y, Is.EqualTo(3.42f).Within(0.0001f));
            Assert.That(bar.Width, Is.EqualTo(42f).Within(0.0001f));
            Assert.That(text.FontSize, Is.EqualTo(11));

            ref PerformerEmitCache emitCache = ref world.Get<PerformerEmitCache>(performer);
            Assert.That(emitCache.RetainedBarBufferIndexPlusOne, Is.GreaterThan(0));
            Assert.That(emitCache.RetainedTextBufferIndexPlusOne, Is.GreaterThan(0));
            Assert.That(world.Get<PerformerChildren>(performer).Count, Is.EqualTo(0));

            world.Get<PerformerWorldPosition>(performer).Value = new Vector3(10f, 2f, 5f);
            emit.Update(0.016f);
            Assert.That(worldHud.TryGetByStableId(barStableId, out bar), Is.True);
            Assert.That(bar.WorldPosition.X, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(bar.WorldPosition.Y, Is.EqualTo(3.25f).Within(0.0001f));

            worldHud.Remove(barStableId, ref emitCache.RetainedBarBufferIndexPlusOne);
            worldHud.Remove(textStableId, ref emitCache.RetainedTextBufferIndexPlusOne);
            Assert.That(emitCache.RetainedBarBufferIndexPlusOne, Is.EqualTo(0));
            Assert.That(emitCache.RetainedTextBufferIndexPlusOne, Is.EqualTo(0));
            Assert.That(worldHud.Count, Is.EqualTo(0));
            Assert.That(worldHud.GetRemovedStableIdSpan().Length, Is.EqualTo(2));
        }
        finally
        {
            TeamManager.Clear();
        }
    }
}
