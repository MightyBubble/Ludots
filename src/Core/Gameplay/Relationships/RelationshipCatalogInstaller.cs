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
            RelationshipReasonRegistry reasons,
            EntityCollectionStore collections)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(flags);
            ArgumentNullException.ThrowIfNull(bands);
            ArgumentNullException.ThrowIfNull(reasons);
            ArgumentNullException.ThrowIfNull(collections);

            RegisterCatalog(catalog, types, metrics, flags, bands, reasons);
            return RelationshipCatalogRuntime.Compile(catalog, types, metrics, collections);
        }

        public static void RegisterCatalog(
            RelationshipCatalogConfig catalog,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands,
            RelationshipReasonRegistry reasons)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(flags);
            ArgumentNullException.ThrowIfNull(bands);
            ArgumentNullException.ThrowIfNull(reasons);

            for (int i = 0; i < catalog.Types.Count; i++)
            {
                RelationshipTypeConfig type = catalog.Types[i];
                types.Register(type.Id, type.IsSymmetric);
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

            bands.Clear();
            for (int i = 0; i < catalog.Bands.Count; i++)
            {
                RelationshipBandConfig band = catalog.Bands[i];
                int typeId = types.GetId(band.TypeId);
                int metricId = metrics.GetId(band.MetricId);
                int flagId = flags.GetId(band.FlagId);
                if (!Enum.TryParse(band.Comparison, ignoreCase: true, out RelationshipBandComparison comparison))
                {
                    throw new InvalidOperationException(
                        $"Unknown relationship band comparison '{band.Comparison}' for band '{band.Id}'.");
                }

                bands.Register(new RelationshipBandDefinition(typeId, metricId, flagId, band.Threshold, comparison));
            }

            for (int i = 0; i < catalog.Reasons.Count; i++)
            {
                reasons.Register(catalog.Reasons[i].Id);
            }
        }
    }
}
