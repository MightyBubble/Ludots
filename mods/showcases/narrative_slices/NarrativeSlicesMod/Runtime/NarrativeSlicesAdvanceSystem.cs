using System;
using Arch.System;
using Ludots.Core.Engine;

namespace NarrativeSlicesMod.Runtime
{
    /// <summary>
    /// Cleanup-phase executor for pending slice starts: it waits until the NarrativeDirector
    /// holds no dialogue or cinematic so slice content never re-enters the director from
    /// inside a trigger handler.
    /// </summary>
    internal sealed class NarrativeSlicesAdvanceSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeSlicesRuntime _runtime;
        private bool _disposed;

        public NarrativeSlicesAdvanceSystem(GameEngine engine, NarrativeSlicesRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_disposed)
            {
                return;
            }

            _runtime.ConsumePendingSlice(_engine);
            _runtime.ConsumePendingParityDialogue(_engine);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
