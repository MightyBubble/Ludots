namespace Ludots.Core.Gameplay.AI.Planning
{
    public enum ActionBindingOp : byte
    {
        IntToOrderI0 = 0,
        IntToOrderI1 = 1,
        IntToOrderI2 = 2,
        IntToOrderI3 = 3,
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

