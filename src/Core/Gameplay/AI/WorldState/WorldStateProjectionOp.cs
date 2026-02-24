namespace Ludots.Core.Gameplay.AI.WorldState
{
    public enum WorldStateProjectionOp : byte
    {
        IntEquals = 0,
        IntGreaterOrEqual = 1,
        IntLessOrEqual = 2,
        EntityIsNonNull = 3,
        EntityIsNull = 4
    }
}

