using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace CrowdSimulationS4DeployMod.Runtime;

/// <summary>
/// S4 部署演示交互运行时(F03-b):演示态(视图/建造模式/模板选择)与现场指令
/// (placeStructure/removeStructureAt)全部经输入队列在仿真 tick 边界消费——呈现线程只入队。
/// 首条现场指令进入会话后停用本次激活的自动回放(回放是对拍自证,现场输入后不再成立),
/// 停用走 CrowdSimulationRuntime.SuppressAutoReplay 正式面,回放语义本身不动。
/// </summary>
public sealed class S4DeployDemoRuntime
{
    /// <summary>建造模板(1/2/3):尺寸与演示脚本同值(building 140m / 路障 70m / 道路 30m 宽×250m 长,
    /// 道路自光标向 +X 铺 250m——单击建造的演示口径,脚本里的道路同为 X 向条带)。</summary>
    public sealed record StructureChoice(string Id, string Label, int SizeCm, int RoadLengthCm);

    public static readonly StructureChoice[] Templates =
    {
        new("building", "建筑 rect 140m", 14000, 0),
        new("s7barrier", "路障 rect 70m(4s 寿命)", 7000, 0),
        new("road", "道路 path 30m×250m(+X)", 3000, 25000),
    };

    public static readonly string[] ViewModeLabels = { "路线(流场)", "可走区域", "NavMesh+HPA" };

    private readonly GameEngine _engine;
    private readonly object _inputGate = new();
    private readonly Queue<Action> _inputQueue = new();
    private CrowdSimSession? _boundSession;

    /// <summary>视图模式:0 路线(主力组流场贴花+目标标记) · 1 可走区域 · 2 NavMesh+HPA 线框。</summary>
    public int ViewMode { get; private set; }
    public bool BuildMode { get; private set; }
    public int TemplateIndex { get; private set; }
    /// <summary>现场指令已进入本次会话(自动回放停用;会话重激活时随运行时一起复位)。</summary>
    public bool InteractiveMode { get; private set; }
    public StructureChoice Template => Templates[TemplateIndex];

    public S4DeployDemoRuntime(GameEngine engine) => _engine = engine;

    // ── 呈现线程入口(只入队) ─────────────────────────────
    public void EnqueueCycleView() => Enqueue(() => ViewMode = (ViewMode + 1) % ViewModeLabels.Length);
    public void EnqueueToggleBuild() => Enqueue(() => BuildMode = !BuildMode);
    public void EnqueueSelectTemplate(int index) => Enqueue(() => TemplateIndex = index);
    public void EnqueuePlace(int xCm, int yCm) => Enqueue(() =>
    {
        var tpl = Template;
        var cmd = new JsonObject
        {
            ["type"] = "placeStructure",
            ["template"] = tpl.Id,
            ["xCm"] = xCm,
            ["yCm"] = yCm,
            ["sizeCm"] = tpl.SizeCm,
        };
        if (tpl.RoadLengthCm > 0)
        {
            cmd["toXCm"] = xCm + tpl.RoadLengthCm;
            cmd["toYCm"] = yCm;
        }

        SubmitInteractive(cmd);
    });
    public void EnqueueRemove(int xCm, int yCm) => Enqueue(() =>
        SubmitInteractive(new JsonObject { ["type"] = "removeStructureAt", ["xCm"] = xCm, ["yCm"] = yCm }));

    private void Enqueue(Action action)
    {
        lock (_inputGate) _inputQueue.Enqueue(action);
    }

    /// <summary>仿真 tick 边界消费(PostMovement 组,先于 Cleanup 组的会话步进)。</summary>
    public void Tick()
    {
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

    private void SubmitInteractive(JsonObject cmd)
    {
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

/// <summary>输入队列消费系统(固定 tick,PostMovement 组:现场指令在本 tick 的会话步进前落地)。</summary>
public sealed class S4DeployDemoInteractionSystem : Arch.System.ISystem<float>
{
    private readonly S4DeployDemoRuntime _runtime;

    public S4DeployDemoInteractionSystem(S4DeployDemoRuntime runtime) => _runtime = runtime;

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void Update(in float dt) => _runtime.Tick();
    public void AfterUpdate(in float t) { }
    public void Dispose() { }
}
