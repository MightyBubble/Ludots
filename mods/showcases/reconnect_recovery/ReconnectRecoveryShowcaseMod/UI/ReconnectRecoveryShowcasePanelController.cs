using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using ReconnectRecoveryShowcaseMod.Runtime;

namespace ReconnectRecoveryShowcaseMod.UI;

internal sealed class ReconnectRecoveryShowcasePanelController
{
    private readonly ReconnectRecoveryShowcaseRuntime _runtime;
    private ReactivePage<ReconnectRecoveryShowcasePanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public ReconnectRecoveryShowcasePanelController(ReconnectRecoveryShowcaseRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        var state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer)!;
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider)!;
            _page = new ReactivePage<ReconnectRecoveryShowcasePanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);

        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("Showcase.ReconnectRecovery.Hud", UiSurfaceSegment.Overlay, priority: 60), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
            host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ReconnectRecoveryShowcasePanelState> ctx)
    {
        var s = ctx.State;
        var kids = new List<UiElementBuilder>
        {
            Ui.Text(s.Banner).FontSize(14f).Bold().Color("#FFB38A").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Header).FontSize(22f).Bold().Color("#F5F7FA"),
            Ui.Text(s.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Controls).FontSize(10f).Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"消融：{s.Ablation} · 连接：{(s.Disconnected ? "断线" : "在线")}").FontSize(12f).Bold().Color("#F0C36B"),
            Ui.Text(s.Timeline).FontSize(11f).Bold().Color("#FFB38A").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"权威 tick {s.AuthorityTick}  |  客户端 tick {s.ClientTick}  |  下一帧 seq {s.NextSequence}").FontSize(12f).Color("#8DE3AE"),
            Ui.Text($"恢复来源：{s.RecoverySource}").FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"最近故障：{s.LastFault}").FontSize(11f).Color("#EE5EDC").WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(s.Status).FontSize(12f).Color("#8DE3AE").WhiteSpace(UiWhiteSpace.Normal),
        };
        if (!string.IsNullOrWhiteSpace(s.Error))
            kids.Add(Ui.Text($"错误：{s.Error}").FontSize(12f).Bold().Color("#FF7A7A").WhiteSpace(UiWhiteSpace.Normal));

        kids.Add(Ui.Row(
            B("检查点", "cp", r => r.RequestCheckpoint()),
            B("断线", "dc", r => r.Disconnect()),
            B("推进权威", "adv", r => r.AdvanceAuthority())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            B("权威恢复", "auth", r => r.ReconnectAuthority()),
            B("本地重置", "reset", r => r.ReconnectLocalReset())).Gap(6f).Wrap());
        kids.Add(Ui.Row(
            B("缺帧", "miss", r => r.InjectMissing()),
            B("重复", "dup", r => r.InjectDuplicate()),
            B("过期", "stale", r => r.InjectStale()),
            B("乱序", "ooo", r => r.InjectOutOfOrder())).Gap(6f).Wrap());
        kids.Add(Scroll("轨迹", s.LogLines, "#FFB38A"));

        return Ui.Column(
                Ui.Column(kids.ToArray()).Width(520f).Height(600f).Padding(16f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, C("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(20f).Align(UiAlignItems.End).ZIndex(60);
    }

    private UiElementBuilder B(string label, string id, Action<ReconnectRecoveryShowcaseRuntime> a)
        => Ui.Button(label, _ => { if (_engine != null) a(_runtime); }).Id($"reconnect-{id}").Height(34f);

    private static UiElementBuilder Scroll(string title, IReadOnlyList<string> lines, string accent)
    {
        var c = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        foreach (var line in lines) c.Add(Ui.Text(line).FontSize(10f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.ScrollView(c.ToArray()).Height(120f).Gap(3f);
    }

    private static UiColor C(string hex) => UiColor.TryParse(hex, out var c) ? c : throw new InvalidOperationException(hex);
}
