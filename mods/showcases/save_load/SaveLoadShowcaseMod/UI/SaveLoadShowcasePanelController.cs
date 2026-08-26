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
        var kids = new List<UiElementBuilder>
        {
            Ui.Text(s.Header).FontSize(22f).Bold().Color("#F5F7FA"),
            Ui.Text(s.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Controls).FontSize(10f).Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"消融模式：{s.Ablation}").FontSize(12f).Bold().Color("#F0C36B"),
            Ui.Text($"落盘根：{s.StorageRoot}").FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"排除临时单位：{(s.ExcludeEphemeral ? "开" : "关")} · autosave 保留 {s.AutosaveRetention}").FontSize(11f).Color("#C7D0DD"),
            Ui.Text($"对比 digest 前 {s.BeforeDigest} / 后 {s.AfterDigest} · 实体 {s.BeforeEntityCount}→{s.AfterEntityCount}").FontSize(11f).Color("#8DE3AE").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Status).FontSize(13f).Bold().Color("#8DE3AE").WhiteSpace(UiWhiteSpace.Normal),
        };
        if (!string.IsNullOrWhiteSpace(s.Error))
            kids.Add(Ui.Text($"错误：{s.Error}").FontSize(12f).Bold().Color("#FF7A7A").WhiteSpace(UiWhiteSpace.Normal));

        kids.Add(Ui.Row(
            Btn("推进世界", "nudge", r => r.NudgeWorld()),
            Btn("无存档重置", "reset", r => r.AblateReset()),
            Btn("有存档恢复", "restore", r => r.AblateRestore())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            Btn("篡改槽位", "tamper", r => r.TamperSelectedSlot()),
            Btn("排除开关", "exclude", r => r.ToggleExclude()),
            Btn("冷启动故事", "cold", r => r.ColdStartStory())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            Btn("保留-1", "ret-down", r => r.AdjustRetention(-1)),
            Btn("保留+1", "ret-up", r => r.AdjustRetention(1))).Gap(6f).Wrap());
        kids.Add(Section("读档对比（绿=回来 / 灰=排除）", s.DiffLines, "#8DE3AE"));
        kids.Add(Section("轨迹", s.LogLines, "#FFB38A"));

        return Ui.Column(
                Ui.Column(kids.ToArray()).Width(480f).Height(620f).Padding(16f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(20f).Align(UiAlignItems.Start).ZIndex(60);
    }

    private UiElementBuilder Btn(string label, string id, Action<SaveLoadShowcaseRuntime> action)
        => Ui.Button(label, _ => { if (_engine != null) action(_runtime); }).Id($"save-load-{id}").Height(34f);

    private static UiElementBuilder Section(string title, IReadOnlyList<string> lines, string accent)
    {
        var c = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        foreach (string line in lines)
            c.Add(Ui.Text(line).FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.ScrollView(c.ToArray()).Height(140f).Gap(4f);
    }

    private static UiColor Color(string hex)
        => UiColor.TryParse(hex, out UiColor c) ? c : throw new InvalidOperationException(hex);
}
