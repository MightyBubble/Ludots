using Arch.System;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Game-thread pump for the agent bridge. Registered as a presentation
    /// system so requests keep flowing while the simulation pacemaker is
    /// paused (agents must be able to resume after pause).
    /// </summary>
    public sealed class AgentBridgeSystem : ISystem<float>
    {
        private readonly AgentBridgeRuntime _runtime;

        public AgentBridgeSystem(AgentBridgeRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float t) { }

        public void Update(in float t) => _runtime.Pump();

        public void AfterUpdate(in float t) { }

        public void Dispose() { }
    }
}
