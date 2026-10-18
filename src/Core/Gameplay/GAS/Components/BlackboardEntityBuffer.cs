using System;
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
        public const string CapacityExceededError = "GAS.BLACKBOARD.ERR.CapacityExceeded";
        
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed int EntityIds[MAX_ENTRIES];
        public fixed int WorldIds[MAX_ENTRIES];
        public fixed int Versions[MAX_ENTRIES];

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, Entity value)
        {
            fixed (int* keys = Keys)
            fixed (int* entityIds = EntityIds)
            fixed (int* worldIds = WorldIds)
            fixed (int* versions = Versions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    entityIds[i] = value.Id;
                    worldIds[i] = value.WorldId;
                    versions[i] = value.Version;
                    return;
                }

                if (Count >= MAX_ENTRIES)
                {
                    throw new InvalidOperationException(
                        $"{CapacityExceededError}: buffer={nameof(BlackboardEntityBuffer)}, key={key}, capacity={MAX_ENTRIES}.");
                }
                keys[Count] = key;
                entityIds[Count] = value.Id;
                worldIds[Count] = value.WorldId;
                versions[Count] = value.Version;
                Count++;
            }
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Count = 0;
        }
    }
}
