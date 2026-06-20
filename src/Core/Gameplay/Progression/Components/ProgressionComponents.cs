using Arch.Core;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.Progression.Components
{
    public unsafe struct ProgressionStateBuffer
    {
        public const int Capacity = 64;

        public fixed int ProgressionIds[Capacity];
        public fixed int ProgressionLevels[Capacity];
        public int Count;
        public uint Revision;

        public readonly bool HasCompleted(int progressionId)
            => GetLevel(progressionId) >= 1;

        public readonly int GetLevel(int progressionId)
        {
            if (progressionId <= 0)
            {
                return 0;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ProgressionIds[i] == progressionId)
                {
                    return ProgressionLevels[i];
                }
            }

            return 0;
        }

        public bool TryComplete(int progressionId)
            => TrySetLevelAtLeast(progressionId, 1);

        public bool TrySetLevel(int progressionId, int level)
        {
            if (progressionId <= 0 || level <= 0)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (ProgressionIds[i] != progressionId)
                {
                    continue;
                }

                if (ProgressionLevels[i] == level)
                {
                    return true;
                }

                ProgressionLevels[i] = level;
                Revision++;
                return true;
            }

            if (Count >= Capacity)
            {
                return false;
            }

            ProgressionIds[Count] = progressionId;
            ProgressionLevels[Count] = level;
            Count++;
            Revision++;
            return true;
        }

        public bool TryAddLevel(int progressionId, int delta)
        {
            if (progressionId <= 0 || delta <= 0)
            {
                return false;
            }

            int nextLevel = GetLevel(progressionId) + delta;
            if (nextLevel <= 0)
            {
                return false;
            }

            return TrySetLevel(progressionId, nextLevel);
        }

        public bool TrySetLevelAtLeast(int progressionId, int level)
        {
            if (progressionId <= 0 || level <= 0)
            {
                return false;
            }

            if (GetLevel(progressionId) >= level)
            {
                return true;
            }

            return TrySetLevel(progressionId, level);
        }

        public bool HasLevelAtLeast(int progressionId, int level)
        {
            if (level <= 0)
            {
                level = 1;
            }

            return GetLevel(progressionId) >= level;
        }
    }

    public unsafe struct ProgressionScopeRefBuffer
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

    public struct ProgressionScopeMembershipRevision
    {
        public uint Revision;
    }

    public struct ProgressionScopeMemberTag
    {
    }

    public unsafe struct ProgressionScopeHostAuthoring
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

    public unsafe struct ProgressionScopeBindingAuthoring
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
