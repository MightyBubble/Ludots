using System.Numerics;

namespace Ludots.Core.Fields
{
    public abstract class FieldLayerData
    {
        protected FieldLayerData(FieldLayerDefinition definition)
        {
            Definition = definition;
        }

        public FieldLayerDefinition Definition { get; }
        public FieldLayerId LayerId => Definition.Id;
        public string LayerKey => Definition.Key;
        public bool Persistent => Definition.Persistent;
    }

    public sealed class DiscreteIdFieldLayerData : FieldLayerData
    {
        public DiscreteIdFieldLayerData(FieldLayerDefinition definition)
            : base(definition)
        {
            Regions = new RegionIdRegistry(definition.Key, definition.MaxRegionIds);
            Field = new ChunkedField2D<int>(
                new FieldGridSpec2D(definition.CellSizeCm, definition.ChunkSizeCells),
                defaultValue: 0);
        }

        public RegionIdRegistry Regions { get; }
        public ChunkedField2D<int> Field { get; }

        /// <summary>
        /// Query-time footprint enumeration: fills <paramref name="destination"/> with the
        /// cells of one region. Returns the number written; a result shorter than the
        /// region's cell count means the buffer was too small (re-query with a larger span).
        /// </summary>
        public int EnumerateRegionCells(int regionId, System.Span<FieldCell2D> destination)
        {
            int written = 0;
            for (int chunkIndex = 0; chunkIndex < Field.ChunkCount && written < destination.Length; chunkIndex++)
            {
                FieldChunk2D<int> chunk = Field.GetChunkAt(chunkIndex);
                for (int local = 0; local < chunk.CellCount && written < destination.Length; local++)
                {
                    if (chunk.Get(local) == regionId)
                    {
                        destination[written++] = Field.Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, local);
                    }
                }
            }

            return written;
        }
    }

    public sealed class Scalar32FieldLayerData : FieldLayerData
    {
        public Scalar32FieldLayerData(FieldLayerDefinition definition)
            : base(definition)
        {
            Field = new ChunkedField2D<float>(
                new FieldGridSpec2D(definition.CellSizeCm, definition.ChunkSizeCells),
                definition.DefaultValue.Scalar);
        }

        public ChunkedField2D<float> Field { get; }
    }

    public sealed class Vector2FieldLayerData : FieldLayerData
    {
        public Vector2FieldLayerData(FieldLayerDefinition definition)
            : base(definition)
        {
            Field = new ChunkedField2D<Vector2>(
                new FieldGridSpec2D(definition.CellSizeCm, definition.ChunkSizeCells),
                definition.DefaultValue.Vector2);
        }

        public ChunkedField2D<Vector2> Field { get; }
    }

    public sealed class Vector3FieldLayerData : FieldLayerData
    {
        public Vector3FieldLayerData(FieldLayerDefinition definition)
            : base(definition)
        {
            Field = new ChunkedField2D<Vector3>(
                new FieldGridSpec2D(definition.CellSizeCm, definition.ChunkSizeCells),
                definition.DefaultValue.Vector3);
        }

        public ChunkedField2D<Vector3> Field { get; }
    }
}
