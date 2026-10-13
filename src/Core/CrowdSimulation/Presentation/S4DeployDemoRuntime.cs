using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S4 部署演示交互运行时:演示态(视图/建造模式/模板选择)与现场指令
/// (placeStructure/removeStructureAt)经输入队列在仿真 tick 边界消费。固定步输入系统入队并在会话步进前调用 Tick。
/// 首条现场指令进入会话后停用本次激活的自动回放(回放是对拍自证,现场输入后不再成立),
/// 停用走 CrowdSimulationRuntime.SuppressAutoReplay 正式面,回放语义本身不动。
/// </summary>
public sealed class S4DeployDemoRuntime
{
    public const string ShapePoint = "point";
    public const string ShapeRoad = "road";
    public const string ShapeDisc = "disc";
    public const string ShapeSpan = "span";

    /// <summary>建造模板。shape=road 时道路自光标向 +X 铺 RoadLengthCm;point 的 RoadLengthCm 恒为 0,形状以 Shape 为准。</summary>
    public sealed record StructureChoice(string Id, string Label, string Shape, int SizeCm, int RoadLengthCm, string SelectAction)
    {
        public bool IsRoad => string.Equals(Shape, ShapeRoad, StringComparison.Ordinal);
    }

    private readonly GameEngine? _engine;
    private readonly object _inputGate = new();
    private readonly Queue<Action> _inputQueue = new();
    private CrowdSimSession? _boundSession;

    /// <summary>本帧光标地面厘米。由输入系统写入，呈现只读。</summary>
    public (double X, double Y)? HoverCm { get; set; }

    /// <summary>视图模式:0 路线(主力组流场贴花+目标标记) · 1 可走区域 · 2 NavMesh+HPA 线框。</summary>
    public bool Active { get; set; } = true;
    public int ViewMode { get; private set; }
    public bool BuildMode { get; private set; }
    public int TemplateIndex { get; private set; }
    /// <summary>现场指令已进入本次会话(自动回放停用;会话重激活时随运行时一起复位)。</summary>
    public bool InteractiveMode { get; private set; }

    public void MarkInteractive(CrowdSimSession? session)
    {
        if (session == null) return;
        if (!ReferenceEquals(_boundSession, session))
        {
            _boundSession = session;
            InteractiveMode = false;
        }

        InteractiveMode = true;
    }
    private StructureChoice[] _choices = Array.Empty<StructureChoice>();
    private string[]? _viewLabels;
    private int _viewModeCount = 3;
    public StructureChoice Template => _choices[TemplateIndex];
    public int StructureCount => _choices.Length;
    public string SelectActionAt(int index) => _choices[index].SelectAction;
    public string ViewLabel => (_viewLabels ?? throw new InvalidOperationException("部署演示视图名未配置。"))[ViewMode];

    public void UseViewLabels(string[] labels)
    {
        if (labels == null || labels.Length == 0)
            throw new InvalidOperationException("部署演示视图名至少一条。");
        _viewLabels = labels;
        _viewModeCount = labels.Length;
    }

    public void UseStructures(StructureChoice[] choices)
    {
        if (choices == null || choices.Length == 0)
            throw new InvalidOperationException("部署演示至少一条结构模板。");
        _choices = choices;
    }

    public S4DeployDemoRuntime(GameEngine? engine) => _engine = engine;

    /// <summary>遥测区块展开与区块调试面板同一可见开关;无引擎(无头)时保持折叠。</summary>
    public bool TelemetryExpanded =>
        _engine?.GetService(CoreServiceKeys.ChunkDebugPanelRuntime) is ChunkDebugPanelRuntime panel && panel.Visible;

    // 固定步入口只入队。
    public void EnqueueCycleView() => Enqueue(() => ViewMode = (ViewMode + 1) % _viewModeCount);
    public void EnqueueToggleBuild() => Enqueue(() => BuildMode = !BuildMode);
    public void EnqueueSelectTemplate(int index) => Enqueue(() => TemplateIndex = index);
    public void EnqueuePlace(int xCm, int yCm) => Enqueue(() =>
    {
        var tpl = Template;
        bool road = tpl.IsRoad;
        SubmitInteractive(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = tpl.Id,
            XCm = xCm,
            YCm = yCm,
            SizeCm = tpl.SizeCm,
            ToXCm = road ? xCm + tpl.RoadLengthCm : 0,
            ToYCm = road ? yCm : 0,
        });
    });
    public void EnqueueRemove(int xCm, int yCm) => Enqueue(() =>
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.RemoveStructureAt, XCm = xCm, YCm = yCm }));

    private void Enqueue(Action action)
    {
        lock (_inputGate) _inputQueue.Enqueue(action);
    }

    /// <summary>固定步消费队列。S4DeployInputSystem 在 PostMovement、会话步进之前调用。</summary>
    public void Tick()
    {
        if (!Active) return;
        for (; ; )
        {
            Action? action = null;
            lock (_inputGate)
            {
                if (_inputQueue.Count > 0) action = _inputQueue.Dequeue();
            }

            if (action == null) break;
            action();
        }
    }

    private void SubmitInteractive(CrowdSimCommand cmd)
    {
        if (_engine == null) return;
        var runtime = _engine.GetService(CoreServiceKeys.CrowdSimulationRuntime);
        var session = _engine.GetService(CoreServiceKeys.CrowdSimulationSession);
        if (runtime == null || session == null) return; // 会话未激活/已卸载:丢弃过期点击

        if (!ReferenceEquals(_boundSession, session))
        {
            _boundSession = session;
            InteractiveMode = false;
        }

        if (!InteractiveMode)
        {
            InteractiveMode = true;
            runtime.SuppressAutoReplay();
        }

        session.Commands.Submit<object?>(session, cmd, CrowdSimCommands.Exec);
    }
}
