using System;
using Arch.System;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Freezes pointer-button gesture state at the start of the local-input phase.
    /// </summary>
    public sealed class AuthoritativePointerButtonSnapshotSystem : ISystem<float>
    {
        private readonly AuthoritativePointerButtonSnapshot _snapshot;
        private readonly AuthoritativePointerButtonAccumulator _accumulator;

        public AuthoritativePointerButtonSnapshotSystem(
            AuthoritativePointerButtonSnapshot snapshot,
            AuthoritativePointerButtonAccumulator accumulator)
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
