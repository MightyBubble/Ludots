using System;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>Selection strategies over scored EQS candidates.</summary>
    public static class EqsSelection
    {
        /// <summary>Return the highest-scoring non-filtered candidate. Deterministic tie-break by index.</summary>
        public static bool Best(ReadOnlySpan<EqsItem> items, out EqsItem best)
        {
            best = default;
            bool found = false;
            float bestScore = float.MinValue;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Filtered)
                {
                    continue;
                }

                if (!found || items[i].Score > bestScore)
                {
                    found = true;
                    bestScore = items[i].Score;
                    best = items[i];
                }
            }

            return found;
        }

        /// <summary>
        /// Write the top-N non-filtered candidates (descending score) into <paramref name="destination"/>.
        /// Returns count written. Uses in-place selection over a copy of indices.
        /// </summary>
        public static int TopN(ReadOnlySpan<EqsItem> items, Span<EqsItem> destination)
        {
            int n = destination.Length;
            int written = 0;

            // Selection sort for top-N (n is small for AI use cases).
            Span<bool> used = stackalloc bool[items.Length <= 256 ? items.Length : 256];
            int limit = Math.Min(items.Length, used.Length);

            for (int slot = 0; slot < n; slot++)
            {
                int bestIdx = -1;
                float bestScore = float.MinValue;
                for (int i = 0; i < limit; i++)
                {
                    if (used[i] || items[i].Filtered)
                    {
                        continue;
                    }

                    if (bestIdx < 0 || items[i].Score > bestScore)
                    {
                        bestIdx = i;
                        bestScore = items[i].Score;
                    }
                }

                if (bestIdx < 0)
                {
                    break;
                }

                used[bestIdx] = true;
                destination[written++] = items[bestIdx];
            }

            return written;
        }

        /// <summary>Count non-filtered candidates whose score is at or above <paramref name="threshold"/>.</summary>
        public static int CountAboveThreshold(ReadOnlySpan<EqsItem> items, float threshold)
        {
            int count = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (!items[i].Filtered && items[i].Score >= threshold)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
