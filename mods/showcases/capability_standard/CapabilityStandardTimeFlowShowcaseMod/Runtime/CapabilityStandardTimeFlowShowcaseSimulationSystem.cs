using System;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardTimeFlowShowcaseMod.Runtime;

internal sealed class CapabilityStandardTimeFlowShowcaseSimulationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardTimeFlowShowcaseRuntime _runtime;

    public CapabilityStandardTimeFlowShowcaseSimulationSystem(
        GameEngine engine,
        CapabilityStandardTimeFlowShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _runtime.AdvanceSimulation(_engine, dt);
    }
}
