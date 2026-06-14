using System;

namespace Ludots.Core.Presentation.Assets
{
    [Flags]
    public enum MaterialAssetFlags : ushort
    {
        None = 0,
        Transparent = 1 << 0,
        DoubleSided = 1 << 1,
        Unlit = 1 << 2,
    }

    public enum MaterialAssetDomain : byte
    {
        Surface = 0,
    }

    public readonly struct MaterialAssetDescriptor
    {
        public MaterialAssetDescriptor(int id, MaterialAssetDomain domain, string[] sourceUris, MaterialAssetFlags flags)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (sourceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceUris));
            }

            Id = id;
            Domain = domain;
            SourceUris = sourceUris;
            Flags = flags;
        }

        public int Id { get; }

        public MaterialAssetDomain Domain { get; }

        public string[] SourceUris { get; }

        public MaterialAssetFlags Flags { get; }
    }
}
