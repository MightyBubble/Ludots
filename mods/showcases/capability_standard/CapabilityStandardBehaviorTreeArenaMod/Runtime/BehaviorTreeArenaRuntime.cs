using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardBehaviorTreeArenaMod.Runtime;

public sealed class BehaviorTreeArenaRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private BehaviorTreeWorld? _world;
    private float _accum;
    private float _time;
    private float[] _posX = Array.Empty<float>();
    private float[] _posY = Array.Empty<float>();
    private float[] _heading = Array.Empty<float>();
    private float[] _orbitRadius = Array.Empty<float>();
    private float[] _orbitPhase = Array.Empty<float>();

    public BehaviorTreeWorld? World => _world;
    public float[] PosX => _posX;
    public float[] PosY => _posY;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_behavior_tree_arena" };

    public void EnsureWorld()
    {
        if (_world != null) return;

        // HoldRunning keeps agents in Running so they patrol every frame; think waves still tick the tree.
        BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateHoldRunningRoot("showcase.bt.arena");
        _world = new BehaviorTreeWorld(tree, _config.AgentCount);
        _posX = new float[_config.AgentCount];
        _posY = new float[_config.AgentCount];
        _heading = new float[_config.AgentCount];
        _orbitRadius = new float[_config.AgentCount];
        _orbitPhase = new float[_config.AgentCount];

        var rng = new Random(20260810);
        for (int i = 0; i < _config.AgentCount; i++)
        {
            _world.AddAgent();
            float ring = 8f + (i % 40) * 0.35f;
            float phase = (float)(i * 0.017 + rng.NextDouble() * 0.2);
            _orbitRadius[i] = ring;
            _orbitPhase[i] = phase;
            _heading[i] = phase;
            _posX[i] = MathF.Cos(phase) * ring;
            _posY[i] = MathF.Sin(phase) * ring;
        }

        Metrics.AgentCount = _config.AgentCount;
        Metrics.Detail = $"BT-only patrol motion N_topo={tree.NodeCount}";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _time += dt;
        IntegrateMotion(dt);

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds)
        {
            return;
        }

        _accum = 0f;
        var world = _world!;

        // Periodically re-root finished agents so think waves keep exercising the tree.
        for (int i = 0; i < world.Count; i++)
        {
            if (world.Statuses[i] is BehaviorTreeStatus.Success or BehaviorTreeStatus.Failure)
            {
                world.RestartThinking(i);
            }
        }

        var sw = Stopwatch.StartNew();
        BehaviorTreeThinkStats stats = world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs)
        {
            Metrics.MaxThinkMs = Metrics.LastThinkMs;
        }

        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"BT-only moving agents visited={stats.NodesVisited} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms";
    }

    private void IntegrateMotion(float dt)
    {
        // Status-driven speeds: Running patrols faster; latched Success drifts slower.
        BehaviorTreeWorld world = _world!;
        for (int i = 0; i < world.Count; i++)
        {
            float speed = world.Statuses[i] switch
            {
                BehaviorTreeStatus.Running => 1.6f,
                BehaviorTreeStatus.Success => 0.35f,
                BehaviorTreeStatus.Failure => 0.2f,
                _ => 0.8f
            };

            _orbitPhase[i] += speed * dt / MathF.Max(0.5f, _orbitRadius[i]);
            _heading[i] = _orbitPhase[i] + MathF.PI * 0.5f;
            _posX[i] = MathF.Cos(_orbitPhase[i]) * _orbitRadius[i];
            _posY[i] = MathF.Sin(_orbitPhase[i]) * _orbitRadius[i];
        }
    }
}
