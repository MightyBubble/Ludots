using System;
using Arch.Core;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Per-tick command-intent submission buffer (constitution §12): the graph-side
    /// <c>SubmitCommandIntent</c> op pushes one record per firing and the order kernel drains
    /// the whole buffer in its own system-group phase — the op never routes inline, so graph
    /// execution and order routing never reenter each other. Fixed capacity from
    /// <c>gasRuntimeCapacity.commandIntentScratchCapacity</c>; overflow fails loud by name
    /// instead of truncating. Not world state: records are consumed every drain tick and never
    /// persisted.
    /// </summary>
    public sealed class CommandIntentSubmissionBuffer
    {
        private readonly CommandIntentSubmission[] _submissions;
        private int _count;

        public CommandIntentSubmissionBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Command intent submission capacity must be positive.");
            }

            _submissions = new CommandIntentSubmission[capacity];
        }

        public int Capacity => _submissions.Length;

        /// <summary>Records queued since the last drain; drained entries reset to zero.</summary>
        public int Count => _count;

        public CommandIntentSubmission this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _submissions[index];
            }
        }

        public void Push(in CommandIntentSubmission submission)
        {
            if (_count >= _submissions.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.SubmissionBufferOverflow: command intent submission buffer capacity {_submissions.Length} exceeded; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity or submit fewer intents per tick.");
            }

            _submissions[_count++] = submission;
        }

        /// <summary>Consumes the whole buffer; the drain is the only reader, so reset is wholesale.</summary>
        public void Clear()
        {
            _count = 0;
        }
    }

    public readonly record struct CommandIntentSubmission(
        Entity Rep,
        Entity Target,
        bool HasTarget,
        IntVector2 GroundCm);
}
