using System;
using Arch.System;
using Ludots.Core.Engine;

namespace CapabilityStandardHfsmShowcaseMod.Runtime;

internal sealed class CapabilityStandardHfsmShowcaseSimulationSystem : ISystem<float>
{
    private readonly CapabilityStandardHfsmShowcaseRuntime _runtime;

    public CapabilityStandardHfsmShowcaseSimulationSystem(CapabilityStandardHfsmShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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

internal sealed class CapabilityStandardHfsmShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardHfsmShowcaseRuntime _runtime;

    public CapabilityStandardHfsmShowcasePresentationSystem(GameEngine engine, CapabilityStandardHfsmShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
