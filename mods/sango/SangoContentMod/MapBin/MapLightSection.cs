using System.Numerics;

namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapLight.OnLoad/OnSave. Fixed four season slots (Autumn, Spring, Summer, Winter); each
/// slot is 11 floats: direction xyz, light color rgb, intensity, shadow color rgb, shadow strength.
/// </summary>
public sealed record MapLightSection
{
    public Vector3[] LightDirection { get; init; } =
    {
        new(45f, 270f, 0f), new(45f, 270f, 0f), new(45f, 270f, 0f), new(45f, 270f, 0f),
    };
    public SangoRgbF[] LightColor { get; init; } = { new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f), new(1f, 1f, 1f) };
    public float[] LightIntensity { get; init; } = { 1f, 1f, 1f, 1f };
    public SangoRgbF[] ShadowColor { get; init; } = { new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f) };
    public float[] ShadowStrength { get; init; } = { 1f, 1f, 1f, 1f };

    public static MapLightSection Read(BinaryReader reader)
    {
        var direction = new Vector3[4];
        var lightColor = new SangoRgbF[4];
        var intensity = new float[4];
        var shadowColor = new SangoRgbF[4];
        var shadowStrength = new float[4];
        for (int i = 0; i < 4; i++)
        {
            direction[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            lightColor[i] = new SangoRgbF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            intensity[i] = reader.ReadSingle();
            shadowColor[i] = new SangoRgbF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            shadowStrength[i] = reader.ReadSingle();
        }
        return new MapLightSection
        {
            LightDirection = direction,
            LightColor = lightColor,
            LightIntensity = intensity,
            ShadowColor = shadowColor,
            ShadowStrength = shadowStrength,
        };
    }

    public void Write(BinaryWriter writer)
    {
        for (int i = 0; i < LightDirection.Length; i++)
        {
            writer.Write(LightDirection[i].X);
            writer.Write(LightDirection[i].Y);
            writer.Write(LightDirection[i].Z);
            writer.Write(LightColor[i].R);
            writer.Write(LightColor[i].G);
            writer.Write(LightColor[i].B);
            writer.Write(LightIntensity[i]);
            writer.Write(ShadowColor[i].R);
            writer.Write(ShadowColor[i].G);
            writer.Write(ShadowColor[i].B);
            writer.Write(ShadowStrength[i]);
        }
    }
}
