using System;

namespace Ludots.Core.Knowledge
{
    public readonly struct KnowledgeProjectionMaintenancePolicy
    {
        public static KnowledgeProjectionMaintenancePolicy Manual => default;

        public KnowledgeProjectionMaintenancePolicy(
            int expirePeriodTicks,
            int compactPeriodTicks,
            int compactInactivePermilleThreshold,
            int compactInactiveCountThreshold)
        {
            if (expirePeriodTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expirePeriodTicks));
            }

            if (compactPeriodTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(compactPeriodTicks));
            }

            if ((uint)compactInactivePermilleThreshold > 1000u)
            {
                throw new ArgumentOutOfRangeException(nameof(compactInactivePermilleThreshold));
            }

            if (compactInactiveCountThreshold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(compactInactiveCountThreshold));
            }

            ExpirePeriodTicks = expirePeriodTicks;
            CompactPeriodTicks = compactPeriodTicks;
            CompactInactivePermilleThreshold = compactInactivePermilleThreshold;
            CompactInactiveCountThreshold = compactInactiveCountThreshold;
        }

        public readonly int ExpirePeriodTicks;
        public readonly int CompactPeriodTicks;
        public readonly int CompactInactivePermilleThreshold;
        public readonly int CompactInactiveCountThreshold;

        public bool ShouldExpire(int currentTick, int lastExpireTick)
        {
            return IsDue(ExpirePeriodTicks, currentTick, lastExpireTick);
        }

        public bool ShouldCompact(int currentTick, int lastCompactTick, int activeCount, int physicalCount)
        {
            if (!IsDue(CompactPeriodTicks, currentTick, lastCompactTick))
            {
                return false;
            }

            int inactiveCount = physicalCount - activeCount;
            if (inactiveCount <= 0)
            {
                return false;
            }

            if (inactiveCount >= CompactInactiveCountThreshold)
            {
                return true;
            }

            if (physicalCount <= 0)
            {
                return false;
            }

            int inactivePermille = (int)(((long)inactiveCount * 1000L) / physicalCount);
            return inactivePermille >= CompactInactivePermilleThreshold;
        }

        private static bool IsDue(int periodTicks, int currentTick, int lastTick)
        {
            if (periodTicks <= 0)
            {
                return false;
            }

            if (lastTick == int.MinValue)
            {
                return true;
            }

            return currentTick - lastTick >= periodTicks;
        }
    }

    public readonly struct KnowledgeProjectionMaintenanceResult
    {
        public KnowledgeProjectionMaintenanceResult(
            int expiredCount,
            int compactedCount,
            int activeCount,
            int physicalRecordCount,
            int recordCapacity)
        {
            ExpiredCount = expiredCount;
            CompactedCount = compactedCount;
            ActiveCount = activeCount;
            PhysicalRecordCount = physicalRecordCount;
            RecordCapacity = recordCapacity;
        }

        public readonly int ExpiredCount;
        public readonly int CompactedCount;
        public readonly int ActiveCount;
        public readonly int PhysicalRecordCount;
        public readonly int RecordCapacity;
    }
}
