using Arch.System;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

internal sealed class LiveSkillWorkbenchVignetteSimulationSystem : ISystem<float>
{
    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;

    public LiveSkillWorkbenchVignetteSimulationSystem(LiveSkillWorkbenchVignetteRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt) => _runtime.Tick(dt);
}
