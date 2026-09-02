using System.Numerics;

namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapLayer.LayerData, persisted fields only: per-season terrain texture names in source
/// season order Autumn, Spring, Summer, Winter. The last layer of the array is the water layer.
/// </summary>
public sealed record MapLayerData
{
    public bool IsLit { get; init; }
    public Vector2 TextureScale { get; init; } = Vector2.One;
    public string[] DiffuseTexNames { get; init; } = new string[4];
    public string[] NormalTexNames { get; init; } = new string[4];
    public string[] MaskTexNames { get; init; } = new string[4];

    public static MapLayerData Read(int version, BinaryReader reader)
    {
        bool isLit = reader.ReadBoolean();
        float scaleX = reader.ReadSingle();
        float scaleY = reader.ReadSingle();
        string[] diffuse = new string[4];
        string[] normal = new string[4];
        string[] mask = new string[4];

        for (int i = 0; i < 4; i++)
        {
            diffuse[i] = reader.ReadString();
            if (version < 6)
                diffuse[i] = FixLegacyDiffuseName(diffuse[i]);
            normal[i] = reader.ReadString();
            mask[i] = reader.ReadString();
        }

        if (version < 7)
        {
            RotateFirstThree(diffuse);
            RotateFirstThree(normal);
            RotateFirstThree(mask);
        }

        return new MapLayerData
        {
            IsLit = isLit,
            TextureScale = new Vector2(scaleX, scaleY),
            DiffuseTexNames = diffuse,
            NormalTexNames = normal,
            MaskTexNames = mask,
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(IsLit);
        writer.Write(TextureScale.X);
        writer.Write(TextureScale.Y);
        for (int i = 0; i < DiffuseTexNames.Length; i++)
        {
            writer.Write(string.IsNullOrEmpty(DiffuseTexNames[i]) ? "" : DiffuseTexNames[i]);
            writer.Write(string.IsNullOrEmpty(NormalTexNames[i]) ? "" : NormalTexNames[i]);
            writer.Write(string.IsNullOrEmpty(MaskTexNames[i]) ? "" : MaskTexNames[i]);
        }
    }

    private static string FixLegacyDiffuseName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        string[] parts = name.Split('_');
        if (parts.Length > 1)
            return name.StartsWith("water", StringComparison.Ordinal) ? "water_" + parts[1] : "layer_" + parts[1];
        return name == "water" ? "water_0" : name;
    }

    private static void RotateFirstThree(string[] names)
    {
        string temp = names[0];
        names[0] = names[1];
        names[1] = names[2];
        names[2] = temp;
    }
}

/// <summary>Port of MapLayer.OnLoad/OnSave: terrain material layers plus the trailing water layer.</summary>
public sealed record MapLayerSection
{
    public MapLayerData[] Layers { get; init; } = Array.Empty<MapLayerData>();

    public static MapLayerSection Read(int version, BinaryReader reader)
    {
        int layerSize = reader.ReadInt32();
        var layers = new MapLayerData[layerSize];
        for (int i = 0; i < layerSize; i++)
            layers[i] = MapLayerData.Read(version, reader);
        return new MapLayerSection { Layers = layers };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Layers.Length);
        for (int i = 0; i < Layers.Length; i++)
            Layers[i].Write(writer);
    }
}
