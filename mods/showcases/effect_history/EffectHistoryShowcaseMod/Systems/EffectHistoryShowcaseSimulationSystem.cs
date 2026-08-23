using Arch.Core;
using Arch.System;
using EffectHistoryShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace EffectHistoryShowcaseMod.Systems;

internal sealed class EffectHistoryShowcaseSimulationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly EffectHistoryShowcaseRuntime _runtime;

    public EffectHistoryShowcaseSimulationSystem(GameEngine engine, EffectHistoryShowcaseRuntime runtime) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
            _runtime.ProcessInput(_engine, input);
        _runtime.Advance(_engine);
    }
}
