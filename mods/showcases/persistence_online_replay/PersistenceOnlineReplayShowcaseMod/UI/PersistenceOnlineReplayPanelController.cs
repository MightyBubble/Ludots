using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using PersistenceOnlineReplayShowcaseMod.Runtime;

namespace PersistenceOnlineReplayShowcaseMod.UI;

internal sealed class PersistenceOnlineReplayPanelController
{
    private readonly PersistenceOnlineReplayRuntime _runtime;
    private ReactivePage<PersistenceOnlineReplayPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;
    public PersistenceOnlineReplayPanelController(PersistenceOnlineReplayRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        PersistenceOnlineReplayPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<PersistenceOnlineReplayPanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("Showcase.PersistenceOnlineReplay.Panel", UiSurfaceSegment.Overlay, priority: 55), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<PersistenceOnlineReplayPanelState> context)
    {
        PersistenceOnlineReplayPanelState state = context.State;
        return Ui.Column(
            Ui.Column(
                Ui.Text(state.Header).FontSize(22f).Bold().Color("#F5F7FA"),
                Ui.Text(state.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Keys: F5 Checkpoint, F6 Save, F7 Restore, F8 Record, F9 Stop, F10 Replay, F11 Disconnect, F12 Reconnect")
                    .FontSize(10f).Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.Status).FontSize(13f).Bold().Color("#F0C36B"),
                BuildButtons(),
                BuildSection("Live authoritative state", state.Metrics, "#8DE3AE").Height(178f),
                Ui.ScrollView(
                    BuildSection("How to play", state.Controls, "#8AD7FF"),
                    BuildSection("Trace", state.LogLines, "#FFB38A"))
                    .Height(126f).Gap(8f))
            .Width(500f).Height(680f).Padding(16f).Gap(10f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(20f).Align(UiAlignItems.End).ZIndex(55);
    }

    private UiElementBuilder BuildButtons()
    {
        return Ui.Column(
            Ui.Row(Button("Checkpoint", "checkpoint", r => r.RequestCheckpoint()), Button("Save", "save", r => r.SaveSlot()), Button("Restore", "restore", r => r.RestoreSlot())).Gap(6f).Wrap(),
            Ui.Row(Button("Record", "record", r => r.StartRecording()), Button("Stop", "stop", r => r.StopRecording()), Button("Replay", "replay", r => r.PlayReplay())).Gap(6f).Wrap(),
            Ui.Row(Button("Pause / Resume", "pause", r => r.ToggleReplayPause()), Button("Step", "step", r => r.StepReplay()), Button("Reset", "reset", r => r.ResetReplay())).Gap(6f).Wrap(),
            Ui.Row(Button("Disconnect", "disconnect", r => r.SimulateDisconnect()), Button("Reconnect", "reconnect", r => r.Reconnect()), Button("Delete frame", "ablate", r => r.AblateFrame())).Gap(6f).Wrap());
    }

    private UiElementBuilder Button(string label, string id, Action<PersistenceOnlineReplayRuntime> action)
        => Ui.Button(label, _ => Run(action)).Id($"persistence-replay-{id}");

    private void Run(Action<PersistenceOnlineReplayRuntime> action)
    {
        if (_engine != null) action(_runtime);
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++) children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(448f).Padding(10f).Gap(5f).Background("#0E1823").Border(1f, Color("#284154"));
    }
    private static UiColor Color(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
