using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct ActionOrderSpec
    {
        public readonly int OrderTypeId;
        public readonly OrderSubmitMode SubmitMode;
        public readonly int PlayerId;
        public readonly int AbilityId;

        public ActionOrderSpec(int orderTypeId, OrderSubmitMode submitMode, int playerId, int abilityId = 0)
        {
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId), "AI orders must declare a positive player id.");
            }

            OrderTypeId = orderTypeId;
            SubmitMode = submitMode;
            PlayerId = playerId;
            AbilityId = abilityId;
        }
    }
}

