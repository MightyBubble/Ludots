using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Arch.Core.Extensions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerAssetKindTests
    {
        [Test]
        public void AssetKindContract_ArchitectureExposesNineKinds()
        {
            AssetKind[] values = (AssetKind[])System.Enum.GetValues(typeof(AssetKind));
            Assert.That(values.Length, Is.EqualTo(10), "AssetKind SSOT is the architecture enum, which defines 10 kinds.");
            Assert.That(values, Does.Contain(AssetKind.Mesh));
            Assert.That(values, Does.Contain(AssetKind.SkinnedMesh));
            Assert.That(values, Does.Contain(AssetKind.Decal));
            Assert.That(values, Does.Contain(AssetKind.VFX));
            Assert.That(values, Does.Contain(AssetKind.Sound));
            Assert.That(values, Does.Contain(AssetKind.Spline));
            Assert.That(values, Does.Contain(AssetKind.WorldHud));
            Assert.That(values, Does.Contain(AssetKind.WorldText));
            Assert.That(values, Does.Contain(AssetKind.GroundOverlay));
            Assert.That(values, Does.Contain(AssetKind.Surface));
        }

        [Test]
        public void PerformerVisualStableIdTable_AllocatesDistinctHandles_ForLegacyHashCollision()
        {
            int legacyMesh = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(1, 0, AssetKind.Mesh, 32);
            int legacySkinned = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(1, 0, AssetKind.SkinnedMesh, 1);
            Assert.That(legacySkinned, Is.EqualTo(legacyMesh), "The legacy projected int is the collision reproduced by issue #170.");

            var allocator = new PresentationStableIdAllocator();
            var table = new PerformerVisualStableIdTable(allocator, capacity: 8);
            PerformerVisualStableKey meshKey = PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(1, 0, AssetKind.Mesh, 32);
            PerformerVisualStableKey skinnedKey = PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(1, 0, AssetKind.SkinnedMesh, 1);

            int meshStableId = table.GetOrAllocate(meshKey);
            int skinnedStableId = table.GetOrAllocate(skinnedKey);

            Assert.That(skinnedStableId, Is.Not.EqualTo(meshStableId));
            Assert.That(table.GetOrAllocate(meshKey), Is.EqualTo(meshStableId), "The table must remain stable for the same semantic key.");
            Assert.That(table.TryGet(skinnedKey, out int resolvedSkinned), Is.True);
            Assert.That(resolvedSkinned, Is.EqualTo(skinnedStableId));
        }

        [Test]
        public void StaticStableVisual_ProductionPath_AllocatesDistinctHandles_WhenLegacyProjectionCollides()
        {
            const int meshPerformerStableId = 219_522;
            const int vfxPerformerStableId = 247_666;
            const int meshDefinitionId = 1;
            const int vfxDefinitionId = 817;

            int legacyMesh = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                meshPerformerStableId,
                slotIndex: 1,
                AssetKind.Mesh,
                meshDefinitionId);
            int legacyVfx = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                vfxPerformerStableId,
                slotIndex: 0,
                AssetKind.VFX,
                vfxDefinitionId);
            Assert.That(legacyVfx, Is.EqualTo(legacyMesh), "This pair reproduces a real static performer legacy StableId collision.");

            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PerformerVisualStableIdTable(stableIds, capacity: 4);
            var stableDrawCache = new StableDrawCache(4);

            int meshDefId = definitions.Register("collision.mesh", CreateStaticStableDefinition(1, AssetKind.Mesh, 101, 201));
            Assert.That(meshDefId, Is.EqualTo(meshDefinitionId));
            for (int id = meshDefinitionId + 1; id < vfxDefinitionId; id++)
            {
                Assert.That(definitions.Register($"collision.padding.{id}", new PerformerDefinition()), Is.EqualTo(id));
            }

            int vfxDefId = definitions.Register("collision.vfx", CreateStaticStableDefinition(0, AssetKind.VFX, 102, 202));
            Assert.That(vfxDefId, Is.EqualTo(vfxDefinitionId));

            PerformerDefinition meshDefinition = definitions.Get(meshDefId);
            PerformerDefinition vfxDefinition = definitions.Get(vfxDefId);
            instances.Create(meshDefId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, meshPerformerStableId, Entity.Null, meshDefinition);
            instances.Create(vfxDefId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(2f, 0f, 0f), vfxPerformerStableId, Entity.Null, vfxDefinition);

            using var emit = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);

            emit.Update(0.016f);

            PerformerVisualStableKey meshKey = PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(
                meshPerformerStableId,
                slotIndex: 1,
                AssetKind.Mesh,
                meshDefId);
            PerformerVisualStableKey vfxKey = PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(
                vfxPerformerStableId,
                slotIndex: 0,
                AssetKind.VFX,
                vfxDefId);
            Assert.That(visualStableIds.TryGet(meshKey, out int meshStableId), Is.True);
            Assert.That(visualStableIds.TryGet(vfxKey, out int vfxStableId), Is.True);
            Assert.That(vfxStableId, Is.Not.EqualTo(meshStableId));
            Assert.That(stableDrawCache.Count, Is.EqualTo(2));
            Assert.That(stableDrawCache.Contains(meshStableId), Is.True);
            Assert.That(stableDrawCache.Contains(vfxStableId), Is.True);
            Assert.That(requests.Count, Is.EqualTo(0), "Static stable visuals must stay in StableDrawCache, not fall back to transient requests.");
        }

        [Test]
        public void AssetKindContract_ArchitecturePreservesExplicitEnumValues()
        {
            Assert.That((byte)AssetKind.Mesh, Is.EqualTo(1));
            Assert.That((byte)AssetKind.SkinnedMesh, Is.EqualTo(2));
            Assert.That((byte)AssetKind.Decal, Is.EqualTo(3));
            Assert.That((byte)AssetKind.VFX, Is.EqualTo(4));
            Assert.That((byte)AssetKind.Sound, Is.EqualTo(5));
            Assert.That((byte)AssetKind.Spline, Is.EqualTo(6));
            Assert.That((byte)AssetKind.WorldHud, Is.EqualTo(7));
            Assert.That((byte)AssetKind.WorldText, Is.EqualTo(8));
            Assert.That((byte)AssetKind.GroundOverlay, Is.EqualTo(9));
            Assert.That((byte)AssetKind.Surface, Is.EqualTo(10));
        }

        [Test]
        public void VisualRenderPathContract_ExposesSurfaceLane()
        {
            Assert.That((byte)VisualRenderPath.Surface, Is.EqualTo(6));
            Assert.That(VisualRenderPath.Surface.IsSurfaceLane(), Is.True);
            Assert.That(VisualRenderPath.Surface.IsStaticInstanceLane(), Is.False);
            Assert.That(VisualRenderPath.Surface.IsSkinnedLane(), Is.False);
        }

        [TestCase(AssetKind.Mesh, 1001, 2001, VisualRenderPath.StaticMesh)]
        [TestCase(AssetKind.SkinnedMesh, 1002, 2002, VisualRenderPath.SkinnedMesh)]
        [TestCase(AssetKind.Decal, 1003, 2003, VisualRenderPath.StaticMesh)]
        [TestCase(AssetKind.VFX, 1004, 2004, VisualRenderPath.StaticMesh)]
        public void AssetBinding_VisualKinds_EmitVisualProxy(
            AssetKind assetKind,
            int assetId,
            int materialId,
            VisualRenderPath renderPath)
        {
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = 7001 },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PerformerAnimatorStateBuffer(4);
            var soundRequests = new SoundRequestBuffer();

            int defId = definitions.Register($"asset.{assetKind}", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = assetId,
                            MaterialId = materialId,
                            RenderPath = renderPath,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 9100 + (int)assetKind, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = new Vector3(1.5f, 2f, 2.5f);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                soundRequests);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(span[0].VisualProxy.MeshAssetId, Is.EqualTo(assetId));
            Assert.That(span[0].VisualProxy.MaterialId, Is.EqualTo(materialId));
            Assert.That(span[0].VisualProxy.RenderPath, Is.EqualTo(renderPath));
            Assert.That(span[0].VisualProxy.Position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            Assert.That(span[0].VisualProxy.Scale, Is.EqualTo(new Vector3(1.5f, 2f, 2.5f)));
            Assert.That(span[0].VisualProxy.OwnerStableId, Is.EqualTo(7001));
            Assert.That(span[0].VisualProxy.OwnerStableId, Is.Not.EqualTo(9100 + (int)assetKind));
            Assert.That(span[0].VisualProxy.StableId, Is.Not.EqualTo(span[0].VisualProxy.OwnerStableId));
        }

        [Test]
        public void ChildVisual_UsesGameplayOwnerIdentity_NotRootOrChildPerformerIdentity()
        {
            const int gameplayOwnerStableId = 7301;
            const int rootPerformerStableId = 9301;
            const int childPerformerStableId = 9302;
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = gameplayOwnerStableId },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int childDefId = definitions.Register("owner.identity.child", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1001,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });
            int rootDefId = definitions.Register("owner.identity.root", new PerformerDefinition
            {
                Children =
                [
                    new ChildPerformerRef { DefinitionId = childDefId, ScopeTag = 1 },
                ],
            });
            instances.BindDefinitions(definitions);

            Entity root = instances.CreateHierarchy(
                definitions,
                rootDefId,
                owner,
                scopeId: 0,
                PresentationAnchorKind.Entity,
                Vector3.Zero,
                rootPerformerStableId,
                Entity.Null,
                definitions.Get(rootDefId),
                () => childPerformerStableId);
            Entity child = world.Get<PerformerChildren>(root).Get(0);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);
            system.Update(0.016f);

            PerformerState rootState = world.Get<PerformerState>(root);
            PerformerState childState = world.Get<PerformerState>(child);
            ReadOnlySpan<PresentationRequest> emitted = requests.GetSpan();
            Assert.That(emitted.Length, Is.EqualTo(1));
            PresentationVisualProxy proxy = emitted[0].VisualProxy;
            Assert.Multiple(() =>
            {
                Assert.That(rootState.OwnerStableId, Is.EqualTo(gameplayOwnerStableId));
                Assert.That(rootState.StableId, Is.EqualTo(rootPerformerStableId));
                Assert.That(childState.OwnerStableId, Is.EqualTo(gameplayOwnerStableId));
                Assert.That(childState.StableId, Is.EqualTo(childPerformerStableId));
                Assert.That(proxy.OwnerStableId, Is.EqualTo(gameplayOwnerStableId));
                Assert.That(proxy.OwnerStableId, Is.Not.EqualTo(rootPerformerStableId));
                Assert.That(proxy.OwnerStableId, Is.Not.EqualTo(childPerformerStableId));
                Assert.That(proxy.StableId, Is.Not.EqualTo(gameplayOwnerStableId));
                Assert.That(proxy.StableId, Is.Not.EqualTo(rootPerformerStableId));
                Assert.That(proxy.StableId, Is.Not.EqualTo(childPerformerStableId));
            });
        }

        [Test]
        public void AssetBinding_Surface_EmitsVisualProxyWithSurfaceRoutingAndCustomData()
        {
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = 7101 },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            int heatParamKey = 410;
            int flowParamKey = 411;
            int defId = definitions.Register("asset.surface", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Surface,
                            AssetId = 1201,
                            MaterialId = 2201,
                            RenderPath = VisualRenderPath.Surface,
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                            SurfaceLayerKey = "terrain.rvt",
                            SortId = 7,
                            MaterialCustomData = new MaterialCustomDataBinding
                            {
                                Slots =
                                [
                                    new MaterialCustomDataSlotBinding
                                    {
                                        Slot = 0,
                                        Lane = MaterialCustomDataLane.Float,
                                        ParamKey = heatParamKey,
                                    },
                                    new MaterialCustomDataSlotBinding
                                    {
                                        Slot = 1,
                                        Lane = MaterialCustomDataLane.Vector,
                                        ParamKey = flowParamKey,
                                    },
                                ],
                            },
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 9100, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = Vector3.One;
            instances.SetParam(performer, heatParamKey, ParamLane.Float, 3.5f, 0, default);
            instances.SetParam(performer, flowParamKey, ParamLane.Vector, 0f, 0, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                CreateWorldHudProjectionGlobals(world, owner),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            PresentationVisualProxy proxy = span[0].VisualProxy;
            Assert.That(proxy.AssetKind, Is.EqualTo(AssetKind.Surface));
            Assert.That(proxy.RenderPath, Is.EqualTo(VisualRenderPath.Surface));
            Assert.That(proxy.SurfaceLayerKey, Is.EqualTo("terrain.rvt"));
            Assert.That(proxy.SortId, Is.EqualTo(7));
            Assert.That(proxy.MaterialCustomData.Count, Is.EqualTo(2));
            Assert.That(proxy.MaterialCustomData.GetSlot(0).X, Is.EqualTo(3.5f).Within(0.001f));
            Assert.That(proxy.MaterialCustomData.GetSlot(1), Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        }

        [Test]
        public void AssetBinding_Spline_EmitsRoadSplineRequest()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register("asset.spline", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Spline,
                            AssetId = 3001,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            ScaleParamKey = 41,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(2f, 3f, 4f), 9201, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = Vector3.One;
            instances.SetParam(performer, 41, ParamLane.Float, 2.25f, 0, default);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                CreateWorldHudProjectionGlobals(world, owner),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.RoadSpline));
            Assert.That(span[0].RoadSpline.StableId, Is.GreaterThan(0));
            Assert.That(span[0].RoadSpline.Width, Is.EqualTo(2.25f).Within(0.001f));
            Assert.That(span[0].RoadSpline.P0, Is.EqualTo(new Vector3(2f, 3f, 4f)));
        }

        [Test]
        public void AssetBinding_Sound_EmitsSoundRequest()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var soundRequests = new SoundRequestBuffer();

            int defId = definitions.Register("asset.sound", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Sound,
                            AssetId = 3501,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(5f, 6f, 7f), 9251, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = Vector3.One;

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests);

            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(0));
            Assert.That(soundRequests.Count, Is.EqualTo(1));
            Assert.That(soundRequests.GetSpan()[0].Kind, Is.EqualTo(SoundRequestKind.PlayOrUpdate));
            Assert.That(soundRequests.GetSpan()[0].SoundAssetId, Is.EqualTo(3501));
            Assert.That(soundRequests.GetSpan()[0].WorldPosition, Is.EqualTo(new Vector3(5f, 6f, 7f)));
        }

        [Test]
        public void AssetBinding_WorldHud_EmitsHudBarRequest()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var soundRequests = new SoundRequestBuffer();

            int defId = definitions.Register("asset.world_hud", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.WorldHud,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = new Vector3(60f, 8f, 1f),
                            MaterialParamKey = 51,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
                DefaultColor = new Vector4(0.2f, 0.8f, 0.2f, 1f),
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 8f, 9f), 9301, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = Vector3.One;
            instances.SetParam(performer, 51, ParamLane.Float, 0.65f, 0, default);

            using (var behaviorSystem = new PerformerBehaviorSystem(
                       world,
                       instances,
                       definitions,
                       events,
                       new PresentationOwnerChangeBuffer(8),
                       soundRequests))
            {
                behaviorSystem.Update(0.016f);
            }

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                CreateWorldHudProjectionGlobals(world, owner),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.WorldHud));
            Assert.That(span[0].WorldHud.Kind, Is.EqualTo(WorldHudItemKind.Bar));
            Assert.That(span[0].WorldHud.WorldPosition, Is.EqualTo(new Vector3(7f, 8f, 9f)));
            Assert.That(span[0].WorldHud.Value0, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(span[0].WorldHud.Width, Is.EqualTo(60f).Within(0.001f));
            Assert.That(span[0].WorldHud.Height, Is.EqualTo(8f).Within(0.001f));
        }

        [Test]
        public void AssetBinding_WorldText_EmitsHudTextRequest()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register("asset.world_text", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.WorldText,
                            AssetId = 4001,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            ScaleParamKey = 61,
                            MaterialParamKey = 62,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
                DefaultColor = new Vector4(1f, 0.3f, 0.2f, 1f),
                DefaultFontSize = 18,
                WorldTextMode = WorldHudValueMode.AttributeCurrentOverBase,
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(10f, 11f, 12f), 9401, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = Vector3.One;
            instances.SetParam(performer, 61, ParamLane.Float, 23f, 0, default);
            instances.SetParam(performer, 62, ParamLane.Float, 99f, 0, default);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                CreateWorldHudProjectionGlobals(world, owner),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.WorldHud));
            Assert.That(span[0].WorldHud.Kind, Is.EqualTo(WorldHudItemKind.Text));
            Assert.That(span[0].WorldHud.WorldPosition, Is.EqualTo(new Vector3(10f, 11f, 12f)));
            Assert.That(span[0].WorldHud.FontSize, Is.EqualTo(18));
            Assert.That(span[0].WorldHud.Text.TokenId, Is.EqualTo(4001));
            Assert.That(span[0].WorldHud.Text.ArgCount, Is.EqualTo(2));
        }

        [Test]
        public void AssetBinding_GroundOverlay_EmitsGroundOverlayRequest()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register("asset.ground_overlay", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.GroundOverlay,
                            AssetId = (int)GroundOverlayShape.Ring,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
                DefaultColor = new Vector4(0.2f, 0.6f, 1f, 0.4f),
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(3f, 0.1f, 4f), 9501, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = new Vector3(2.5f, 1.25f, 0.08f);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.GroundOverlay));
            Assert.That(span[0].GroundOverlay.Shape, Is.EqualTo(GroundOverlayShape.Ring));
            Assert.That(span[0].GroundOverlay.Center, Is.EqualTo(new Vector3(3f, 0.1f, 4f)));
            Assert.That(span[0].GroundOverlay.Radius, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(span[0].GroundOverlay.InnerRadius, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(span[0].GroundOverlay.BorderWidth, Is.EqualTo(0.08f).Within(0.001f));
        }

        [Test]
        public void AssetBinding_GroundOverlay_RejectsUnknownShapeId()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register("asset.ground_overlay.invalid", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.GroundOverlay,
                            AssetId = 99,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9502, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            Assert.Throws<System.InvalidOperationException>(() => system.Update(0.016f));
        }

        [TestCase(AssetKind.Spline, PresentationRequestKind.RoadSpline)]
        [TestCase(AssetKind.WorldHud, PresentationRequestKind.WorldHud)]
        [TestCase(AssetKind.WorldText, PresentationRequestKind.WorldHud)]
        [TestCase(AssetKind.GroundOverlay, PresentationRequestKind.GroundOverlay)]
        public void RetainedPresentationRequest_StaticSubtype_EmitsOnlyWhenDirty(
            AssetKind assetKind,
            PresentationRequestKind expectedKind)
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            int scaleKey = 77;

            int defId = definitions.Register($"asset.retained.{assetKind}", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = ResolveRetainedAssetId(assetKind),
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            ScaleParamKey = scaleKey,
                            MaterialParamKey = assetKind == AssetKind.WorldHud ? scaleKey : -1,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(1f, 0f, 2f), 9601, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, scaleKey, ParamLane.Float, 1.5f, 0, default);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                RequiresWorldHudProjection(assetKind)
                    ? CreateWorldHudProjectionGlobals(world, owner)
                    : new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(expectedKind));

            requests.Clear();
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0), "Retained static performer subtypes must not re-emit unchanged requests every frame.");

            instances.SetParam(performer, scaleKey, ParamLane.Float, 2.0f, 0, default);
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(expectedKind));
        }

        [TestCase(AssetKind.WorldHud, PresentationRequestKind.RemoveWorldHud)]
        [TestCase(AssetKind.WorldText, PresentationRequestKind.RemoveWorldHud)]
        [TestCase(AssetKind.Spline, PresentationRequestKind.RemoveRoadSpline)]
        [TestCase(AssetKind.GroundOverlay, PresentationRequestKind.RemoveGroundOverlay)]
        public void RetainedPresentationRequest_StaticSubtype_RemovesExplicitlyWhenOwnerDies(
            AssetKind assetKind,
            PresentationRequestKind removeKind)
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();

            int defId = definitions.Register($"asset.retained.remove.{assetKind}", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = ResolveRetainedAssetId(assetKind),
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9651, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                RequiresWorldHudProjection(assetKind)
                    ? CreateWorldHudProjectionGlobals(world, owner)
                    : new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            requests.Clear();

            world.Destroy(owner);
            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(removeKind));
            Assert.That(world.IsAlive(performer), Is.False);
        }

        [TestCase(AssetKind.WorldHud, PresentationRequestKind.RemoveWorldHud)]
        [TestCase(AssetKind.WorldText, PresentationRequestKind.RemoveWorldHud)]
        [TestCase(AssetKind.Spline, PresentationRequestKind.RemoveRoadSpline)]
        [TestCase(AssetKind.GroundOverlay, PresentationRequestKind.RemoveGroundOverlay)]
        public void RuntimeDestroy_RetainedPresentationSubtype_QueuesAdapterRemovalInProductionOrder(
            AssetKind assetKind,
            PresentationRequestKind removeKind)
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var commands = new PerformerCommandBuffer();
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();

            int defId = definitions.Register($"asset.retained.runtime_destroy.{assetKind}", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = ResolveRetainedAssetId(assetKind),
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9661, Entity.Null, definitions.Get(defId));
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            using (var emitSystem = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                RequiresWorldHudProjection(assetKind)
                    ? CreateWorldHudProjectionGlobals(world, owner)
                    : new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!))
            {
                emitSystem.Update(0.016f);
            }
            Assert.That(requests.Count, Is.EqualTo(1));
            requests.Clear();

            using var runtimeSystem = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions);

            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformer,
                PerformerEntity = performer,
            }), Is.True);

            runtimeSystem.Update(0.016f);

            Assert.That(world.IsAlive(performer), Is.False);
            Assert.That(requests.Count, Is.EqualTo(1), "Runtime destroy runs before emit in production order, so it must queue retained adapter cleanup itself.");
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(removeKind));
        }

        [Test]
        public void RuntimeDestroy_SurfaceSource_QueuesRemovalInProductionOrder()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var commands = new PerformerCommandBuffer();
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            int scopeId = 4243;

            var surfaceDefinition = new PerformerDefinition
            {
                Surface = new SurfaceAuthoringBlock
                {
                    Kind = PerformerSurfaceKind.SplineRibbon,
                    LodProfileId = "default_surface_lod",
                    MaterialSet = new PerformerSurfaceMaterialSet { PrimaryMaterialId = "default_surface" },
                },
            };
            int defId = definitions.Register("surface.runtime_destroy", surfaceDefinition);
            Entity performer = instances.Create(defId, owner, scopeId, PresentationAnchorKind.Entity, Vector3.Zero, 9702, Entity.Null, definitions.Get(defId));
            using (var emitSystem = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!))
            {
                emitSystem.Update(0.016f);
            }
            Assert.That(requests.Count, Is.EqualTo(1));
            requests.Clear();

            using var runtimeSystem = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions);

            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformer,
                PerformerEntity = performer,
            }), Is.True);

            runtimeSystem.Update(0.016f);

            Assert.That(world.IsAlive(performer), Is.False);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(PresentationRequestKind.RemoveSurfaceSource));
            Assert.That(requests.GetSpan()[0].StableId, Is.EqualTo(9702));
        }

        [TestCase(AssetKind.Mesh)]
        [TestCase(AssetKind.SkinnedMesh)]
        [TestCase(AssetKind.Decal)]
        [TestCase(AssetKind.VFX)]
        public void StaticStableVisual_CacheableSubtype_EmitsOnlyWhenDirty_AndRemovesWhenDeactivated(AssetKind assetKind)
        {
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = 7002 },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var cache = new StableDrawCache(4);
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PerformerVisualStableIdTable(stableIds, capacity: 4);

            int materialKey = 76;
            int defId = definitions.Register($"asset.{assetKind}.static", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = 1004,
                            MaterialId = 2004,
                            Mobility = VisualMobility.Static,
                            RenderPath = assetKind == AssetKind.SkinnedMesh
                                ? VisualRenderPath.SkinnedMesh
                                : VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            MaterialParamKey = materialKey,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            PerformerDefinition definition = definitions.Get(defId);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9801, Entity.Null, definition);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, materialKey, ParamLane.Int, 0f, 2004, default);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!,
                stableDrawCache: cache,
                visualStableIds: visualStableIds);

            system.Update(0.016f);
            Assert.That(visualStableIds.TryGet(
                PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(9801, 0, assetKind, defId),
                out int stableId), Is.True);
            Assert.That(cache.Contains(stableId), Is.True);
            var projected = new PrimitiveDrawBuffer(capacity: 4);
            cache.Project(new PresentationVisualProxyEmitter(projected), evictUntouched: false);
            Assert.That(projected.Count, Is.EqualTo(1));
            Assert.That(projected.GetSpan()[0].OwnerStableId, Is.EqualTo(7002));
            Assert.That(projected.GetSpan()[0].OwnerStableId, Is.Not.EqualTo(9801));
            Assert.That(projected.GetSpan()[0].StableId, Is.EqualTo(stableId));
            Assert.That(requests.Count, Is.EqualTo(0), "Static stable visual subtypes must write directly into StableDrawCache, not spam PresentationRequest.");
            int revisionAfterFirstEmit = cache.ContentRevision;

            system.Update(0.016f);
            Assert.That(cache.ContentRevision, Is.EqualTo(revisionAfterFirstEmit), "Unchanged static stable visuals must not rewrite cache content every frame.");

            instances.SetParam(performer, materialKey, ParamLane.Int, 0f, 3004, default);
            system.Update(0.016f);
            Assert.That(cache.Contains(stableId), Is.True);
            Assert.That(cache.ContentRevision, Is.GreaterThan(revisionAfterFirstEmit), "Material changes must dirty and rewrite the stable cache entry.");

            instances.SetBehaviorActive(performer, definition, 0, active: false);
            system.Update(0.016f);

            Assert.That(cache.Contains(stableId), Is.False, "Inactive cacheable visual subtypes must remove retained stable draw entries.");
            Assert.That(visualStableIds.TryGet(
                PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(9801, 0, assetKind, defId),
                out int retainedStableId), Is.True, "Behavior deactivation removes output, not semantic identity.");
            Assert.That(retainedStableId, Is.EqualTo(stableId));

            instances.SetBehaviorActive(performer, definition, 0, active: true);
            system.Update(0.016f);

            Assert.That(cache.Contains(stableId), Is.True, "Reactivated static visuals must reuse the same adapter-facing handle.");

            var commands = new PerformerCommandBuffer();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var markers = new TransientMarkerBuffer();
            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions,
                stableDrawCache: cache,
                visualStableIds: visualStableIds);
            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.DestroyPerformer,
                PerformerEntity = performer,
            }), Is.True);

            runtime.Update(0.016f);

            Assert.That(world.IsAlive(performer), Is.False);
            Assert.That(cache.Contains(stableId), Is.False);
            Assert.That(visualStableIds.TryGet(
                PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(9801, 0, assetKind, defId),
                out _), Is.False, "Performer destroy releases semantic visual keys; adapter handles remain non-reused by the allocator.");
        }

        [Test]
        public void SkinnedMesh_WithAnimator_RemainsDynamicAndCarriesUpdatedOverlay()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PerformerAnimatorStateBuffer(4);
            var cache = new StableDrawCache(4);

            int defId = definitions.Register("asset.skinned.dynamic.animator", new PerformerDefinition
            {
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
                            AssetId = 1101,
                            MaterialId = 2101,
                            RenderPath = VisualRenderPath.SkinnedMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Animator,
                        ActiveByDefault = true,
                        Animator = new AnimatorConfig
                        {
                            AnimatorControllerId = 3101,
                            AnimationProfileId = 4101,
                            SpeedParamKey = -1,
                            StateParamKey = -1,
                        },
                    },
                ],
            });

            PerformerDefinition definition = definitions.Get(defId);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9903, Entity.Null, definition);
            animatorStates.Ensure(performer, 3101);
            ref AnimationOverlayRequest overlay = ref animatorStates.GetOverlay(performer);
            overlay.BaseClip = AnimatorBuiltinClipState.Create(AnimatorBuiltinClipId.LocomotionCycle, 0.25f, 0.5f);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                soundRequests: null!,
                stableDrawCache: cache);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].VisualProxy.AnimationOverlay.BaseClip.NormalizedTime01, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(cache.Count, Is.EqualTo(0), "Movable skinned animator output must not enter static stable cache.");

            requests.Clear();
            overlay.BaseClip = AnimatorBuiltinClipState.Create(AnimatorBuiltinClipId.LocomotionCycle, 0.75f, 1f);
            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].VisualProxy.AnimationOverlay.BaseClip.NormalizedTime01, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(requests.GetSpan()[0].VisualProxy.AnimationOverlay.BaseClip.Weight01, Is.EqualTo(1f).Within(0.001f));
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void MovableInstancedMesh_RemainsTransientAndReemitsMovedPosition()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var cache = new StableDrawCache(4);

            int defId = definitions.Register("asset.mesh.movable.ism", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1201,
                            MaterialId = 2201,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            PerformerDefinition definition = definitions.Get(defId);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9911, Entity.Null, definition);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!,
                stableDrawCache: cache);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].VisualProxy.Position, Is.EqualTo(Vector3.Zero));
            Assert.That(cache.Count, Is.EqualTo(0), "Movable ISM visuals must not be treated as stable static cache entries.");

            requests.Clear();
            world.Get<PerformerWorldPosition>(performer).Value = new Vector3(12f, 0f, 34f);
            instances.MarkTransformDrivenEmitDirty(performer);
            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].VisualProxy.Position, Is.EqualTo(new Vector3(12f, 0f, 34f)));
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void SurfaceOnlyDefinition_AttributeBindingUpdatesParamsThroughProductionBehaviorPath()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(17, 100f);
            attributes.SetCurrent(17, 25f);
            Entity owner = world.Create(attributes);
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var soundRequests = new SoundRequestBuffer();
            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            int attributeId = 17;
            int paramKey = 101;

            int defId = definitions.Register("surface.with.attribute.binding", new PerformerDefinition
            {
                Surface = new SurfaceAuthoringBlock
                {
                    Kind = PerformerSurfaceKind.SplineRibbon,
                    LodProfileId = "default_surface_lod",
                    MaterialSet = new PerformerSurfaceMaterialSet { PrimaryMaterialId = "default_surface" },
                },
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = attributeId,
                            TargetParamKey = paramKey,
                            Mode = ValueSourceKind.AttributeRatio,
                        },
                    },
                ],
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9751, Entity.Null, definitions.Get(defId));
            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                ownerChanges,
                soundRequests);

            system.Update(0.016f);
            Assert.That(instances.ResolveFloat(performer, paramKey, -1f), Is.EqualTo(0.25f).Within(0.001f));

            ref AttributeBuffer updated = ref world.Get<AttributeBuffer>(owner);
            updated.SetCurrent(attributeId, 80f);
            Assert.That(ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, attributeId)), Is.True);

            system.Update(0.016f);
            Assert.That(instances.ResolveFloat(performer, paramKey, -1f), Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void SetParam_PropagatesOnlyToAffectedStaticVisualChildren()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int swapParamKey = 104;

            int rootId = definitions.Register("param.root", new PerformerDefinition
            {
                Children =
                [
                    new ChildPerformerRef { DefinitionId = 2, ScopeTag = 1 },
                    new ChildPerformerRef { DefinitionId = 3, ScopeTag = 2 },
                ],
            });
            int affectedId = definitions.Register("param.child.affected", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1001,
                            AssetSwapParamKey = swapParamKey,
                            AssetSwapTable =
                            [
                                new AssetSwapEntry { ParamValue = 0f, AssetId = 1001 },
                                new AssetSwapEntry { ParamValue = 1f, AssetId = 1001 },
                            ],
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
                ParamDefaults =
                [
                    new ParamDefault { ParamKey = swapParamKey, Lane = ParamLane.Int, IntValue = 0 },
                ],
            });
            int unaffectedId = definitions.Register("param.child.unaffected", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1002,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });

            Assert.That(rootId, Is.EqualTo(1));
            Assert.That(affectedId, Is.EqualTo(2));
            Assert.That(unaffectedId, Is.EqualTo(3));
            instances.BindDefinitions(definitions);

            Entity root = instances.CreateHierarchy(
                definitions,
                rootId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                Vector3.Zero,
                9901,
                Entity.Null,
                definitions.Get(rootId),
                () => 9902);

            ref PerformerChildren rootChildren = ref world.Get<PerformerChildren>(root);
            Assert.That(rootChildren.Count, Is.EqualTo(2));
            Entity affected = rootChildren.Get(0);
            Entity unaffected = rootChildren.Get(1);
            Assert.That(world.Get<PerformerState>(affected).DefId, Is.EqualTo(affectedId));
            Assert.That(world.Get<PerformerState>(unaffected).DefId, Is.EqualTo(unaffectedId));

            instances.SetParamAndPropagateToAffectedChildren(root, swapParamKey, ParamLane.Int, 0f, 1, Vector4.Zero);

            Assert.That(world.Get<PerformerIntParams>(affected).TryGet(swapParamKey, out int affectedLocal), Is.True);
            Assert.That(affectedLocal, Is.EqualTo(1));
            Assert.That(world.Get<PerformerIntParams>(unaffected).TryGet(swapParamKey, out _), Is.False);
        }

        [Test]
        public void AttributeBindingThreshold_PropagatesAssetSwapParamToAffectedStaticVisualChildren()
        {
            using var world = World.Create();
            var attributes = default(AttributeBuffer);
            attributes.SetBase(17, 100f);
            attributes.SetCurrent(17, 50f);
            Entity owner = world.Create(attributes, new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var ownerChanges = new PresentationOwnerChangeBuffer(8);
            var soundRequests = new SoundRequestBuffer();
            var commands = new PerformerCommandBuffer();
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            int swapParamKey = 104;

            int rootId = definitions.Register("attr.threshold.root", new PerformerDefinition
            {
                Children =
                [
                    new ChildPerformerRef { DefinitionId = 2, ScopeTag = 1 },
                ],
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = 17,
                            TargetParamKey = 101,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds =
                            [
                                new ThresholdMapping { Threshold = 0.5f, OutputParamKey = swapParamKey, OutputValue = 2f },
                            ],
                        },
                    },
                ],
            });
            int childId = definitions.Register("attr.threshold.child", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1001,
                            AssetSwapParamKey = swapParamKey,
                            AssetSwapTable =
                            [
                                new AssetSwapEntry { ParamValue = 2f, AssetId = 1001 },
                            ],
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
            });
            Assert.That(rootId, Is.EqualTo(1));
            Assert.That(childId, Is.EqualTo(2));
            instances.BindDefinitions(definitions);

            using var runtimeSystem = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions);
            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = rootId,
                Source = owner,
                ScopeTag = 1,
                AnchorKind = PresentationAnchorKind.Entity,
                Position = Vector3.Zero,
            }), Is.True);
            runtimeSystem.Update(0.016f);

            IReadOnlyList<Entity> roots = instances.GetActiveByOwnerDefinition(rootId, owner);
            Assert.That(roots.Count, Is.EqualTo(1));
            Entity root = roots[0];

            using var system = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                events,
                ownerChanges,
                soundRequests);

            system.Update(0.016f);

            Entity child = world.Get<PerformerChildren>(root).Get(0);
            Assert.That(world.Get<PerformerIntParams>(child).TryGet(swapParamKey, out int localSwap), Is.True);
            Assert.That(localSwap, Is.EqualTo(2));
        }

        [Test]
        public void MaterialBinding_SourceParamChange_RecomputesMaterialBeforeStaticEmit()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            var commands = new PerformerCommandBuffer();
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PerformerVisualStableIdTable(stableIds, capacity: 16);
            var soundRequests = new SoundRequestBuffer();
            var stableDrawCache = new StableDrawCache(16);
            int materialParamKey = 300;
            int defId = definitions.Register("material.source.static", new PerformerDefinition
            {
                Rules =
                [
                    new PerformerRule
                    {
                        Event = new EventFilter { Kind = PresentationEventKind.EntitySpawned },
                        Command = new PerformerCommand { CommandKind = PerformerCommandKind.CreatePerformer },
                    },
                ],
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1001,
                            MaterialId = 5001,
                            MaterialParamKey = materialParamKey,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.Material,
                        ActiveByDefault = true,
                        Material = new MaterialConfig
                        {
                            BaseMaterialId = 5001,
                            MaterialSwapParamKey = materialParamKey,
                            SwapTable =
                            [
                                new MaterialSwapEntry { ParamValue = 0f, MaterialId = 5001 },
                                new MaterialSwapEntry { ParamValue = 1f, MaterialId = 5002 },
                            ],
                        },
                    },
                ],
                ParamDefaults =
                [
                    new ParamDefault { ParamKey = materialParamKey, Lane = ParamLane.Float, FloatValue = 0f },
                ],
            });

            instances.BindDefinitions(definitions);
            Assert.That(commands.TryAdd(new PerformerCommand
            {
                CommandKind = PerformerCommandKind.CreatePerformer,
                PerformerDefinitionId = defId,
                Source = owner,
                ScopeTag = 1,
                AnchorKind = PresentationAnchorKind.Entity,
                Position = Vector3.Zero,
            }), Is.True);

            using var runtime = new PerformerRuntimeSystem(
                world,
                commands,
                events,
                markers,
                requests,
                instances,
                stableIds,
                definitions,
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);
            using var behavior = new PerformerBehaviorSystem(world, instances, definitions, events, new PresentationOwnerChangeBuffer(8), soundRequests);
            using var emit = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);

            runtime.Update(0.016f);
            behavior.Update(0.016f);
            emit.Update(0.016f);

            IReadOnlyList<Entity> performers = instances.GetActiveByDefinition(defId);
            Assert.That(performers.Count, Is.EqualTo(1));
            Entity performer = performers[0];
            Assert.That(visualStableIds.TryGet(
                PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(
                    world.Get<PerformerState>(performer).StableId,
                    slotIndex: 0,
                    AssetKind.Mesh,
                    defId),
                out int visualStableId), Is.True);
            Assert.That(ReadMaterialFromStableDrawCache(stableDrawCache, visualStableId, out int initialMaterial), Is.True);
            Assert.That(initialMaterial, Is.EqualTo(5001));

            requests.Clear();
            instances.SetParamAndPropagateToAffectedChildren(performer, materialParamKey, ParamLane.Float, 1f, 0, Vector4.Zero);
            behavior.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(world.Has<PerfMaterialDirty>(performer), Is.False);
            Assert.That(world.Get<PerformerIntParams>(performer).TryGet(materialParamKey, out int materialId), Is.True);
            Assert.That(materialId, Is.EqualTo(5002));
            Assert.That(ReadMaterialFromStableDrawCache(stableDrawCache, visualStableId, out int swappedMaterial), Is.True);
            Assert.That(swappedMaterial, Is.EqualTo(5002));
        }

        [Test]
        public void SurfaceSource_RetainedEmitter_EmitsOnlyWhenDirty_AndRemovesExplicitly()
        {
            using var world = World.Create();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            int scopeId = 4242;

            var surfaceDefinition = new PerformerDefinition
            {
                Surface = new SurfaceAuthoringBlock
                {
                    Kind = PerformerSurfaceKind.SplineRibbon,
                    LodProfileId = "default_surface_lod",
                    MaterialSet = new PerformerSurfaceMaterialSet { PrimaryMaterialId = "default_surface" },
                },
            };
            int defId = definitions.Register("surface.retained", surfaceDefinition);

            Entity performer = instances.Create(defId, owner, scopeId, PresentationAnchorKind.Entity, new Vector3(10f, 0f, 20f), 9701, Entity.Null, surfaceDefinition);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(PresentationRequestKind.SurfaceSource));
            Assert.That(requests.GetSpan()[0].SurfaceSource.ScopeId, Is.EqualTo(scopeId));

            requests.Clear();
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0), "SurfaceSource must be retained by lifecycle, not kept alive by per-frame request spam.");

            ref CullState ownerCull = ref world.Get<CullState>(owner);
            ownerCull.IsVisible = false;
            ownerCull.LOD = LODLevel.Culled;
            instances.SyncCullVisibility();

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0), "Temporary cull must not destroy the retained SurfaceSource.");

            ownerCull.IsVisible = true;
            ownerCull.LOD = LODLevel.High;
            instances.SyncCullVisibility();

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(PresentationRequestKind.SurfaceSource));
            Assert.That(requests.GetSpan()[0].StableId, Is.EqualTo(9701));
            requests.Clear();

            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0), "Visible-again retained SurfaceSource should settle back to dirty-only emission.");

            world.Destroy(owner);
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests.GetSpan()[0].Kind, Is.EqualTo(PresentationRequestKind.RemoveSurfaceSource));
            Assert.That(requests.GetSpan()[0].StableId, Is.EqualTo(9701));
        }

        private static int ResolveRetainedAssetId(AssetKind assetKind)
        {
            return assetKind == AssetKind.GroundOverlay ? (int)GroundOverlayShape.Circle : 1;
        }

        private static bool RequiresWorldHudProjection(AssetKind assetKind)
        {
            return assetKind is AssetKind.WorldHud or AssetKind.WorldText;
        }

        private static Dictionary<string, object> CreateWorldHudProjectionGlobals(World world, Entity owner)
        {
            Entity viewer = world.Create();
            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
            projectionStore.Upsert(
                viewer,
                owner,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));

            return new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(projectionStore),
            };
        }

        private static PerformerDefinition CreateStaticStableDefinition(
            int slotIndex,
            AssetKind assetKind,
            int assetId,
            int materialId)
        {
            return new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = slotIndex,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = assetKind,
                            AssetId = assetId,
                            MaterialId = materialId,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            };
        }

        private static bool ReadMaterialFromStableDrawCache(StableDrawCache cache, int stableId, out int materialId)
        {
            var drawBuffer = new PrimitiveDrawBuffer(16);
            var proxies = new PresentationVisualProxyBuffer(16);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, proxyBuffer: proxies);
            cache.Project(emitter, evictUntouched: false);
            foreach (ref readonly PresentationVisualProxy proxy in proxies.GetSpan())
            {
                if (proxy.StableId == stableId)
                {
                    materialId = proxy.MaterialId;
                    return true;
                }
            }

            materialId = 0;
            return false;
        }
    }
}
