using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// Abstraction over a node graph (transport network, board, nav graph) that yields
    /// candidate node positions near an origin. Keeps EQS decoupled from concrete
    /// navigation / transport implementations.
    /// </summary>
    public interface IEqsNodeSource
    {
        /// <summary>
        /// Write world positions of nodes within <paramref name="radiusCm"/> of <paramref name="center"/>.
        /// Returns count written to <paramref name="positions"/>.
        /// </summary>
        int QueryNodePositions(WorldCmInt2 center, int radiusCm, Span<WorldCmInt2> positions);
    }
}
