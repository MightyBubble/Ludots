using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
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
            Assert.That(values.Length, Is.EqualTo(9), "AssetKind SSOT is the architecture enum, which defines 9 kinds.");
            Assert.That(values, Does.Contain(AssetKind.Mesh));
            Assert.That(values, Does.Contain(AssetKind.SkinnedMesh));
            Assert.That(values, Does.Contain(AssetKind.Decal));
            Assert.That(values, Does.Contain(AssetKind.VFX));
            Assert.That(values, Does.Contain(AssetKind.Sound));
            Assert.That(values, Does.Contain(AssetKind.Spline));
            Assert.That(values, Does.Contain(AssetKind.WorldHud));
            Assert.That(values, Does.Contain(AssetKind.WorldText));
            Assert.That(values, Does.Contain(AssetKind.GroundOverlay));
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
                        },
                    },
                ],
            });

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
                            LocalScale = Vector3.One,
                            ScaleParamKey = 41,
                        },
                    },
                ],
            });

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
                new Dictionary<string, object>(),
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
                            LocalScale = Vector3.One,
                        },
                    },
                ],
            });

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
            var events = new PresentationEventStream();
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
                            LocalScale = new Vector3(60f, 8f, 1f),
                            MaterialParamKey = 51,
                        },
                    },
                ],
                DefaultColor = new Vector4(0.2f, 0.8f, 0.2f, 1f),
            });

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
                       soundRequests))
            {
                behaviorSystem.Update(0.016f);
            }

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
                            LocalScale = Vector3.One,
                            ScaleParamKey = 61,
                            MaterialParamKey = 62,
                        },
                    },
                ],
                DefaultColor = new Vector4(1f, 0.3f, 0.2f, 1f),
                DefaultFontSize = 18,
                DefaultTextId = 4001,
                WorldTextMode = WorldHudValueMode.AttributeCurrentOverBase,
            });

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
                new Dictionary<string, object>(),
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
                            LocalScale = Vector3.One,
                        },
                    },
                ],
                DefaultColor = new Vector4(0.2f, 0.6f, 1f, 0.4f),
            });

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
                            LocalScale = Vector3.One,
                        },
                    },
                ],
            });

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
                            LocalScale = Vector3.One,
                            ScaleParamKey = scaleKey,
                            MaterialParamKey = assetKind == AssetKind.WorldHud ? scaleKey : -1,
                        },
                    },
                ],
                DefaultTextId = 1,
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(1f, 0f, 2f), 9601, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
            instances.SetParam(performer, scaleKey, ParamLane.Float, 1.5f, 0, default);

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
                            LocalScale = Vector3.One,
                        },
                    },
                ],
                DefaultTextId = 1,
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9651, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

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
            var events = new PresentationEventStream();
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
                            LocalScale = Vector3.One,
                        },
                    },
                ],
                DefaultTextId = 1,
            });

            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 9661, Entity.Null, definitions.Get(defId));
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;
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
            var events = new PresentationEventStream();
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
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var cache = new StableDrawCache(4);

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
                            LocalScale = Vector3.One,
                            MaterialParamKey = materialKey,
                        },
                    },
                ],
            });

            PerformerDefinition definition = definitions.Get(defId);
            Entity performer = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9801, Entity.Null, definition);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

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
            int stableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(9801, 0, assetKind, defId);
            Assert.That(cache.Contains(stableId), Is.True);
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
            var events = new PresentationEventStream();
            var soundRequests = new SoundRequestBuffer();
            var ownerChanges = new PresentationOwnerChangeBuffer();
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
                Bindings =
                [
                    new PerformerParamBinding
                    {
                        ParamKey = paramKey,
                        Value = new ValueRef
                        {
                            Source = ValueSourceKind.AttributeRatio,
                            SourceId = attributeId,
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
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.One,
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
                            LocalScale = Vector3.One,
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
            var events = new PresentationEventStream();
            var ownerChanges = new PresentationOwnerChangeBuffer();
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
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.One,
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
            var events = new PresentationEventStream();
            var commands = new PerformerCommandBuffer();
            var markers = new TransientMarkerBuffer();
            var requests = new PresentationRequestBuffer();
            var stableIds = new PresentationStableIdAllocator();
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
                stableDrawCache: stableDrawCache);
            using var behavior = new PerformerBehaviorSystem(world, instances, definitions, events, soundRequests);
            using var emit = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                stableDrawCache: stableDrawCache);

            runtime.Update(0.016f);
            behavior.Update(0.016f);
            emit.Update(0.016f);

            IReadOnlyList<Entity> performers = instances.GetActiveByDefinition(defId);
            Assert.That(performers.Count, Is.EqualTo(1));
            Entity performer = performers[0];
            int visualStableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                world.Get<PerformerState>(performer).StableId,
                slotIndex: 0,
                AssetKind.Mesh,
                defId);
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
