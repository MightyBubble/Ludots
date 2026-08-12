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
        public const int LegacyEmbeddedTagIdSpaceBridge = 256;

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

        /// <summary>Hot-path accessor; caller must have already bound a store.</summary>
        internal static GasWorldColumnStore ActiveStoreUnchecked => _store!;

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
        /// Tests-only bootstrap: freeze the legacy 64/256 plan if needed and bind a world store.
        /// Production must use <see cref="FreezeEnsureStoreAndSealFromRegistries"/>.
        /// </summary>
        public static GasWorldColumnStore EnsureLegacyPlanAndStoreForTests(
            int entityRowCapacity = DefaultEntityRowCapacity)
        {
            if (!_frozen)
            {
                Freeze(GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline());
            }

            return EnsureStore(Plan, entityRowCapacity);
        }

        /// <summary>
        /// Production commit after mod/config registration closes: freeze from dense registry
        /// counts, bind the world store, seal gameplay growth, and fail-closed on P3 bridges.
        /// </summary>
        public static GasWorldColumnStore FreezeEnsureStoreAndSealFromRegistries(
            int entityRowCapacity = DefaultEntityRowCapacity,
            GasLoadTimeCapacityRounding rounding = GasLoadTimeCapacityRounding.WordAlignTags)
        {
            if (_frozen)
            {
                throw new InvalidOperationException(
                    "GasLoadTimeCapacityPlan is already frozen. Production freeze runs once after registration.");
            }

            var plan = FreezeFromRegistries(rounding);
            var store = EnsureStore(plan, entityRowCapacity);
            store.SealGameplay();
            return store;
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

            EnsureP3BitmapBridge(plan);

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

            EnsureP3BitmapBridge(plan);
            _plan = plan;
            _frozen = true;
            return plan;
        }

        private static void EnsureP3BitmapBridge(GasLoadTimeCapacityPlan plan)
        {
            // Nav TagBits256 / KnowledgeIdMask256 / TagDisplayTable remain 256 until RFC-0066 P3.
            // Fail closed when content needs a wider tag universe — never silent truncate.
            if (plan.TagIdSpace > LegacyEmbeddedTagIdSpaceBridge)
            {
                throw new InvalidOperationException(
                    $"Frozen tag id space {plan.TagIdSpace} exceeds P3 bridge ceiling " +
                    $"{LegacyEmbeddedTagIdSpaceBridge} (TagBits256 / KnowledgeIdMask256 / TagDisplayTable). " +
                    "Complete RFC-0066 P3 cross-domain bitmaps before raising content past this bridge.");
            }
        }
    }
}
