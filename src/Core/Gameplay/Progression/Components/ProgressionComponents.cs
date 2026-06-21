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

}
