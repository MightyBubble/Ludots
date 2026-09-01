using System;

namespace Ludots.Core.Networking.Simulation
{
    /// <summary>
    /// Owns the single authoritative fixed-step timeline. A tick is observable as committed only
    /// after the complete simulation pipeline has finished.
    /// </summary>
    public sealed class AuthoritativeSimulationTickState
    {
        private int _executingTick;
        private int _committedTick;
        private bool _isExecuting;

        public bool IsExecuting => _isExecuting;

        public int ExecutingTick => _isExecuting
            ? _executingTick
            : throw new InvalidOperationException("No authoritative simulation tick is executing.");

        public int CommittedTick => _committedTick;

        public void Begin(int tick)
        {
            if (_isExecuting)
            {
                throw new InvalidOperationException(
                    $"Cannot begin authoritative simulation tick {tick} while tick {_executingTick} is executing.");
            }

            if (_committedTick == int.MaxValue)
            {
                throw new InvalidOperationException("Authoritative simulation tick overflowed.");
            }

            int expected = _committedTick + 1;
            if (tick != expected)
            {
                throw new InvalidOperationException(
                    $"Authoritative simulation tick must begin at {expected}; got {tick}.");
            }

            _executingTick = tick;
            _isExecuting = true;
        }

        public void Commit(int tick)
        {
            if (!_isExecuting)
            {
                throw new InvalidOperationException(
                    $"Cannot commit authoritative simulation tick {tick} because no tick is executing.");
            }

            if (tick != _executingTick)
            {
                throw new InvalidOperationException(
                    $"Authoritative simulation tick {_executingTick} is executing; cannot commit tick {tick}.");
            }

            _committedTick = tick;
            _executingTick = 0;
            _isExecuting = false;
        }

        public void RestoreCommittedTick(int tick)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "Committed tick must not be negative.");
            }

            if (_isExecuting)
            {
                throw new InvalidOperationException(
                    $"Cannot restore committed tick {tick} while tick {_executingTick} is executing.");
            }

            _committedTick = tick;
        }
    }
}
