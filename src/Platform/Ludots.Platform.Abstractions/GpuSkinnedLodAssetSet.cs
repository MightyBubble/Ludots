using System;

namespace Ludots.Platform.Abstractions
{
    public readonly struct MeshLodAssetIds
    {
        public MeshLodAssetIds(int high, int medium, int low)
        {
            if (high <= 0) throw new ArgumentOutOfRangeException(nameof(high));
            if (medium <= 0) throw new ArgumentOutOfRangeException(nameof(medium));
            if (low <= 0) throw new ArgumentOutOfRangeException(nameof(low));

            High = high;
            Medium = medium;
            Low = low;
        }

        public int High { get; }

        public int Medium { get; }

        public int Low { get; }

        public bool IsConfigured => High > 0 && Medium > 0 && Low > 0;

        public int Resolve(LODLevel lod)
        {
            int assetId = lod switch
            {
                LODLevel.High => High,
                LODLevel.Medium => Medium,
                LODLevel.Low => Low,
                _ => throw new ArgumentOutOfRangeException(nameof(lod), lod, "Unknown presentation LOD level."),
            };

            if (assetId <= 0)
            {
                throw new InvalidOperationException("GPU-skinned mesh LOD assets are not configured.");
            }

            return assetId;
        }
    }

    public readonly struct GpuSkinnedLodAssetSet
    {
        public GpuSkinnedLodAssetSet(in MeshLodAssetIds main, in MeshLodAssetIds shadow)
        {
            if (!main.IsConfigured) throw new ArgumentException("Main mesh LOD assets must all be configured.", nameof(main));
            if (!shadow.IsConfigured) throw new ArgumentException("Shadow mesh LOD assets must all be configured.", nameof(shadow));

            Main = main;
            Shadow = shadow;
        }

        public MeshLodAssetIds Main { get; }

        public MeshLodAssetIds Shadow { get; }

        public bool IsConfigured => Main.IsConfigured && Shadow.IsConfigured;

        public int ResolveMain(LODLevel lod) => Main.Resolve(lod);

        public int ResolveShadow(LODLevel lod) => Shadow.Resolve(lod);
    }
}
