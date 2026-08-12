using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

public sealed class AbilityGraphSandboxRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphFunctionCatalog? _catalog;
    private float _accum;
    private int _castWave;
    private float[] _tx = Array.Empty<float>();
    private float[] _ty = Array.Empty<float>();
    private byte[] _flash = Array.Empty<byte>();
    private int _lastHit = -1;
    private string _lastSpell = string.Empty;
    private int _nearbyCount;
    private int _effectApplications;
    private int _relationshipScore;
    private string _statusToken = "无";

    public float CasterX => 0f;
    public float CasterY => 0f;
    public float[] TargetX => _tx;
    public float[] TargetY => _ty;
    public byte[] Flash => _flash;
    public int TargetCount => _tx.Length;
    public int LastHit => _lastHit;
    public string LastSpell => _lastSpell;
    public int NearbyCount => _nearbyCount;
    public int EffectApplications => _effectApplications;
    public int RelationshipScore => _relationshipScore;
    public string StatusToken => _statusToken;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_ability_graph_sandbox" };

    public void Bind(GraphProgramRegistry programs, GraphFunctionCatalog catalog)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void EnsureWorld()
    {
        if (_tx.Length > 0) return;
        if (_programs == null || _catalog == null)
        {
            throw new InvalidOperationException("AbilityGraphSandboxRuntime.Bind(Registry, Catalog) required before EnsureWorld.");
        }

        // Fail-closed: Func Lib entries must exist in engine-loaded catalog.
        _ = _catalog.Require("ability.slash");
        _ = _catalog.Require("ability.bash");

        int targets = Math.Min(_config.FeaturedAgentCount, 8);
        _tx = new float[targets];
        _ty = new float[targets];
        _flash = new byte[targets];
        for (int i = 0; i < targets; i++)
        {
            float t = targets <= 1 ? 0.5f : i / (float)(targets - 1);
            float ang = -0.7f + t * 1.4f;
            _tx[i] = MathF.Sin(ang) * 6f;
            _ty[i] = 4.5f + MathF.Cos(ang) * 0.8f;
        }

        Metrics.AgentCount = targets;
        Metrics.Detail = "巡逻队就位：查一圈找目标，命中后挂状态、加好感，并读状态牌。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        for (int i = 0; i < _flash.Length; i++)
        {
            if (_flash[i] > 0) _flash[i]--;
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _castWave++;

        _lastSpell = (_castWave & 1) == 0 ? "ability.slash" : "ability.bash";
        GraphFunctionEntry fn = _catalog!.Require(_lastSpell);
        if (!_programs!.TryGetProgram(fn.GraphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"FuncLib '{_lastSpell}' graph id {fn.GraphId} missing from Registry.");
        }

        _lastHit = _castWave % _tx.Length;
        _nearbyCount = CountTargetsInRadius(7.0f);
        _effectApplications += _nearbyCount;
        _relationshipScore += 3;
        _statusToken = (_castWave & 1) == 0 ? "鼓舞" : "标记";

        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

        var sw = Stopwatch.StartNew();
        var cursor = new GraphExecutionCursor();
        var state = new GraphExecutionState
        {
            I = ints,
            B = bools,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running
        };
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, 32);
        if (!result.Halted) throw new InvalidOperationException("Ability scripts must halt.");
        _flash[_lastHit] = 12;

        int crowd = Math.Min(_config.CrowdBandCount, 2000);
        for (int i = 0; i < crowd; i++)
        {
            cursor.Reset();
            state.Status = GraphExecutionStatus.Running;
            GasGraphOpHandlerTable.ExecuteSlice(
                ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, 16);
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail = $"巡逻查一圈：{_nearbyCount}个目标；对{_lastHit + 1}号挂状态「{_statusToken}」；" +
            $"加好感+3={_relationshipScore}；状态牌读到「{_statusToken}」；" +
            $"施放{SpellDisplayName(_lastSpell)}；耗时{Metrics.LastThinkMs:F3}ms";
    }

    private int CountTargetsInRadius(float radius)
    {
        float radiusSq = radius * radius;
        int count = 0;
        for (int i = 0; i < _tx.Length; i++)
        {
            float dx = _tx[i] - CasterX;
            float dy = _ty[i] - CasterY;
            if (dx * dx + dy * dy <= radiusSq)
            {
                count++;
            }
        }

        return count;
    }

    private static string SpellDisplayName(string spell)
        => spell switch
        {
            "ability.slash" => "挥砍",
            "ability.bash" => "盾击",
            _ => "技能"
        };
}
