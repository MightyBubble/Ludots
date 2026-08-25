using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Persistence
{
    public readonly record struct AuthoritativeAction(
        string ActionId,
        Vector3 Value,
        bool IsDown,
        bool Pressed,
        bool Released);

    public sealed record AuthoritativeFrame(
        long Sequence,
        int Tick,
        IReadOnlyList<AuthoritativeAction> Actions)
    {
        public AuthoritativeFrame Validate(long expectedSequence, int minimumTick)
        {
            if (Sequence != expectedSequence)
            {
                throw new SaveContextException($"Authoritative frame sequence is not contiguous: expected {expectedSequence}, actual {Sequence}.");
            }

            if (Tick < minimumTick)
            {
                throw new SaveContextException($"Authoritative frame tick moved backwards: minimum {minimumTick}, actual {Tick}.");
            }

            if (Actions == null)
            {
                throw new SaveContextException("Authoritative frame actions are required.");
            }

            string? previous = null;
            for (int i = 0; i < Actions.Count; i++)
            {
                AuthoritativeAction action = Actions[i];
                if (string.IsNullOrWhiteSpace(action.ActionId))
                {
                    throw new SaveContextException($"Authoritative frame action {i} has an empty action id.");
                }

                if (!float.IsFinite(action.Value.X) || !float.IsFinite(action.Value.Y) || !float.IsFinite(action.Value.Z))
                {
                    throw new SaveContextException($"Authoritative frame action {i} contains a non-finite value.");
                }

                if (previous != null && string.CompareOrdinal(previous, action.ActionId) >= 0)
                {
                    throw new SaveContextException("Authoritative frame actions must be sorted uniquely by action id.");
                }

                previous = action.ActionId;
            }

            return this;
        }
    }

    public sealed record AuthoritativeFrameBatch(
        long BatchSequence,
        long FirstFrameSequence,
        IReadOnlyList<AuthoritativeFrame> Frames)
    {
        public AuthoritativeFrameBatch Validate(long expectedBatchSequence, long expectedFrameSequence, int minimumTick)
        {
            if (BatchSequence != expectedBatchSequence)
            {
                throw new SaveContextException($"Authoritative frame batch sequence is not contiguous: expected {expectedBatchSequence}, actual {BatchSequence}.");
            }

            if (FirstFrameSequence != expectedFrameSequence)
            {
                throw new SaveContextException($"Authoritative frame batch starts at the wrong frame sequence: expected {expectedFrameSequence}, actual {FirstFrameSequence}.");
            }

            if (Frames == null || Frames.Count == 0)
            {
                throw new SaveContextException("Authoritative frame batch must contain at least one frame.");
            }

            var stream = new AuthoritativeFrameStream(FirstFrameSequence, minimumTick);
            stream.AppendRange(Frames);
            return this;
        }
    }

    public sealed class AuthoritativeFrameBatchStream
    {
        private long _nextBatchSequence;
        private long _nextFrameSequence;
        private int _lastTick;

        public AuthoritativeFrameBatchStream(long nextBatchSequence = 0, long nextFrameSequence = 0, int lastTick = 0)
        {
            if (nextBatchSequence < 0) throw new ArgumentOutOfRangeException(nameof(nextBatchSequence));
            if (nextFrameSequence < 0) throw new ArgumentOutOfRangeException(nameof(nextFrameSequence));
            if (lastTick < 0) throw new ArgumentOutOfRangeException(nameof(lastTick));
            _nextBatchSequence = nextBatchSequence;
            _nextFrameSequence = nextFrameSequence;
            _lastTick = lastTick;
        }

        public void Append(AuthoritativeFrameBatch batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            batch.Validate(_nextBatchSequence, _nextFrameSequence, _lastTick);
            _nextBatchSequence++;
            _nextFrameSequence += batch.Frames.Count;
            _lastTick = batch.Frames[^1].Tick;
        }
    }

    public sealed class AuthoritativeFrameStream
    {
        private readonly List<AuthoritativeFrame> _frames = new();
        private long _nextSequence;
        private int _lastTick;

        public AuthoritativeFrameStream(long nextSequence = 0, int lastTick = 0)
        {
            if (nextSequence < 0) throw new ArgumentOutOfRangeException(nameof(nextSequence));
            if (lastTick < 0) throw new ArgumentOutOfRangeException(nameof(lastTick));
            _nextSequence = nextSequence;
            _lastTick = lastTick;
        }

        public IReadOnlyList<AuthoritativeFrame> Frames => _frames;
        public long NextSequence => _nextSequence;
        public int LastTick => _lastTick;

        public void Clear()
        {
            _frames.Clear();
            _nextSequence = 0;
            _lastTick = 0;
        }

        public void Append(AuthoritativeFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            frame.Validate(_nextSequence, _lastTick);
            _frames.Add(frame);
            _nextSequence++;
            _lastTick = frame.Tick;
        }

        public void AppendRange(IEnumerable<AuthoritativeFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            foreach (AuthoritativeFrame frame in frames) Append(frame);
        }
    }
}
