using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterSinkRejection : byte
    {
        None = 0,
        TargetPresenterMissing = 1,
        TargetDefinitionMissing = 2,
        TargetEmitComponentsMissing = 3,
        AssetSlotMissing = 4,
        AssetSlotNotAssetBinding = 5,
        AssetSlotInactive = 6,
        LaneMissing = 7,
        LaneTypeMismatch = 8,
        AssetWriteSuppressed = 9,
    }

    public readonly struct PresenterSinkOutcome
    {
        public readonly bool Accepted;
        public readonly PresenterSinkRejection Rejection;
        public readonly int CommandKindId;
        public readonly Entity Presenter;
        public readonly int DefinitionId;
        public readonly int ParamKey;
        public readonly ParamLane Lane;
        public readonly int BehaviorSlot;
        public readonly string Message;

        public PresenterSinkOutcome(
            bool accepted,
            PresenterSinkRejection rejection,
            int commandKindId,
            Entity presenter,
            int definitionId,
            int paramKey,
            ParamLane lane,
            int behaviorSlot,
            string message)
        {
            Accepted = accepted;
            Rejection = rejection;
            CommandKindId = commandKindId;
            Presenter = presenter;
            DefinitionId = definitionId;
            ParamKey = paramKey;
            Lane = lane;
            BehaviorSlot = behaviorSlot;
            Message = message;
        }
    }

    public sealed class PresenterSinkDiagnostics
    {
        private readonly PresenterSinkOutcome[] _ring;

        public PresenterSinkDiagnostics(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ring = new PresenterSinkOutcome[capacity];
        }

        public const int DefaultCapacity = 64;

        public int Capacity => _ring.Length;

        public int TotalRecorded { get; private set; }

        public void Record(in PresenterSinkOutcome outcome)
        {
            _ring[TotalRecorded % _ring.Length] = outcome;
            TotalRecorded++;
        }

        /// <summary>back=0 returns the most recent outcome; back=1 the one before it.</summary>
        public PresenterSinkOutcome GetRecent(int back)
        {
            if ((uint)back >= (uint)Math.Min(TotalRecorded, _ring.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(back));
            }

            return _ring[(TotalRecorded - 1 - back) % _ring.Length];
        }

        public void CopyTo(PresenterSinkOutcome[] destination, int index)
        {
            int live = Math.Min(TotalRecorded, _ring.Length);
            int start = TotalRecorded > _ring.Length ? TotalRecorded % _ring.Length : 0;
            for (int i = 0; i < live; i++)
            {
                destination[index + i] = _ring[(start + i) % _ring.Length];
            }
        }
    }
}
