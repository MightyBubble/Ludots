namespace Ludots.Core.Gameplay.AI.Planning
{
    public enum ActionBindingOp : byte
    {
        IntToOrderArg0 = 0,
        IntToOrderArg1 = 1,
        IntToOrderArg2 = 2,
        IntToOrderArg3 = 3,
        EntityToTarget = 4,
        EntityToTargetContext = 5
    }

    public readonly struct ActionBinding
    {
        public readonly ActionBindingOp Op;
        public readonly int SourceKey;

        public ActionBinding(ActionBindingOp op, int sourceKey)
        {
            Op = op;
            SourceKey = sourceKey;
        }
    }
}
