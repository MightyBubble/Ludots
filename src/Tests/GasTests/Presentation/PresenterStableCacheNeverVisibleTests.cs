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
    /// Issue #1558:never-visible 静态(出生即隐藏)不得分配 StableDrawCache 条目;
    /// 曾可见后转隐藏保留槽位(Hidden 快照只更新不新分配)。
    /// </summary>
    [TestFixture]
    public sealed class PresenterStableCacheNeverVisibleTests
    {
        [Test]
        public void StableCache_RequestPath_NeverVisibleKeepsCacheEmpty_OnceVisibleCulledKeepsHiddenSlot()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int scaleKey = PresenterParamKeyRegistry.Register("stable-cache-nevervisible.scale");
            int defId = definitions.Register("stable-cache-nevervisible", new PresenterDefinition
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
                            LocalOffset = Vector3.Zero,
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

            Entity owner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.High });
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

            emit.Update(0.016f);
            flush.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(0), "never-visible 静态不得分配缓存条目");

            world.Get<CullState>(owner).IsVisible = true;
            instances.SyncCullVisibility();
            emit.Update(0.016f);
            flush.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1), "转可见后正常分配");

            world.Get<CullState>(owner).IsVisible = false;
            instances.SyncCullVisibility();
            emit.Update(0.016f);
            flush.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1), "曾可见后转隐藏保留 Hidden 槽位,不新增不丢失");
        }
    }
}
