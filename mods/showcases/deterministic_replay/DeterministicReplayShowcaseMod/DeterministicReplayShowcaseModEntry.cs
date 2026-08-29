using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using DeterministicReplayShowcaseMod.Runtime;

namespace DeterministicReplayShowcaseMod;

internal sealed class ReplayShowcaseInputSystem : BaseSystem<World, float>
{
    private readonly ReplayPlaybackRuntime _runtime;
    public ReplayShowcaseInputSystem(GameEngine engine, ReplayPlaybackRuntime runtime) : base(engine.World) { _runtime = runtime; }
    public override void Update(in float dt)
    {
        if (!_runtime.IsShowcaseMap) return;
        if (_runtime.IsReplaying)
        {
            _runtime.ConsumeNudgeAction();
            _runtime.AdvanceReplayFixedStep();
            return;
        }

        // Mirrors the recording end boundary: the replay's last tick has now completed its full
        // tail, so sampling here is phase-identical to FinishRecordingAtFixedBoundary.
        _runtime.SettleEndDigestIfPending();

        if (_runtime.StopRequested)
        {
            _runtime.FinishRecordingAtFixedBoundary();
            return;
        }

        if (_runtime.StartRequested)
        {
            _runtime.BeginRecordingAtFixedBoundary();
        }

        _runtime.ConsumeNudgeAction();
        _runtime.CaptureRecordingFrame();
    }
}

internal sealed class ReplayShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly ReplayPlaybackRuntime _runtime;
    private readonly ReplayPanelController _panel;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly List<Vector2> _liveTrail = new(256);
    private readonly List<Vector2> _replayTrail = new(256);
    private bool _lastWasReplaying;

    public ReplayShowcasePresentationSystem(GameEngine engine, ReplayPlaybackRuntime runtime, DebugDrawCommandBuffer debugDraw) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
        _debugDraw = debugDraw;
        _panel = new ReplayPanelController(runtime);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsShowcaseMap) { _panel.ClearIfOwned(); return; }
        DrawTrails();
        _panel.MountOrRefresh(_engine);
    }

    private void DrawTrails()
    {
        if (_runtime.TryGetScoutPositionCm(out var scout) == false) { _debugDraw.Clear(); return; }
        Vector2 live = new(scout.x * 0.01f, scout.y * 0.01f);

        // replay start clears the replay trail; live trail resets on record start
        if (_runtime.IsReplaying && !_lastWasReplaying) _replayTrail.Clear();
        if (_runtime.IsRecording && !_runtime.WasRecording) _liveTrail.Clear();
        _lastWasReplaying = _runtime.IsReplaying;
        _runtime.WasRecording = _runtime.IsRecording;

        if (_runtime.IsReplaying) _replayTrail.Add(live); else _liveTrail.Add(live);
        if (_liveTrail.Count > 256) _liveTrail.RemoveAt(0);
        if (_replayTrail.Count > 256) _replayTrail.RemoveAt(0);

        _debugDraw.Clear();
        var cyan = new DebugDrawColor(72, 226, 210);
        _debugDraw.Circles.Add(new DebugDrawCircle2D { Center = live, Radius = 3.2f, Thickness = 0.22f, Color = cyan });

        // recorded path (magenta, thin) vs replay path (gold, thick) — overlap is the visible proof
        DrawTrail(_liveTrail, new DebugDrawColor(72, 226, 210, 150));
        DrawTrail(_replayTrail, new DebugDrawColor(255, 202, 72, 220));
    }

    private void DrawTrail(List<Vector2> trail, DebugDrawColor color)
    {
        for (int i = 1; i < trail.Count; i++)
        {
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = trail[i - 1], B = trail[i], Thickness = 0.10f, Color = color });
        }
    }
}

internal sealed class ReplayPanelController
{
    private readonly ReplayPlaybackRuntime _runtime;
    private ReactivePage<ReplayShowcaseState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public ReplayPanelController(ReplayPlaybackRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        ReplayShowcaseState state = _runtime.BuildState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<ReplayShowcaseState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("DeterministicReplayShowcase.Panel", UiSurfaceSegment.Overlay, priority: 55), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ReplayShowcaseState> context)
    {
        ReplayShowcaseState state = context.State;
        // Verdict color carries the only semantic weight on the panel: green = proof landed,
        // amber = proof failed (diverged), grey = no verdict yet (not played).
        (string verdictColor, string verdictLabel) = state.Verdict switch
        {
            "Matched" => ("#8DE3AE", $"MATCH — {state.VerdictHint}"),
            "Mismatched" => ("#F0C36B", $"MISMATCH — {state.VerdictHint}"),
            _ => ("#9AA8B5", state.VerdictHint),
        };
        return Ui.Column(
            Ui.Column(
                Ui.Text("Deterministic Replay — record, replay, compare").FontSize(20f).Bold().Color("#F5F7FA"),
                Ui.Text("Command the hero while recording, then replay the same inputs: the world must re-evolve identically. Random nudges included — the replay reproduces them exactly.")
                    .FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.Phase).FontSize(12f).Bold().Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                    Btn("Nudge hero", "nudge", r => r.NudgeHero()),
                    Btn("Start record", "record", r => r.StartRecording()),
                    Btn("Stop record", "stop", r => r.StopRecording())).Gap(6f),
                Ui.Row(
                    Btn("Play replay", "play", r => r.PlayReplay()),
                    Btn("Pause / Resume", "pause", r => r.TogglePause()),
                    Btn("Step one frame", "step", r => r.StepOne()),
                    Btn("Reset replay", "reset", r => r.ResetReplay())).Gap(6f),
                Ui.Row(
                    Btn("Save archive", "save", r => r.SaveArchive()),
                    Btn("Load latest archive", "load", r => r.LoadLatestArchive()),
                    Btn("Jump to end (world save)", "jend", r => r.JumpToEndViaWorldSave())).Gap(6f),
                Section("Proof (digest of the whole world, per tick)", new[]
                {
                    $"recorded end  {state.RecordedEndDigest}",
                    $"replay end    {state.PlaybackDigest}",
                    verdictLabel,
                    $"live-input isolation (engine): {state.IsolationState}",
                }, verdictColor),
                Section("Live state", new[]
                {
                    $"tick {state.CurrentTick}   frames {state.Frames}   playback frame {System.Math.Max(state.PlaybackIndex, 0)}{(state.PlaybackIndex < 0 ? " (not playing)" : "")}",
                    $"recording {state.IsRecording}   replaying {state.IsReplaying}   paused {state.IsPaused}",
                    $"archive on disk: {state.ArchiveLine}",
                }, "#7FB4D8"),
                Section("Legend", new[]
                {
                    "grey verdict = not played yet · green = replay matched · amber = diverged (engine gap #1311)",
                    "frames = captured input snapshots (one per tick during record/replay)",
                    "digest = hash of every entity's every component (whole-world fingerprint)",
                    "on stage: cyan = live scout · magenta = recorded path being replayed · overlap = proof you can see",
                }, "#8A93A0"),
                Section("Trace", state.LogLines, "#FFB38A"))
            .Width(560f).Padding(14f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(16f).Align(UiAlignItems.Start).ZIndex(55);
    }

    private UiElementBuilder Btn(string label, string id, Action<ReplayPlaybackRuntime> action)
        => Ui.Button(label, _ => action(_runtime)).Id($"replay-show-{id}").Height(30f);

    private static UiElementBuilder Section(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++) children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(520f).Padding(10f).Gap(5f).Background("#0E1823").Border(1f, Color("#284154"));
    }

    private static UiColor Color(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}

public sealed class DeterministicReplayShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtimeHolder = new ReplayPlaybackRuntime?[] { null };
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(DeterministicReplayShowcaseIds.InstalledKey, out object? value) && value is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[DeterministicReplayShowcaseIds.InstalledKey] = true;
            var runtime = runtimeHolder[0] ??= new ReplayPlaybackRuntime(engine);
            engine.GlobalContext[DeterministicReplayShowcaseIds.RuntimeKey] = runtime;
            var debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterSystem(new ReplayShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ReplayShowcasePresentationSystem(engine, runtime, debugDraw));
            context.Log("[DeterministicReplayShowcaseMod] Deterministic replay showcase installed.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
