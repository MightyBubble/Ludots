using Arch.System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// #1126 deterministic Continuation phase: drains completed AwaitCallback handles in
    /// registration order. Must run after DeferredTriggerCollection (heartbeat Yield resume)
    /// and before Cleanup.
    /// </summary>
    public sealed class GraphCallbackContinuationSystem : ISystem<float>
    {
        private readonly GraphCallbackService _callbacks;

        public GraphCallbackContinuationSystem(GraphCallbackService callbacks)
        {
            _callbacks = callbacks ?? throw new System.ArgumentNullException(nameof(callbacks));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _callbacks.Drain();
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}
