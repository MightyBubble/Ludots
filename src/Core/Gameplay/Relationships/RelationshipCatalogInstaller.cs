using System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    public static class RelationshipCatalogInstaller
    {
        public static RelationshipCatalogRuntime Install(
            RelationshipCatalogConfig catalog,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands,
            EntityCollectionStore collections)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(flags);
            ArgumentNullException.ThrowIfNull(bands);
            ArgumentNullException.ThrowIfNull(collections);

            RegisterCatalog(catalog, types, metrics, flags, bands);
            return RelationshipCatalogRuntime.Compile(catalog, types, metrics, collections);
        }

        public static void RegisterCatalog(
            RelationshipCatalogConfig catalog,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(flags);
            ArgumentNullException.ThrowIfNull(bands);

            for (int i = 0; i < catalog.Types.Count; i++)
            {
                RelationshipTypeConfig type = catalog.Types[i];
                types.Register(type.Id, type.IsSymmetric);
            }

            if (catalog.Stance != null)
            {
                // Stance keys are relationship types carried on rep→rep / team→team edges (RFC-0065 DEC-3).
                // An explicit entry in Types wins because Register is first-registration idempotent.
                for (int i = 0; i < catalog.Stance.StanceTypes.Count; i++)
                {
                    types.Register(catalog.Stance.StanceTypes[i]);
                }
            }

            for (int i = 0; i < catalog.Metrics.Count; i++)
            {
                RelationshipMetricConfig metric = catalog.Metrics[i];
                metrics.Register(metric.Id, metric.MinValue, metric.MaxValue, metric.DefaultValue);
            }

            for (int i = 0; i < catalog.Flags.Count; i++)
            {
                flags.Register(catalog.Flags[i].Id);
            }

            for (int i = 0; i < catalog.Bands.Count; i++)
            {
                RelationshipBandConfig band = catalog.Bands[i];
                bands.Register(new RelationshipBandDefinition(
                    types.GetId(band.TypeId),
                    metrics.GetId(band.MetricId),
                    flags.GetId(band.FlagId),
                    band.Threshold,
                    band.Comparison));
            }
        }
    }
}
