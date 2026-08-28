using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Production
{
    /// <summary>
    /// Bridge tool surface under multiple local seats: session.info enumerates the seat
    /// table, camera.control addresses each seat's PresentBinding camera, entities.pick
    /// routes per seat (explicit seatId or rect-routed window point) with that seat's
    /// possessed rep as knowledge owner, and input.inject drives the seat's own input
    /// channel. Sole-seat calls keep the pre-split behavior.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    public sealed class AgentBridgeMultiSeatToolsTests
    {
        private const float DeltaTime = 1f / 60f;
        private const float FovDeg = 60f;

        private static readonly string[] Mods = { "LudotsCoreMod", "CoreInputMod" };

        [Test]
        public void SessionInfo_DualSeat_ListsEverySeatWithBindingRects()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var runtime = new AgentBridgeRuntime(engine, new AgentToolRegistry());
            var context = new AgentToolContext(engine);

            var result = (JsonObject)new SessionInfoTool(runtime).Execute(null, context)!;

            JsonArray seats = (JsonArray)result["seats"]!;
            Assert.That(seats.Count, Is.EqualTo(2), "both seats appear in seat order");
            JsonObject seatZero = (JsonObject)seats[0]!;
            JsonObject seatOne = (JsonObject)seats[1]!;
            Assert.That((string)seatZero["seatId"]!, Is.EqualTo("seat.0"));
            Assert.That((string)seatOne["seatId"]!, Is.EqualTo("seat.1"));
            Assert.That((int)seatZero["playerId"]!, Is.EqualTo(7));
            Assert.That((int)seatOne["playerId"]!, Is.EqualTo(8));
            Assert.That((bool)seatZero["possessed"]!, Is.True);
            Assert.That((bool)seatOne["possessed"]!, Is.True);

            JsonObject rectZero = (JsonObject)seatZero["presentRect"]!;
            JsonObject rectOne = (JsonObject)seatOne["presentRect"]!;
            Assert.That((float)rectZero["x"]!, Is.EqualTo(0f).Within(0.0001f));
            Assert.That((float)rectZero["w"]!, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That((float)rectOne["x"]!, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That((float)rectOne["w"]!, Is.EqualTo(0.5f).Within(0.0001f));

            Assert.That((string)result["camera"]!["seatId"]!, Is.EqualTo("seat.0"),
                "the top-level camera is the first binding in seat order under split-screen");
            Assert.That(result.ContainsKey("localPlayerId"), Is.False,
                "localPlayerId stays a sole-seat compatibility field");
        }

        [Test]
        public void SessionInfo_SoleSeat_KeepsLocalPlayerFields()
        {
            using GameEngine engine = CreateSoleSeatEngine();
            var runtime = new AgentBridgeRuntime(engine, new AgentToolRegistry());
            var context = new AgentToolContext(engine);

            var result = (JsonObject)new SessionInfoTool(runtime).Execute(null, context)!;

            JsonArray seats = (JsonArray)result["seats"]!;
            Assert.That(seats.Count, Is.EqualTo(1));
            Assert.That((int)result["localPlayerId"]!, Is.EqualTo(7));
            Assert.That((string)result["localSeatId"]!, Is.EqualTo("seat.0"));
        }

        [Test]
        public void CameraControl_SeatId_AddressesEachSeatsOwnCamera()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var context = new AgentToolContext(engine);
            var tool = new CameraControlTool();

            tool.Execute(new JsonObject
            {
                ["action"] = "set",
                ["seatId"] = "seat.0",
                ["targetXCm"] = 1000d,
                ["targetYCm"] = 2000d,
            }, context);
            tool.Execute(new JsonObject
            {
                ["action"] = "set",
                ["seatId"] = "seat.1",
                ["targetXCm"] = 80000d,
                ["targetYCm"] = 90000d,
            }, context);

            JsonObject zero = (JsonObject)tool.Execute(
                new JsonObject { ["action"] = "get", ["seatId"] = "seat.0" }, context)!;
            JsonObject one = (JsonObject)tool.Execute(
                new JsonObject { ["action"] = "get", ["seatId"] = "seat.1" }, context)!;
            JsonObject defaultStatus = (JsonObject)tool.Execute(
                new JsonObject { ["action"] = "get" }, context)!;

            Assert.That((float)zero["targetCm"]!["x"]!, Is.EqualTo(1000f).Within(0.01f));
            Assert.That((float)zero["targetCm"]!["y"]!, Is.EqualTo(2000f).Within(0.01f));
            Assert.That((float)one["targetCm"]!["x"]!, Is.EqualTo(80000f).Within(0.01f));
            Assert.That((float)one["targetCm"]!["y"]!, Is.EqualTo(90000f).Within(0.01f),
                "seat.1's camera keeps its own pose; seat.0's set never touched it");
            Assert.That((string)defaultStatus["seatId"]!, Is.EqualTo("seat.0"),
                "seatless get resolves the first binding instead of failing on the split");

            AgentToolException missing = Assert.Throws<AgentToolException>(() => tool.Execute(
                new JsonObject { ["action"] = "get", ["seatId"] = "seat.9" }, context));
            Assert.That(missing!.Code, Is.EqualTo(AgentBridgeErrorCodes.InvalidParams));
            Assert.That(missing.Message, Does.Contain("seat.0").And.Contains("seat.1"),
                "unknown seat names the known seats");
        }

        [Test]
        public void EntitiesPick_DualSeat_RoutesWindowPointToEachSeatsViewport()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var context = new AgentToolContext(engine);
            Entity targetZero = CreateSelectableEntity(engine.World, "LeftQuarterTarget", 0, 0);
            Entity targetOne = CreateSelectableEntity(engine.World, "RightQuarterTarget", 60000, 0);
            PoseSeatCamera(engine, "seat.0", new Vector2(0f, 0f));
            PoseSeatCamera(engine, "seat.1", new Vector2(60000f, 0f));
            Disclose(engine, "seat.0", targetZero);
            Disclose(engine, "seat.1", targetOne);

            var tool = new EntitiesPickTool();
            JsonObject leftHit = (JsonObject)tool.Execute(
                new JsonObject { ["x"] = 480d, ["y"] = 540d }, context)!;
            JsonObject rightHit = (JsonObject)tool.Execute(
                new JsonObject { ["x"] = 1440d, ["y"] = 540d }, context)!;

            Assert.That((bool)leftHit["hit"]!, Is.True);
            Assert.That((int)leftHit["entityId"]!, Is.EqualTo(targetZero.Id),
                "the left-half center routes to seat.0's binding and its camera-centered target");
            Assert.That((string)leftHit["seatId"]!, Is.EqualTo("seat.0"));
            Assert.That((bool)leftHit["routedByPoint"]!, Is.True);

            Assert.That((bool)rightHit["hit"]!, Is.True);
            Assert.That((int)rightHit["entityId"]!, Is.EqualTo(targetOne.Id),
                "the right-half center routes to seat.1's own camera target");
            Assert.That((string)rightHit["seatId"]!, Is.EqualTo("seat.1"));
        }

        [Test]
        public void EntitiesPick_ExplicitSeatId_AddressesThatSeatsViewportOnly()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var context = new AgentToolContext(engine);
            Entity targetZero = CreateSelectableEntity(engine.World, "LeftQuarterTarget", 0, 0);
            Entity targetOne = CreateSelectableEntity(engine.World, "RightQuarterTarget", 60000, 0);
            PoseSeatCamera(engine, "seat.0", new Vector2(0f, 0f));
            PoseSeatCamera(engine, "seat.1", new Vector2(60000f, 0f));
            Disclose(engine, "seat.0", targetZero);
            Disclose(engine, "seat.1", targetOne);

            var tool = new EntitiesPickTool();
            JsonObject autoRouted = (JsonObject)tool.Execute(
                new JsonObject { ["x"] = 1440d, ["y"] = 540d }, context)!;
            JsonObject seatZeroScoped = (JsonObject)tool.Execute(new JsonObject
            {
                ["seatId"] = "seat.0",
                ["x"] = 1440d,
                ["y"] = 540d,
            }, context)!;

            Assert.That((bool)autoRouted["hit"]!, Is.True);
            Assert.That((int)autoRouted["entityId"]!, Is.EqualTo(targetOne.Id),
                "without seatId the right-half point routes to seat.1's binding");

            Assert.That((bool)seatZeroScoped["hit"]!, Is.False,
                "explicit seatId=seat.0 interprets the window point in seat.0's binding-local space; " +
                "a right-half point is not inside seat.0's 960px viewport, so nothing is picked there");
            Assert.That((string)seatZeroScoped["seatId"]!, Is.EqualTo("seat.0"));
            Assert.That((bool)seatZeroScoped["routedByPoint"]!, Is.False);
        }

        [Test]
        public void EntitiesPick_PerSeatKnowledgeGate_OwnerIsTheSeatsPossessedRep()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var context = new AgentToolContext(engine);
            Entity targetOne = CreateSelectableEntity(engine.World, "RightQuarterTarget", 60000, 0);
            PoseSeatCamera(engine, "seat.1", new Vector2(60000f, 0f));

            var tool = new EntitiesPickTool();
            JsonObject gated = (JsonObject)tool.Execute(
                new JsonObject { ["x"] = 1440d, ["y"] = 540d }, context)!;
            Assert.That((bool)gated["hit"]!, Is.False,
                "without a disclosure record for seat.1's possessed rep the candidate is not inspectable");

            Disclose(engine, "seat.1", targetOne);
            JsonObject disclosed = (JsonObject)tool.Execute(
                new JsonObject { ["x"] = 1440d, ["y"] = 540d }, context)!;
            Assert.That((bool)disclosed["hit"]!, Is.True);
            Assert.That((int)disclosed["entityId"]!, Is.EqualTo(targetOne.Id));
        }

        [Test]
        public void InputInject_SeatId_DrivesOnlyThatSeatsChannel()
        {
            using GameEngine engine = CreateDualSeatEngine();
            var runtime = new AgentBridgeRuntime(engine, new AgentToolRegistry());
            var context = new AgentToolContext(engine);
            ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)!;
            Assert.That(seatInput.TryGetChannel("seat.0", out ClientLocalSeatInputChannel channelZero), Is.True);
            Assert.That(seatInput.TryGetChannel("seat.1", out ClientLocalSeatInputChannel channelOne), Is.True);

            var tool = new InputInjectTool(runtime);
            JsonObject result = (JsonObject)tool.Execute(new JsonObject
            {
                ["actionId"] = "Command",
                ["mode"] = "set",
                ["value"] = new JsonObject { ["x"] = 0d, ["y"] = 1d },
                ["seatId"] = "seat.1",
            }, context)!;

            Assert.That((bool)result["injected"]!, Is.True);
            Assert.That((string)result["seatId"]!, Is.EqualTo("seat.1"));
            Tick(engine, 1);

            Assert.That(channelOne.Reader.ReadAction<Vector3>("Command"), Is.EqualTo(new Vector3(0f, 1f, 1f)),
                "seat.1's frozen snapshot carries the injected value (unset axes default to 1, tool semantics)");
            Assert.That(channelZero.Reader.ReadAction<Vector3>("Command"), Is.EqualTo(default(Vector3)),
                "seat.0's channel never sees seat.1's injection");

            JsonArray ledger = runtime.InputEventLog();
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That((string)ledger[0]!["seatId"]!, Is.EqualTo("seat.1"),
                "the injection ledger records which seat the event entered");

            AgentToolException unknown = Assert.Throws<AgentToolException>(() => tool.Execute(new JsonObject
            {
                ["actionId"] = "Command",
                ["mode"] = "press",
                ["seatId"] = "seat.9",
            }, context));
            Assert.That(unknown!.Code, Is.EqualTo(AgentBridgeErrorCodes.InvalidParams));
        }

        [Test]
        public void InputInject_SoleSeatWithSeatId_RoutesGlobalHandler()
        {
            using GameEngine engine = CreateSoleSeatEngine();
            var runtime = new AgentBridgeRuntime(engine, new AgentToolRegistry());
            var context = new AgentToolContext(engine);
            ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)!;
            Assert.That(seatInput.ChannelCount, Is.EqualTo(0),
                "the sole seat keeps the global interpretation chain");

            var tool = new InputInjectTool(runtime);
            JsonObject result = (JsonObject)tool.Execute(new JsonObject
            {
                ["actionId"] = "Command",
                ["mode"] = "press",
                ["seatId"] = "seat.0",
            }, context)!;

            Assert.That((bool)result["injected"]!, Is.True);
            PlayerInputHandler global = engine.GetService(CoreServiceKeys.InputHandler)!;
            Assert.That(global.IsInjectionActive("Command"), Is.True,
                "sole-seat seatId addressing lands on the engine-global handler");
        }

        private static GameEngine CreateDualSeatEngine()
        {
            GameEngine engine = CreateEngine(dualSeat: true);
            Tick(engine, 2);
            return engine;
        }

        private static GameEngine CreateSoleSeatEngine()
        {
            GameEngine engine = CreateEngine(dualSeat: false);
            Tick(engine, 2);
            return engine;
        }

        private static GameEngine CreateEngine(bool dualSeat)
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, Mods),
                Path.Combine(repoRoot, "assets"));
            InstallInput(engine);
            engine.Start();
            engine.MergedConfig.StartupPresentLayout = PresentBinding.HorizontalEqualSplitLayoutId;
            engine.SetService(CoreServiceKeys.ViewController, new HostSurface(1920f, 1080f));

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

            ParticipantBindingResolver.PublishFocused(
                engine.GlobalContext,
                new ParticipantBindingResult(new TeamEntityLookup(), players, localSeats));
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var backend = new IdleInputBackend();
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

        private static Entity CreateSelectableEntity(World world, string name, int xCm, int yCm)
        {
            return world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new Name { Value = name },
                new CommandSourceSelectableTag());
        }

        private static void PoseSeatCamera(GameEngine engine, string seatId, Vector2 targetCm)
        {
            ClientLocalSeatAccess.TryResolvePresentCamera(engine, seatId, out var camera, out _);
            camera.State.TargetCm = targetCm;
            camera.State.DistanceCm = 3000f;
            camera.State.Pitch = 50f;
            camera.State.FovYDeg = FovDeg;
        }

        private static void Disclose(GameEngine engine, string seatId, Entity target)
        {
            Entity viewer = ClientLocalSeatAccess.RequireRegistry(engine).Require(seatId).PossessedRep;
            KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)!;
            store.Upsert(viewer, target, new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                default,
                default,
                default,
                viewer,
                observedTick: 0,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 1));
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
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

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class HostSurface : IViewController
        {
            public HostSurface(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => FovDeg;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class IdleInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
