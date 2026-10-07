using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// End-to-end key-to-walk acceptance for the WASD sandbox (#1729): each WASD key must move the
    /// sole possessed rep along the ground-plane direction declared by the engine's Move composite
    /// (W/S = ±Y, A/D = ∓X), through the real order pipeline (AxisMoveOrderSystem → OrderQueue →
    /// MoveToWorldCmOrderSystem). This is the seam the 2026-07-09 merge regression hid behind —
    /// payload mirror tests stay green when both sides flip together.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class WasdSandboxWalkAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "wasd_sandbox_entry";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "WasdSandboxMod"
        };

        [Test]
        public void WasdSandbox_EachKey_WalksRepAlongItsScreenDirection()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend);
            engine.LoadMap(MapId);
            Tick(engine, 8);

            Entity rep = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(engine.World.Has<WorldPositionCm>(rep), Is.True);
            ControlSchemeRuntime schemes = RequireSchemes(engine);
            Assert.That(
                schemes.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding),
                Is.True,
                "seat-declared scheme.wasd_move must be active from map entry with no timeline or manual switch.");
            Assert.That(
                binding.DirectionMode,
                Is.EqualTo(ControlSchemeAxisMoveDirectionMode.CameraRelative),
                "the sandbox declares camera-relative axis move: W is screen-up regardless of camera yaw.");

            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            Assert.That(views.TryGetDefaultViewId(rep, out string viewId), Is.True,
                "the possessed rep's logic view (and its camera) must exist for camera-relative movement.");
            float cameraYaw = views.Require(viewId).Camera.State.Yaw;

            WalkAndAssertAxis(engine, backend, rep, "<Keyboard>/d", new Vector2(1f, 0f), cameraYaw);
            WalkAndAssertAxis(engine, backend, rep, "<Keyboard>/a", new Vector2(-1f, 0f), cameraYaw);
            WalkAndAssertAxis(engine, backend, rep, "<Keyboard>/w", new Vector2(0f, 1f), cameraYaw);
            WalkAndAssertAxis(engine, backend, rep, "<Keyboard>/s", new Vector2(0f, -1f), cameraYaw);
        }

        private static void WalkAndAssertAxis(
            GameEngine engine,
            TestInputBackend backend,
            Entity rep,
            string key,
            Vector2 screenAxis,
            float cameraYaw)
        {
            Vector2 expected = OrbitCameraDirectionUtil.MoveInputToDirection(cameraYaw, screenAxis);
            Vector2 before = engine.World.Get<WorldPositionCm>(rep).Value.ToVector2();

            backend.SetButton(key, true);
            Vector2 after = before;
            bool moved = false;
            for (int frame = 0; frame < 300; frame++)
            {
                Tick(engine, 1);
                after = engine.World.Get<WorldPositionCm>(rep).Value.ToVector2();
                if ((after - before).LengthSquared() >= 100f * 100f)
                {
                    moved = true;
                    break;
                }
            }

            backend.SetButton(key, false);
            Tick(engine, 2);

            Assert.That(moved, Is.True,
                $"{key} held for 300 frames must walk the rep at least 100cm (before={before}, after={after}).");

            Vector2 delta = after - before;
            float distance = delta.Length();
            float along = Vector2.Dot(delta, expected) / distance;
            Assert.That(along, Is.GreaterThan(0.99f),
                $"{key} must walk the rep along its camera-relative screen direction " +
                $"(yaw={cameraYaw}, expected={expected}, delta={delta}).");
        }

        [Test]
        public void WasdSandbox_ToggleKey_SwitchesPossessionAndCameraProfile()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend);
            engine.LoadMap(MapId);
            Tick(engine, 8);

            Entity zhangSan = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            ControlSchemeRuntime schemes = RequireSchemes(engine);
            Assert.That(schemes.TryGetActiveAxisMove(out _), Is.True);
            Assert.That(ActiveCameraId(engine, zhangSan), Is.EqualTo("Camera.Profile.WasdSandboxTps"),
                "张三's binding adopts the TPS profile at map entry.");

            backend.SetButton("<Keyboard>/t", true);
            Tick(engine, 1);
            backend.SetButton("<Keyboard>/t", false);
            Tick(engine, 4);

            Entity current = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(current, Is.Not.EqualTo(zhangSan),
                "T must cycle possession to the other bound rep (李四).");
            Assert.That(ActiveCameraId(engine, current), Is.EqualTo("Camera.Profile.WasdSandboxTopdown"),
                "李四's binding adopts the top-down profile after the possession switch.");

            // The seam that bit us: camera-relative axis move resolves the seat's view camera, not
            // a per-rep view — 李四 owns no LogicView, and movement must still work.
            Vector2 beforeWalk = engine.World.Get<WorldPositionCm>(current).Value.ToVector2();
            backend.SetButton("<Keyboard>/d", true);
            Vector2 afterWalk = beforeWalk;
            bool walked = false;
            for (int frame = 0; frame < 300; frame++)
            {
                Tick(engine, 1);
                afterWalk = engine.World.Get<WorldPositionCm>(current).Value.ToVector2();
                if ((afterWalk - beforeWalk).LengthSquared() >= 100f * 100f)
                {
                    walked = true;
                    break;
                }
            }

            backend.SetButton("<Keyboard>/d", false);
            Tick(engine, 2);
            Assert.That(walked, Is.True,
                "D must walk 李四 after the possession switch (before={0}, after={1}).");
        }

        private static string ActiveCameraId(GameEngine engine, Entity rep)
        {
            // Sole-seat fullscreen: the seat's PresentBinding view is the single camera authority;
            // profile adoption targets it regardless of which rep is possessed.
            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine.GlobalContext);
            Assert.That(seats.TryGetSoleSeat(out ClientLocalSeat seat), Is.True);
            PresentBinding binding = seat.PresentBinding
                ?? throw new InvalidOperationException("sole seat has no PresentBinding view.");
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            return views.Require(binding.LogicViewId).Camera.VirtualCameraBrain?.ActiveCameraId
                ?? string.Empty;
        }

        private static ControlSchemeRuntime RequireSchemes(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException("ControlSchemeRuntime service is missing.");
        }

        private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
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

            throw new DirectoryNotFoundException("Repository root not found above test output directory.");
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
