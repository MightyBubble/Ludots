using Arch.System;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>会话步进系统:系统组的每个 FixedHz tick 调 Runtime.Tick。Tick 开头先重读玩家代表的 Team。队伍变了就抛。修订号变了就在临时缓冲重算；与现场矩阵不同也抛，不写回。相同则只更新修订号。先于指令和运动。</summary>
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
