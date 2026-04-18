using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
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
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PerformerInstanceBuffer(capacity: 4);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 9100 + (int)assetKind, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldRotation = Quaternion.Identity;
            instance.WorldScale = new Vector3(1.5f, 2f, 2.5f);

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(2f, 3f, 4f), 9201, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldRotation = Quaternion.Identity;
            instance.WorldScale = Vector3.One;
            instances.SetParam(handle, 41, ParamLane.Float, 2.25f, 0, default);

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(5f, 6f, 7f), 9251, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldRotation = Quaternion.Identity;
            instance.WorldScale = Vector3.One;

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 8f, 9f), 9301, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldScale = Vector3.One;
            instances.SetParam(handle, 51, ParamLane.Float, 0.65f, 0, default);

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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
                LegacyWorldTextMode = WorldHudValueMode.AttributeCurrentOverBase,
            });

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(10f, 11f, 12f), 9401, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldScale = Vector3.One;
            instances.SetParam(handle, 61, ParamLane.Float, 23f, 0, default);
            instances.SetParam(handle, 62, ParamLane.Float, 99f, 0, default);

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(3f, 0.1f, 4f), 9501, -1, out int handle),
                Is.True);
            ref PerformerInstance instance = ref instances.Get(handle);
            instance.BehaviorActiveMask = 1u;
            instance.WorldScale = new Vector3(2.5f, 1.25f, 0.08f);

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
            var instances = new PerformerInstanceBuffer(capacity: 2);
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

            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9502, -1, out int handle),
                Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;

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
    }
}
