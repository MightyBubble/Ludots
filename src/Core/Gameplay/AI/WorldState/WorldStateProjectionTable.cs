using System;

namespace Ludots.Core.Gameplay.AI.WorldState
{
    public sealed class WorldStateProjectionTable
    {
        public readonly WorldStateProjectionRule[] Rules;
        public readonly int AtomCapacity;

        public WorldStateProjectionTable(WorldStateProjectionRule[] rules, int atomCapacity = 256)
        {
            Rules = rules ?? Array.Empty<WorldStateProjectionRule>();
            AtomCapacity = atomCapacity > 0 ? atomCapacity : 256;
        }
    }
}

