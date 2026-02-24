using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Blackboard buffer for Entity references.
    /// Stores key-value pairs where values are Entity references.
    /// </summary>
    public unsafe struct BlackboardEntityBuffer
    {
        public const int MAX_ENTRIES = 16;
        
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed int EntityIds[MAX_ENTRIES];
        public fixed int WorldIds[MAX_ENTRIES];
        public fixed int Versions[MAX_ENTRIES];

        /// <summary>
        /// Try to get an Entity value by key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out Entity value)
        {
            fixed (int* keys = Keys)
            fixed (int* entityIds = EntityIds)
            fixed (int* worldIds = WorldIds)
            fixed (int* versions = Versions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    value = EntityUtil.Reconstruct(entityIds[i], worldIds[i], versions[i]);
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Set an Entity value for a key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, Entity value)
        {
            fixed (int* keys = Keys)
            fixed (int* entityIds = EntityIds)
            fixed (int* worldIds = WorldIds)
            fixed (int* versions = Versions)
            {
                // Check if key exists
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    entityIds[i] = value.Id;
                    worldIds[i] = value.WorldId;
                    versions[i] = value.Version;
                    return;
                }

                // Add new entry
                if (Count >= MAX_ENTRIES) return;
                keys[Count] = key;
                entityIds[Count] = value.Id;
                worldIds[Count] = value.WorldId;
                versions[Count] = value.Version;
                Count++;
            }
        }

        /// <summary>
        /// Remove an entry by key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(int key)
        {
            fixed (int* keys = Keys)
            fixed (int* entityIds = EntityIds)
            fixed (int* worldIds = WorldIds)
            fixed (int* versions = Versions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    
                    // Shift remaining entries
                    for (int j = i; j < Count - 1; j++)
                    {
                        keys[j] = keys[j + 1];
                        entityIds[j] = entityIds[j + 1];
                        worldIds[j] = worldIds[j + 1];
                        versions[j] = versions[j + 1];
                    }
                    Count--;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if a key exists.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasKey(int key)
        {
            fixed (int* keys = Keys)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] == key) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clear all entries.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Count = 0;
        }
    }
}
