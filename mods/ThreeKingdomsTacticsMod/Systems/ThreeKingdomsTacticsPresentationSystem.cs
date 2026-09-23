using Arch.System;
using Ludots.Core.Engine;
using ThreeKingdomsTacticsMod.Runtime;

namespace ThreeKingdomsTacticsMod.Systems;

internal sealed class ThreeKingdomsTacticsPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ThreeKingdomsTacticsRuntime _runtime;

    public ThreeKingdomsTacticsPresentationSystem(GameEngine engine, ThreeKingdomsTacticsRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        _runtime.Update(_engine, t);
    }
}
