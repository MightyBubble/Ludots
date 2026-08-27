using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Map.Hex;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Raylib.Render;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ProjectedDecalContractTests
    {
        [Test]
        public void FromVisualScale_UsesXzForStampAndYForThickness()
        {
            var volume = ProjectedDecalVolume.FromVisualScale(new Vector3(3.8f, 2.5f, 4.2f));

            Assert.That(volume.StampSizeMeters, Is.EqualTo(new Vector2(3.8f, 4.2f)));
            Assert.That(volume.StampSizeMeters, Is.Not.EqualTo(Vector2.One));
            Assert.That(volume.ProjectionThicknessMeters, Is.EqualTo(2.5f));
        }

        [Test]
        public void AssetBindingVisualScale_Decal_UsesAuthoredScaleWithoutUnitFallback()
        {
            var asset = new AssetBindingConfig
            {
                AssetKind = AssetKind.Decal,
                LocalScale = new Vector3(3.8f, 2.5f, 4.2f),
                ScaleParamKey = -1,
            };
            Vector3 resolved = AssetBindingVisualScale.Resolve(in asset, Vector3.One, 1f);
            Assert.That(resolved, Is.EqualTo(new Vector3(3.8f, 2.5f, 4.2f)));

            var zero = asset;
            zero.LocalScale = Vector3.Zero;
            Assert.That(
                () => AssetBindingVisualScale.Resolve(in zero, Vector3.One, 1f),
                Throws.InvalidOperationException.With.Message.Contains("non-zero"));
        }

        [Test]
        public void FromVisualScale_RejectsZeroOrNonFiniteAxes()
        {
            Assert.That(
                () => ProjectedDecalVolume.FromVisualScale(new Vector3(0f, 1f, 1f)),
                Throws.InvalidOperationException.With.Message.Contains("non-zero"));
            Assert.That(
                () => ProjectedDecalVolume.FromVisualScale(new Vector3(1f, 0f, 1f)),
                Throws.InvalidOperationException.With.Message.Contains("non-zero"));
            Assert.That(
                () => ProjectedDecalVolume.FromVisualScale(new Vector3(1f, 1f, float.NaN)),
                Throws.InvalidOperationException.With.Message.Contains("finite"));
        }

        [Test]
        public void TryBuildWorldToLocal_StampExtentFollowsAuthoredScaleNotUnitSquare()
        {
            var unit = ProjectedDecalVolume.FromVisualScale(Vector3.One);
            Assert.That(
                unit.TryBuildWorldToLocal(Vector3.Zero, 0f, out _, out float unitMinX, out _, out float unitMinZ, out float unitMaxX, out _, out float unitMaxZ),
                Is.True);

            var authored = ProjectedDecalVolume.FromVisualScale(new Vector3(3.8f, 2.5f, 4.2f));
            Assert.That(
                authored.TryBuildWorldToLocal(Vector3.Zero, 0f, out _, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ),
                Is.True);

            Assert.That(maxX - minX, Is.EqualTo(3.8f).Within(1e-4f));
            Assert.That(maxZ - minZ, Is.EqualTo(4.2f).Within(1e-4f));
            Assert.That(maxY - minY, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(maxX - minX, Is.Not.EqualTo(unitMaxX - unitMinX).Within(1e-4f));
            Assert.That(maxZ - minZ, Is.Not.EqualTo(unitMaxZ - unitMinZ).Within(1e-4f));
        }

        [Test]
        public void AssetBinding_Decal_PreservesNonUnitLocalScaleOnVisualProxy()
        {
            Vector3 authored = new Vector3(3.8f, 2.5f, 4.2f);
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = 7201 },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PresenterAnimatorStateBuffer(4);
            var soundRequests = new SoundRequestBuffer();

            int defId = definitions.Register("asset.decal.authored-scale", new PresenterDefinition
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
                            AssetKind = AssetKind.Decal,
                            AssetId = 1003,
                            MaterialId = 2003,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalScale = authored,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 9303, Entity.Null, default);
            ref var state = ref world.Get<PresenterState>(presenter);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PresenterWorldRotation>(presenter);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PresenterWorldScale>(presenter);
            scale.Value = Vector3.One;

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new System.Collections.Generic.Dictionary<string, object>(),
                animatorStates,
                soundRequests);

            system.Update(0.016f);

            ReadOnlySpan<PresentationRequest> span = requests.GetSpan();
            Assert.That(span.Length, Is.EqualTo(1));
            Assert.That(span[0].Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(span[0].VisualProxy.AssetKind, Is.EqualTo(AssetKind.Decal));
            Assert.That(span[0].VisualProxy.Scale, Is.EqualTo(authored));

            var volume = ProjectedDecalVolume.FromVisualScale(span[0].VisualProxy.Scale);
            Assert.That(volume.StampSizeMeters, Is.EqualTo(new Vector2(authored.X, authored.Z)));
            Assert.That(volume.StampSizeMeters, Is.Not.EqualTo(Vector2.One));
            Assert.That(volume.ProjectionThicknessMeters, Is.EqualTo(authored.Y));
        }

        [Test]
        public void AssetBinding_Decal_ZeroLocalScaleThrowsInsteadOfUnitFallback()
        {
            using var world = World.Create();
            Entity owner = world.Create(
                new PresentationStableId { Value = 7202 },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var animatorStates = new PresenterAnimatorStateBuffer(4);
            var soundRequests = new SoundRequestBuffer();

            int defId = definitions.Register("asset.decal.zero-scale", new PresenterDefinition
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
                            AssetKind = AssetKind.Decal,
                            AssetId = 1003,
                            MaterialId = 2003,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Static,
                            LocalScale = Vector3.Zero,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, 0, PresentationAnchorKind.WorldPosition, Vector3.Zero, 9304, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            world.Get<PresenterWorldRotation>(presenter).Value = Quaternion.Identity;
            world.Get<PresenterWorldScale>(presenter).Value = Vector3.One;

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new System.Collections.Generic.Dictionary<string, object>(),
                animatorStates,
                soundRequests);

            Assert.That(() => system.Update(0.016f), Throws.InvalidOperationException.With.Message.Contains("non-zero"));
        }

        [Test]
        public void DrawDecal_WithoutReceiverProjector_Throws()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            var draw = new PrimitiveDrawBuffer(8);
            Assert.That(draw.TryAdd(new PrimitiveDrawItem
            {
                AssetKind = AssetKind.Decal,
                Scale = new Vector3(3.8f, 2.5f, 4.2f),
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
                Color = Vector4.One,
                StableId = 42,
                MaterialId = 7,
                RenderPath = VisualRenderPath.StaticMesh,
                Mobility = VisualMobility.Static,
                Visibility = VisualVisibility.Visible,
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => renderer.Draw(draw, default(Camera3D), new MeshAssetRegistry()))!;
            Assert.That(ex.Message, Does.Contain(nameof(RaylibPrimitiveRenderer.BindReceiverMeshProjector)));
            Assert.That(ex.Message, Does.Not.Contain("terrain-only"));
        }

        [Test]
        public void BindReceiverMeshProjector_RejectsNullAndAcceptsNonHeightmapImplementation()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            Assert.That(
                () => renderer.BindReceiverMeshProjector(null!),
                Throws.ArgumentNullException);
            renderer.BindReceiverMeshProjector(new StubReceiverMeshProjector());
        }

        [Test]
        public void RequireBoundReceiverMeshProjector_ThrowsWhenUnbound()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => RaylibPrimitiveRenderer.RequireBoundReceiverMeshProjector(null, 9))!;
            Assert.That(ex.Message, Does.Contain(nameof(RaylibPrimitiveRenderer.BindReceiverMeshProjector)));
        }

        [Test]
        public void TryDrawDecalItem_DoesNotHardcodeUnitStamp()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "Client",
                "Ludots.Raylib.Render",
                "Rendering",
                "RaylibPrimitiveRenderer.cs"));
            int method = source.IndexOf("private bool TryDrawDecalItem", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThanOrEqualTo(0));
            int next = source.IndexOf("private void SubmitAssetRecursive", method, StringComparison.Ordinal);
            Assert.That(next, Is.GreaterThan(method));
            string body = source[method..next];
            Assert.That(body, Does.Not.Contain("Vector2.One"));
            Assert.That(body, Does.Contain("ProjectedDecalVolume.FromVisualScale"));
            Assert.That(source, Does.Not.Contain("IRaylibTerrainMeshProjector"));
            Assert.That(source, Does.Contain("IRaylibReceiverMeshProjector"));
        }

        [Test]
        public void DecalProjector_BoardScaleStampRaisesBiasAndPaintsCliffs()
        {
            Assert.That(
                RaylibDecalProjectorRenderer.ResolveReceiverDepthBiasMeters(new Vector2(3.8f, 3.8f)),
                Is.EqualTo(RaylibDecalProjectorRenderer.DecalReceiverDepthBiasMeters).Within(1e-6f));
            Assert.That(
                RaylibDecalProjectorRenderer.ResolveMinReceiverNDotUp(new Vector2(3.8f, 3.8f)),
                Is.EqualTo(0.05f).Within(1e-6f));

            Vector2 eastAsia = new(63992.32f, 36567.04f);
            Assert.That(
                RaylibDecalProjectorRenderer.ResolveReceiverDepthBiasMeters(eastAsia),
                Is.EqualTo(63992.32f * RaylibDecalProjectorRenderer.DecalReceiverDepthBiasPerStampMeter).Within(1e-3f));
            Assert.That(
                RaylibDecalProjectorRenderer.ResolveMinReceiverNDotUp(eastAsia),
                Is.EqualTo(-1f));
        }

        [Test]
        public void DecalProjectShaders_ClipStampPlaneNotThinBoxLid()
        {
            string repo = FindRepoRoot();
            string fragment = File.ReadAllText(Path.Combine(repo, "src", "Platforms", "Desktop", "decal_project.fs"));
            string vertex = File.ReadAllText(Path.Combine(repo, "src", "Platforms", "Desktop", "decal_project.vs"));
            Assert.That(fragment, Does.Contain("abs(local.x) > 0.5 || abs(local.z) > 0.5"));
            Assert.That(fragment, Does.Not.Contain("abs(local.y)"));
            Assert.That(vertex, Does.Contain("receiverDepthBias"));
            Assert.That(vertex, Does.Not.Contain("0.04"));
            Assert.That(fragment, Does.Contain("minReceiverNDotUp"));
        }

        [Test]
        public void DecalProjectorDraw_FitsThroughReceiverContractNotFrameHeightmap()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "Client",
                "Ludots.Raylib.Render",
                "Rendering",
                "RaylibDecalProjectorRenderer.cs"));
            int method = source.IndexOf("public void Draw(", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThanOrEqualTo(0));
            int next = source.IndexOf("private void EnsureDecalResources", method, StringComparison.Ordinal);
            Assert.That(next, Is.GreaterThan(method));
            string body = source[method..next];
            Assert.That(body, Does.Contain("FitYawedStampProjectorCenter"));
            Assert.That(body, Does.Not.Contain("_frameVisualHeightmap"));
            Assert.That(source, Does.Not.Contain("FitDecalProjectorVolume"));
            Assert.That(source, Does.Not.Contain("if (_frameVisualHeightmap == null)"));
        }

        [Test]
        public void AssetBindingVisualScale_IsTheOnlyDecalScaleResolver()
        {
            string repo = FindRepoRoot();
            string helper = File.ReadAllText(Path.Combine(
                repo, "src", "Core", "Presentation", "Presenters", "AssetBindingVisualScale.cs"));
            string emit = File.ReadAllText(Path.Combine(
                repo, "src", "Core", "Presentation", "Systems", "PresenterEmitSystem.cs"));
            string assetEmit = File.ReadAllText(Path.Combine(
                repo, "src", "Core", "Presentation", "Systems", "PresenterAssetEmitRuntime.cs"));

            Assert.That(helper, Does.Contain("ProjectedDecalVolume.FromVisualScale"));
            Assert.That(emit, Does.Not.Contain("ProjectedDecalVolume.FromVisualScale"));
            Assert.That(assetEmit, Does.Not.Contain("ProjectedDecalVolume.FromVisualScale"));
            Assert.That(emit, Does.Contain("_assetEmitter.ResolveScale"));
            Assert.That(assetEmit, Does.Contain("AssetBindingVisualScale.Resolve"));
        }

        [Test]
        public void HeightmapProjector_FitWithoutSampleSourceThrows()
        {
            using var projector = new RaylibVisualHeightmapRenderer();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => projector.FitYawedStampProjectorCenter(
                    new Vector3(1f, 9f, 2f),
                    0f,
                    new Vector2(3.8f, 3.8f),
                    44))!;
            Assert.That(ex.Message, Does.Contain(nameof(RaylibVisualHeightmapRenderer.BindStampHeightSampleSource)));
        }

        [Test]
        public void HeightmapProjector_FitCentersOnSampledReceiverHeight()
        {
            using var projector = new RaylibVisualHeightmapRenderer();
            projector.BindStampHeightSampleSource(new FlatVisualHeightmap());
            Vector3 center = projector.FitYawedStampProjectorCenter(
                new Vector3(4f, 99f, 6f),
                0.3f,
                new Vector2(3.8f, 4.2f),
                45);
            Assert.That(center.X, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(center.Z, Is.EqualTo(6f).Within(1e-4f));
            Assert.That(center.Y, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void HeightmapProjector_FitAppliesDisplayHeightScaleToSampledHeight()
        {
            using var projector = new RaylibVisualHeightmapRenderer();
            projector.DisplayHeightScale = 50f;
            projector.AbsoluteColorSeaLevelCm = 0f;
            projector.AbsoluteColorPeakSpanCm = 5000f;
            projector.BindStampHeightSampleSource(new ConstantVisualHeightmap(200f));
            Vector3 center = projector.FitYawedStampProjectorCenter(
                new Vector3(4f, 99f, 6f),
                0f,
                new Vector2(3.8f, 4.2f),
                47);
            Assert.That(center.Y, Is.EqualTo(100f).Within(1e-3f));
        }

        [Test]
        public void HeightmapProjector_FitClampsOceanSentinelToSeaBeforeDisplayScale()
        {
            using var projector = new RaylibVisualHeightmapRenderer();
            projector.DisplayHeightScale = 50f;
            projector.AbsoluteColorSeaLevelCm = 0f;
            projector.AbsoluteColorPeakSpanCm = 5000f;
            projector.BindStampHeightSampleSource(new ConstantVisualHeightmap(999_999f));
            Vector3 center = projector.FitYawedStampProjectorCenter(
                new Vector3(4f, 99f, 6f),
                0f,
                new Vector2(3.8f, 4.2f),
                48);
            Assert.That(center.Y, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void HeightmapProjector_FitMissingOverlapThrowsInsteadOfSkipping()
        {
            using var projector = new RaylibVisualHeightmapRenderer();
            projector.BindStampHeightSampleSource(new MissingOverlapHeightmap());
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => projector.FitYawedStampProjectorCenter(
                    Vector3.Zero,
                    0f,
                    new Vector2(2f, 2f),
                    46))!;
            Assert.That(ex.Message, Does.Contain("does not overlap"));
        }

        [Test]
        public void ReceiverMeshProjectorContract_VertexMapTerrainRendererIsABindableLane()
        {
            using var terrainRenderer = new RaylibTerrainRenderer();
            Assert.That(terrainRenderer, Is.InstanceOf<IRaylibReceiverMeshProjector>());
        }

        [Test]
        public void ReceiverMeshProjectorContract_StaticMeshReceiverIsABindableLane()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            Assert.That(renderer.StaticMeshReceiverProjector, Is.InstanceOf<IRaylibReceiverMeshProjector>());
        }

        [Test]
        public void StaticMeshProjector_StubReceiverRemainsBindableForStaticLaneDecals()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            renderer.BindReceiverMeshProjector(new StubReceiverMeshProjector());
        }

        [Test]
        public void StaticMeshProjector_FitThrowsInsteadOfLeavingAuthoredY()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => projector.FitYawedStampProjectorCenter(
                    new Vector3(1f, 9f, 2f),
                    0f,
                    new Vector2(3.8f, 4.2f),
                    50))!;
            Assert.That(ex.Message, Does.Contain("no height sampling"));
        }

        [Test]
        public void StaticMeshProjector_AabbDrawRejectsNonFiniteAndInvertedBounds()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
            Assert.That(
                () => projector.DrawMeshesOverlappingAabbMeters(float.NaN, 0f, 0f, 10f, 10f, 10f, default),
                Throws.ArgumentException.With.Message.Contains("finite"));
            Assert.That(
                () => projector.DrawMeshesOverlappingAabbMeters(10f, 0f, 0f, 0f, 10f, 10f, default),
                Throws.ArgumentException.With.Message.Contains("min must be <= max"));
        }

        [Test]
        public void StaticMeshProjector_EmptyLaneDrawsZeroInsteadOfThrowing()
        {
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
            IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
            Assert.That(
                projector.DrawMeshesOverlappingAabbMeters(0f, 0f, 0f, 10f, 10f, 10f, default(Material)),
                Is.EqualTo(0));
        }

        [Test]
        public void StaticMeshProjector_ComputeWorldAabbTracksTransformedLocalBounds()
        {
            Matrix4x4 world = Matrix4x4.CreateScale(2f, 3f, 4f) *
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f) *
                Matrix4x4.CreateTranslation(10f, 20f, 30f);
            RaylibStaticMeshReceiverProjector.ComputeWorldAabbMeters(
                in world,
                new Vector3(-1f, -2f, -3f),
                new Vector3(1f, 2f, 3f),
                out float minX,
                out float minY,
                out float minZ,
                out float maxX,
                out float maxY,
                out float maxZ);

            Assert.That(minX, Is.EqualTo(-2f).Within(1e-4f));
            Assert.That(maxX, Is.EqualTo(22f).Within(1e-4f));
            Assert.That(minY, Is.EqualTo(14f).Within(1e-4f));
            Assert.That(maxY, Is.EqualTo(26f).Within(1e-4f));
            Assert.That(minZ, Is.EqualTo(28f).Within(1e-4f));
            Assert.That(maxZ, Is.EqualTo(32f).Within(1e-4f));
        }

        [Test]
        public void VertexMapProjector_FitWithoutSampleSourceThrows()
        {
            using var projector = new RaylibTerrainRenderer();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => projector.FitYawedStampProjectorCenter(
                    new Vector3(1f, 9f, 2f),
                    0f,
                    new Vector2(3.8f, 3.8f),
                    47))!;
            Assert.That(ex.Message, Does.Contain(nameof(RaylibTerrainRenderer.BindStampHeightSampleSource)));
        }

        [Test]
        public void VertexMapProjector_FitCentersOnVertexMapPlateauHeights()
        {
            var map = new Ludots.Core.Map.Hex.VertexMap();
            map.Initialize(widthInChunks: 1, heightInChunks: 1);
            for (int r = 0; r < 64; r++)
            {
                for (int c = 0; c < 64; c++)
                {
                    map.SetHeight(c, r, c <= 31 ? (byte)2 : (byte)4);
                }
            }

            using var projector = new RaylibTerrainRenderer();
            projector.BindStampHeightSampleSource(new VertexMapVisualHeightmap(() => map));

            // 贴花横跨 31/32 列高度阶地边界：左侧 level2(4m)、右侧 level4(8m)，中心应落在两阶中点
            float boundaryX = HexCoordinates.HexWidth * 31.5f;
            float rowZ = HexCoordinates.RowSpacing * 4f;
            Vector3 center = projector.FitYawedStampProjectorCenter(
                new Vector3(boundaryX, 99f, rowZ),
                0f,
                new Vector2(3.8f, 4.2f),
                48);

            Assert.That(center.X, Is.EqualTo(boundaryX).Within(1e-4f));
            Assert.That(center.Z, Is.EqualTo(rowZ).Within(1e-4f));
            Assert.That(center.Y, Is.EqualTo(((2f + 4f) * VertexMapVisualHeightmap.DefaultHeightScaleMeters) * 0.5f).Within(0.02f));
        }

        [Test]
        public void VertexMapProjector_FitMissingOverlapThrowsInsteadOfSkipping()
        {
            var map = new Ludots.Core.Map.Hex.VertexMap();
            map.Initialize(widthInChunks: 1, heightInChunks: 1);
            using var projector = new RaylibTerrainRenderer();
            projector.BindStampHeightSampleSource(new VertexMapVisualHeightmap(() => map));

            float outOfLatticeXCm = HexCoordinates.HexWidth * 64f * WorldUnits.CmPerMeter;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => projector.FitYawedStampProjectorCenter(
                    new Vector3(outOfLatticeXCm * 0.01f, 0f, 0f),
                    0f,
                    new Vector2(2f, 2f),
                    49))!;
            Assert.That(ex.Message, Does.Contain("does not overlap"));
        }

        [Test]
        public void AtmosphereFootprintPresenter_AuthorsNonUnitLocalScale()
        {
            string path = Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "raylib_visual_atmosphere",
                "RaylibVisualAtmosphereShowcaseMod",
                "assets",
                "Presentation",
                "presenters.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))
                ?? throw new InvalidOperationException("presenters.json parsed to null.");
            JsonArray presenters = root.AsArray();
            JsonObject? footprints = null;
            foreach (JsonNode? node in presenters)
            {
                if (node?["id"]?.GetValue<string>() == "raylib_visual_atmosphere_decal_footprints_actor")
                {
                    footprints = node.AsObject();
                    break;
                }
            }

            Assert.That(footprints, Is.Not.Null);
            JsonArray scale = footprints!["behaviors"]![0]!["assetBinding"]!["localScale"]!.AsArray();
            Assert.That(scale[0]!.GetValue<float>(), Is.EqualTo(3.8f).Within(1e-4f));
            Assert.That(scale[1]!.GetValue<float>(), Is.EqualTo(1.0f).Within(1e-4f));
            Assert.That(scale[2]!.GetValue<float>(), Is.EqualTo(3.8f).Within(1e-4f));
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

        private sealed class StubReceiverMeshProjector : IRaylibReceiverMeshProjector
        {
            public int DrawMeshesOverlappingAabbMeters(
                float minX,
                float minY,
                float minZ,
                float maxX,
                float maxY,
                float maxZ,
                Material material)
            {
                throw new InvalidOperationException("Stub receiver is bindable; it is not a draw implementation.");
            }

            public Vector3 FitYawedStampProjectorCenter(
                in Vector3 stampCenter,
                float yawRad,
                in Vector2 stampSizeMeters,
                int stableId)
            {
                throw new InvalidOperationException(
                    "Stub receiver cannot sample stamp height; bind a receiver that implements FitYawedStampProjectorCenter.");
            }
        }

        private sealed class ConstantVisualHeightmap : IVisualHeightmap
        {
            private readonly float _heightCm;

            public ConstantVisualHeightmap(float heightCm)
            {
                _heightCm = heightCm;
            }

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = _heightCm;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                outHeightCm.Fill(_heightCm);
                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
            {
                hit = default;
                return false;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = -1)
            {
                outHitMask.Clear();
                return false;
            }
        }

        private sealed class MissingOverlapHeightmap : IVisualHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = 0f;
                return false;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                return false;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
            {
                hit = default;
                return false;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = -1)
            {
                outHitMask.Clear();
                return false;
            }
        }
    }
}
