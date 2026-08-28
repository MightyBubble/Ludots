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
    [TestFixture]
    public sealed class PresentBindingMultiBindingPipelineTests
    {
        private const float FovDeg = 60f;

        [Test]
        public void SyncPresentPipelines_DualBindings_SyncsAllMetricsAndServesFirstSeatBinding()
        {
            using var engine = CreateDualSeatEngine(PresentBinding.HorizontalEqualSplitLayoutId);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var presenter = new CameraPresenter(engine.SpatialCoords, new NullCameraAdapter());
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var rayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);

            bool synced = PresentBindingPresentation.TrySyncPresentPipelines(
                engine,
                presenter,
                projector,
                rayProvider,
                interpolationAlpha: 1f,
                fovYDeg: FovDeg,
                hostView: hostSurface);

            Assert.That(synced, Is.True, "dual published bindings must sync the present pipeline without the sole-seat assertion.");
            Assert.That(projector.TryGetProjectionSnapshot(out ProjectionSnapshot snapshot), Is.True);
            Assert.That(snapshot.Resolution, Is.EqualTo(new Vector2(960f, 1080f)),
                "the first seat binding in seat order drives the host surface metrics (half width).");
        }

        [Test]
        public void PerBindingRebind_PickingUsesEachBindingOwnCameraAndSurface()
        {
            using var engine = CreateDualSeatEngine(PresentBinding.HorizontalEqualSplitLayoutId);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var rayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out PresentBinding bindingZero);
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out PresentBinding bindingOne);
            PoseCamera(cameraZero, targetCm: Vector2.Zero);
            PoseCamera(cameraOne, targetCm: new Vector2(60000f, 0f));
            var viewportCenter = new Vector2(bindingZero.PresentResolutionPx.X * 0.5f, bindingZero.PresentResolutionPx.Y * 0.5f);

            Assert.That(PresentBindingPresentation.TryRebindPresentBindingPipeline(engine, "seat.0", projector, rayProvider, FovDeg), Is.True);
            ScreenRay rayZero = rayProvider.GetRay(viewportCenter);

            Assert.That(PresentBindingPresentation.TryRebindPresentBindingPipeline(engine, "seat.1", projector, rayProvider, FovDeg), Is.True);
            ScreenRay rayOne = rayProvider.GetRay(viewportCenter);

            Assert.That(MathF.Abs(rayOne.Origin.X - rayZero.Origin.X), Is.GreaterThan(500f),
                "each binding picks through its own LogicView camera (targets 60000cm apart); no merged authority camera may drive both.");
            Assert.That(rayProvider.GetRay(viewportCenter).Origin, Is.EqualTo(rayOne.Origin),
                "the rebound provider is stable per binding until the next rebind.");
        }

        [Test]
        public void PerBindingRebind_CullingComputesVisibilityAgainstItsOwnBindingCamera()
        {
            using var engine = CreateDualSeatEngine(PresentBinding.HorizontalEqualSplitLayoutId);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var rayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            Entity entity = CreateCullableEntity(engine.World, 0, 0);
            var spatial = new StubSpatialQueryService(entity);
            var cullingZero = CreateDisarmedCulling(engine, spatial, hostSurface);
            var cullingOne = CreateDisarmedCulling(engine, spatial, hostSurface);
            using (cullingZero)
            using (cullingOne)
            {
                ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.0", out CameraManager cameraZero, out _);
                ClientLocalSeatAccess.TryResolvePresentCamera(engine, "seat.1", out CameraManager cameraOne, out _);
                PoseCamera(cameraZero, targetCm: new Vector2(50000f, 50000f));
                PoseCamera(cameraOne, targetCm: Vector2.Zero);

                Assert.That(PresentBindingPresentation.TryRebindPresentBindingPipeline(engine, "seat.0", projector, rayProvider, FovDeg, cullingZero), Is.True);
                Assert.That(PresentBindingPresentation.TryRebindPresentBindingPipeline(engine, "seat.1", projector, rayProvider, FovDeg, cullingOne), Is.True);

                cullingZero.Update(0.016f);
                Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.False,
                    "binding seat.0 culls against its own far-away camera pose.");

                cullingOne.Update(0.016f);
                Assert.That(engine.World.Get<CullState>(entity).IsVisible, Is.True,
                    "binding seat.1 culls against its own near camera pose; no merged global visible set decides.");
            }
        }

        [Test]
        public void HostSurfaceResize_RefreshesEveryBindingResolutionByRect()
        {
            using var engine = CreateDualSeatEngine(PresentBinding.HorizontalEqualSplitLayoutId);
            var hostSurface = new ResizablePresentSurface(1920f, 1080f);
            var projector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);
            var rayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), hostSurface);

            hostSurface.Resize(2560f, 1440f);
            Assert.That(PresentBindingPresentation.TryEnsurePresentBindings(engine, projector, rayProvider, FovDeg, hostSurface), Is.True);

            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine);
            Assert.That(seats.Require("seat.0").PresentBinding!.Value.PresentResolutionPx, Is.EqualTo(new Vector2(1280f, 1440f)));
            Assert.That(seats.Require("seat.1").PresentBinding!.Value.PresentResolutionPx, Is.EqualTo(new Vector2(1280f, 1440f)));
            Assert.That(seats.Require("seat.0").PresentBinding!.Value.NormalizedScreenRect, Is.EqualTo(new Vector4(0f, 0f, 0.5f, 1f)),
                "metric refresh must keep the declared rect and only recompute the binding-local resolution.");
        }

        private static GameEngine CreateDualSeatEngine(string layoutId)
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
            Entity playerEight = engine.World.Create(new PlayerIdentity { PlayerId = 8 }, new PlayerOwner { PlayerId = 8 });
            var players = new PlayerEntityLookup();
            players.Register(7, playerSeven);
            players.Register(8, playerEight);
            var result = new ParticipantBindingResult(
                new TeamEntityLookup(),
                players,
                localSeats: new[]
                {
                    new ResolvedLocalSeatPossession("seat.0", 7, playerSeven, ControlSchemeId: null),
                    new ResolvedLocalSeatPossession("seat.1", 8, playerEight, ControlSchemeId: null),
                });
            ParticipantBindingResolver.PublishFocused(engine.GlobalContext, result);
            return engine;
        }

        private static void PoseCamera(CameraManager camera, Vector2 targetCm)
        {
            camera.State.TargetCm = targetCm;
            camera.State.DistanceCm = 2000f;
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

            public void Resize(float width, float height) => Resolution = new Vector2(width, height);
        }

        private sealed class NullCameraAdapter : ICameraAdapter
        {
            public void UpdateCamera(in CameraRenderState3D state)
            {
            }
        }

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity _entity;

            public StubSpatialQueryService(Entity entity)
            {
                _entity = entity;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                if (buffer.Length == 0)
                {
                    return new SpatialQueryResult(0, 1);
                }

                buffer[0] = _entity;
                return new SpatialQueryResult(1, 0);
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
