using System;

namespace Ludots.Platform.Abstractions
{
    [Flags]
    public enum MaterialAssetFlags : ushort
    {
        None = 0,
        Transparent = 1 << 0,
        DoubleSided = 1 << 1,
        Unlit = 1 << 2,
        Cutout = 1 << 3,
        Additive = 1 << 4,
    }

    public enum MaterialBlendMode : byte
    {
        Opaque = 0,
        Cutout = 1,
        AlphaBlend = 2,
        Additive = 3,
    }

    public static class MaterialBlendModeResolver
    {
        private const MaterialAssetFlags ExclusiveBlendFlags =
            MaterialAssetFlags.Transparent | MaterialAssetFlags.Cutout | MaterialAssetFlags.Additive;

        public static MaterialBlendMode Resolve(MaterialAssetFlags flags)
        {
            MaterialAssetFlags blend = flags & ExclusiveBlendFlags;
            return blend switch
            {
                MaterialAssetFlags.None => MaterialBlendMode.Opaque,
                MaterialAssetFlags.Cutout => MaterialBlendMode.Cutout,
                MaterialAssetFlags.Transparent => MaterialBlendMode.AlphaBlend,
                MaterialAssetFlags.Additive => MaterialBlendMode.Additive,
                _ => throw new InvalidOperationException(
                    $"Material flags '{flags}' combine mutually exclusive blend modes; use exactly one of Cutout, Transparent/AlphaBlend, or Additive (or none for Opaque)."),
            };
        }
    }

    public enum MaterialAssetDomain : byte
    {
        Surface = 0,
    }

    public readonly struct MaterialAssetDescriptor
    {
        public MaterialAssetDescriptor(int id, MaterialAssetDomain domain, string[] sourceUris, MaterialAssetFlags flags, float roughness = DefaultRoughness, float metalness = DefaultMetalness)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (sourceUris == null)
            {
                throw new ArgumentNullException(nameof(sourceUris));
            }

            if (roughness < 0f || roughness > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(roughness), roughness, "Roughness must be within [0, 1].");
            }

            if (metalness < 0f || metalness > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(metalness), metalness, "Metalness must be within [0, 1].");
            }

            Id = id;
            Domain = domain;
            SourceUris = sourceUris;
            Flags = flags;
            Roughness = roughness;
            Metalness = metalness;
        }

        public const float DefaultRoughness = 0.85f;
        public const float DefaultMetalness = 0f;

        public int Id { get; }

        public MaterialAssetDomain Domain { get; }

        public string[] SourceUris { get; }

        public MaterialAssetFlags Flags { get; }

        public float Roughness { get; }

        public float Metalness { get; }
    }
}
