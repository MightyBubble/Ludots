using System;
using System.Collections.Generic;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// EQS query: one generator + ordered list of tests. Run against a context to
    /// produce scored, filtered candidates. Warm path is 0-alloc (caller provides Span buffer).
    /// </summary>
    public sealed class EqsQuery
    {
        private readonly IEqsGenerator _generator;
        private readonly IEqsTest[] _tests;

        public EqsQuery(IEqsGenerator generator, params IEqsTest[] tests)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _tests = tests ?? Array.Empty<IEqsTest>();
        }

        /// <summary>
        /// Generate + score candidates into <paramref name="buffer"/>.
        /// Returns count of candidates written (including filtered — check EqsItem.Filtered).
        /// </summary>
        public int Run(in EqsContext ctx, Span<EqsItem> buffer)
        {
            int count = _generator.Generate(ctx.Origin, buffer);

            for (int t = 0; t < _tests.Length; t++)
            {
                IEqsTest test = _tests[t];
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i].Filtered)
                    {
                        continue;
                    }

                    test.Score(in ctx, ref buffer[i]);
                }
            }

            return count;
        }

        /// <summary>
        /// Run and return the single best (highest-score, non-filtered) candidate.
        /// </summary>
        public bool RunBest(in EqsContext ctx, Span<EqsItem> buffer, out EqsItem best)
        {
            int count = Run(in ctx, buffer);
            return EqsSelection.Best(buffer.Slice(0, count), out best);
        }
    }
}
