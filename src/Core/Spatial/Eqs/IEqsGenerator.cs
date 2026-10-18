using System;
using Ludots.Platform.Abstractions;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs
{
    public interface IEqsGenerator
    {
        /// <summary>
        /// Generate candidates around <paramref name="origin"/>.
        /// Returns count written to <paramref name="buffer"/>.
        /// </summary>
        int Generate(WorldCmInt2 origin, Span<EqsItem> buffer);
    }
}
