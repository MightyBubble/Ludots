namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapSkyBox.SkyArea: a world-space rect plus per-season sky texture names (Autumn,
/// Spring, Summer, Winter).
/// </summary>
public sealed record MapSkyArea
{
    public SangoRectF Bounds { get; init; }
    public string[] SeasonTextureNames { get; init; } = new string[4];
}

/// <summary>
/// Port of MapSkyBox.OnLoad/OnSave. Note the source quirk: OnSave writes the never-assigned fields
/// sky_blend_start/sky_blend_end (-200f/-150f), not the blendStart/blendEnd values that OnLoad
/// reads back — Write reproduces that exactly so its bytes match MapRender.SaveMap.
/// </summary>
public sealed record MapSkyBoxSection
{
    private const float SavedBlendStart = -200f;
    private const float SavedBlendEnd = -150f;

    public float BlendStart { get; init; }
    public float BlendEnd { get; init; }
    public MapSkyArea[] Areas { get; init; } = Array.Empty<MapSkyArea>();

    public static MapSkyBoxSection Read(int version, BinaryReader reader)
    {
        if (version == 1)
        {
            float blendStart = reader.ReadSingle();
            float blendEnd = reader.ReadSingle();
            int length = reader.ReadInt32();
            for (int i = 0; i < length; i++)
                _ = reader.ReadString();
            _ = reader.ReadInt32();
            return new MapSkyBoxSection { BlendStart = blendStart, BlendEnd = blendEnd };
        }

        var blendStartValue = reader.ReadSingle();
        var blendEndValue = reader.ReadSingle();
        int areaCount = reader.ReadInt32();
        var areas = new MapSkyArea[areaCount];
        for (int i = 0; i < areaCount; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float w = reader.ReadSingle();
            float h = reader.ReadSingle();
            string[] textureNames = new string[4];
            for (int j = 0; j < 4; j++)
                textureNames[j] = reader.ReadString();
            areas[i] = new MapSkyArea { Bounds = new SangoRectF(x, y, w, h), SeasonTextureNames = textureNames };
        }
        return new MapSkyBoxSection { BlendStart = blendStartValue, BlendEnd = blendEndValue, Areas = areas };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(SavedBlendStart);
        writer.Write(SavedBlendEnd);
        writer.Write(Areas.Length);
        for (int i = 0; i < Areas.Length; i++)
        {
            MapSkyArea area = Areas[i];
            writer.Write(area.Bounds.X);
            writer.Write(area.Bounds.Y);
            writer.Write(area.Bounds.Width);
            writer.Write(area.Bounds.Height);
            for (int j = 0; j < 4; j++)
            {
                string name = area.SeasonTextureNames[j];
                writer.Write(string.IsNullOrEmpty(name) ? "" : name);
            }
        }
    }
}
