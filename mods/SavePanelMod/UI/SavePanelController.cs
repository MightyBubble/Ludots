using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using SavePanelMod.Runtime;

namespace SavePanelMod.UI;

internal sealed class SavePanelController
{
    private readonly SavePanelRuntime _runtime;
    private ReactivePage<SavePanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public SavePanelController(SavePanelRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        SavePanelState state = _runtime.StateWithStatus();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<SavePanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("SavePanelMod.Panel", UiSurfaceSegment.Overlay, priority: 60), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<SavePanelState> context)
    {
        SavePanelState state = context.State;
        var rows = new List<UiElementBuilder>();
        if (state.Rows.Count == 0)
        {
            rows.Add(Ui.Text("No slots yet — press Save.").FontSize(11f).Color("#C7D0DD"));
        }
        else
        {
            foreach (SaveSlotRow row in state.Rows)
            {
                string kind = row.Kind, name = row.Name;
                rows.Add(Ui.Row(
                    Ui.Text($"{row.Kind,-9} {row.Name,-22} tick {row.Tick,-5} {row.MapId,-10} {row.CreatedUtc,-19} v{row.SchemaVersion} {row.Bytes / 1024.0:F0}KB")
                        .FontSize(10f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal).WidthPercent(100f),
                    Ui.Row(
                        Button("Load", "load", r => r.RestoreSlot(kind, name)),
                        Button("Del", "del", r => r.DeleteSlot(kind, name))).Gap(4f))
                    .Width(620f).Padding(4f).Background("#0E1823").Border(1f, UiColorFrom("#22384E")));
            }
        }

        return Ui.Column(
            Ui.Column(
                Ui.Text(state.Header).FontSize(20f).Bold().Color("#F5F7FA"),
                Ui.Text("Slots on disk; storage flows through the engine ISaveStorage service.")
                    .FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.StorageLine).FontSize(10f).Color("#8AD7FF"),
                Ui.Row(
                    Button("Save slot", "save", r => r.SaveSlot()),
                    Button("Autosave now", "autosave", r => r.WriteAutosave()),
                    Button("Hide (F5)", "hide", r => r.ToggleVisible())).Gap(6f),
                string.IsNullOrEmpty(state.Error)
                    ? Ui.Text(state.Status).FontSize(12f).Bold().Color("#8DE3AE").WhiteSpace(UiWhiteSpace.Normal)
                    : Ui.Text(state.Error).FontSize(12f).Bold().Color("#FF8A8A").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"manual {state.ManualCount} · autosave {state.AutosaveCount} (autosave ring never deletes manual slots)")
                    .FontSize(10f).Color("#8AD7FF"),
                Ui.ScrollView(Ui.Column(rows.ToArray()).Gap(3f)).Height(240f))
            .Width(660f).Padding(14f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, UiColorFrom("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(16f).Align(UiAlignItems.End).ZIndex(60);
    }

    private UiElementBuilder Button(string label, string id, Action<SavePanelRuntime> action)
        => Ui.Button(label, _ => Run(action)).Id($"save-panel-{id}").Height(30f);

    private void Run(Action<SavePanelRuntime> action)
    {
        if (_engine != null) action(_runtime);
    }

    private static UiColor UiColorFrom(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
