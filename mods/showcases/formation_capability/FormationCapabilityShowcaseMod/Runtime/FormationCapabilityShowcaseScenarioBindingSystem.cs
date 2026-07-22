using System;
using Arch.System;
using Ludots.Core.Engine;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseScenarioBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _runtime;

    public FormationCapabilityShowcaseScenarioBindingSystem(
        GameEngine engine,
        FormationCapabilityShowcaseRuntime runtime)
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
        if (!_runtime.IsCurrentShowcaseMap(_engine))
        {
            return;
        }

        _runtime.BindComponentAuthoredScenarioEntities(_engine, _runtime.RequireCurrentSimulation(_engine));
    }
}
