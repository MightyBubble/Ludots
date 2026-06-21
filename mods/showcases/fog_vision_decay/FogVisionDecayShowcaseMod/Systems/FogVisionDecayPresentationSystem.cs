using Arch.System;
using FogVisionDecayShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace FogVisionDecayShowcaseMod.Systems;

internal sealed class FogVisionDecayPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly FogVisionDecayShowcaseRuntime _runtime;

    public FogVisionDecayPresentationSystem(GameEngine engine, FogVisionDecayShowcaseRuntime runtime)
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
        _runtime.RefreshMinimap(_engine);
        _runtime.RefreshPanel(_engine);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
