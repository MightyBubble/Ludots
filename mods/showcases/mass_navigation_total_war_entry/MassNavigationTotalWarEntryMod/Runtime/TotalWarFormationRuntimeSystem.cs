using System;
using Arch.System;
using Ludots.Core.Engine;
using MassNavigationMod.Runtime;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarFormationRuntimeSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TotalWarShowcaseRuntime _runtime;
    private readonly MassNavigationSimulationRuntime _simulation;

    public TotalWarFormationRuntimeSystem(
        GameEngine engine,
        TotalWarShowcaseRuntime runtime,
        MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            return;
        }

        _runtime.Tick(_engine, _simulation);
    }
}
