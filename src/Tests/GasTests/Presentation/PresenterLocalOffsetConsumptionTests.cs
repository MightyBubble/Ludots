using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Issue #1559:LocalOffset 单次消费合同的回归——慢车道(EmitAssetBindings)里
    /// fast 批量发射被拒后回退到逐槽发射,同一槽位不得被标记消费两次。
    /// </summary>
    [TestFixture]
    public sealed class PresenterLocalOffsetConsumptionTests
    {
        [Test]
        public void EmitAssetBindings_LocalOffsetSlotWithParams_ConsumesOffsetExactlyOnce()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int scaleKey = PresenterParamKeyRegistry.Register("local-offset-single-consume.scale");
            int defId = definitions.Register("local-offset-single-consume", new PresenterDefinition
            {
                DefaultLifetime = 5f,
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 51,
                            MaterialId = 61,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.One,
                            LocalOffset = new Vector3(1f, 0f, 0f),
                            ScaleParamKey = scaleKey,
                            AssetIdParamKey = -1,
                        },
                    },
                },
                ParamDefaults = new[]
                {
                    new ParamDefault { ParamKey = scaleKey, Lane = ParamLane.Float, FloatValue = 1.5f },
                },
            });

            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var stableDrawCache = new StableDrawCache();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 16);

            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = instances.Create(
                defId,
                owner,
                0,
                PresentationAnchorKind.Entity,
                new Vector3(1f, 2f, 3f),
                4841,
                Entity.Null,
                definition: null);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SetParam(presenter, scaleKey, ParamLane.Float, 1.5f, 0, Vector4.Zero);

            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);
            var snapshotBuffer = new PrimitiveDrawBuffer();
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                new PrimitiveDrawBuffer(),
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            Assert.DoesNotThrow(() =>
            {
                emit.Update(0.016f);
                flush.Update(0.016f);
            }, "非 fast 定义 + LocalOffset≠0 的 Mesh 槽在一次发射访问里只消费一次局部偏移");
            Assert.That(stableDrawCache.Count, Is.EqualTo(1), "慢车道静态视觉经 flush 落入稳定绘制缓存");
        }
    }
}
