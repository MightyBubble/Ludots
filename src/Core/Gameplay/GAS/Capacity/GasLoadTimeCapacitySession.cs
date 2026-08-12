using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    /// <summary>
    /// Process-wide freeze gate for the load-time capacity plan.
    /// World column stores (P1+) must size exclusively from <see cref="Plan"/>.
    /// </summary>
    public static class GasLoadTimeCapacitySession
    {
        private static GasLoadTimeCapacityPlan? _plan;
        private static bool _frozen;

        public static bool IsFrozen => _frozen;

        public static GasLoadTimeCapacityPlan Plan =>
            _plan ?? throw new InvalidOperationException(
                "GasLoadTimeCapacityPlan is not frozen. Freeze after mod registration, before gameplay.");

        public static void ClearForTests()
        {
            _plan = null;
            _frozen = false;
        }

        public static GasLoadTimeCapacityPlan FreezeFromRegistries(
            GasLoadTimeCapacityRounding rounding = GasLoadTimeCapacityRounding.WordAlignTags)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("GasLoadTimeCapacityPlan is already frozen.");
            }

            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(
                AttributeRegistry.RegisteredCount,
                TagRegistry.RegisteredCount,
                rounding);

            // P0 bridge: still reject plans that exceed today's embedded component contracts.
            // P1+ replaces this with world-store allocation sized to the plan.
            EnsureLegacyEmbeddedCeiling(plan);

            _plan = plan;
            _frozen = true;
            return plan;
        }

        public static GasLoadTimeCapacityPlan Freeze(GasLoadTimeCapacityPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (_frozen)
            {
                throw new InvalidOperationException("GasLoadTimeCapacityPlan is already frozen.");
            }

            EnsureLegacyEmbeddedCeiling(plan);
            _plan = plan;
            _frozen = true;
            return plan;
        }

        private static void EnsureLegacyEmbeddedCeiling(GasLoadTimeCapacityPlan plan)
        {
            if (plan.AttributeSlotCount > AttributeRegistry.MaxAttributes)
            {
                throw new InvalidOperationException(
                    $"Frozen attribute slots {plan.AttributeSlotCount} exceed legacy AttributeBuffer ceiling " +
                    $"{AttributeRegistry.MaxAttributes}. Complete RFC-0066 P1 world attribute columns before raising content past this bridge.");
            }

            if (plan.TagIdSpace > TagRegistry.MaxTags)
            {
                throw new InvalidOperationException(
                    $"Frozen tag id space {plan.TagIdSpace} exceeds legacy GameplayTagContainer ceiling " +
                    $"{TagRegistry.MaxTags}. Complete RFC-0066 P2/P3 world tag columns before raising content past this bridge.");
            }
        }
    }
}
