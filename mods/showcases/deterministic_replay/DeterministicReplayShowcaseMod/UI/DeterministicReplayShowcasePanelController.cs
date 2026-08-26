using System;
using System.Collections.Generic;
using DeterministicReplayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace DeterministicReplayShowcaseMod.UI;

internal sealed class DeterministicReplayShowcasePanelController
{
    private readonly DeterministicReplayShowcaseRuntime _runtime;
    private ReactivePage<DeterministicReplayShowcasePanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public DeterministicReplayShowcasePanelController(DeterministicReplayShowcaseRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        var state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer)!;
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider)!;
            _page = new ReactivePage<DeterministicReplayShowcasePanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);

        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("Showcase.DeterministicReplay.Hud", UiSurfaceSegment.Overlay, priority: 60), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
            host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<DeterministicReplayShowcasePanelState> ctx)
    {
        var s = ctx.State;
        string compareColor = s.Compare.Contains("绿", StringComparison.Ordinal) ? "#8DE3AE" : (s.Compare.Contains("红", StringComparison.Ordinal) ? "#FF7A7A" : "#F0C36B");
        var kids = new List<UiElementBuilder>
        {
            Ui.Text(s.Header).FontSize(22f).Bold().Color("#F5F7FA"),
            Ui.Text(s.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Controls).FontSize(10f).Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"模式：{s.Mode} · 速度×{s.Speed} · 帧 {s.PlaybackIndex}/{s.TotalFrames} · tick {s.Tick}").FontSize(11f).Color("#C7D0DD"),
            Ui.Text($"资产：{s.ArchivePath}  schema={s.SchemaVersion}").FontSize(10f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"录制 digest {s.RecordingDigest}  |  回放 digest {s.PlaybackDigest}").FontSize(12f).Bold().Color("#8AD7FF"),
            Ui.Text($"对比：{s.Compare}").FontSize(13f).Bold().Color(compareColor),
            Ui.Text(s.Status).FontSize(12f).Color("#8DE3AE").WhiteSpace(UiWhiteSpace.Normal),
        };
        if (!string.IsNullOrWhiteSpace(s.Error))
            kids.Add(Ui.Text($"错误：{s.Error}").FontSize(12f).Bold().Color("#FF7A7A").WhiteSpace(UiWhiteSpace.Normal));

        kids.Add(Ui.Row(
            B("检查点", "cp", r => r.RequestCheckpoint()),
            B("录制", "rec", r => r.StartRecording()),
            B("停止", "stop", r => r.StopRecording()),
            B("播放", "play", r => r.Play())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            B("暂停", "pause", r => r.TogglePause()),
            B("逐帧", "step", r => r.Step()),
            B("重置", "reset", r => r.Reset()),
            B("调速", "speed", r => r.CycleSpeed())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            B("中途跳转", "mid", r => r.JumpMid()),
            B("注入输入", "inj", r => r.InjectDuringPlay()),
            B("快照消融", "ablate", r => r.ToggleSnapshotAblation())).Gap(6f).Wrap());
        kids.Add(Scroll("指纹滚动", s.HashRows, "#EE5EDC"));
        kids.Add(Scroll("轨迹", s.LogLines, "#FFB38A"));

        return Ui.Column(
                Ui.Column(kids.ToArray()).Width(520f).Height(640f).Padding(16f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, C("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(20f).Align(UiAlignItems.End).ZIndex(60);
    }

    private UiElementBuilder B(string label, string id, Action<DeterministicReplayShowcaseRuntime> a)
        => Ui.Button(label, _ => { if (_engine != null) a(_runtime); }).Id($"det-replay-{id}").Height(34f);

    private static UiElementBuilder Scroll(string title, IReadOnlyList<string> lines, string accent)
    {
        var c = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        foreach (var line in lines) c.Add(Ui.Text(line).FontSize(10f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.ScrollView(c.ToArray()).Height(120f).Gap(3f);
    }

    private static UiColor C(string hex) => UiColor.TryParse(hex, out var c) ? c : throw new InvalidOperationException(hex);
}
