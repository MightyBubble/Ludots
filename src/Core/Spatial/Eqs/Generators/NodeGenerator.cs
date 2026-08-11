using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Generators
{
    /// <summary>
    /// Generates candidates from a node graph (transport network / board / nav graph)
    /// via IEqsNodeSource. Range scale = radiusCm; density is defined by the graph's node spacing.
    /// </summary>
    public sealed class NodeGenerator : IEqsGenerator
    {
        private readonly IEqsNodeSource _nodeSource;
        private readonly int _radiusCm;

        public NodeGenerator(IEqsNodeSource nodeSource, int radiusCm)
        {
            _nodeSource = nodeSource ?? throw new ArgumentNullException(nameof(nodeSource));
            if (radiusCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            _radiusCm = radiusCm;
        }

        public int Generate(WorldCmInt2 origin, Span<EqsItem> buffer)
        {
            // Borrow the item buffer's positions via a temporary span of positions.
            Span<WorldCmInt2> positions = buffer.Length <= 256
                ? stackalloc WorldCmInt2[buffer.Length]
                : new WorldCmInt2[buffer.Length];

            int count = _nodeSource.QueryNodePositions(origin, _radiusCm, positions);
            for (int i = 0; i < count && i < buffer.Length; i++)
            {
                buffer[i] = new EqsItem(positions[i]);
            }

            return Math.Min(count, buffer.Length);
        }
    }
}
