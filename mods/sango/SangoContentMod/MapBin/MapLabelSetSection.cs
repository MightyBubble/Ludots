using System.Numerics;

namespace Sango.Content.MapBin;

/// <summary>
/// Port of the MapLabelSet.OnLoad payload (MapLabel persisted fields): world-space text label with
/// RGB color and font size. Only present in streams with version &gt;= 10.
/// </summary>
public sealed record MapLabel
{
    public string Text { get; init; } = "";
    public Vector3 Position { get; init; }
    public SangoRgb32 Color { get; init; }
    public int FontSize { get; init; }
}

/// <summary>Port of MapLabelSet.OnLoad/OnSave: the count-prefixed list of map labels.</summary>
public sealed record MapLabelSetSection
{
    public MapLabel[] Labels { get; init; } = Array.Empty<MapLabel>();

    public static MapLabelSetSection Read(int version, BinaryReader reader)
    {
        if (version < 10)
            return new MapLabelSetSection();

        int count = reader.ReadInt32();
        var labels = new MapLabel[count];
        for (int i = 0; i < count; i++)
        {
            string text = reader.ReadString();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var color = new SangoRgb32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            int fontSize = reader.ReadInt32();
            labels[i] = new MapLabel { Text = text, Position = position, Color = color, FontSize = fontSize };
        }
        return new MapLabelSetSection { Labels = labels };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Labels.Length);
        for (int i = 0; i < Labels.Length; i++)
        {
            MapLabel label = Labels[i];
            writer.Write(label.Text);
            writer.Write(label.Position.X);
            writer.Write(label.Position.Y);
            writer.Write(label.Position.Z);
            writer.Write(label.Color.R);
            writer.Write(label.Color.G);
            writer.Write(label.Color.B);
            writer.Write(label.FontSize);
        }
    }
}
