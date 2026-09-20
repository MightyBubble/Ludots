using System;
using Arch.Core;
using Arch.System;
using Ludots.AgentBridge;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace RngShowcaseMod.Runtime;

/// <summary>
/// Drives the auto-pick loop and lazily registers bridge tools once the
/// AgentBridgeMod tool registry service appears (order-independent hookup).
/// </summary>
public sealed class RngShowcaseSystem : BaseSystem<World, float>
{
    // Name-coupled to AgentBridgeModEntry.ToolRegistryKey without a compile-time mod dependency.
    private static readonly ServiceKey<AgentToolRegistry> BridgeToolRegistryKey = new("AgentToolRegistry");

    private readonly GameEngine _engine;
    private readonly RngShowcaseRuntime _runtime;
    private readonly Action<string> _log;
    private bool _toolsRegistered;

    public RngShowcaseSystem(GameEngine engine, RngShowcaseRuntime runtime, Action<string> log) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
        _log = log;
    }

    public override void Update(in float dt)
    {
        _runtime.Tick();
        TryRegisterBridgeTools();
    }

    private void TryRegisterBridgeTools()
    {
        if (_toolsRegistered)
        {
            return;
        }

        if (!_engine.TryGetService(BridgeToolRegistryKey, out var registry))
        {
            return;
        }

        registry.Register(new RngStateTool(_runtime));
        registry.Register(new RngDrawTool(_runtime));
        registry.Register(new RngKnobTool(_runtime));
        registry.Register(new RngReplayTool(_runtime));
        _toolsRegistered = true;
        _log("[RngShowcaseMod] Bridge tools registered: ludots.rng.state/draw/knob/replay");
    }
}
