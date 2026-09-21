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
            _casts = new CastIntentSubmission[capacity];
            _engages = new EngageIntentSubmission[capacity];
            _memberPool = new Entity[capacity * 4];
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

        public void Push(in CommandIntentSubmission submission, System.ReadOnlySpan<Entity> members)
        {
            var range = PushMembers(members);
            var record = submission with { MemberOffset = range.Offset, MemberCount = range.Count };
            if (_count >= _submissions.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.SubmissionBufferOverflow: command intent submission buffer capacity {_submissions.Length} exceeded; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity or submit fewer intents per tick.");
            }

            _submissions[_count++] = record;
        }

        /// <summary>Consumes the whole buffer; the drain is the only reader, so reset is wholesale.</summary>
        public void Clear()
        {
            _count = 0;
            _castCount = 0;
            _engageCount = 0;
            _memberPoolCount = 0;
        }

        private readonly Entity[] _memberPool;
        private int _memberPoolCount;

        /// <summary>
        /// Copies one graph-supplied actor set into the shared member pool; empty spans mean
        /// "the acting rep alone" (v2: the submitting graph owns fan-out membership). Overflow
        /// fails loud instead of truncating.
        /// </summary>
        private (int Offset, int Count) PushMembers(System.ReadOnlySpan<Entity> members)
        {
            if (members.Length == 0)
            {
                return (0, 0);
            }

            if (_memberPoolCount + members.Length > _memberPool.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.INTENT.ERR.MemberPoolOverflow: member pool capacity {_memberPool.Length} exceeded; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity or submit fewer actors per intent.");
            }

            int offset = _memberPoolCount;
            members.CopyTo(_memberPool.AsSpan(_memberPoolCount));
            _memberPoolCount += members.Length;
            return (offset, members.Length);
        }

        public System.ReadOnlySpan<Entity> Members(int offset, int count)
        {
            return _memberPool.AsSpan(offset, count);
        }

        private readonly CastIntentSubmission[] _casts;
        private int _castCount;

        /// <summary>Cast intents queued since the last drain (same tick contract as commands).</summary>
        public int CastCount => _castCount;

        public CastIntentSubmission Cast(int index)
        {
            if ((uint)index >= (uint)_castCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _casts[index];
        }

        public void PushCast(in CastIntentSubmission submission, System.ReadOnlySpan<Entity> members)
        {
            var range = PushMembers(members);
            var record = submission with { MemberOffset = range.Offset, MemberCount = range.Count };
            if (_castCount >= _casts.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.CAST_INTENT.ERR.SubmissionBufferOverflow: cast intent submission buffer capacity {_casts.Length} exceeded; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity or submit fewer intents per tick.");
            }

            _casts[_castCount++] = record;
        }

        private readonly EngageIntentSubmission[] _engages;
        private int _engageCount;

        /// <summary>Engage intents queued since the last drain (same tick contract as commands).</summary>
        public int EngageCount => _engageCount;

        public EngageIntentSubmission Engage(int index)
        {
            if ((uint)index >= (uint)_engageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _engages[index];
        }

        public void PushEngage(in EngageIntentSubmission submission, System.ReadOnlySpan<Entity> members)
        {
            var range = PushMembers(members);
            var record = submission with { MemberOffset = range.Offset, MemberCount = range.Count };
            if (_engageCount >= _engages.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.ENGAGE_INTENT.ERR.SubmissionBufferOverflow: engage intent submission buffer capacity {_engages.Length} exceeded; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity or submit fewer intents per tick.");
            }

            _engages[_engageCount++] = record;
        }
    }

    public readonly record struct CommandIntentSubmission(
        Entity Rep,
        Entity Target,
        bool HasTarget,
        IntVector2 GroundCm,
        int MemberOffset = 0,
        int MemberCount = 0);

    /// <summary>
    /// One graph-submitted cast intent: the slot lands as Args.I0; GroundCm applies only when
    /// HasGround (the graph asserted a resolved ground point this run); OrderTypeKeyId is the
    /// cast order-type config-key symbol id, resolved by the drain through OrderTypeRegistry.
    /// </summary>
    public readonly record struct CastIntentSubmission(
        Entity Rep,
        int Slot,
        Entity Target,
        bool HasTarget,
        bool HasGround,
        IntVector2 GroundCm,
        int OrderTypeKeyId,
        int MemberOffset = 0,
        int MemberCount = 0);

    /// <summary>
    /// One graph-submitted engage intent: the drain resolves actors from the rep's
    /// active-context-declared collection, runs the profile's EQS query around the target
    /// (in-batch exclusion + per-target slot claims), and submits per-actor move-then-cast
    /// with the assigned ring point. ProfileKeyId indexes the EqsQueryRegistry;
    /// OrderTypeKeyId is the follow-up cast order-type config-key symbol id.
    /// </summary>
    public readonly record struct EngageIntentSubmission(
        Entity Rep,
        int Slot,
        Entity Target,
        int ProfileKeyId,
        int OrderTypeKeyId,
        int MemberOffset = 0,
        int MemberCount = 0);
}
