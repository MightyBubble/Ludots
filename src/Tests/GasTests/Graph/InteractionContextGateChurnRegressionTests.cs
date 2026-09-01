using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph;

/// <summary>
/// Real-machine rhythm regression for the context trigger gate: visual frames decouple from
/// logic ticks (jittered frame delta, accumulator catch-up bursts) while a derived context is
/// held for a long drag, with periodic UI-capture windows like the pointer crossing interactive
/// UI mid-drag. A held button must not re-report press edges after blocked frames (duplicate
/// InputActionFired re-executes context graphs), interaction judges must keep their gesture
/// anchor across blocked frames (long drags must not misjudge as taps), and the profile's
/// triggers must mount exactly once per context activation.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class InteractionContextGateChurnRegressionTests
{
    private const string MapId = "case_e_selection_field";

    private RecordingLogBackend _log = null!;

    [SetUp]
    public void SetUp()
    {
        _log = new RecordingLogBackend();
        Log.Initialize(_log);
    }

    [TearDown]
    public void TearDown()
    {
        Log.Initialize(NullLogBackend.Instance);
        _log.Dispose();
        _log = null!;
    }

    [Test]
    public void LongHeldDerivedContextUnderRealMachineRhythm_MountsOnceAndFiresOnce()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 60, () => engine.CurrentMapSession != null);

        Entity commander = ResolveCommander(engine);

        // One press; the button stays held for the whole window (long drag).
        backend.SetMousePosition(new Vector2(200f, 200f));
        SetGroundOverride(engine, new Vector2(-1100f, -200f));
        backend.SetButton("<Mouse>/leftButton", true);
        TickUntil(engine, 40, BoxingActive(engine, commander));
        Assert.That(BoxingActive(engine, commander)(), Is.True, "按下后衍生 boxing context 激活");

        // The boxing profile's mount lands one gate tick after activation; the hold window
        // must start after that single mount so any later append is churn.
        TickUntil(engine, 20, () => _log.Appends.Count >= 2);
        int appendsAtHoldStart = _log.Appends.Count;
        Assert.That(_log.Appends.Count, Is.EqualTo(2), "按下窗口恰好一次注册 + 一次追加：" + string.Join(" | ", _log.Appends));

        var handler = engine.GetService(CoreServiceKeys.InputHandler) as PlayerInputHandler
            ?? throw new InvalidOperationException("PlayerInputHandler service is missing.");
        var groundOverride = engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)
            as AuthoritativeGroundPointerOverride
            ?? throw new InvalidOperationException("AuthoritativeGroundPointerOverride service is missing.");
        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            engine.GlobalContext,
            nameof(InteractionContextGateChurnRegressionTests));
        int spuriousPressEdges = 0;
        bool prevBlocked = false;
        for (int frame = 0; frame < 900; frame++)
        {
            float dt = frame % 97 == 0 ? 0.25f : (frame % 3 == 0 ? 1f / 30f : 1f / 60f);
            backend.SetMousePosition(new Vector2(200f + (frame % 40) * 12f, 200f + (frame % 17) * 7f));
            // Re-arm the ground override each frame so the bridge's ground read stays
            // resolvable like the real machine's always-present ray provider.
            groundOverride.Set(bindings.CommandActionId, new Vector2(-600f, 0f));
            bool uiCaptured = frame % 61 < 3;
            engine.SetService(CoreServiceKeys.UiCaptured, uiCaptured);
            engine.Tick(dt);
            if (prevBlocked && !uiCaptured && handler.PressedThisFrame("CaseE.BoxSelectBegin"))
            {
                spuriousPressEdges++;
            }

            prevBlocked = uiCaptured;
        }

        engine.SetService(CoreServiceKeys.UiCaptured, false);

        Assert.That(spuriousPressEdges, Is.EqualTo(0),
            "UI 捕获窗口结束后按钮仍按住，不得伪造新的按下边沿");
        Assert.That(BoxingActive(engine, commander)(), Is.True, "长持期间 boxing context 保持激活");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            $"触发图执行不应出错：{string.Join(" | ", engine.TriggerManager.Errors)}");
        Assert.That(_log.Appends.Count - appendsAtHoldStart, Is.EqualTo(0),
            "长持衍生 context 期间不得重复挂载追加：" + string.Join(" | ", _log.Infos));

        // Release with a long travel: the drag gesture must complete as a drag (commit runs,
        // boxing deactivates exactly once) — not misjudge as a tap from a re-anchored judge.
        backend.SetMousePosition(new Vector2(900f, 500f));
        groundOverride.Set(bindings.CommandActionId, new Vector2(-600f, 0f));
        backend.SetButton("<Mouse>/leftButton", false);
        TickUntil(engine, 40, BoxingCleared(engine, commander));
        Assert.That(BoxingCleared(engine, commander)(), Is.True, "抬起后 Drag 判定完成并停用 boxing");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            $"整段链路零触发错误：{string.Join(" | ", engine.TriggerManager.Errors)}");
    }

    private static Func<bool> BoxingActive(GameEngine engine, Entity commander)
    {
        return () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances instances) &&
            instances.Count == 1;
    }

    private static Func<bool> BoxingCleared(GameEngine engine, Entity commander)
    {
        return () =>
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances instances) ||
            instances.Count == 0;
    }

    private static Entity ResolveCommander(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
        return session.EntityIndex.GetRequired(session.MapId.Value, "case-e-commander", "GateChurnRegression");
    }

    private static void SetGroundOverride(GameEngine engine, Vector2 worldCm)
    {
        if (engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride) is not AuthoritativeGroundPointerOverride groundOverride)
        {
            throw new InvalidOperationException("AuthoritativeGroundPointerOverride service is missing.");
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            engine.GlobalContext,
            nameof(InteractionContextGateChurnRegressionTests));
        groundOverride.Set(bindings.CommandActionId, worldCm);
    }

    private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CaseESelectionMod" }),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.SetService(
            CoreServiceKeys.ViewController,
            (Ludots.Core.Presentation.Camera.IViewController)new HeadlessViewController(1600f, 900f));
        engine.Start();
        return engine;
    }

    private static void TickUntil(GameEngine engine, int maxFrames, Func<bool> condition)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 60f);
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

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private sealed class HeadlessViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public HeadlessViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 50f;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public Vector2 MousePosition { get; set; }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
        public Vector2 GetMousePosition() => MousePosition;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        public void SetButton(string devicePath, bool down)
        {
            if (down)
            {
                _buttons.Add(devicePath);
            }
            else
            {
                _buttons.Remove(devicePath);
            }
        }

        public void SetMousePosition(Vector2 position) => MousePosition = position;
    }

    private sealed class RecordingLogBackend : ILogBackend
    {
        public readonly List<string> Appends = new();
        public readonly List<string> Duplicates = new();
        public readonly List<string> Infos = new();

        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            if (level < LogLevel.Info)
            {
                return;
            }

            lock (Infos)
            {
                Infos.Add(message);
                if (message.Contains("Appended", StringComparison.Ordinal) ||
                    message.Contains("Registered", StringComparison.Ordinal))
                {
                    Appends.Add(message);
                }

                if (message.Contains("Duplicate registration", StringComparison.Ordinal))
                {
                    Duplicates.Add(message);
                }
            }
        }

        public void Flush() { }
        public void Dispose() { }
    }
}
