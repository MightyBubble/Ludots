using Arch.System;
using Ludots.AgentBridge;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using ConfigurableDataSchemaSharedMod.Runtime;

namespace ConfigurableDataSchemaSharedMod.Systems;

internal sealed class ConfigurableDataSchemaPresentationSystem : ISystem<float>
{
    private static readonly ServiceKey<AgentToolRegistry> BridgeToolRegistryKey = new("AgentToolRegistry");

    private readonly GameEngine _engine;
    private readonly ConfigurableDataSchemaRuntime _runtime;
    private bool _toolsRegistered;

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
        TryRegisterBridgeTools();
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }

    private void TryRegisterBridgeTools()
    {
        if (_toolsRegistered)
        {
            return;
        }

        if (!_engine.TryGetService(BridgeToolRegistryKey, out AgentToolRegistry? registry) || registry == null)
        {
            return;
        }

        registry.Register(new DataSchemaStateTool(_runtime));
        registry.Register(new DataSchemaAuthoringTool(_engine, _runtime));
        _toolsRegistered = true;
    }
}
