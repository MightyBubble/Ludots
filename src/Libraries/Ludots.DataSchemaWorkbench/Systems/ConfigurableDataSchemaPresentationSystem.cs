using Arch.System;
using Ludots.Core.Engine;
using ConfigurableDataSchemaSharedMod.Runtime;

namespace ConfigurableDataSchemaSharedMod.Systems;

internal sealed class ConfigurableDataSchemaPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ConfigurableDataSchemaRuntime _runtime;

    public ConfigurableDataSchemaPresentationSystem(GameEngine engine, ConfigurableDataSchemaRuntime runtime)
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
        _runtime.TickPresentation(_engine);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
