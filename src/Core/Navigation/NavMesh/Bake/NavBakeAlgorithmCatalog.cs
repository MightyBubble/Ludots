using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Startup composition SSOT for runtime nav-bake adapters.
    /// Core always owns CDT + LayeredSpan; external adapters (e.g. Recast) are injected by hosts.
    /// </summary>
    public static class NavBakeAlgorithmCatalog
    {
        /// <summary>
        /// Compose Core adapters first (Cdt, LayeredSpan), then external adapters sorted by
        /// <see cref="NavBakeAlgorithmKind"/> with duplicate Kind fail-fast.
        /// </summary>
        public static INavBakeAlgorithm[] Compose(
            INavBakeAlgorithm cdt,
            INavBakeAlgorithm layeredSpan,
            IReadOnlyList<INavBakeAlgorithm>? externalAdapters)
        {
            if (cdt == null) throw new ArgumentNullException(nameof(cdt));
            if (layeredSpan == null) throw new ArgumentNullException(nameof(layeredSpan));
            if (cdt.Kind != NavBakeAlgorithmKind.Cdt)
            {
                throw new InvalidOperationException(
                    $"NavBakeAlgorithmCatalog core Cdt adapter must declare Kind=Cdt; got {cdt.Kind}.");
            }

            if (layeredSpan.Kind != NavBakeAlgorithmKind.LayeredSpan)
            {
                throw new InvalidOperationException(
                    $"NavBakeAlgorithmCatalog core LayeredSpan adapter must declare Kind=LayeredSpan; got {layeredSpan.Kind}.");
            }

            int externalCount = externalAdapters?.Count ?? 0;
            var composed = new INavBakeAlgorithm[2 + externalCount];
            composed[0] = cdt;
            composed[1] = layeredSpan;

            if (externalCount == 0)
            {
                return composed;
            }

            var sortedExternal = new INavBakeAlgorithm[externalCount];
            for (int i = 0; i < externalCount; i++)
            {
                INavBakeAlgorithm adapter = externalAdapters![i]
                    ?? throw new InvalidOperationException($"NavBakeAlgorithmCatalog external adapter[{i}] is null.");
                sortedExternal[i] = adapter;
            }

            Array.Sort(sortedExternal, static (a, b) => ((byte)a.Kind).CompareTo((byte)b.Kind));

            var seen = new HashSet<NavBakeAlgorithmKind>
            {
                NavBakeAlgorithmKind.Cdt,
                NavBakeAlgorithmKind.LayeredSpan
            };

            for (int i = 0; i < sortedExternal.Length; i++)
            {
                INavBakeAlgorithm adapter = sortedExternal[i];
                if (!seen.Add(adapter.Kind))
                {
                    throw new InvalidOperationException(
                        $"NavBakeAlgorithmCatalog duplicate algorithm adapter: {adapter.Kind}.");
                }

                composed[2 + i] = adapter;
            }

            return composed;
        }

        public static NavBakeAlgorithmKind[] ToOrderedKinds(IReadOnlyList<INavBakeAlgorithm> adapters)
        {
            if (adapters == null) throw new ArgumentNullException(nameof(adapters));
            var kinds = new NavBakeAlgorithmKind[adapters.Count];
            for (int i = 0; i < adapters.Count; i++)
            {
                INavBakeAlgorithm adapter = adapters[i]
                    ?? throw new InvalidOperationException($"NavBakeAlgorithmCatalog adapter[{i}] is null.");
                kinds[i] = adapter.Kind;
            }

            return kinds;
        }
    }
}
