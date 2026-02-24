namespace Ludots.Core.Gameplay.AI.WorldState
{
    public readonly struct WorldStateProjectionRule
    {
        public readonly int AtomId;
        public readonly WorldStateProjectionOp Op;
        public readonly int IntKey;
        public readonly int IntValue;
        public readonly int EntityKey;

        public WorldStateProjectionRule(int atomId, WorldStateProjectionOp op, int intKey, int intValue, int entityKey)
        {
            AtomId = atomId;
            Op = op;
            IntKey = intKey;
            IntValue = intValue;
            EntityKey = entityKey;
        }

        public static WorldStateProjectionRule IntEquals(int atomId, int intKey, int intValue)
        {
            return new WorldStateProjectionRule(atomId, WorldStateProjectionOp.IntEquals, intKey, intValue, -1);
        }

        public static WorldStateProjectionRule IntGreaterOrEqual(int atomId, int intKey, int intValue)
        {
            return new WorldStateProjectionRule(atomId, WorldStateProjectionOp.IntGreaterOrEqual, intKey, intValue, -1);
        }

        public static WorldStateProjectionRule IntLessOrEqual(int atomId, int intKey, int intValue)
        {
            return new WorldStateProjectionRule(atomId, WorldStateProjectionOp.IntLessOrEqual, intKey, intValue, -1);
        }

        public static WorldStateProjectionRule EntityIsNonNull(int atomId, int entityKey)
        {
            return new WorldStateProjectionRule(atomId, WorldStateProjectionOp.EntityIsNonNull, -1, 0, entityKey);
        }

        public static WorldStateProjectionRule EntityIsNull(int atomId, int entityKey)
        {
            return new WorldStateProjectionRule(atomId, WorldStateProjectionOp.EntityIsNull, -1, 0, entityKey);
        }
    }
}

