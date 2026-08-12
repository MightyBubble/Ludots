using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

internal static class DecalPlacementBootstrap
{
    private const string RelativePath = "Presentation/decal_placements.json";
    private const string ModAssetUri =
        "RaylibVisualAtmosphereShowcaseMod:assets/Presentation/decal_placements.json";

    public static int SpawnForActiveMap(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException(
                "Decal placement requires an active map session.");

        if (!RaylibVisualAtmosphereShowcaseIds.IsShowcaseMap(session.MapId.Value))
        {
            return 0;
        }

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException(
                "Decal placement requires RuntimeEntitySpawnQueue.");

        if (engine.VFS == null)
        {
            throw new InvalidOperationException("Decal placement requires engine VFS.");
        }

        if (!engine.VFS.TryResolveFullPath(ModAssetUri, out string? fullPath) ||
            string.IsNullOrWhiteSpace(fullPath) ||
            !File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Decal placement file missing: uri='{ModAssetUri}'.");
        }

        using FileStream stream = File.OpenRead(fullPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{RelativePath} root must be an object.");
        }

        string mapId = root.GetProperty("mapId").GetString()
            ?? throw new InvalidOperationException($"{RelativePath} must declare mapId.");
        if (!string.Equals(mapId, session.MapId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{RelativePath} mapId='{mapId}' does not match active map '{session.MapId.Value}'.");
        }

        if (!root.TryGetProperty("placements", out JsonElement placements) ||
            placements.ValueKind != JsonValueKind.Array ||
            placements.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"{RelativePath} placements must be a non-empty array.");
        }

        int count = placements.GetArrayLength();
        if (spawnQueue.FreeCapacity < count)
        {
            throw new InvalidOperationException(
                $"RuntimeEntitySpawnQueue free capacity {spawnQueue.FreeCapacity} < decal count {count}.");
        }

        for (int i = 0; i < count; i++)
        {
            JsonElement item = placements[i];
            string templateId = item.GetProperty("templateId").GetString()
                ?? throw new InvalidOperationException($"{RelativePath} placements[{i}] missing templateId.");
            float xCm = item.GetProperty("xCm").GetSingle();
            float yCm = item.GetProperty("yCm").GetSingle();
            short heightCm = IslandTerrainGenerator.HeightCmAt(xCm, yCm);
            if (heightCm <= IslandTerrainGenerator.SeaLevelCm)
            {
                throw new InvalidOperationException(
                    $"{RelativePath} placements[{i}] template='{templateId}' at ({xCm.ToString(CultureInfo.InvariantCulture)},{yCm.ToString(CultureInfo.InvariantCulture)}) is at/below sea (heightCm={heightCm}, sea={IslandTerrainGenerator.SeaLevelCm}).");
            }

            Fix64Vec2 world = Fix64Vec2.FromInt(
                (int)MathF.Round(xCm),
                (int)MathF.Round(yCm));

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = templateId,
                MapId = session.MapId,
                WorldPositionCm = world,
                HasWorldPosition = 1,
                ComponentPatches = Array.Empty<RuntimeEntitySpawnComponentPatch>(),
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                throw new InvalidOperationException(
                    $"Failed to enqueue decal spawn [{i}] template='{templateId}' at ({xCm.ToString(CultureInfo.InvariantCulture)},{yCm.ToString(CultureInfo.InvariantCulture)}).");
            }
        }

        return count;
    }
}
