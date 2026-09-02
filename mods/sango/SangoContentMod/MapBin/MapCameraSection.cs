namespace Sango.Content.MapBin;

/// <summary>
/// Port of MapCamera.OnLoad/OnSave. Only the camera border margin (source field name: safeBoder)
/// is persisted, and only from version 9 on; earlier streams carry nothing for the camera except
/// 17 discarded floats in version &le; 2.
/// </summary>
public sealed record MapCameraSection
{
    private const float DefaultSafeBorder = 560f;

    public float SafeBorder { get; init; } = DefaultSafeBorder;

    public static MapCameraSection Read(int version, BinaryReader reader)
    {
        if (version <= 2)
        {
            for (int i = 0; i < 17; i++)
                _ = reader.ReadSingle();
        }

        return new MapCameraSection { SafeBorder = version >= 9 ? reader.ReadSingle() : DefaultSafeBorder };
    }

    public void Write(BinaryWriter writer) => writer.Write(SafeBorder);
}
