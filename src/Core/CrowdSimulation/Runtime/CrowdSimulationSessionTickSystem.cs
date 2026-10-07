using Arch.System;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>会话步进系统:系统组的每个 FixedHz tick 推会话一步(宿主空转时零开销)。</summary>
public sealed class CrowdSimulationSessionTickSystem : ISystem<float>
{
    private readonly CrowdSimulationRuntime _runtime;

    public CrowdSimulationSessionTickSystem(CrowdSimulationRuntime runtime) => _runtime = runtime;

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void Update(in float dt) => _runtime.Tick();
    public void AfterUpdate(in float t) { }
    public void Dispose() { }
}
