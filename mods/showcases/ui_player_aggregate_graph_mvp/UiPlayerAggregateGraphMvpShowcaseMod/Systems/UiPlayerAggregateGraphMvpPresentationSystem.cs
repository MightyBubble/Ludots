using Arch.System;
using Ludots.Core.Engine;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Systems;

internal sealed class UiPlayerAggregateGraphMvpPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly UiPlayerAggregateGraphMvpRuntime _runtime;

    public UiPlayerAggregateGraphMvpPresentationSystem(GameEngine engine, UiPlayerAggregateGraphMvpRuntime runtime)
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
