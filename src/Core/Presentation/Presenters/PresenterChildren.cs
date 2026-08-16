using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Presentation.Presenters
{
    public unsafe struct PresenterChildren
    {
        public const int MAX_CHILDREN = 32;
        public int Count;
        public fixed int ChildIds[MAX_CHILDREN];
        public fixed int ChildWorldIds[MAX_CHILDREN];
        public fixed int ChildVersions[MAX_CHILDREN];

        public bool Add(Entity child)
        {
            if (Count >= MAX_CHILDREN) return false;
            fixed (int* ids = ChildIds)
            fixed (int* worldIds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                ids[Count] = child.Id;
                worldIds[Count] = child.WorldId;
                versions[Count] = child.Version;
                Count++;
            }
            return true;
        }

        public bool Remove(Entity child)
        {
            fixed (int* ids = ChildIds)
            fixed (int* worldIds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (ids[i] != child.Id) continue;
                    Count--;
                    if (i < Count)
                    {
                        ids[i] = ids[Count];
                        worldIds[i] = worldIds[Count];
                        versions[i] = versions[Count];
                    }
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity Get(int index)
        {
            fixed (int* ids = ChildIds)
            fixed (int* worldIds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                return EntityUtil.Reconstruct(ids[index], worldIds[index], versions[index]);
            }
        }
    }
}
