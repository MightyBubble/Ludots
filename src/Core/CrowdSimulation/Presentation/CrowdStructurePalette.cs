using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;

namespace Ludots.Core.CrowdSimulation.Presentation;

public enum CrowdPlacementGesture : byte
{
    Point,
    FixedPath,
    TwoPoint,
}

/// <summary>
/// 摆放手势条目：面板文字、鼠标怎么落一个结构模板、尺寸。内核不读这份文件。
/// 条目 id 和模板 id 是两套名字，同一个模板可以有多个条目。
/// </summary>
public static class CrowdStructurePalette
{
    public const string RelativePath = "Presentation/crowd_structure_palette.json";
    public const string GesturePoint = "point";
    public const string GestureFixedPath = "fixedPath";
    public const string GestureTwoPoint = "twoPoint";

    internal static IReadOnlyDictionary<string, CrowdPaletteEntry> LoadMerged(
        ConfigPipeline pipeline,
        ConfigCatalog catalog,
        ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, RelativePath, ConfigMergePolicy.DeepObject);
        JsonObject merged = pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null || merged.Count == 0)
        {
            throw new InvalidOperationException($"{RelativePath}: 存在目录登记但合并为空。");
        }

        return Load(merged);
    }

    internal static IReadOnlyDictionary<string, CrowdPaletteEntry> Load(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var entries = new Dictionary<string, CrowdPaletteEntry>(StringComparer.Ordinal);
        foreach (var (id, node) in root)
        {
            if (node is not JsonObject body)
            {
                throw new InvalidOperationException($"{RelativePath}: {id} 必须是对象。");
            }

            CrowdPaletteEntryDto dto;
            try
            {
                dto = body.Deserialize<CrowdPaletteEntryDto>(StrictJsonOptions.CreateCamelCase())
                    ?? throw new InvalidOperationException($"{RelativePath}: {id} 反序列化为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{RelativePath}: {id}: {ex.Message}", ex);
            }

            entries.Add(id, ToEntry(id, dto));
        }

        return entries;
    }

    /// <summary>条目点名的模板必须存在，手势要和模板足迹对得上：square、disc 用 point，path 用 fixedPath 或 twoPoint。</summary>
    internal static void RequireTemplate(string entryId, CrowdPaletteEntry entry, RuntimeStructuresSection structures)
    {
        int index = structures.TemplateIndexOf(entry.TemplateId);
        if (index < 0)
        {
            throw new InvalidOperationException($"{RelativePath}: {entryId}.template \"{entry.TemplateId}\" 不是结构模板。");
        }

        bool path = structures.Templates[index].Footprint == CrowdStructureShape.Path;
        bool pathGesture = entry.Gesture != CrowdPlacementGesture.Point;
        if (path != pathGesture)
        {
            throw new InvalidOperationException(path
                ? $"{RelativePath}: {entryId} 的模板 \"{entry.TemplateId}\" 是 path 足迹，手势要写 {GestureFixedPath} 或 {GestureTwoPoint}。"
                : $"{RelativePath}: {entryId} 的模板 \"{entry.TemplateId}\" 不是 path 足迹，手势要写 {GesturePoint}。");
        }
    }

    private static CrowdPaletteEntry ToEntry(string id, CrowdPaletteEntryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Template))
            throw new InvalidOperationException($"{RelativePath}: {id}.template 必填。");
        if (string.IsNullOrWhiteSpace(dto.Label))
            throw new InvalidOperationException($"{RelativePath}: {id}.label 必填。");
        if (dto.SizeCm <= 0)
            throw new InvalidOperationException($"{RelativePath}: {id}.sizeCm 必须 > 0。");
        CrowdPaletteGestureDto gesture = dto.Gesture
            ?? throw new InvalidOperationException($"{RelativePath}: {id}.gesture 必填。");
        CrowdPlacementGesture kind = gesture.Kind switch
        {
            GesturePoint => CrowdPlacementGesture.Point,
            GestureFixedPath => CrowdPlacementGesture.FixedPath,
            GestureTwoPoint => CrowdPlacementGesture.TwoPoint,
            _ => throw new InvalidOperationException(
                $"{RelativePath}: {id}.gesture.kind \"{gesture.Kind}\" 需为 {GesturePoint} / {GestureFixedPath} / {GestureTwoPoint}。"),
        };

        int offsetX = 0, offsetY = 0;
        if (kind == CrowdPlacementGesture.FixedPath)
        {
            CrowdPaletteOffsetDto offset = gesture.ToOffsetCm
                ?? throw new InvalidOperationException($"{RelativePath}: {id}.gesture.kind={GestureFixedPath} 需要 toOffsetCm。");
            if (offset.XCm == 0 && offset.YCm == 0)
                throw new InvalidOperationException($"{RelativePath}: {id}.gesture.toOffsetCm 不能两轴都为 0。");
            offsetX = offset.XCm;
            offsetY = offset.YCm;
        }
        else if (gesture.ToOffsetCm != null)
        {
            throw new InvalidOperationException($"{RelativePath}: {id}.gesture.kind={gesture.Kind} 不能带 toOffsetCm。");
        }

        return new CrowdPaletteEntry(dto.Template, dto.Label, kind, dto.SizeCm, offsetX, offsetY);
    }

    private sealed class CrowdPaletteEntryDto
    {
        [JsonRequired] public string Template { get; set; } = string.Empty;
        [JsonRequired] public string Label { get; set; } = string.Empty;
        [JsonRequired] public CrowdPaletteGestureDto? Gesture { get; set; }
        [JsonRequired] public int SizeCm { get; set; }
    }

    private sealed class CrowdPaletteGestureDto
    {
        [JsonRequired] public string Kind { get; set; } = string.Empty;
        public CrowdPaletteOffsetDto? ToOffsetCm { get; set; }
    }

    private sealed class CrowdPaletteOffsetDto
    {
        [JsonRequired] public int XCm { get; set; }
        [JsonRequired] public int YCm { get; set; }
    }
}

internal sealed record CrowdPaletteEntry(
    string TemplateId,
    string Label,
    CrowdPlacementGesture Gesture,
    int SizeCm,
    int ToOffsetXCm,
    int ToOffsetYCm);
