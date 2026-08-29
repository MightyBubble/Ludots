using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Per-seat input routing acceptance: dual-seat map entry with per-seat control scheme
    /// declarations, two seats' axis inputs driving their own possessed reps through isolated
    /// per-seat channels (no last-writer-wins), fail-fast on undeclared schemes, and the
    /// sole-seat production path staying on the global chain.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class DualSeatInputRoutingAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string WasdSchemeId = "scheme.wasd_move";
        private const string HubMapId = InteractionShowcaseIds.HubMapId;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "InteractionShowcaseMod"
        };

        [Test]
        public void DualSeatEntry_PerSeatSchemesActivateAndAxisInputsDriveOwnReps()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend, AcceptanceMods);
            engine.LoadMap(DualSeatLaunch(WasdSchemeId, WasdSchemeId));
            Tick(engine, 8);

            var seats = ClientLocalSeatAccess.RequireRegistry(engine);
            Assert.That(seats.Count, Is.EqualTo(2));
            ControlSchemeRuntime schemes = RequireSchemes(engine);
            int wasdSchemeId = schemes.SchemeIdRegistry.GetId(WasdSchemeId);
            ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                ?? throw new InvalidOperationException("ClientLocalSeatInputRuntime service is missing.");
            Assert.That(seatInput.TryGetChannel("seat.0", out ClientLocalSeatInputChannel channelZero), Is.True);
            Assert.That(seatInput.TryGetChannel("seat.1", out ClientLocalSeatInputChannel channelOne), Is.True);
            Assert.That(channelZero.ActiveSchemeId, Is.EqualTo(wasdSchemeId), "seat.0's declared scheme activates on its own channel.");
            Assert.That(channelOne.ActiveSchemeId, Is.EqualTo(wasdSchemeId), "seat.1's declared scheme activates on its own channel.");
            Assert.That(channelZero.TryGetActiveAxisMove(out _), Is.True);
            Assert.That(channelOne.TryGetActiveAxisMove(out _), Is.True);

            Entity repOne = seats.Require("seat.0").PossessedRep;
            Entity repTwo = seats.Require("seat.1").PossessedRep;
            Assert.That(repOne, Is.Not.EqualTo(repTwo));
            Assert.That(engine.World.Has<Ludots.Core.Components.WorldPositionCm>(repOne), Is.True);
            Assert.That(engine.World.Has<Ludots.Core.Components.WorldPositionCm>(repTwo), Is.True);
            Vector2 startOne = engine.World.Get<Ludots.Core.Components.WorldPositionCm>(repOne).Value.ToVector2();
            int stepDistanceCm = channelZero.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding)
                ? binding.StepDistanceCm
                : 0;

            Order orderOne = default;
            bool captured = false;
            for (int frame = 0; frame < 96 && !captured; frame++)
            {
                channelZero.Handler.InjectAction("Move", new Vector3(1f, 0f, 0f));
                channelOne.Handler.InjectAction("Move", new Vector3(0f, 1f, 0f));
                Tick(engine, 1);
                captured = TryReadMoveOrder(engine, repOne, out orderOne);
                Assert.That(channelOne.Reader.ReadAction<Vector2>("Move"), Is.EqualTo(new Vector2(0f, 1f)),
                    "seat.1's channel keeps its own axis value across the freeze.");
                Assert.That(channelZero.Reader.ReadAction<Vector2>("Move"), Is.EqualTo(new Vector2(1f, 0f)),
                    "seat.0's channel keeps its own axis value across the freeze.");
            }

            Assert.That(captured, Is.True, "seat.0's axis input must submit a move order for its own rep.");
            int moveToId = engine.GetService(CoreServiceKeys.OrderTypeRegistry)!.GetId("moveTo");
            Assert.That(orderOne.OrderTypeId, Is.EqualTo(moveToId));
            Assert.That(orderOne.Actor, Is.EqualTo(repOne));
            Assert.That(orderOne.PlayerId, Is.EqualTo(1));
            Assert.That(orderOne.Args.Spatial.WorldCm.X, Is.EqualTo(startOne.X + stepDistanceCm).Within(0.001f),
                "seat.0's +X axis input drives its own rep's order.");
            Assert.That(orderOne.Args.Spatial.WorldCm.Y, Is.EqualTo(startOne.Y).Within(0.001f),
                "seat.1's +Y input running on the same frame never leaks into seat.0's order — no last-writer-wins.");
            Assert.That(engine.World.TryGet(repOne, out OrderBuffer bufferOne) && bufferOne.QueuedCount == 0,
                "seat.0's rep receives exactly one order for the frame; the other seat's input is not merged onto it.");

            // Reload the same map and drive only seat.1: seat.0's rep must stay order-idle,
            // proving seat.1's channel never routes into another seat's possession.
            engine.LoadMap(DualSeatLaunch(WasdSchemeId, WasdSchemeId));
            Tick(engine, 8);
            seats = ClientLocalSeatAccess.RequireRegistry(engine);
            Entity reloadedRepOne = seats.Require("seat.0").PossessedRep;
            Assert.That(reloadedRepOne, Is.Not.EqualTo(repOne), "the reload publishes fresh reps; stale entity ids must not be reused.");
            seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                ?? throw new InvalidOperationException("ClientLocalSeatInputRuntime service is missing.");
            Assert.That(seatInput.TryGetChannel("seat.1", out ClientLocalSeatInputChannel reloadedChannelOne), Is.True,
                "seat channels are rebuilt on map re-entry.");
            for (int frame = 0; frame < 16; frame++)
            {
                reloadedChannelOne.Handler.InjectAction("Move", new Vector3(0f, 1f, 0f));
                Tick(engine, 1);
            }

            Assert.That(TryReadMoveOrder(engine, reloadedRepOne, out _), Is.False,
                "seat.1's axis input never submits an order for seat.0's rep.");
            Assert.That(engine.World.TryGet(reloadedRepOne, out OrderBuffer idleBuffer) && idleBuffer.QueuedCount == 0,
                "seat.0's rep stays order-idle while seat.1's input flows on its own channel.");
        }

        [Test]
        public void DualSeatEntry_DeclaredSchemeNotInstalled_FailsMapLoadFast()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend, AcceptanceMods);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => engine.LoadMap(DualSeatLaunch(WasdSchemeId, "scheme.dual.missing")));

            Assert.That(error!.Message, Does.Contain("seat.1"));
            Assert.That(error.Message, Does.Contain("scheme.dual.missing"));
            Assert.That(error.Message, Does.Contain("not installed"));
        }

        [Test]
        public void SoleSeatEntry_DeclaredSchemeActivatesGlobalChainAndAxisMoveStillWorks()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend, AcceptanceMods);
            engine.LoadMap(new MapLoadRequest(
                new MapId(InteractionShowcaseIds.HubMapId),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, WasdSchemeId) })));
            Tick(engine, 8);

            var seats = ClientLocalSeatAccess.RequireRegistry(engine);
            Assert.That(seats.Count, Is.EqualTo(1));
            ControlSchemeRuntime schemes = RequireSchemes(engine);
            Assert.That(schemes.ActiveSchemeId, Is.EqualTo(schemes.SchemeIdRegistry.GetId(WasdSchemeId)),
                "the sole seat's declared scheme keeps activating the global runtime.");
            ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                ?? throw new InvalidOperationException("ClientLocalSeatInputRuntime service is missing.");
            Assert.That(seatInput.ChannelCount, Is.EqualTo(0),
                "the sole seat keeps the global interpretation chain; no per-seat channel shadows it.");

            Entity rep = seats.Require("seat.0").PossessedRep;
            Assert.That(engine.World.Has<Ludots.Core.Components.WorldPositionCm>(rep), Is.True);
            Vector2 start = engine.World.Get<Ludots.Core.Components.WorldPositionCm>(rep).Value.ToVector2();

            backend.SetButton("<Keyboard>/d", true);
            Order order = default;
            bool captured = false;
            for (int frame = 0; frame < 48 && !captured; frame++)
            {
                Tick(engine, 1);
                captured = TryReadMoveOrder(engine, rep, out order);
            }
            backend.SetButton("<Keyboard>/d", false);
            Tick(engine, 2);

            Assert.That(captured, Is.True, "sole-seat keyboard axis input keeps flowing through the global chain.");
            Assert.That(order.Actor, Is.EqualTo(rep));
            Assert.That(order.PlayerId, Is.EqualTo(1));
            Assert.That(order.Args.Spatial.WorldCm.X, Is.EqualTo(start.X + 400f).Within(0.001f));
        }

        private static MapLoadRequest DualSeatLaunch(string seatZeroScheme, string seatOneScheme)
        {
            return new MapLoadRequest(
                new MapId(HubMapId),
                MapLaunchContext.Create(new[]
                {
                    new LocalSeatLaunchBinding("seat.0", 1, seatZeroScheme),
                    new LocalSeatLaunchBinding("seat.1", 2, seatOneScheme),
                }));
        }

        private static ControlSchemeRuntime RequireSchemes(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException("ControlSchemeRuntime service is missing.");
        }

        private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend, string[] mods)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, mods),
                Path.Combine(repoRoot, "assets"));
            InstallInput(engine, backend);
            AcceptanceUiHostInstaller.Install(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine, TestInputBackend backend)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static bool TryReadMoveOrder(GameEngine engine, Entity actor, out Order order)
        {
            order = default;
            if (!engine.World.IsAlive(actor) ||
                !engine.World.TryGet(actor, out OrderBuffer buffer) ||
                !buffer.HasActive)
            {
                return false;
            }

            Order candidate = buffer.ActiveOrder.Order;
            if (candidate.OrderId <= 0 ||
                candidate.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                candidate.Args.Spatial.Mode != OrderCollectionMode.Single)
            {
                return false;
            }

            order = candidate;
            return true;
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

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetButton(string path, bool down)
            {
                if (down)
                {
                    _buttons.Add(path);
                }
                else
                {
                    _buttons.Remove(path);
                }
            }
        }
    }
}
