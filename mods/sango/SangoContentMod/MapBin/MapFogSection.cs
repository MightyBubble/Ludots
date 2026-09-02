namespace Sango.Content.MapBin;

/// <summary>Port of MapFog.OnLoad/OnSave. Fixed four season slots; each slot is 6 floats (rgb, start, end, density).</summary>
public sealed record MapFogSection
{
    public SangoRgbF[] FogColor { get; init; } = { new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f) };
    public float[] FogStart { get; init; } = { 546.5f, 546.5f, 546.5f, 546.5f };
    public float[] FogEnd { get; init; } = { 1068.8f, 1068.8f, 1068.8f, 1068.8f };
    public float[] FogDensity { get; init; } = { 11f, 11f, 11f, 11f };

    public static MapFogSection Read(BinaryReader reader)
    {
        var color = new SangoRgbF[4];
        var start = new float[4];
        var end = new float[4];
        var density = new float[4];
        for (int i = 0; i < 4; i++)
        {
            color[i] = new SangoRgbF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            start[i] = reader.ReadSingle();
            end[i] = reader.ReadSingle();
            density[i] = reader.ReadSingle();
        }
        return new MapFogSection { FogColor = color, FogStart = start, FogEnd = end, FogDensity = density };
    }

    public void Write(BinaryWriter writer)
    {
        for (int i = 0; i < FogColor.Length; i++)
        {
            writer.Write(FogColor[i].R);
            writer.Write(FogColor[i].G);
            writer.Write(FogColor[i].B);
            writer.Write(FogStart[i]);
            writer.Write(FogEnd[i]);
            writer.Write(FogDensity[i]);
        }
    }
}
