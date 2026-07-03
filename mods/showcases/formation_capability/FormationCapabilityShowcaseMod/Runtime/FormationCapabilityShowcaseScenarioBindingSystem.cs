using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseScenarioBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _runtime;
    private readonly MassNavigationSimulationRuntime _simulation;

    public FormationCapabilityShowcaseScenarioBindingSystem(
        GameEngine engine,
        FormationCapabilityShowcaseRuntime runtime,
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

        _runtime.BindComponentAuthoredScenarioEntities(_engine, _simulation);
    }
}
