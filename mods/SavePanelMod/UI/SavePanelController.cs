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

    public SavePanelController(SavePanelRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host)
        {
            return;
        }

        _engine = engine;
        SavePanelState state = _runtime.BuildPanelState(engine);
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer)!;
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider)!;
            _page = new ReactivePage<SavePanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        host.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Capability.SavePanel", UiSurfaceSegment.Overlay, priority: 70),
            _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
        {
            host.ReleaseLease(ref _lease);
        }

        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<SavePanelState> context)
    {
        SavePanelState state = context.State;
        var children = new List<UiElementBuilder>
        {
            Ui.Text(state.Header).FontSize(22f).Bold().Color("#F5F7FA"),
            Ui.Text(state.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(state.Controls).FontSize(10f).Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"落盘根：{state.StorageRoot}").FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"手动槽名：{state.ManualName}").FontSize(11f).Color("#C7D0DD"),
            Ui.Text(state.Status).FontSize(13f).Bold().Color(state.PendingCapture ? "#F0C36B" : "#8DE3AE").WhiteSpace(UiWhiteSpace.Normal),
        };

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            children.Add(
                Ui.Text($"错误：{state.Error}")
                    .FontSize(12f)
                    .Bold()
                    .Color("#FF7A7A")
                    .WhiteSpace(UiWhiteSpace.Normal));
        }

        children.Add(BuildButtons());
        children.Add(BuildSlotList(state));
        children.Add(BuildSection("自动存档轮换", state.AutosaveLines, "#EE5EDC"));

        return Ui.Column(
                Ui.Column(children.ToArray())
                    .Width(520f)
                    .Height(640f)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(8f)
                    .Background("#0B1520")
                    .Border(1f, Color("#2F475E")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Align(UiAlignItems.End)
            .ZIndex(70);
    }

    private UiElementBuilder BuildButtons()
    {
        return Ui.Column(
            Ui.Row(
                Button("存档", "save", r => r.RequestManualSave),
                Button("读档", "restore", r => r.RestoreSelected),
                Button("删除", "delete", r => r.DeleteSelected),
                Button("自动存", "autosave", r => r.RequestAutosave),
                Button("关闭", "hide", r => eng => r.Hide(eng))).Gap(6f).Wrap());
    }

    private UiElementBuilder BuildSlotList(SavePanelState state)
    {
        var rows = new List<UiElementBuilder>
        {
            Ui.Text("槽位列表").FontSize(12f).Bold().Color("#8AD7FF"),
        };

        if (state.Slots.Count == 0)
        {
            rows.Add(Ui.Text("（空）还没有任何存档。").FontSize(11f).Color("#C7D0DD"));
        }
        else
        {
            foreach (SavePanelSlotRow slot in state.Slots)
            {
                bool selected = string.Equals(state.SelectedSlot, slot.Slot, StringComparison.Ordinal);
                string label =
                    $"{slot.Kind}/{slot.Name}  tick={slot.Tick}  map={slot.MapId}  {slot.Bytes}B  v{slot.SchemaVersion}  mod={slot.ModSetHashShort}  fp={slot.RegistryFingerprintShort}  {slot.CreatedUtc}";
                string capturedSlot = slot.Slot;
                rows.Add(
                    Ui.Button(label, _ => Run(r => eng => r.SelectSlot(capturedSlot)))
                        .Id($"save-panel-slot-{slot.Kind}-{slot.Name}")
                        .Height(34f)
                        .Background(selected ? "#1E3A2F" : "#0E1823")
                        .Border(1f, Color(selected ? "#8DE3AE" : "#284154")));
            }
        }

        return Ui.ScrollView(rows.ToArray()).Height(260f).Gap(6f);
    }

    private UiElementBuilder Button(string label, string id, Func<SavePanelRuntime, Action<GameEngine>> action)
        => Ui.Button(label, _ => Run(action)).Id($"save-panel-{id}").Height(36f);

    private void Run(Func<SavePanelRuntime, Action<GameEngine>> action)
    {
        if (_engine == null) return;
        action(_runtime)(_engine);
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++)
        {
            children.Add(Ui.Text(lines[i]).FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Column(children.ToArray()).Width(468f).Padding(10f).Gap(5f).Background("#0E1823").Border(1f, Color("#284154"));
    }

    private static UiColor Color(string hex)
        => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
