namespace Sango.Content.MapBin;

/// <summary>Embedded PNG of one season's base color texture; only present in version 5 streams.</summary>
public sealed record MapBaseColorTexture(int Width, int Height, byte[] PngBytes);

/// <summary>
/// Port of MapBaseColor.OnLoad/OnSave. The stream contribution is version dependent: version &gt; 5
/// carries nothing (the four season textures live in sidecar files "BaseTex/BaseMap{0..3}" next to
/// the bin), version 5 embeds four optional PNG payloads, versions &lt; 5 store four name strings.
/// OnSave never writes to the stream — Unity exports RenderTextures as sidecar PNGs instead — so
/// Write is intentionally empty.
/// </summary>
public sealed record MapBaseColorSection
{
    public MapBaseColorTexture?[] EmbeddedTextures { get; init; } = new MapBaseColorTexture?[4];
    public string[]? LegacyNames { get; init; }

    public static MapBaseColorSection Read(int version, BinaryReader reader)
    {
        if (version > 5)
            return new MapBaseColorSection();

        if (version == 5)
        {
            var textures = new MapBaseColorTexture?[4];
            for (int i = 0; i < 4; i++)
            {
                int length = reader.ReadInt32();
                if (length > 0)
                {
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    textures[i] = new MapBaseColorTexture(width, height, reader.ReadBytes(length));
                }
            }
            return new MapBaseColorSection { EmbeddedTextures = textures };
        }

        var names = new string[4];
        for (int i = 0; i < 4; i++)
            names[i] = reader.ReadString();
        return new MapBaseColorSection { LegacyNames = names };
    }

    public void Write(BinaryWriter writer)
    {
    }
}
