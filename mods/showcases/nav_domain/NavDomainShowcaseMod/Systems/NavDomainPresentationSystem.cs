using Arch.System;
using Ludots.Core.Engine;
using NavDomainShowcaseMod.Runtime;

namespace NavDomainShowcaseMod.Systems;

internal sealed class NavDomainPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly NavDomainAuthoringRuntime _runtime;

    public NavDomainPresentationSystem(GameEngine engine, NavDomainAuthoringRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        _runtime.Update(_engine);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}
