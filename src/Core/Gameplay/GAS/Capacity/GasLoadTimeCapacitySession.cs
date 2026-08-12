using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    /// <summary>
    /// Process-wide freeze gate for the load-time capacity plan.
    /// World column stores must size exclusively from <see cref="Plan"/>.
    /// </summary>
    public static class GasLoadTimeCapacitySession
    {
        public const int DefaultEntityRowCapacity = 65_536;

        private static GasLoadTimeCapacityPlan? _plan;
        private static GasWorldColumnStore? _store;
        private static bool _frozen;

        public static bool IsFrozen => _frozen;

        public static bool HasStore => _store != null && !_store.IsDisposed;

        public static GasLoadTimeCapacityPlan Plan =>
            _plan ?? throw new InvalidOperationException(
                "GasLoadTimeCapacityPlan is not frozen. Freeze after mod registration, before gameplay.");

        public static GasWorldColumnStore ActiveStore =>
            _store ?? throw new InvalidOperationException(
                "GasWorldColumnStore is not bound. Call EnsureStore after Freeze, or BindStore in tests.");

        public static void ClearForTests()
        {
            if (_store != null)
            {
                _store.Dispose();
                _store = null;
            }

            _plan = null;
            _frozen = false;
        }

        public static void BindStore(GasWorldColumnStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (store.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(GasWorldColumnStore));
            }

            if (_store != null && !ReferenceEquals(_store, store))
            {
                _store.Dispose();
            }

            _store = store;
            if (!_frozen)
            {
                _plan = store.Plan;
                _frozen = true;
            }
            else if (!ReferenceEquals(_plan, store.Plan) &&
                     (_plan!.AttributeSlotCount != store.Plan.AttributeSlotCount ||
                      _plan.TagIdSpace != store.Plan.TagIdSpace))
            {
                throw new InvalidOperationException(
                    "BindStore plan does not match the frozen GasLoadTimeCapacityPlan.");
            }
        }

        public static GasWorldColumnStore EnsureStore(GasLoadTimeCapacityPlan plan, int entityRowCapacity)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (entityRowCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityRowCapacity));
            }

            if (!_frozen)
            {
                Freeze(plan);
            }
            else if (_plan!.AttributeSlotCount != plan.AttributeSlotCount ||
                     _plan.TagIdSpace != plan.TagIdSpace)
            {
                throw new InvalidOperationException(
                    "EnsureStore plan does not match the frozen GasLoadTimeCapacityPlan.");
            }

            if (_store == null || _store.IsDisposed)
            {
                _store = new GasWorldColumnStore(Plan, entityRowCapacity);
            }
            else if (entityRowCapacity > _store.EntityRowCapacity)
            {
                _store.EnsureEntityRowCapacity(entityRowCapacity);
            }

            return _store;
        }

        /// <summary>
        /// Interim bootstrap (tests + pre-hook production): freeze legacy 64/256 plan if needed and bind a world store.
        /// Production should replace this with FreezeFromRegistries after the registration window closes.
        /// </summary>
        public static GasWorldColumnStore EnsureLegacyPlanAndStore(
            int entityRowCapacity = DefaultEntityRowCapacity)
        {
            if (!_frozen)
            {
                Freeze(GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline());
            }

            return EnsureStore(Plan, entityRowCapacity);
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
            // Attribute slots may exceed the old embedded 64 once a world column store is in use.
            // Tag bit containers remain on the P0/P2 legacy ceiling until tag columns land.
            if (plan.TagIdSpace > TagRegistry.MaxTags)
            {
                throw new InvalidOperationException(
                    $"Frozen tag id space {plan.TagIdSpace} exceeds legacy GameplayTagContainer ceiling " +
                    $"{TagRegistry.MaxTags}. Complete RFC-0066 P2/P3 world tag columns before raising content past this bridge.");
            }
        }
    }
}
