using Arch.System;
using Ludots.Core.Engine;

namespace TimeFlowShowcaseMod.Systems;

internal sealed class TimeFlowShowcaseSimulationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TimeFlowShowcaseRuntime _runtime;

    public TimeFlowShowcaseSimulationSystem(GameEngine engine, TimeFlowShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _runtime.AdvanceFixedStep(_engine);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
