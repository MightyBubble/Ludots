using Arch.System;
using Ludots.Core.Engine;
using TimeflowShowcaseMod.Runtime;

namespace TimeflowShowcaseMod.Systems;

internal sealed class TimeflowShowcaseOverlaySystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TimeflowShowcaseRuntime _runtime;

    public TimeflowShowcaseOverlaySystem(GameEngine engine, TimeflowShowcaseRuntime runtime)
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
        _runtime.HandleLiveInput(_engine);
        _runtime.DrawOverlay(_engine);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}
