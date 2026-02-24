using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct ActionOrderSpec
    {
        public readonly int OrderTagId;
        public readonly OrderSubmitMode SubmitMode;
        public readonly int PlayerId;

        public ActionOrderSpec(int orderTagId, OrderSubmitMode submitMode, int playerId = 0)
        {
            OrderTagId = orderTagId;
            SubmitMode = submitMode;
            PlayerId = playerId;
        }
    }
}

