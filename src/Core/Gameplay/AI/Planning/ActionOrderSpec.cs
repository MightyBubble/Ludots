using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public enum AiOrderPayloadKind : byte
    {
        None = 0,
        CastAbility = 1,
        MoveToWorldCm = 2,
        Stop = 3,
        TargetEntity = 4
    }

    public readonly struct ActionOrderSpec
    {
        public readonly AiOrderPayloadKind PayloadKind;
        public readonly int OrderTypeId;
        public readonly OrderSubmitMode SubmitMode;
        public readonly int PlayerId;

        public ActionOrderSpec(AiOrderPayloadKind payloadKind, int orderTypeId, OrderSubmitMode submitMode, int playerId)
        {
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId), "AI orders must declare a positive player id.");
            }

            PayloadKind = payloadKind;
            OrderTypeId = orderTypeId;
            SubmitMode = submitMode;
            PlayerId = playerId;
        }
    }
}
