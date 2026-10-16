using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 地图 Mod 的玩家颜色。键是玩家号，值是 #rrggbb / #rrggbbaa。
/// </summary>
public static class CrowdPlayerColors
{
    public const string RelativePath = "Presentation/crowd_player_colors.json";

    public static TeamColorPalette LoadMerged(
        ConfigPipeline pipeline,
        ConfigCatalog catalog,
        ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
        JsonObject merged = pipeline.MergeDeepObjectFromCatalog(in entry, report)
            ?? throw new InvalidOperationException($"{RelativePath}: 存在目录登记但合并为空。");
        if (merged.Count == 0)
            throw new InvalidOperationException($"{RelativePath}: 存在目录登记但合并为空。");
        return Load(merged);
    }

    public static TeamColorPalette Load(JsonObject node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int max = 0;
        var rows = new List<(int PlayerId, Vector4 Color)>(node.Count);
        foreach (var pair in node)
        {
            if (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int playerId) || playerId < 1)
                throw new InvalidOperationException($"{RelativePath}: 键 \"{pair.Key}\" 必须是 ≥ 1 的玩家号。");
            if (pair.Value is not JsonValue value || !value.TryGetValue(out string? hex) || string.IsNullOrWhiteSpace(hex))
                throw new InvalidOperationException($"{RelativePath}: {pair.Key} 必须是 #rrggbb 颜色。");
            rows.Add((playerId, TeamColorPalette.ParseHex(hex, $"{RelativePath} {pair.Key}")));
            if (playerId > max) max = playerId;
        }

        if (rows.Count == 0)
            throw new InvalidOperationException($"{RelativePath}: 至少一名玩家。");

        var byPlayerId = new Vector4[max + 1];
        var seen = new bool[max + 1];
        var ids = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            (int playerId, Vector4 color) = rows[i];
            if (seen[playerId])
                throw new InvalidOperationException($"{RelativePath}: 玩家 {playerId} 重复。");
            seen[playerId] = true;
            byPlayerId[playerId] = color;
            ids[i] = playerId;
        }

        return new TeamColorPalette(byPlayerId, ids);
    }

    public static void RequirePlayers(TeamColorPalette palette, IReadOnlyList<int> playerIds, string mapId)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(playerIds);
        var expected = new HashSet<int>();
        var authored = new HashSet<int>(palette.PlayerIds);
        for (int i = 0; i < playerIds.Count; i++)
        {
            int playerId = playerIds[i];
            if (!expected.Add(playerId))
                throw new InvalidOperationException($"Maps/{mapId}.json: 玩家 {playerId} 重复。");
            if (!authored.Contains(playerId))
                throw new InvalidOperationException($"{RelativePath}: 缺少玩家 {playerId}。");
        }

        foreach (int playerId in palette.PlayerIds)
        {
            if (!expected.Contains(playerId))
                throw new InvalidOperationException($"{RelativePath}: 玩家 {playerId} 不在地图 {mapId} 上。");
        }
    }
}
