using Arch.System;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

internal sealed class LiveSkillWorkbenchVignetteSimulationSystem : ISystem<float>
{
    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;
    private bool _started;

    public LiveSkillWorkbenchVignetteSimulationSystem(LiveSkillWorkbenchVignetteRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        // Defer first ticks — native host under llvmpipe has been observed to SIGSEGV
        // if Mod systems run in the same frame as instancing shader upload.
        if (!_started)
        {
            _started = true;
            return;
        }

        _runtime.Tick(dt);
    }
}
