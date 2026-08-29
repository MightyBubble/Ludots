using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Headless acceptance for the multi-viewport present slice: per-binding cull-pass
    /// multiplexing with union CullState semantics, the host-agnostic per-binding drive sequence,
    /// and rect-routed picking under multiple PresentBindings.
    /// </summary>
    [TestFixture]
    public sealed class PresentBindingMultiViewportDrawTests
    {
        private const float FovDeg = 60f;

        [Test]
        public void ArmedCullingPasses_Union_VisibleInAnyBindingIsDrawn()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            Entity entity = CreateCullableEntity(engine.World, 0, 0);
            var spatial = new StubSpatialQueryService(entity);
            using var culling = CreateDisarmedCulling(engine, spatial, hostSurface);

            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            PoseCamera(cameraZero, targetCm: new Vector2(50000f, 50000f), distanceCm: 2000f);
            PoseCamera(cameraOne, targetCm: Vector2.Zero, distanceCm: 2000f);

            Assert.That(
                PresentBindingPresentation.TryArmPresentBindingCullingPasses(engine, FovDeg, hostSurface, culling),
                Is.True,
                "arming must collect one cull pass per present binding in seat order.");
            culling.Update(0.016f);

            Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.True,
                "entity outside binding seat.0's view but inside seat.1's view stays visible: union, not a merged global truth.");
        }

        [Test]
        public void ArmedCullingPasses_Union_NeverRemovesFirstBindingVisibility()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            Entity nearZero = CreateCullableEntity(engine.World, 0, 0);
            Entity nearNeither = CreateCullableEntity(engine.World, 200000, 200000);
            var spatial = new StubSpatialQueryService(nearZero, nearNeither);
            using var culling = CreateDisarmedCulling(engine, spatial, hostSurface);

            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            PoseCamera(cameraZero, targetCm: Vector2.Zero, distanceCm: 2000f);
            PoseCamera(cameraOne, targetCm: new Vector2(50000f, 50000f), distanceCm: 2000f);

            Assert.That(PresentBindingPresentation.TryArmPresentBindingCullingPasses(engine, FovDeg, hostSurface, culling), Is.True);
            culling.Update(0.016f);

            Assert.That(engine.World.Get<CullState>(nearZero).IsVisible, Is.True,
                "seat.0 sees the entity; the later seat.1 union pass must not cull what an earlier binding sees.");
            Assert.That(engine.World.Get<CullState>(nearNeither).IsVisible, Is.False,
                "visible in no binding's viewport ⇒ not drawn.");
        }

        [Test]
        public void ArmedCullingPasses_StaticEntities_UnionSurvivesBaselineRebuild()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            Entity entity = CreateStaticCullableEntity(engine.World, 0, 0);
            var spatial = new StubSpatialQueryService(entity);
            using var culling = CreateDisarmedCulling(engine, spatial, hostSurface);

            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            PoseCamera(cameraZero, targetCm: new Vector2(50000f, 50000f), distanceCm: 2000f);
            PoseCamera(cameraOne, targetCm: Vector2.Zero, distanceCm: 2000f);

            Assert.That(PresentBindingPresentation.TryArmPresentBindingCullingPasses(engine, FovDeg, hostSurface, culling), Is.True);
            culling.Update(0.016f);
            Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.True,
                "static entity visible only to seat.1 is drawn under union culling.");

            // Cameras unchanged: incremental (dirty) static path must keep the union state.
            culling.Update(0.016f);
            Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.True,
                "dirty static passes must not lose the union visibility of an untouched entity.");

            // A baseline rebuild in pass 0 chains full re-evaluation into later passes, so the
            // union restores what the moved seat.0 camera alone would cull.
            cameraZero.State.TargetCm = new Vector2(40000f, 40000f);
            culling.Update(0.016f);
            Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.True,
                "a full static rebuild in the baseline pass must not strand entities only later bindings see.");
        }

        [Test]
        public void ArmedCullingPasses_Union_BestBindingLodAndMetricsWin()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            Entity entity = CreateCullableEntity(engine.World, 0, 0);
            var spatial = new StubSpatialQueryService(entity);
            using var culling = CreateDisarmedCulling(engine, spatial, hostSurface);

            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            // Entity sits in both viewports: 6000cm from seat.0's target (Medium), 1200cm from seat.1's (High).
            PoseCamera(cameraZero, targetCm: new Vector2(6000f, 0f), distanceCm: 15000f);
            PoseCamera(cameraOne, targetCm: new Vector2(1200f, 0f), distanceCm: 4000f);

            Assert.That(PresentBindingPresentation.TryArmPresentBindingCullingPasses(engine, FovDeg, hostSurface, culling), Is.True);
            culling.Update(0.016f);

            CullState cull = engine.World.Get<CullState>(entity);
            Assert.That(cull.IsVisible, Is.True);
            Assert.That(cull.LOD, Is.EqualTo(LODLevel.High),
                "the binding that sees the entity best upgrades the shared CullState's LOD.");
            Assert.That(MathF.Abs(cull.DistanceToCameraSq - (1200f * 1200f)), Is.LessThan(1f),
                "distance metrics come from the best-viewing binding, not the baseline one.");
        }

        [Test]
        public void DrivePresentBindings_HostCallbackPerBindingInSeatOrder_RestsOnFirstBinding()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var presenter = new CameraPresenter(engine.SpatialCoords, new NullCameraAdapter());
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var rayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out PresentBinding bindingZero);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            PoseCamera(cameraZero, targetCm: new Vector2(2000f, 0f), distanceCm: 2000f);
            PoseCamera(cameraOne, targetCm: new Vector2(60000f, 0f), distanceCm: 2000f);
            var driven = new List<PresentBindingDrawFrame>(2);

            bool drove = PresentBindingPresentation.TryDrivePresentBindings(
                engine,
                presenter,
                projector,
                rayProvider,
                interpolationAlpha: 1f,
                fovYDeg: FovDeg,
                drawBinding: (in PresentBindingDrawFrame frame) => driven.Add(frame),
                hostView: hostSurface);

            Assert.That(drove, Is.True);
            Assert.That(driven.Count, Is.EqualTo(2), "one host draw callback per present binding.");
            Assert.That(driven[0].SeatId, Is.EqualTo("seat.0"));
            Assert.That(driven[1].SeatId, Is.EqualTo("seat.1"));
            Assert.That(driven[0].Binding.NormalizedScreenRect, Is.EqualTo(new Vector4(0f, 0f, 0.5f, 1f)));
            Assert.That(driven[1].Binding.NormalizedScreenRect, Is.EqualTo(new Vector4(0.5f, 0f, 0.5f, 1f)));
            Assert.That(ReferenceEquals(driven[0].Camera, cameraZero) && ReferenceEquals(driven[1].Camera, cameraOne), Is.True,
                "each frame carries its own binding's LogicView camera.");

            // Per-binding presenter interpolation must have run before each callback.
            Assert.That(driven[1].InterpolationAlpha, Is.EqualTo(1f));
            Assert.That(presenter.SmoothedRenderState.Target.X, Is.EqualTo(20f).Within(0.5f),
                "after the walk the presenter rests on the first binding in seat order (2000cm → 20m).");
            Assert.That(projector.TryGetProjectionSnapshot(out ProjectionSnapshot snapshot), Is.True);
            Assert.That(snapshot.Resolution, Is.EqualTo(bindingZero.PresentResolutionPx),
                "projector/ray rest on the first binding for single-viewport consumers after the drive.");
        }

        [Test]
        public void RoutedRayProvider_HostPointRoutesToOwningBinding()
        {
            using var engine = CreateEngine(PresentBinding.HorizontalEqualSplitLayoutId, dualSeat: true);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var inner = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var router = new PresentBindingScreenRayProvider(engine, inner);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
            PoseCamera(cameraZero, targetCm: Vector2.Zero, distanceCm: 2000f);
            PoseCamera(cameraOne, targetCm: new Vector2(60000f, 0f), distanceCm: 2000f);

            ScreenRay leftRay = router.GetRay(new Vector2(300f, 540f));
            ScreenRay rightRay = router.GetRay(new Vector2(1500f, 540f));
            ScreenRay edgeRay = router.GetRay(new Vector2(960f, 540f));

            Assert.That(MathF.Abs(rightRay.Origin.X - leftRay.Origin.X), Is.GreaterThan(500f),
                "host points in different halves pick through different bindings' cameras (targets 60000cm apart).");
            Assert.That(MathF.Abs(edgeRay.Origin.X - rightRay.Origin.X), Is.LessThan(1f),
                "the shared edge at nx=0.5 belongs to the later binding in seat order.");
        }

        [Test]
        public void RoutedRayProvider_SoleBindingDelegatesUnchanged()
        {
            using var engine = CreateEngine(null, dualSeat: false);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var inner = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var router = new PresentBindingScreenRayProvider(engine, inner);
            Assert.That(PresentBindingPresentation.TryEnsurePresentBindings(engine, projector, inner, FovDeg, hostSurface), Is.True);

            var point = new Vector2(320f, 240f);
            ScreenRay routed = router.GetRay(point);
            ScreenRay direct = inner.GetRay(point);

            Assert.That(routed.Origin, Is.EqualTo(direct.Origin),
                "sole binding: the router is a pass-through to the host's single-binding provider.");
            Assert.That(routed.Direction, Is.EqualTo(direct.Direction));
        }

        private static GameEngine CreateEngine(string? layoutId, bool dualSeat)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod" });
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.Start();
            engine.MergedConfig.StartupPresentLayout = layoutId;
            engine.SetService(CoreServiceKeys.ViewController, new ResizablePresentSurface(1920f, 1080f));

            Entity playerSeven = engine.World.Create(new PlayerIdentity { PlayerId = 7 }, new PlayerOwner { PlayerId = 7 });
            var players = new PlayerEntityLookup();
            players.Register(7, playerSeven);
            ResolvedLocalSeatPossession[] localSeats;
            if (dualSeat)
            {
                Entity playerEight = engine.World.Create(new PlayerIdentity { PlayerId = 8 }, new PlayerOwner { PlayerId = 8 });
                players.Register(8, playerEight);
                localSeats = new[]
                {
                    new ResolvedLocalSeatPossession("seat.0", 7, playerSeven, ControlSchemeId: null),
                    new ResolvedLocalSeatPossession("seat.1", 8, playerEight, ControlSchemeId: null),
                };
            }
            else
            {
                localSeats = new[] { new ResolvedLocalSeatPossession("seat.0", 7, playerSeven, ControlSchemeId: null) };
            }

            var result = new ParticipantBindingResult(
                new TeamEntityLookup(),
                players,
                localSeats: localSeats);
            ParticipantBindingResolver.PublishFocused(engine.GlobalContext, result);
            return engine;
        }

        private static void PoseCamera(CameraManager camera, Vector2 targetCm, float distanceCm)
        {
            camera.State.TargetCm = targetCm;
            camera.State.DistanceCm = distanceCm;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = FovDeg;
        }

        private static CameraCullingSystem CreateDisarmedCulling(
            GameEngine engine,
            ISpatialQueryService spatial,
            IViewController view)
        {
            var culling = new CameraCullingSystem(
                engine.World,
                ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine),
                spatial,
                view,
                cullingConfig: new CameraCullingRuntimeConfig
                {
                    HighLodDistanceCm = 4000f,
                    MediumLodDistanceCm = 10000f,
                    LowLodDistanceCm = 20000f,
                });
            culling.DisarmPresentBindingCulling();
            return culling;
        }

        private static Entity CreateCullableEntity(World world, int xCm, int yCm)
        {
            return world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new VisualTransform
                {
                    Position = new Vector3(xCm / 100f, 0f, yCm / 100f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
        }

        private static Entity CreateStaticCullableEntity(World world, int xCm, int yCm)
        {
            return world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new PresentationStaticTransform(),
                new VisualTransform
                {
                    Position = new Vector3(xCm / 100f, 0f, yCm / 100f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
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

                current = Directory.GetParent(current)!.FullName;
            }

            throw new InvalidOperationException("Repository root not found from " + TestContext.CurrentContext.WorkDirectory);
        }

        private sealed class ResizablePresentSurface : IViewController
        {
            public ResizablePresentSurface(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; private set; }
            public float Fov => FovDeg;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class NullCameraAdapter : ICameraAdapter
        {
            public void UpdateCamera(in CameraRenderState3D state)
            {
            }
        }

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity[] _entities;

            public StubSpatialQueryService(params Entity[] entities)
            {
                _entities = entities;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                if (buffer.Length == 0)
                {
                    return new SpatialQueryResult(0, 1);
                }

                int count = Math.Min(_entities.Length, buffer.Length);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _entities[i];
                }

                return new SpatialQueryResult(count, _entities.Length > buffer.Length ? 1 : 0);
            }

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
        }
    }
}
