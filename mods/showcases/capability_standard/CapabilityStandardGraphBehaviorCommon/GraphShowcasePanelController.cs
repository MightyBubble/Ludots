using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CapabilityStandardGraphBehaviorCommon;

public sealed class GraphShowcasePanelController
{
    private readonly Func<GraphShowcaseControlState> _readState;
    private readonly Action _togglePaused;
    private readonly Action _step;
    private readonly Action _toggleL2;
    private readonly Action _toggleStimulus;
    private readonly Action _increaseSight;
    private readonly Action _decreaseSight;
    private readonly Action _increaseThinkPeriod;
    private readonly Action _decreaseThinkPeriod;
    private readonly Action _reset;
    private ReactivePage<GraphShowcaseControlState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public GraphShowcasePanelController(
        Func<GraphShowcaseControlState> readState,
        Action togglePaused,
        Action step,
        Action toggleL2,
        Action toggleStimulus,
        Action increaseSight,
        Action decreaseSight,
        Action increaseThinkPeriod,
        Action decreaseThinkPeriod,
        Action reset)
    {
        _readState = readState;
        _togglePaused = togglePaused;
        _step = step;
        _toggleL2 = toggleL2;
        _toggleStimulus = toggleStimulus;
        _increaseSight = increaseSight;
        _decreaseSight = decreaseSight;
        _increaseThinkPeriod = increaseThinkPeriod;
        _decreaseThinkPeriod = decreaseThinkPeriod;
        _reset = reset;
    }

    public void MountOrRefresh(GameEngine engine)
    {
        var host = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Graph behavior showcase requires UiSurfaceHost.");
        _ = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Graph behavior showcase requires UIRoot.");

        _engine = engine;
        GraphShowcaseControlState state = _readState();
        if (_page == null)
        {
            var text = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer
                ?? throw new InvalidOperationException("Graph behavior showcase requires UiTextMeasurer.");
            var images = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider
                ?? throw new InvalidOperationException("Graph behavior showcase requires UiImageSizeProvider.");
            _page = new ReactivePage<GraphShowcaseControlState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        host.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.GraphBehavior.Panel", UiSurfaceSegment.Overlay, priority: 48),
            _page);
    }

    public void Clear()
    {
        if (_lease.IsValid && _engine?.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
        {
            host.ReleaseLease(ref _lease);
        }

        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<GraphShowcaseControlState> context)
    {
        GraphShowcaseControlState state = context.State;
        return Ui.Column(
                Ui.Text(state.Title).FontSize(21f).Bold().Color("#F5F7FA"),
                Ui.Text(state.Summary).FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("按按钮改变场景；颜色和连线显示当前决策。").FontSize(11f).Color("#8AD7FF"),
                Ui.Row(
                    Button(state.Paused ? "继续" : "暂停", "pause", _togglePaused),
                    Button("单步", "step", _step),
                    Button(state.L2Enabled ? "L2 开" : "L2 关", "l2", _toggleL2),
                    Button(state.StimulusEnabled ? "入侵者开" : "入侵者关", "stimulus", _toggleStimulus),
                    Button("重置", "reset", _reset)).Wrap().Gap(6f),
                Ui.Text($"状态：{state.Status}").FontSize(12f).Bold().Color("#F4C95D").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"L2：{(state.L2Enabled ? state.L2Description : "已关闭决策，同场保留不响应的对照")}").FontSize(11f).Color(state.L2Enabled ? "#8DE3AE" : "#FFB38A").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"刺激：{(state.StimulusEnabled ? "入侵者会进入场景" : "入侵者已移除")}").FontSize(11f).Color("#C7D0DD"),
                Ui.Text($"守卫 {state.AgentCount} | 视野 {state.SightRadius:0.0}m | 思考间隔 {state.ThinkPeriod:0.00}s").FontSize(11f).Color("#C7D0DD"),
                Ui.Row(
                    Button("视野 -", "sight-down", _decreaseSight),
                    Button("视野 +", "sight-up", _increaseSight),
                    Button("思考慢", "period-up", _increaseThinkPeriod),
                    Button("思考快", "period-down", _decreaseThinkPeriod)).Wrap().Gap(6f),
                Section("运行时读数", new[] { state.Detail, "绿色=巡逻；黄色=发现；红色=攻击/战斗；灰点=万人无图压测基线。" }, "#8DE3AE"))
            .Width(490f).Padding(14f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E"))
            .Absolute(16f, 16f).ZIndex(48);
    }

    private static UiElementBuilder Button(string label, string id, Action action)
        => Ui.Button(label, _ => action()).Id($"graph-showcase-{id}").Height(30f);

    private static UiElementBuilder Section(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++) children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(460f).Padding(9f).Gap(5f).Background("#0E1823");
    }

    private static UiColor Color(string hex)
        => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
