using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Mirrors CaseESelectionShowcaseAcceptanceTests' box-select chain against the 10K
    /// MassNavigation showcase, so a broken migration cannot hide behind the retired
    /// command-source path.
    /// </summary>
    [TestFixture]
    public sealed class MassNavSelectionChainTests
    {
        private const string SelectableKey = "case_e.selectable";
        private const string SelectedKey = "selected";

        private static readonly string[] Mods =
        {
            "LudotsCoreMod", "CoreInputMod", "SelectionInteractionMod",
            "MassNavigationMod", "CapabilityStandardMassNavigationLargeWorld10kMod"
        };

        [Test]
        public void BattleContextProjectsControlsAndBoxSelectMounts()
        {
            var backend = new TestInputBackend();
            using GameEngine engine = CreateEngine(backend);
            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.default") })));
            Tick(engine, 40);

            Entity player = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Console.WriteLine($"player entity: {player.Id}");

            var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("InteractionContextProfileRegistry missing.");
            int battleId = profiles.ProfileIdRegistry.GetId("interaction.context.case_e.battle");
            int boxingId = profiles.ProfileIdRegistry.GetId("interaction.context.case_e.boxing");
            Console.WriteLine($"battle id={battleId} installed={profiles.IsInstalled(battleId)}");
            Console.WriteLine($"boxing id={boxingId} installed={profiles.IsInstalled(boxingId)}");

            bool hasContext = engine.World.TryGet<InteractionContextInstance>(player, out InteractionContextInstance ctx);
            Console.WriteLine($"has InteractionContextInstance: {hasContext} ctxId={ctx.ContextId} inputCtx={ctx.InputContextId}");
            Assert.That(hasContext, Is.True, "local player must carry the battle context instance");
            Assert.That(ctx.ContextId, Is.EqualTo(battleId));

            var handler = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler missing.");
            Console.WriteLine($"startupInputContexts: {string.Join(",", engine.MergedConfig.StartupInputContexts)}");
            Console.WriteLine($"HasContext(CaseE.Controls) before ticks: {handler.HasContext("CaseE.Controls")}");
            Tick(engine, 20);
            Console.WriteLine($"HasContext(CaseE.Controls) after ticks: {handler.HasContext("CaseE.Controls")}");
            Assert.That(handler.HasContext("CaseE.Controls"), Is.True,
                "possessed seat must project the entity's battle context input context");

            // Candidate collection should already hold the commandable agents.
            int count = CollectionCount(engine, player, SelectableKey);
            Console.WriteLine($"selectable collection count: {count}");
            Assert.That(count, Is.GreaterThan(0), "roster graph must fill case_e.selectable");

            // Box select a marquee derived from the RENDERED projection of live agents, so the test
            // exercises the same height-aware path the player's mouse does.
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)!;
            Vector2 resolution = engine.GetService(CoreServiceKeys.ViewController)!.Resolution;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int screenSeen = 0;
            var marqueeQuery = new QueryDescription().WithAll<Ludots.Core.Presentation.Components.VisualTransform>();
            engine.World.Query(in marqueeQuery, (Entity e, ref Ludots.Core.Presentation.Components.VisualTransform visual) =>
            {
                if (!engine.World.Has<Ludots.Core.Input.CommandSources.CommandSourceSelectableTag>(e))
                {
                    return;
                }

                var sp = projector.WorldToScreen(visual.Position);
                if (float.IsNaN(sp.X) || float.IsNaN(sp.Y) ||
                    sp.X < 0 || sp.Y < 0 || sp.X > resolution.X || sp.Y > resolution.Y)
                {
                    return;
                }

                screenSeen++;
                minX = MathF.Min(minX, sp.X); minY = MathF.Min(minY, sp.Y);
                maxX = MathF.Max(maxX, sp.X); maxY = MathF.Max(maxY, sp.Y);
            });
            Console.WriteLine($"marquee source: {screenSeen} on-screen selectable, bbox=({minX:F0},{minY:F0})-({maxX:F0},{maxY:F0})");
            Assert.That(screenSeen, Is.GreaterThan(0), "expected selectable agents projecting inside the viewport");

            var press = new Vector2(MathF.Max(0, minX - 12), MathF.Max(0, minY - 12));
            var release = new Vector2(MathF.Min(resolution.X, maxX + 12), MathF.Min(resolution.Y, maxY + 12));
            backend.SetMousePosition(press);
            backend.SetButton("<Mouse>/leftButton", true);
            Tick(engine, 6);
            bool boxingActive = engine.World.TryGet<InteractionContextInstances>(player, out InteractionContextInstances boxing) &&
                                boxing.Count > 0;
            Console.WriteLine($"boxing active after press: {boxingActive}");
            backend.SetMousePosition(release);
            Tick(engine, 6);

            backend.SetButton("<Mouse>/leftButton", false);
            Tick(engine, 30);

            int selected = CollectionCount(engine, player, SelectedKey);
            Console.WriteLine($"selected collection count: {selected}");
            Assert.That(boxingActive, Is.True, "pressing must activate the boxing context (box_begin graph mount)");
            Assert.That(selected, Is.GreaterThan(0), "releasing must commit the rectangle hits into the selected collection");
        }

        private static int CollectionCount(GameEngine engine, Entity owner, string key)
        {
            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            int keyId = store.KeyRegistry.GetId(key);
            return keyId > 0 && store.TryGet(owner, keyId, out EntityCollectionHandle handle) &&
                store.TryGetView(handle, out EntityCollectionView view)
                ? view.Count
                : -1;
        }

        private static GameEngine CreateEngine(TestInputBackend backend)
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, Mods), Path.Combine(repoRoot, "assets"));
            var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var handler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                handler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.ViewController, (IViewController)new HeadlessViewController(1280f, 720f));
            HeadlessPresentationTestHost.Install(engine);
            engine.Start();
            return engine;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("repo root not found");
        }

        [Test]
        public void MarqueeHitAccountsForTerrainHeight()
        {
            var backend = new TestInputBackend();
            using GameEngine engine = CreateEngine(backend);
            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));
            Tick(engine, 40);

            Entity player = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            var proj = engine.GetService(CoreServiceKeys.ScreenProjector)!;
            var heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(heightmap, Is.Not.Null, "the mass navigation map declares a continuous heightmap");
            // Pick a live agent and measure both projections.
            Entity sample = Entity.Null;
            var q = new QueryDescription().WithAll<Ludots.Core.Components.WorldPositionCm>();
            engine.World.Query(in q, (Entity e, ref Ludots.Core.Components.WorldPositionCm wp) =>
            {
                if (sample != Entity.Null) return;
                if (wp.Value.Y.ToFloat() < 3000f) return;
                sample = e;
            });
            Assert.That(sample, Is.Not.EqualTo(Entity.Null), "expected an agent on the raised terrain");

            var wpSample = engine.World.Get<Ludots.Core.Components.WorldPositionCm>(sample);
            float flatX = wpSample.Value.X.ToFloat();
            float flatY = wpSample.Value.Y.ToFloat();
            Assert.That(heightmap!.TrySampleHeightCm(flatX, flatY, out float heightCm), Is.True);
            Assert.That(heightCm, Is.GreaterThan(10000f), "the origin relief is hundreds of metres");

            var flatPos = Ludots.Core.Mathematics.WorldPlane2D.LogicCmToVisualMeters(in wpSample.Value);
            var raisedPos = Ludots.Core.Mathematics.WorldPlane2D.LogicCmToVisualMeters(in wpSample.Value, heightCm / 100f);
            var flatScreen = proj.WorldToScreen(flatPos);
            var raisedScreen = proj.WorldToScreen(raisedPos);
            float error = Vector2.Distance(flatScreen, raisedScreen);
            Console.WriteLine($"height={heightCm:F0}cm flat=({flatScreen.X:F0},{flatScreen.Y:F0}) raised=({raisedScreen.X:F0},{raisedScreen.Y:F0}) error={error:F0}px");
            Assert.That(error, Is.GreaterThan(64f),
                "ignoring terrain height must move the projected point by a large screen distance");

            // A marquee built around the RAISED point must select; the flat point must not.
            var raisedRect = new Ludots.Core.Spatial.ScreenRect(raisedScreen.X - 30, raisedScreen.Y - 30, raisedScreen.X + 30, raisedScreen.Y + 30);
            Assert.That(
                Ludots.Core.Spatial.SpatialBoundsUtility.EntityIntersectsScreenRect(
                    engine.World, sample, proj, in raisedRect,
                    new Ludots.Core.Spatial.ScreenProjectionPoseContext(1f, heightmap)),
                Is.True,
                "height-aware marquee must intersect the unit");

            // Contract: the SAME marquee must answer differently with and without terrain height,
            // because the projection differs by hundreds of pixels. That is the defect this locks.
            var flatRect = new Ludots.Core.Spatial.ScreenRect(flatScreen.X - 30, flatScreen.Y - 30, flatScreen.X + 30, flatScreen.Y + 30);
            bool flatMarqueeWithHeight = Ludots.Core.Spatial.SpatialBoundsUtility.EntityIntersectsScreenRect(
                engine.World, sample, proj, in flatRect,
                new Ludots.Core.Spatial.ScreenProjectionPoseContext(1f, heightmap));
            bool raisedMarqueeWithHeight = Ludots.Core.Spatial.SpatialBoundsUtility.EntityIntersectsScreenRect(
                engine.World, sample, proj, in raisedRect,
                new Ludots.Core.Spatial.ScreenProjectionPoseContext(1f, heightmap));
            Console.WriteLine($"flatRect+height={flatMarqueeWithHeight} raisedRect+height={raisedMarqueeWithHeight}");
            Assert.That(raisedMarqueeWithHeight, Is.True, "height-aware test must hit the marquee drawn around the rendered position");
            Assert.That(flatMarqueeWithHeight, Is.False, "the flat-projected marquee must miss once the rendered elevation is applied");
        }

        [Test]
        public void MarqueeHitUsesTheLastPresentedInterpolatedGroundPose()
        {
            using World world = World.Create();
            var previous = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(1000, 1000);
            var current = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(5000, 1000);
            var heightmap = new SlopedHeightmap();
            Entity entity = world.Create(
                new Ludots.Core.Components.PreviousWorldPositionCm { Value = previous },
                new Ludots.Core.Components.WorldPositionCm { Value = current },
                new Ludots.Core.Presentation.Components.ContinuousHeightmapSampleState { FrameId = 7, Sampled = 1 },
                new Ludots.Core.Presentation.Components.VisualTransform
                {
                    Position = new Vector3(20f, 2f, 10f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var projector = new HeightAwareTestProjector();
            Vector2 renderedPoint = projector.WorldToScreen(world.Get<Ludots.Core.Presentation.Components.VisualTransform>(entity).Position);
            var renderedRect = new Ludots.Core.Spatial.ScreenRect(
                renderedPoint.X - 1f,
                renderedPoint.Y - 1f,
                renderedPoint.X + 1f,
                renderedPoint.Y + 1f);

            Assert.That(
                Ludots.Core.Spatial.SpatialBoundsUtility.EntityIntersectsScreenRect(
                    world,
                    entity,
                    projector,
                    in renderedRect,
                    new Ludots.Core.Spatial.ScreenProjectionPoseContext(0.25f, heightmap)),
                Is.True,
                "screen interaction must use the same interpolated, terrain-grounded pose as the last rendered frame");
        }

        [Test]
        public void ScreenRegionBroadphaseRetainsCandidatesAcrossTheDeclaredHeightSpan()
        {
            var rays = new ShallowPerspectiveRayProvider();
            var rect = new Ludots.Core.Spatial.ScreenRect(-0.1f, -0.11f, 0.1f, -0.09f);
            var worldBounds = new WorldAabbCm(-20000, 0, 40000, 20000);

            Assert.That(
                Ludots.Core.Spatial.ScreenRegionBroadphase.TryGetBounds(
                    rays,
                    in rect,
                    in worldBounds,
                    0f,
                    out WorldAabbCm flatBounds),
                Is.True);
            Assert.That(Contains(flatBounds, 0, 1000), Is.False,
                "the ground-plane-only broadphase excludes the elevated point's horizontal position");

            Assert.That(
                Ludots.Core.Spatial.ScreenRegionBroadphase.TryGetBounds(
                    rays,
                    in rect,
                    in worldBounds,
                    0f,
                    out WorldAabbCm heightAwareBounds,
                    0f,
                    9f),
                Is.True);
            Assert.That(Contains(heightAwareBounds, 0, 1000), Is.True,
                "the broadphase must retain points visible at any height in the declared span");
        }

        [Test]
        public void ScreenProjectionRejectsInvalidHeightContracts()
        {
            var rays = new ShallowPerspectiveRayProvider();
            var rect = new Ludots.Core.Spatial.ScreenRect(-0.1f, -0.11f, 0.1f, -0.09f);
            var worldBounds = new WorldAabbCm(-20000, 0, 40000, 20000);
            Assert.That(
                () => Ludots.Core.Spatial.ScreenRegionBroadphase.TryGetBounds(
                    rays,
                    in rect,
                    in worldBounds,
                    0f,
                    out _,
                    10f,
                    5f),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Message.Contains("SPATIAL.ERR.InvalidHeightRange"));

            using World world = World.Create();
            Entity entity = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 1000),
                new Ludots.Core.Presentation.Components.ContinuousHeightmapSampleState());
            var projectionPose = new Ludots.Core.Spatial.ScreenProjectionPoseContext(1f, new FailingHeightmap());
            var projector = new HeightAwareTestProjector();
            var hitRect = new Ludots.Core.Spatial.ScreenRect(0f, 0f, 1000f, 1000f);
            Assert.That(
                () => Ludots.Core.Spatial.SpatialBoundsUtility.EntityIntersectsScreenRect(
                    world,
                    entity,
                    projector,
                    in hitRect,
                    in projectionPose),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("SPATIAL.ERR.GroundHeightSampleFailed"));
        }

        private static bool Contains(in WorldAabbCm bounds, int xCm, int yCm) =>
            xCm >= bounds.Left && xCm <= bounds.Right &&
            yCm >= bounds.Top && yCm <= bounds.Bottom;

        private sealed class WindowPointGroundRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
            }
        }

        private sealed class WindowPointScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition) => new(worldPosition.X * 100f, worldPosition.Z * 100f);
        }

        private sealed class HeightAwareTestProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition) =>
                new(worldPosition.X * 10f, (worldPosition.Z - worldPosition.Y) * 10f);
        }

        private sealed class ShallowPerspectiveRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition) =>
                new(
                    new Vector3(0f, 10f, 0f),
                    Vector3.Normalize(new Vector3(screenPosition.X, screenPosition.Y, 1f)));
        }

        private sealed class SlopedHeightmap : IContinuousHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = worldXCm * 0.1f;
                return true;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
                {
                    throw new ArgumentException("Heightmap batch spans must have identical lengths.");
                }

                for (int i = 0; i < worldXCm.Length; i++)
                {
                    outHeightCm[i] = worldXCm[i] * 0.1f;
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
            {
                throw new NotSupportedException();
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
                throw new NotSupportedException();
            }
        }

        private sealed class FailingHeightmap : IContinuousHeightmap
        {
            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                heightCm = float.NaN;
                return false;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1) => false;

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
                int layerIndex = -1) => false;
        }

        private sealed class HeadlessViewController : IViewController
        {
            public HeadlessViewController(float width, float height) { Resolution = new Vector2(width, height); }
            public Vector2 Resolution { get; }
            public float Fov => 50f;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
            public Vector2 MousePosition { get; set; }
            public void SetMousePosition(Vector2 v) => MousePosition = v;
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
            public void SetButton(string devicePath, bool down)
            {
                if (down) _buttons.Add(devicePath);
                else _buttons.Remove(devicePath);
            }
        }

    }
}
