using System;
using Arch.System;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DShowcaseControlSystem : ISystem<float>
{
    private readonly Physics3DShowcaseRuntime _runtime;

    public Physics3DShowcaseControlSystem(Physics3DShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        _runtime.PrepareFixedStep();
    }
}

internal sealed class Physics3DShowcaseObservationSystem : ISystem<float>
{
    private readonly Physics3DShowcaseRuntime _runtime;

    public Physics3DShowcaseObservationSystem(Physics3DShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        _runtime.ObserveFixedStep();
    }
}
