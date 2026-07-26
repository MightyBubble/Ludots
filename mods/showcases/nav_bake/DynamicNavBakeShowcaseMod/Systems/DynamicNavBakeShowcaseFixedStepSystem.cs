using System;
using Arch.System;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace DynamicNavBakeShowcaseMod.Systems;

/// <summary>
/// Real FixedStep orchestration for Dynamic NavBake open-world corridor / generation refresh.
/// Runs after MassNavigation move-plan execution (AbilityActivation append) so ordinary engine
/// FixedSteps advance long marches without DrainUntilIdle.
/// </summary>
internal sealed class DynamicNavBakeShowcaseFixedStepSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly DynamicNavBakeShowcaseRuntime _runtime;

    public DynamicNavBakeShowcaseFixedStepSystem(GameEngine engine, DynamicNavBakeShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    public void Update(in float dt)
    {
        _ = dt;
        _runtime.AdvanceFixedStepOrchestration(_engine);
    }
}
