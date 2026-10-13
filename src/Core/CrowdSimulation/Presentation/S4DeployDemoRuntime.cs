using System;
using System.Collections.Generic;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Presentation;

public enum CrowdClickTool : byte
{
    Off,
    Boom,
    Spawn,
    Structure,
    Demolish,
}

/// <summary>
/// S4 部署演示交互运行时。视图、点击工具和播放态在仿真 tick 边界消费。
    /// 爆炸、生成、放置、拆除、部署、散开、满容量、清缓存、避让频率和重烘切片数只提交群体指令。
/// 暂停只挡住 tick 推进。倍速是 Simulation 域的时间缩放令牌。
/// 首条现场指令进入会话后停用本次激活的自动回放。
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
        public bool IsDisc => string.Equals(Shape, ShapeDisc, StringComparison.Ordinal);
        public bool IsSpan => string.Equals(Shape, ShapeSpan, StringComparison.Ordinal);
    }

    private const string PlaybackOwner = "crowd-simulation.deploy";

    private readonly GameEngine? _engine;
    private readonly object _inputGate = new();
    private readonly Queue<Action> _inputQueue = new();
    private CrowdSimSession? _boundSession;
    private CrowdDebugTools? _tools;
    private CrowdDebugRates? _rates;
    private Fix64 _impulse;
    private Fix64 _avoidMin;
    private Fix64 _avoidMax;
    private Fix64 _avoidStep;
    private int _speedIndex;
    private TimeFlowToken _speedToken;

    /// <summary>本帧光标地面厘米。由输入系统写入，呈现只读。</summary>
    public (double X, double Y)? HoverCm { get; set; }

    /// <summary>视图模式:0 路线(主力组流场贴花+目标标记) · 1 可走区域 · 2 NavMesh+HPA 线框。</summary>
    public bool Active { get; set; } = true;
    public int ViewMode { get; private set; }
    public CrowdClickTool Tool { get; private set; }
    public bool BuildMode => Tool == CrowdClickTool.Structure;
    public int TemplateIndex { get; private set; }
    public bool PlaybackHeld { get; private set; }
    public int SpeedPermille { get; private set; } = TimeFlowService.DefaultScalePermille;
    public int BoomRadiusCm { get; private set; }
    public Fix64 BlastRingScale { get; private set; }
    public string TickHzWindow { get; private set; } = string.Empty;
    public bool HasSpanAnchor { get; private set; }
    public int SpanAnchorX { get; private set; }
    public int SpanAnchorY { get; private set; }
    public string ToolLabel => Tool switch
    {
        CrowdClickTool.Off => "关闭",
        CrowdClickTool.Boom => "爆炸",
        CrowdClickTool.Spawn => "生成",
        CrowdClickTool.Structure => "建筑",
        CrowdClickTool.Demolish => "拆除",
        _ => throw new InvalidOperationException($"未知点击工具 {Tool}。"),
    };
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
        ApplyPlaceTemplate();
    }

    internal void UseDebugTools(CrowdDebugTools tools, CrowdDebugRates rates)
    {
        if (tools == null) throw new ArgumentNullException(nameof(tools));
        if (rates == null) throw new ArgumentNullException(nameof(rates));
        _tools = tools;
        _rates = rates;
        BoomRadiusCm = tools.BoomRadiusCm;
        BlastRingScale = Fix64.FromDouble(tools.RingScale);
        _impulse = Fix64.FromDouble(tools.Impulse);
        _avoidMin = Fix64.FromDouble(rates.AvoidHzMin);
        _avoidMax = Fix64.FromDouble(rates.AvoidHzMax);
        _avoidStep = Fix64.FromDouble(rates.AvoidStep);
        TickHzWindow = rates.TickWindowText;
        _speedIndex = IndexOfSpeed(tools.SpeedStepsPermille, TimeFlowService.DefaultScalePermille);
        SpeedPermille = tools.SpeedStepsPermille[_speedIndex];
        ApplyPlaceTemplate();
    }

    public void ArmSession(CrowdSimSession session)
    {
        if (session == null) return;
        if (!ReferenceEquals(_boundSession, session))
        {
            _boundSession = session;
            InteractiveMode = false;
            ClearSpan();
        }

        session.PlaybackHeld = PlaybackHeld;
    }

    public void ReleasePlayback()
    {
        if (_engine == null || !_speedToken.IsValid) return;
        var flow = _engine.GetService(CoreServiceKeys.TimeFlow);
        if (flow == null) return;
        flow.ReleaseToken(_speedToken);
        _speedToken = default;
    }

    public S4DeployDemoRuntime(GameEngine? engine) => _engine = engine;

    /// <summary>遥测区块展开与区块调试面板同一可见开关;无引擎(无头)时保持折叠。</summary>
    public bool TelemetryExpanded =>
        _engine?.GetService(CoreServiceKeys.ChunkDebugPanelRuntime) is ChunkDebugPanelRuntime panel && panel.Visible;

    // 固定步入口只入队。
    public void EnqueueCycleView() => Enqueue(() => ViewMode = (ViewMode + 1) % _viewModeCount);
    public void EnqueueToggleBuild() => Enqueue(() =>
    {
        Tool = Tool == CrowdClickTool.Structure ? CrowdClickTool.Off : CrowdClickTool.Structure;
        if (Tool != CrowdClickTool.Structure) ClearSpan();
    });
    public void EnqueueSelectTemplate(int index) => Enqueue(() => TemplateIndex = index);
    public void EnqueueTool(CrowdClickTool tool) => Enqueue(() =>
    {
        Tool = tool;
        if (tool != CrowdClickTool.Structure) ClearSpan();
    });
    public void EnqueuePlace(int xCm, int yCm) => Enqueue(() => PlaceAt(xCm, yCm));
    public void EnqueueRemove(int xCm, int yCm) => Enqueue(() =>
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.RemoveStructureAt, XCm = xCm, YCm = yCm }));
    public void EnqueueDeploy() => Enqueue(() =>
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = RequireTools().SpawnCount }));
    public void EnqueueScatter() => Enqueue(() =>
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.Scatter }));
    public void EnqueueStress() => Enqueue(() =>
    {
        if (!TrySession(out CrowdSimSession session)) return;
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = session.Config.Sim.MaxUnits });
    });
    public void EnqueueClearCaches() => Enqueue(() =>
    {
        if (!TrySession(out CrowdSimSession session) || session.Planner == null) return;
        SubmitInteractive(new CrowdSimCommand { Kind = CrowdSimCommandKind.ClearCaches });
    });
    public void EnqueueAvoid(int direction) => Enqueue(() => StepAvoid(direction));
    public void EnqueueSlices(int direction) => Enqueue(() => StepSlices(direction));
    public void EnqueuePause() => Enqueue(() =>
    {
        PlaybackHeld = !PlaybackHeld;
        if (TrySession(out CrowdSimSession session)) session.PlaybackHeld = PlaybackHeld;
    });
    public void EnqueueSpeed(int direction) => Enqueue(() => StepSpeed(direction));
    public void EnqueueMapClick(int xCm, int yCm) => Enqueue(() =>
    {
        switch (Tool)
        {
            case CrowdClickTool.Off:
                return;
            case CrowdClickTool.Boom:
                CrowdDebugTools tools = RequireTools();
                SubmitInteractive(new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.Kill,
                    XCm = xCm,
                    YCm = yCm,
                    RadiusCm = tools.BoomRadiusCm,
                    RingScale = BlastRingScale,
                    Impulse = _impulse,
                });
                return;
            case CrowdClickTool.Spawn:
                SubmitSpawnAt(xCm, yCm);
                return;
            case CrowdClickTool.Structure:
                PlaceAt(xCm, yCm);
                return;
            case CrowdClickTool.Demolish:
                SubmitInteractive(new CrowdSimCommand
                {
                    Kind = CrowdSimCommandKind.RemoveStructureAt,
                    XCm = xCm,
                    YCm = yCm,
                });
                return;
            default:
                throw new InvalidOperationException($"未知点击工具 {Tool}。");
        }
    });

    private void PlaceAt(int xCm, int yCm)
    {
        StructureChoice tpl = Template;
        if (tpl.IsSpan)
        {
            if (!HasSpanAnchor)
            {
                HasSpanAnchor = true;
                SpanAnchorX = xCm;
                SpanAnchorY = yCm;
                return;
            }

            int x0 = SpanAnchorX;
            int y0 = SpanAnchorY;
            ClearSpan();
            SubmitInteractive(new CrowdSimCommand
            {
                Kind = CrowdSimCommandKind.PlaceStructure,
                TemplateId = tpl.Id,
                XCm = x0,
                YCm = y0,
                SizeCm = tpl.SizeCm,
                ToXCm = xCm,
                ToYCm = yCm,
            });
            return;
        }

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
    }

    private void SubmitSpawnAt(int xCm, int yCm)
    {
        CrowdDebugTools tools = RequireTools();
        SubmitInteractive(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = RequireSeatPlayer(),
            XCm = xCm,
            YCm = yCm,
            Count = tools.SpawnCount,
            UnitType = tools.SpawnUnitType,
            RIdx = tools.SpawnRadiusClass,
        });
    }

    private void StepAvoid(int direction)
    {
        if (direction == 0) return;
        if (_rates == null) throw new InvalidOperationException("部署演示未装载 rates。");
        if (!TrySession(out CrowdSimSession session) || session.Movement == null) return;
        Fix64 next = session.AvoidHz + (direction > 0 ? _avoidStep : -_avoidStep);
        Fix64 cap = Fix64.FromInt(session.Config.FixedHz * session.SubSteps);
        Fix64 max = _avoidMax < cap ? _avoidMax : cap;
        if (next <= Fix64.Zero || next < _avoidMin || next > max) return;
        SubmitInteractive(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Rates,
            TickHz = session.Config.FixedHz,
            AvoidHz = next,
        });
    }

    private void StepSlices(int direction)
    {
        if (direction == 0) return;
        if (!TrySession(out CrowdSimSession session)) return;
        int next = session.RebakeSliceCount + direction;
        if (next < 1 || next > CrowdSimulationConfigValidator.MaxRebakeSlices) return;
        SubmitInteractive(new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RebakeSlices,
            Slices = next,
        });
    }

    private void StepSpeed(int direction)
    {
        if (direction == 0) return;
        CrowdDebugTools tools = RequireTools();
        int next = _speedIndex + direction;
        if ((uint)next >= (uint)tools.SpeedStepsPermille.Length) return;
        _speedIndex = next;
        ApplySpeed(tools.SpeedStepsPermille[next]);
    }

    private void ApplySpeed(int permille)
    {
        SpeedPermille = permille;
        if (_engine == null) return;
        var flow = _engine.GetService(CoreServiceKeys.TimeFlow);
        if (flow == null) return;
        if (_speedToken.IsValid)
        {
            flow.ReleaseToken(_speedToken);
            _speedToken = default;
        }

        if (permille == TimeFlowService.DefaultScalePermille) return;
        _speedToken = flow.AcquireScaleToken(
            TimeFlowDomainIds.Simulation, permille, PlaybackOwner, "playback");
    }

    private void ApplyPlaceTemplate()
    {
        if (_tools == null || _choices.Length == 0) return;
        for (int i = 0; i < _choices.Length; i++)
        {
            if (!string.Equals(_choices[i].Id, _tools.PlaceTemplate, StringComparison.Ordinal)) continue;
            TemplateIndex = i;
            return;
        }
    }

    private void ClearSpan() => HasSpanAnchor = false;

    private CrowdDebugTools RequireTools() =>
        _tools ?? throw new InvalidOperationException("部署演示未装载 tools。");

    private int RequireSeatPlayer()
    {
        if (_engine != null
            && _engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats)
            && seats != null
            && seats.TryGetSoleSeat(out ClientLocalSeat seat)
            && seat.PossessedPlayerId > 0)
        {
            return seat.PossessedPlayerId;
        }

        throw new InvalidOperationException("spawnAt: 没有本机座位玩家。");
    }

    private bool TrySession(out CrowdSimSession session)
    {
        session = null!;
        if (_engine == null) return false;
        CrowdSimSession? found = _engine.GetService(CoreServiceKeys.CrowdSimulationSession);
        if (found == null) return false;
        session = found;
        return true;
    }

    private static int IndexOfSpeed(int[] steps, int permille)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i] == permille) return i;
        }

        throw new InvalidOperationException($"tools.speedStepsPermille 必须包含 {permille}。");
    }

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
