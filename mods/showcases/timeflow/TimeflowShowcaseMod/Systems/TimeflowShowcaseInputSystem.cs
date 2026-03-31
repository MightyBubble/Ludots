using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using TimeflowShowcaseMod.Runtime;

namespace TimeflowShowcaseMod.Systems;

internal sealed class TimeflowShowcaseInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TimeflowShowcaseRuntime _runtime;

    public TimeflowShowcaseInputSystem(GameEngine engine, TimeflowShowcaseRuntime runtime)
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
        if (!_runtime.IsActive(_engine))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        _runtime.HandleInput(_engine, input);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }
}
