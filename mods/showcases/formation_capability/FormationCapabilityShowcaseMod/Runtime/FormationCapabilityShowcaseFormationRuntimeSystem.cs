using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation.Runtime;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseFormationRuntimeSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _runtime;
    private readonly MassNavigationRuntimeBinding _binding;

    public FormationCapabilityShowcaseFormationRuntimeSystem(
        GameEngine engine,
        FormationCapabilityShowcaseRuntime runtime,
        MassNavigationRuntimeBinding binding)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
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

        MassNavigationSimulationRuntime? simulation = _binding.Current;
        if (simulation == null || !simulation.IsReadyForWorldOperations)
        {
            return;
        }

        _runtime.Tick(_engine, simulation);
    }
}
