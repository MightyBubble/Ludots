using System;
using Arch.System;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Freezes one authoritative input snapshot at the start of each fixed-step InputCollection phase.
    /// </summary>
    public sealed class AuthoritativeInputSnapshotSystem : ISystem<float>
    {
        private readonly FrozenInputActionReader _snapshot;
        private readonly AuthoritativeInputAccumulator _accumulator;

        public AuthoritativeInputSnapshotSystem(FrozenInputActionReader snapshot, AuthoritativeInputAccumulator accumulator)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            _accumulator.BuildTickSnapshot(_snapshot);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
