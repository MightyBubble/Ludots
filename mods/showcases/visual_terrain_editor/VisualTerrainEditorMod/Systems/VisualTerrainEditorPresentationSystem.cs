using Arch.System;
using Ludots.Core.Engine;
using VisualTerrainEditorMod.Runtime;

namespace VisualTerrainEditorMod.Systems;

internal sealed class VisualTerrainEditorPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly VisualTerrainEditorRuntime _runtime;

    public VisualTerrainEditorPresentationSystem(GameEngine engine, VisualTerrainEditorRuntime runtime)
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
