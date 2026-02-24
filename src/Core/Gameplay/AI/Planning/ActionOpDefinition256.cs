using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct ActionOpDefinition256
    {
        public readonly WorldStateBits256 PreMask;
        public readonly WorldStateBits256 PreValues;
        public readonly WorldStateBits256 PostMask;
        public readonly WorldStateBits256 PostValues;
        public readonly int Cost;
        public readonly ActionExecutorKind ExecutorKind;
        public readonly ActionOrderSpec OrderSpec;
        public readonly ActionBinding[] Bindings;

        public ActionOpDefinition256(
            in WorldStateBits256 preMask,
            in WorldStateBits256 preValues,
            in WorldStateBits256 postMask,
            in WorldStateBits256 postValues,
            int cost,
            ActionExecutorKind executorKind,
            in ActionOrderSpec orderSpec,
            ActionBinding[] bindings)
        {
            PreMask = preMask;
            PreValues = preValues;
            PostMask = postMask;
            PostValues = postValues;
            Cost = cost;
            ExecutorKind = executorKind;
            OrderSpec = orderSpec;
            Bindings = bindings ?? System.Array.Empty<ActionBinding>();
        }
    }
}

