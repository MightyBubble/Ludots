using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct ActionOrderSpec
    {
        public readonly int OrderTypeId;
        public readonly OrderSubmitMode SubmitMode;
        public readonly int PlayerId;

        public ActionOrderSpec(int orderTypeId, OrderSubmitMode submitMode, int playerId = 0)
        {
            OrderTypeId = orderTypeId;
            SubmitMode = submitMode;
            PlayerId = playerId;
        }
    }
}


