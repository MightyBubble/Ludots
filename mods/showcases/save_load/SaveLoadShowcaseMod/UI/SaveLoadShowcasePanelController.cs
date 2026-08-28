using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using SaveLoadShowcaseMod.Runtime;

namespace SaveLoadShowcaseMod.UI;

internal sealed class SaveLoadShowcasePanelController
{
    private readonly SaveLoadShowcaseRuntime _runtime;
    private ReactivePage<SaveLoadShowcaseState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public SaveLoadShowcasePanelController(SaveLoadShowcaseRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        SaveLoadShowcaseState state = _runtime.BuildState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<SaveLoadShowcaseState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("SaveLoadShowcase.Panel", UiSurfaceSegment.Overlay, priority: 55), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<SaveLoadShowcaseState> context)
    {
        SaveLoadShowcaseState state = context.State;
        string driftColor = !state.SavedExists ? "#C7D0DD" : state.DriftIsZero ? "#8DE3AE" : "#F0C36B";
        return Ui.Column(
            Ui.Column(
                Ui.Text("Save / Load — the world comes back").FontSize(20f).Bold().Color("#F5F7FA"),
                Ui.Text("Move the hero, save to a real disk slot, keep playing, restore: state returns to the save point. Quit and relaunch — the slot survives the cold start.")
                    .FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.Phase).FontSize(12f).Bold().Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                    Button("Nudge hero", "nudge", r => r.NudgeHero()),
                    Button("Save via panel", "save", r => r.SaveViaPanel()),
                    Button("Restore latest", "restore", r => r.RestoreLatest()),
                    Button("Spawn excluded decoy", "decoy", r => r.SpawnExcludedDecoy()),
                    Button("Corrupt latest slot", "corrupt", r => r.CorruptLatestSlot())).Gap(6f).Wrap(),
                Section("Live state", new[]
                {
                    $"tick {state.CurrentTick}   hero {state.HeroPosition}",
                    $"save point {state.SavedAt}",
                    $"drift {state.Drift}" + (state.SavedExists ? (state.DriftIsZero ? "   [restored — matches save point]" : "   [world moved since save]") : ""),
                    $"storage {state.StorageLine}",
                }, "#8DE3AE"),
                Section("How to read the scene", new[]
                {
                    "cyan ring = live hero position",
                    "magenta box/ring = save point; the line shows drift",
                    "after restore the rings overlap (drift 0)",
                    "excluded decoy spawns next to the hero and disappears on restore",
                }, "#8AD7FF"),
                Section("Fault demo", new[]
                {
                    "corrupt flips bytes in the newest slot; restore then fails with a readable red error (section hash mismatch) — no silent fallback",
                }, "#FF8A8A"),
                Section("Trace", state.LogLines, "#FFB38A"))
            .Width(560f).Padding(14f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(16f).Align(UiAlignItems.Start).ZIndex(55);
    }

    private UiElementBuilder Button(string label, string id, Action<SaveLoadShowcaseRuntime> action)
        => Ui.Button(label, _ => action(_runtime)).Id($"save-load-{id}").Height(32f);

    private static UiElementBuilder Section(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++) children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(520f).Padding(10f).Gap(5f).Background("#0E1823").Border(1f, Color("#284154"));
    }

    private static UiColor Color(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
