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
    private ReactivePage<SaveLoadShowcasePanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public SaveLoadShowcasePanelController(SaveLoadShowcaseRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        SaveLoadShowcasePanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer)!;
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider)!;
            _page = new ReactivePage<SaveLoadShowcasePanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("Showcase.SaveLoad.Hud", UiSurfaceSegment.Overlay, priority: 60), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
            host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<SaveLoadShowcasePanelState> context)
    {
        SaveLoadShowcasePanelState s = context.State;
        int step = Math.Clamp(s.StepIndex, 0, 4);
        var kids = new List<UiElementBuilder>
        {
            Ui.Text(s.Header).FontSize(24f).Bold().Color("#F5F7FA"),
            Ui.Text(s.Hook).FontSize(14f).Bold().Color("#FFD27A").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"当前步骤：{s.StepGuide}").FontSize(16f).Bold().Color("#8DE3AE"),
            StepRow(step),
            Ui.Text($"巡逻兵现在 {s.PatrolNow} · 已挪 {s.MoveCount} 步").FontSize(13f).Color("#8AD7FF"),
            Ui.Text($"存档点 {s.SavedPoint}").FontSize(13f).Bold().Color(s.HasSavedPoint ? "#8DE3AE" : "#8899AA"),
            Ui.Text(s.Outcome).FontSize(13f).Bold().Color("#F0C36B").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Status).FontSize(13f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
        };
        if (!string.IsNullOrWhiteSpace(s.Error))
            kids.Add(Ui.Text($"注意：{s.Error}").FontSize(12f).Bold().Color("#FF7A7A").WhiteSpace(UiWhiteSpace.Normal));

        kids.Add(Ui.Text("挪巡逻兵").FontSize(12f).Bold().Color("#F5F7FA"));
        kids.Add(Ui.Row(
            Btn("←西", "west", r => r.Move(-SaveLoadShowcaseIds.MoveStepCm, 0)),
            Btn("↑北", "north", r => r.Move(0, SaveLoadShowcaseIds.MoveStepCm)),
            Btn("↓南", "south", r => r.Move(0, -SaveLoadShowcaseIds.MoveStepCm)),
            Btn("东→", "east", r => r.Move(SaveLoadShowcaseIds.MoveStepCm, 0))).Gap(6f).Wrap());

        kids.Add(Ui.Text("主循环").FontSize(12f).Bold().Color("#F5F7FA"));
        kids.Add(Ui.Row(
            Btn("存这一档", "save", r => r.QuickSave()),
            Btn("读档回来", "load", r => r.QuickLoad())).Gap(8f).Wrap());

        kids.Add(Ui.Text("对照与故障").FontSize(12f).Bold().Color("#F5F7FA"));
        kids.Add(Ui.Row(
            Btn("无存档重置", "reset", r => r.AblateReset()),
            Btn("有存档恢复", "restore", r => r.AblateRestore())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            Btn("弄坏这一档", "tamper", r => r.TamperSelectedSlot()),
            Btn(s.ExcludeScout ? "排除：开" : "排除：关", "exclude", r => r.ToggleExclude())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            Btn("冷启动故事", "cold", r => r.ColdStartStory()),
            Btn("保留-1", "ret-down", r => r.AdjustRetention(-1)),
            Btn("保留+1", "ret-up", r => r.AdjustRetention(1))).Gap(6f).Wrap());

        kids.Add(Ui.Text($"消融：{s.Ablation} · 自动档保留 {s.AutosaveRetention}").FontSize(11f).Color("#C7D0DD"));
        kids.Add(Ui.Text($"落盘：{s.StorageRoot}").FontSize(10f).Color("#8899AA").WhiteSpace(UiWhiteSpace.Normal));
        kids.Add(Ui.Text("图例：青圈=巡逻兵现在 · 绿幽灵=存档点 · 灰圈=不进档的临时侦察").FontSize(10f).Color("#A8B4C4").WhiteSpace(UiWhiteSpace.Normal));
        kids.Add(Ui.Text(s.Controls).FontSize(10f).Color("#6F8499").WhiteSpace(UiWhiteSpace.Normal));
        kids.Add(Scroll("刚才发生了什么", s.LogLines));

        return Ui.Column(
                Ui.Column(kids.ToArray()).Width(520f).Height(680f).Padding(16f).Gap(7f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(16f).Align(UiAlignItems.Start).ZIndex(60);
    }

    private static UiElementBuilder StepRow(int step)
    {
        string[] labels = { "①挪", "②存", "③再挪", "④读", "✓" };
        var cells = new List<UiElementBuilder>(labels.Length);
        for (int i = 0; i < labels.Length; i++)
        {
            bool on = i == Math.Min(step, labels.Length - 1);
            cells.Add(Ui.Text(labels[i]).FontSize(12f).Bold().Color(on ? "#8DE3AE" : "#5A6B7C"));
        }

        return Ui.Row(cells.ToArray()).Gap(10f);
    }

    private UiElementBuilder Btn(string label, string id, Action<SaveLoadShowcaseRuntime> action)
        => Ui.Button(label, _ => { if (_engine != null) action(_runtime); }).Id($"save-load-{id}").Height(36f);

    private static UiElementBuilder Scroll(string title, IReadOnlyList<string> lines)
    {
        var c = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color("#FFB38A") };
        foreach (string line in lines)
            c.Add(Ui.Text(line).FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.ScrollView(c.ToArray()).Height(110f).Gap(3f);
    }

    private static UiColor Color(string hex)
        => UiColor.TryParse(hex, out UiColor c) ? c : throw new InvalidOperationException(hex);
}
