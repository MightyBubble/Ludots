using Arch.Core;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.Technology.Components
{
    public unsafe struct TechnologyStateBuffer
    {
        public const int Capacity = 64;

        public fixed int TechnologyIds[Capacity];
        public fixed int TechnologyLevels[Capacity];
        public int Count;
        public uint Revision;

        public readonly bool HasCompleted(int technologyId)
            => GetLevel(technologyId) >= 1;

        public readonly int GetLevel(int technologyId)
        {
            if (technologyId <= 0)
            {
                return 0;
            }

            for (int i = 0; i < Count; i++)
            {
                if (TechnologyIds[i] == technologyId)
                {
                    return TechnologyLevels[i];
                }
            }

            return 0;
        }

        public bool TryComplete(int technologyId)
            => TrySetLevelAtLeast(technologyId, 1);

        public bool TrySetLevel(int technologyId, int level)
        {
            if (technologyId <= 0 || level <= 0)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (TechnologyIds[i] != technologyId)
                {
                    continue;
                }

                if (TechnologyLevels[i] == level)
                {
                    return true;
                }

                TechnologyLevels[i] = level;
                Revision++;
                return true;
            }

            if (Count >= Capacity)
            {
                return false;
            }

            TechnologyIds[Count] = technologyId;
            TechnologyLevels[Count] = level;
            Count++;
            Revision++;
            return true;
        }

        public bool TryAddLevel(int technologyId, int delta)
        {
            if (technologyId <= 0 || delta <= 0)
            {
                return false;
            }

            int nextLevel = GetLevel(technologyId) + delta;
            if (nextLevel <= 0)
            {
                return false;
            }

            return TrySetLevel(technologyId, nextLevel);
        }

        public bool TrySetLevelAtLeast(int technologyId, int level)
        {
            if (technologyId <= 0 || level <= 0)
            {
                return false;
            }

            if (GetLevel(technologyId) >= level)
            {
                return true;
            }

            return TrySetLevel(technologyId, level);
        }

        public bool HasLevelAtLeast(int technologyId, int level)
        {
            if (level <= 0)
            {
                level = 1;
            }

            return GetLevel(technologyId) >= level;
        }
    }

    public unsafe struct TechnologyScopeRefBuffer
    {
        public const int Capacity = 8;

        public fixed int ScopeKeyIds[Capacity];
        public fixed int EntityIds[Capacity];
        public fixed int EntityWorldIds[Capacity];
        public fixed int EntityVersions[Capacity];
        public int Count;

        public bool TryAdd(int scopeKeyId, Entity scopeHost)
            => TryAdd(scopeKeyId, scopeHost, out _);

        public bool TryAdd(int scopeKeyId, Entity scopeHost, out bool changed)
            => TryAdd(scopeKeyId, scopeHost, out changed, out _);

        public bool TryAdd(int scopeKeyId, Entity scopeHost, out bool changed, out Entity previousScopeHost)
        {
            changed = false;
            previousScopeHost = Entity.Null;
            if (scopeKeyId <= 0 || scopeHost == Entity.Null)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeKeyIds[i] == scopeKeyId)
                {
                    if (EntityIds[i] == scopeHost.Id &&
                        EntityWorldIds[i] == scopeHost.WorldId &&
                        EntityVersions[i] == scopeHost.Version)
                    {
                        return true;
                    }

                    previousScopeHost = EntityUtil.Reconstruct(
                        EntityIds[i],
                        EntityWorldIds[i],
                        EntityVersions[i]);
                    EntityIds[i] = scopeHost.Id;
                    EntityWorldIds[i] = scopeHost.WorldId;
                    EntityVersions[i] = scopeHost.Version;
                    changed = true;
                    return true;
                }
            }

            if (Count >= Capacity)
            {
                return false;
            }

            ScopeKeyIds[Count] = scopeKeyId;
            EntityIds[Count] = scopeHost.Id;
            EntityWorldIds[Count] = scopeHost.WorldId;
            EntityVersions[Count] = scopeHost.Version;
            Count++;
            changed = true;
            return true;
        }

        public readonly bool TryGet(int scopeKeyId, out Entity scopeHost)
        {
            if (scopeKeyId <= 0)
            {
                scopeHost = Entity.Null;
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeKeyIds[i] == scopeKeyId)
                {
                    scopeHost = EntityUtil.Reconstruct(EntityIds[i], EntityWorldIds[i], EntityVersions[i]);
                    return true;
                }
            }

            scopeHost = Entity.Null;
            return false;
        }
    }

    public struct TechnologyScopeMembershipRevision
    {
        public uint Revision;
    }

    public struct TechnologyScopeMemberTag
    {
    }

    public unsafe struct TechnologyScopeHostAuthoring
    {
        public const int Capacity = 8;

        public fixed int ScopeNameKeyIds[Capacity];
        public fixed int HostKeyIds[Capacity];
        public int Count;

        public bool TryAdd(int scopeNameKeyId, int hostKeyId)
        {
            if (scopeNameKeyId <= 0 || hostKeyId <= 0 || Count >= Capacity)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeNameKeyIds[i] == scopeNameKeyId && HostKeyIds[i] == hostKeyId)
                {
                    return true;
                }
            }

            ScopeNameKeyIds[Count] = scopeNameKeyId;
            HostKeyIds[Count] = hostKeyId;
            Count++;
            return true;
        }
    }

    public unsafe struct TechnologyScopeBindingAuthoring
    {
        public const int Capacity = 8;

        public fixed int ScopeNameKeyIds[Capacity];
        public fixed int HostKeyIds[Capacity];
        public int Count;

        public bool TryAdd(int scopeNameKeyId, int hostKeyId)
        {
            if (scopeNameKeyId <= 0 || hostKeyId <= 0 || Count >= Capacity)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ScopeNameKeyIds[i] == scopeNameKeyId && HostKeyIds[i] == hostKeyId)
                {
                    return true;
                }
            }

            ScopeNameKeyIds[Count] = scopeNameKeyId;
            HostKeyIds[Count] = hostKeyId;
            Count++;
            return true;
        }
    }
}
