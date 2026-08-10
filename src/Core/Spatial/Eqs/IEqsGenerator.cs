using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// Generator produces candidate positions for EQS query.
    /// </summary>
    public interface IEqsGenerator
    {
        /// <summary>
        /// Generate candidates around <paramref name="origin"/>.
        /// Returns count written to <paramref name="buffer"/>.
        /// </summary>
        int Generate(WorldCmInt2 origin, Span<EqsItem> buffer);
    }
}
