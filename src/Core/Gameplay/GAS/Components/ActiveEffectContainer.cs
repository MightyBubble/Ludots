using System;
using Arch.Core;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Stores references to active effects on this character.
    /// Uses flattened arrays to store Entity struct fields (Id, WorldId, Version).
    /// </summary>
    public unsafe struct ActiveEffectContainer
    {
        public const int CAPACITY = GasConstants.ACTIVE_EFFECT_CONTAINER_CAPACITY;
        
        // Parallel arrays for Entity fields
        public fixed int Ids[CAPACITY];
        public fixed int WorldIds[CAPACITY];
        public fixed int Versions[CAPACITY];
        
        public int Count;

        public bool Add(Entity entity)
        {
            if (Count >= CAPACITY) return false;
            
            // We need to read private fields? 
            // Entity fields are public readonly in the file I read!
            // public readonly int Id;
            // public readonly int WorldId;
            // public readonly int Version;
            // So we can read them directly.
            
            Ids[Count] = entity.Id;
            WorldIds[Count] = entity.WorldId;
            Versions[Count] = entity.Version;
            Count++;
            return true;
        }

        public void Remove(Entity entity)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Ids[i] == entity.Id && WorldIds[i] == entity.WorldId && Versions[i] == entity.Version)
                {
                    // Swap remove
                    int last = Count - 1;
                    Ids[i] = Ids[last];
                    WorldIds[i] = WorldIds[last];
                    Versions[i] = Versions[last];
                    Count--;
                    return;
                }
            }
        }
        
        public Entity GetEntity(int index)
        {
            if (index < 0 || index >= Count) return Entity.Null;
            return EntityUtil.Reconstruct(Ids[index], WorldIds[index], Versions[index]);
        }
    }
}
