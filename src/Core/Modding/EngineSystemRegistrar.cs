using Arch.System;
using Ludots.Core.Engine;

namespace Ludots.Core.Modding;

public sealed class EngineSystemRegistrar : ISystemRegistrar
{
    private readonly GameEngine _engine;

    public EngineSystemRegistrar(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void RegisterSystem(ISystem<float> system, SystemGroup group)
        => _engine.RegisterSystem(system, group);

    public void RegisterPresentationSystem(ISystem<float> system)
        => _engine.RegisterPresentationSystem(system);

    public void InsertSystemBeforeRequired<TAnchor>(ISystem<float> system, SystemGroup group)
        where TAnchor : class
        => _engine.InsertSystemBeforeRequired<TAnchor>(system, group);
}
