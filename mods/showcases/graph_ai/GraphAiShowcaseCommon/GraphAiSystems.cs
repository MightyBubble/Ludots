using Arch.System;
using Ludots.Core.Engine;

namespace GraphAiShowcaseCommon;

public sealed class GraphAiShowcaseSimulationSystem : ISystem<float>
{
    private readonly GraphAiShowcaseRuntime _runtime;

    public GraphAiShowcaseSimulationSystem(GraphAiShowcaseRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _runtime.Update(dt);
    }
}

public sealed class GraphAiShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly GraphAiShowcaseRuntime _runtime;

    public GraphAiShowcasePresentationSystem(GameEngine engine, GraphAiShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _runtime.RenderOverlay(_engine);
    }
}
