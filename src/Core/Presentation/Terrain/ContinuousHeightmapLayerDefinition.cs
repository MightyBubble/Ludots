namespace Ludots.Core.Presentation.Terrain
{
    public readonly struct ContinuousHeightmapLayerDefinition
    {
        public ContinuousHeightmapLayerDefinition(int layerId, string name, int sampleOffset, int sampleCount)
        {
            LayerId = layerId;
            Name = name ?? string.Empty;
            SampleOffset = sampleOffset;
            SampleCount = sampleCount;
        }

        public int LayerId { get; }

        public string Name { get; }

        public int SampleOffset { get; }

        public int SampleCount { get; }
    }
}
