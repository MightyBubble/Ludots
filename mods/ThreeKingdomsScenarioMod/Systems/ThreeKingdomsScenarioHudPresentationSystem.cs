using Arch.System;
using Ludots.Core.Engine;
using ThreeKingdomsScenarioMod.Runtime;

namespace ThreeKingdomsScenarioMod.Systems;

internal sealed class ThreeKingdomsScenarioHudPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ThreeKingdomsScenarioRuntime _runtime;

    public ThreeKingdomsScenarioHudPresentationSystem(GameEngine engine, ThreeKingdomsScenarioRuntime runtime)
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
        _runtime.RefreshPanel(_engine);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
