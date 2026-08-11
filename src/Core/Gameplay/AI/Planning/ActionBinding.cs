namespace Ludots.Core.Gameplay.AI.Planning
{
    public enum ActionBindingOp : byte
    {
        IntToAbilitySlot = 0,
        EntityToTarget = 4,
        EntityToTargetContext = 5,
        EntityPositionToMoveDestination = 6
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
