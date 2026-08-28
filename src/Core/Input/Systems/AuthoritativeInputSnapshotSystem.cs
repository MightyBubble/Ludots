using System;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Freezes one authoritative input snapshot at the start of each fixed-step InputCollection phase.
    /// Per-seat channels freeze on the same tick with the same live/discard semantics so replay
    /// isolation replaces every channel's live input together with the global snapshot.
    /// </summary>
    public sealed class AuthoritativeInputSnapshotSystem : ISystem<float>
    {
        private readonly FrozenInputActionReader _snapshot;
        private readonly AuthoritativeInputAccumulator _accumulator;
        private readonly ClientLocalSeatInputRuntime? _seatInput;

        public AuthoritativeInputSnapshotSystem(
            FrozenInputActionReader snapshot,
            AuthoritativeInputAccumulator accumulator,
            ClientLocalSeatInputRuntime? seatInput = null)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
            _seatInput = seatInput;
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            if (_snapshot.TryConsumeReplayActions(out var replayActions))
            {
                _accumulator.DiscardLiveInput();
                _snapshot.ClearSnapshot();
                for (int i = 0; i < replayActions!.Count; i++)
                {
                    var action = replayActions[i];
                    _snapshot.SetActionState(action.ActionId, action.Value, action.IsDown, action.Pressed, action.Released);
                }
                _seatInput?.FreezeSnapshots(discardLiveInput: true);
                return;
            }
            if (_snapshot.ReplayInputIsolation)
            {
                _accumulator.DiscardLiveInput();
                _snapshot.ClearSnapshot();
                _seatInput?.FreezeSnapshots(discardLiveInput: true);
                return;
            }
            _accumulator.BuildTickSnapshot(_snapshot);
            _seatInput?.FreezeSnapshots(discardLiveInput: false);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
