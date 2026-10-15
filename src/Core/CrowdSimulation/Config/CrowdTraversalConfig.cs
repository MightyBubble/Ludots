using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulation/traversal.json 合并后的通行矩阵。区域、链接种类、移动类型的下标都是合并后的属性顺序。
/// 行 × 区域的每一格都写明：不可通行写 false，可通行写 cost、speedDivisor、depart、arrive。
/// </summary>
public sealed class CrowdTraversalConfig
{
    public const string RelativePath = "CrowdSimulation/traversal.json";
    public const int MaxAreas = byte.MaxValue + 1;
    public const int MaxLinkKinds = byte.MaxValue;

    private readonly Dictionary<string, int> _areaIndex;
    private readonly Dictionary<string, int> _rowIndex;

    private CrowdTraversalConfig(
        IReadOnlyList<TraversalArea> areas,
        IReadOnlyDictionary<string, int> terrainAreas,
        IReadOnlyList<TraversalLinkKind> linkKinds,
        IReadOnlyList<TraversalRow> agentTypes)
    {
        Areas = areas;
        TerrainAreas = terrainAreas;
        LinkKinds = linkKinds;
        AgentTypes = agentTypes;
        _areaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < areas.Count; i++) _areaIndex.Add(areas[i].Id, i);
        _rowIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < agentTypes.Count; i++) _rowIndex.Add(agentTypes[i].Id, i);
    }

    public IReadOnlyList<TraversalArea> Areas { get; }

    /// <summary>地形 id → 区域下标。全局一张表，各地图的地形顺序在 maps.&lt;mapId&gt;.terrainTypes。</summary>
    public IReadOnlyDictionary<string, int> TerrainAreas { get; }

    public IReadOnlyList<TraversalLinkKind> LinkKinds { get; }
    public IReadOnlyList<TraversalRow> AgentTypes { get; }

    public bool TryGetAreaIndex(string areaId, out int index) => _areaIndex.TryGetValue(areaId, out index);

    public bool TryGetRow(string agentTypeId, out TraversalRow row)
    {
        if (_rowIndex.TryGetValue(agentTypeId, out int index))
        {
            row = AgentTypes[index];
            return true;
        }

        row = null!;
        return false;
    }

    public static CrowdTraversalConfig Load(ConfigPipeline pipeline, ConfigCatalog catalog, ConfigConflictReport? report = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
        return ConfigMergeProvenance.Wrap(RelativePath, report, tracked =>
        {
            JsonObject? merged = pipeline.MergeDeepObjectFromCatalog(in entry, tracked);
            if (merged == null)
            {
                throw new InvalidOperationException(
                    $"{RelativePath}: 没有任何来源提供该文件（基础区域与移动类型在 CrowdSimulationStandardMod）。");
            }

            return Parse(merged);
        });
    }

    public static CrowdTraversalConfig Parse(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        RequireOnly(root, "", "areas", "terrainAreas", "linkKinds", "agentTypes");

        JsonObject areasNode = RequireTable(root, "areas", "", allowEmpty: false);
        if (areasNode.Count > MaxAreas)
        {
            throw Error($"areas 有 {areasNode.Count} 个，最多 {MaxAreas} 个（栅格里的区域值是 u8）。");
        }

        var areas = new List<TraversalArea>(areasNode.Count);
        var areaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, node) in areasNode)
        {
            string path = $"areas.{id}";
            JsonObject area = RequireEntryObject(node, path);
            if (area.ContainsKey("slopeFree"))
            {
                throw Error($"{path}.slopeFree 已改名为 ignoresSlope。");
            }

            if (area.ContainsKey("areaId"))
            {
                throw Error($"{path}.areaId 已删除。区域下标就是 areas 合并后的属性顺序。");
            }

            RequireOnly(area, path, "ignoresSlope");
            areaIndex.Add(id, areas.Count);
            areas.Add(new TraversalArea(id, RequireBool(area, "ignoresSlope", path)));
        }

        JsonObject terrainNode = RequireTable(root, "terrainAreas", "", allowEmpty: false);
        var terrainAreas = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (terrainId, node) in terrainNode)
        {
            string path = $"terrainAreas.{terrainId}";
            string areaId = RequireStringValue(node, path);
            if (!areaIndex.TryGetValue(areaId, out int index))
            {
                throw Error($"{path} = \"{areaId}\" 不是 areas 里的区域。");
            }

            terrainAreas.Add(terrainId, index);
        }

        JsonObject linkKindsNode = RequireTable(root, "linkKinds", "", allowEmpty: true);
        if (linkKindsNode.Count > MaxLinkKinds)
        {
            throw Error($"linkKinds 有 {linkKindsNode.Count} 个，最多 {MaxLinkKinds} 个。");
        }

        var linkKinds = new List<TraversalLinkKind>(linkKindsNode.Count);
        var linkIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, node) in linkKindsNode)
        {
            string path = $"linkKinds.{id}";
            JsonObject kind = RequireEntryObject(node, path);
            RequireOnly(kind, path, "traversalSpeedRatio");
            double ratio = RequireNumber(kind, "traversalSpeedRatio", path);
            if (!(ratio > 0d) || double.IsInfinity(ratio))
            {
                throw Error($"{path}.traversalSpeedRatio = {ratio}，需为 > 0。");
            }

            linkIndex.Add(id, linkKinds.Count);
            linkKinds.Add(new TraversalLinkKind(id, ratio));
        }

        JsonObject rowsNode = RequireTable(root, "agentTypes", "", allowEmpty: false);
        var rows = new List<TraversalRow>(rowsNode.Count);
        foreach (var (id, node) in rowsNode)
        {
            rows.Add(ParseRow(id, RequireEntryObject(node, $"agentTypes.{id}"), areas, areaIndex, linkKinds, linkIndex));
        }

        return new CrowdTraversalConfig(areas, terrainAreas, linkKinds, rows);
    }

    private static TraversalRow ParseRow(
        string id,
        JsonObject row,
        IReadOnlyList<TraversalArea> areas,
        IReadOnlyDictionary<string, int> areaIndex,
        IReadOnlyList<TraversalLinkKind> linkKinds,
        IReadOnlyDictionary<string, int> linkIndex)
    {
        string path = $"agentTypes.{id}";
        RequireOnly(row, path, "maxSlopeDeg", "links", "cells");

        double? maxSlopeDeg = null;
        if (row["maxSlopeDeg"] is not null)
        {
            double deg = RequireNumber(row, "maxSlopeDeg", path);
            if (!(deg > 0d && deg < 90d))
            {
                throw Error($"{path}.maxSlopeDeg = {deg}，需在 (0, 90) 内；不限坡度写 null。");
            }

            maxSlopeDeg = deg;
        }

        JsonObject linksNode = RequireTable(row, "links", path, allowEmpty: true);
        RequireExactKeys(linksNode, $"{path}.links", linkKinds.Count, i => linkKinds[i].Id, "linkKinds");
        var links = new TraversalLinkAbility?[linkKinds.Count];
        for (int k = 0; k < linkKinds.Count; k++)
        {
            string linkPath = $"{path}.links.{linkKinds[k].Id}";
            JsonNode? node = linksNode[linkKinds[k].Id];
            if (IsFalse(node))
            {
                continue;
            }

            if (node is not JsonObject link)
            {
                throw Error($"{linkPath} 只能是 false 或对象。");
            }

            RequireOnly(link, linkPath, "upCm", "downCm", "rangeNavCells", "costPerNavCell");
            int upCm = RequireInt(link, "upCm", linkPath);
            int downCm = RequireInt(link, "downCm", linkPath);
            int range = RequireInt(link, "rangeNavCells", linkPath);
            double costPerNavCell = RequireNumber(link, "costPerNavCell", linkPath);
            if (upCm <= 0 || downCm < upCm || range <= 0 || !(costPerNavCell > 0d) || double.IsInfinity(costPerNavCell))
            {
                throw Error($"{linkPath} 参数无效：upCm > 0，downCm ≥ upCm，rangeNavCells > 0，costPerNavCell > 0。");
            }

            links[k] = new TraversalLinkAbility(upCm, downCm, range, costPerNavCell);
        }

        JsonObject cellsNode = RequireTable(row, "cells", path, allowEmpty: false);
        RequireExactKeys(cellsNode, $"{path}.cells", areas.Count, i => areas[i].Id, "areas");
        var cells = new TraversalCell?[areas.Count];
        var used = new bool[linkKinds.Count];
        double minCost = double.PositiveInfinity;
        for (int a = 0; a < areas.Count; a++)
        {
            string cellPath = $"{path}.cells.{areas[a].Id}";
            JsonNode? node = cellsNode[areas[a].Id];
            if (IsFalse(node))
            {
                continue;
            }

            if (node is not JsonObject cell)
            {
                throw Error($"{cellPath} 只能是 false 或对象。");
            }

            if (cell.ContainsKey("passable"))
            {
                throw Error($"{cellPath}.passable 已删除。不可通行整格写 false。");
            }

            RequireOnly(cell, cellPath, "cost", "speedDivisor", "depart", "arrive");
            double cost = RequireNumber(cell, "cost", cellPath);
            double speedDivisor = RequireNumber(cell, "speedDivisor", cellPath);
            if (!(cost > 0d) || double.IsInfinity(cost) || !(speedDivisor > 0d) || double.IsInfinity(speedDivisor))
            {
                throw Error($"{cellPath} 的 cost 和 speedDivisor 需为 > 0。");
            }

            bool[] depart = ParseLinkList(cell, "depart", cellPath, links, linkIndex, used);
            bool[] arrive = ParseLinkList(cell, "arrive", cellPath, links, linkIndex, used);
            minCost = Math.Min(minCost, cost);
            cells[a] = new TraversalCell(cost, speedDivisor, depart, arrive);
        }

        for (int k = 0; k < linkKinds.Count; k++)
        {
            if (links[k] is not { } link)
            {
                continue;
            }

            if (!used[k])
            {
                throw Error(
                    $"{path}.links.{linkKinds[k].Id} 是对象，但这一行没有任何一格在 depart 或 arrive 里写它。删掉能力写 false，或给端点格加上 depart / arrive。");
            }

            if (link.CostPerNavCell < minCost)
            {
                throw Error(
                    $"{path}.links.{linkKinds[k].Id}.costPerNavCell = {link.CostPerNavCell}，低于本行可通行格的最低 cost {minCost}（寻路启发式需可采纳）。");
            }
        }

        return new TraversalRow(id, maxSlopeDeg, links, cells);
    }

    private static bool[] ParseLinkList(
        JsonObject cell,
        string key,
        string cellPath,
        TraversalLinkAbility?[] links,
        IReadOnlyDictionary<string, int> linkIndex,
        bool[] used)
    {
        if (cell[key] is not JsonArray list)
        {
            throw Error($"{cellPath}.{key} 必须是链接种类 id 数组。");
        }

        var mask = new bool[links.Length];
        for (int i = 0; i < list.Count; i++)
        {
            string kindId = RequireStringValue(list[i], $"{cellPath}.{key}[{i}]");
            if (!linkIndex.TryGetValue(kindId, out int k))
            {
                throw Error($"{cellPath}.{key}[{i}] = \"{kindId}\" 不是 linkKinds 里的种类。");
            }

            if (links[k] == null)
            {
                throw Error($"{cellPath}.{key}[{i}] = \"{kindId}\"，但这一行的 links.{kindId} 是 false。");
            }

            if (mask[k])
            {
                throw Error($"{cellPath}.{key} 重复写了 \"{kindId}\"。");
            }

            mask[k] = true;
            used[k] = true;
        }

        return mask;
    }

    private static void RequireExactKeys(JsonObject table, string path, int count, Func<int, string> idAt, string source)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string id = idAt(i);
            expected.Add(id);
            if (!table.ContainsKey(id))
            {
                throw Error($"{path} 缺少 \"{id}\"。每一行都要写全 {source} 里的每一项，不可通行写 false。");
            }
        }

        foreach (var (key, _) in table)
        {
            if (!expected.Contains(key))
            {
                throw Error($"{path}.{key} 不是 {source} 里的 id。");
            }
        }
    }

    private static bool IsFalse(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out bool flag) && !flag;

    private static JsonObject RequireTable(JsonObject parent, string key, string path, bool allowEmpty)
    {
        string where = Join(path, key);
        JsonNode? node = parent[key];
        if (node is not JsonObject table)
        {
            throw Error($"{where} 必须是以 id 为键的对象。");
        }

        if (!allowEmpty && table.Count == 0)
        {
            throw Error($"{where} 不能为空。");
        }

        foreach (var (id, _) in table)
        {
            if (string.IsNullOrWhiteSpace(id) || !string.Equals(id.Trim(), id, StringComparison.Ordinal))
            {
                throw Error($"{where} 的键 \"{id}\" 不能为空或带首尾空白。");
            }
        }

        return table;
    }

    private static JsonObject RequireEntryObject(JsonNode? node, string path)
    {
        if (node is not JsonObject obj)
        {
            throw Error($"{path} 必须是对象（不接受 null 或数组）。");
        }

        return obj;
    }

    private static void RequireOnly(JsonObject obj, string path, params string[] keys)
    {
        foreach (var (key, _) in obj)
        {
            if (Array.IndexOf(keys, key) < 0)
            {
                throw Error($"{Join(path, key)} 是未知键。");
            }
        }

        foreach (string key in keys)
        {
            if (!obj.ContainsKey(key))
            {
                throw Error($"{Join(path, key)} 必须写。");
            }
        }
    }

    private static bool RequireBool(JsonObject obj, string key, string path)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue(out bool flag))
        {
            throw Error($"{path}.{key} 必须是 true 或 false。");
        }

        return flag;
    }

    private static int RequireInt(JsonObject obj, string key, string path)
    {
        if (obj[key] is not JsonValue value || !value.TryGetValue(out int number))
        {
            throw Error($"{path}.{key} 必须是整数。");
        }

        return number;
    }

    private static double RequireNumber(JsonObject obj, string key, string path)
    {
        if (obj[key] is JsonValue value)
        {
            if (value.TryGetValue(out double d) && !double.IsNaN(d)) return d;
            if (value.TryGetValue(out int i)) return i;
            if (value.TryGetValue(out long l)) return l;
        }

        throw Error($"{path}.{key} 必须是数。");
    }

    private static string RequireStringValue(JsonNode? node, string path)
    {
        if (node is not JsonValue value || !value.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text))
        {
            throw Error($"{path} 必须是非空字符串。");
        }

        return text;
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static InvalidOperationException Error(string message) => new($"{RelativePath}: {message}");
}

public sealed record TraversalArea(string Id, bool IgnoresSlope);

public sealed record TraversalLinkKind(string Id, double TraversalSpeedRatio);

/// <summary>Links 按 linkKinds 下标，Cells 按区域下标；null 表示这一项写的是 false。</summary>
public sealed record TraversalRow(
    string Id,
    double? MaxSlopeDeg,
    IReadOnlyList<TraversalLinkAbility?> Links,
    IReadOnlyList<TraversalCell?> Cells);

public sealed record TraversalLinkAbility(int UpCm, int DownCm, int RangeNavCells, double CostPerNavCell);

/// <summary>Depart / Arrive 按 linkKinds 下标。</summary>
public sealed record TraversalCell(double Cost, double SpeedDivisor, IReadOnlyList<bool> Depart, IReadOnlyList<bool> Arrive);
