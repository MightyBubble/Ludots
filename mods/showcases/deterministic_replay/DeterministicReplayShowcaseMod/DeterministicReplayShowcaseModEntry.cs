using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
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
            _runtime.AdvanceReplayFixedStep();
            return;
        }

        _runtime.CaptureRecordingFrame();
    }
}

internal sealed class ReplayShowcasePresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly ReplayPlaybackRuntime _runtime;
    private readonly ReplayPanelController _panel;

    public ReplayShowcasePresentationSystem(GameEngine engine, ReplayPlaybackRuntime runtime) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
        _panel = new ReplayPanelController(runtime);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsShowcaseMap) { _panel.ClearIfOwned(); return; }
        _panel.MountOrRefresh(_engine);
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
        string digestColor = state.EndMatches ? "#8DE3AE" : "#F0C36B";
        return Ui.Column(
            Ui.Column(
                Ui.Text("Deterministic Replay — same inputs, same world").FontSize(20f).Bold().Color("#F5F7FA"),
                Ui.Text("Record authoritative input frames while you command the hero, then replay them: the world re-evolves identically from the recorded checkpoint, and live input during playback is rejected.")
                    .FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.Phase).FontSize(12f).Bold().Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                    Btn("Nudge hero", "nudge", r => r.NudgeHero()),
                    Btn("Start record", "record", r => r.StartRecording()),
                    Btn("Stop record", "stop", r => r.StopRecording()),
                    Btn("Play replay", "play", r => r.PlayReplay()),
                    Btn("Pause / Resume", "pause", r => r.TogglePause()),
                    Btn("Step one frame", "step", r => r.StepOne()),
                    Btn("Reset replay", "reset", r => r.ResetReplay()),
                    Btn("Save archive", "save", r => r.SaveArchive()),
                    Btn("Load latest archive", "load", r => r.LoadLatestArchive())).Gap(6f).Wrap(),
                Section("Determinism proof", new[]
                {
                    $"recorded end digest  {state.RecordedEndDigest}",
                    $"playback digest      {state.PlaybackDigest}",
                    state.EndMatches ? "MATCH — the replay reproduced the recorded end state" : "pending: play the replay to the end, then compare",
                    $"input isolation: {state.IsolationNote}",
                }, digestColor),
                Section("Live state", new[]
                {
                    $"tick {state.CurrentTick}   frames {state.Frames}   playback index {state.PlaybackIndex}",
                    $"recording {state.IsRecording}   replaying {state.IsReplaying}   paused {state.IsPaused}",
                    $"archive {state.ArchiveLine}",
                }, "#8DE3AE"),
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
            engine.RegisterSystem(new ReplayShowcaseInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ReplayShowcasePresentationSystem(engine, runtime));
            context.Log("[DeterministicReplayShowcaseMod] Deterministic replay showcase installed.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
