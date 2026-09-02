namespace Sango.Content.MapBin;

/// <summary>
/// Headless port of the DefaultMap.bin container read by MapRender.LoadMap: full per-section data
/// for one sango map. Unity-only derivatives (meshes, textures, materials, Burst jobs, normals) are
/// intentionally absent — only what OnLoad/OnSave touch survives here.
/// </summary>
public sealed record SangoMapBinArchive
{
    public const int CurrentVersion = 10;

    public int Version { get; init; }
    public string? WorkContent { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public MapGridSection Grid { get; init; } = new();
    public MapDataSection Data { get; init; } = new();
    public MapLayerSection Layer { get; init; } = new();
    public MapTerrainSection Terrain { get; init; } = new();
    public MapLightSection Light { get; init; } = new();
    public MapFogSection Fog { get; init; } = new();
    public MapBaseColorSection BaseColor { get; init; } = new();
    public MapSkyBoxSection SkyBox { get; init; } = new();
    public MapCameraSection Camera { get; init; } = new();
    public MapModelsSection Models { get; init; } = new();
    public MapLabelSetSection Labels { get; init; } = new();
}

/// <summary>
/// BinaryReader/BinaryWriter entry points mirroring MapRender.LoadMap/SaveMap. Read handles every
/// version branch 1..10 the source does; Write only emits the current (version 10) layout, exactly
/// like the source SaveMap which always writes the VERSION constant.
/// </summary>
public static class SangoMapBinIo
{
    public static SangoMapBinArchive Read(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        string? workContent = version >= 6 ? reader.ReadString() : null;
        if (version <= 2)
            _ = reader.ReadInt32();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();

        MapGridSection grid;
        if (version > 5)
        {
            // The grid loads before MapData here, so grid bounds derive from the MapData quadSize
            // field default (5), not from the value stored later in the stream.
            grid = MapGridSection.Read(version, reader, width, height, quadSize: 5);
        }
        else
        {
            grid = new MapGridSection();
        }

        MapDataSection data = MapDataSection.Read(version, reader, width, height);
        MapLayerSection layer = MapLayerSection.Read(version, reader);
        MapTerrainSection terrain = MapTerrainSection.Read(reader);
        MapLightSection light = MapLightSection.Read(reader);
        MapFogSection fog = MapFogSection.Read(reader);
        MapBaseColorSection baseColor = MapBaseColorSection.Read(version, reader);
        MapSkyBoxSection skyBox = MapSkyBoxSection.Read(version, reader);
        if (version <= 5)
            grid = MapGridSection.Read(version, reader, width, height, data.QuadSize);
        MapCameraSection camera = MapCameraSection.Read(version, reader);
        MapModelsSection models = MapModelsSection.Read(version, reader);
        MapLabelSetSection labels = MapLabelSetSection.Read(version, reader);

        return new SangoMapBinArchive
        {
            Version = version,
            WorkContent = workContent,
            Width = width,
            Height = height,
            Grid = grid,
            Data = data,
            Layer = layer,
            Terrain = terrain,
            Light = light,
            Fog = fog,
            BaseColor = baseColor,
            SkyBox = skyBox,
            Camera = camera,
            Models = models,
            Labels = labels,
        };
    }

    public static void Write(BinaryWriter writer, SangoMapBinArchive archive)
    {
        if (archive.Version != SangoMapBinArchive.CurrentVersion)
            throw new NotSupportedException(
                $"SangoMapBinIo.Write only emits the current layout (version {SangoMapBinArchive.CurrentVersion}); got version {archive.Version}.");

        writer.Write(archive.Version);
        writer.Write(archive.WorkContent!);
        writer.Write(archive.Width);
        writer.Write(archive.Height);
        archive.Grid.Write(writer);
        archive.Data.Write(writer);
        archive.Layer.Write(writer);
        archive.Terrain.Write(writer);
        archive.Light.Write(writer);
        archive.Fog.Write(writer);
        archive.BaseColor.Write(writer);
        archive.SkyBox.Write(writer);
        archive.Camera.Write(writer);
        archive.Models.Write(writer);
        archive.Labels.Write(writer);
    }
}
