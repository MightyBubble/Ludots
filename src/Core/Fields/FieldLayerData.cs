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
