using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Vision;

/// <summary>
/// 配置驱动的脚本视野源:按 JSON 运动把可见盘写入已登记的雾层,并可把此前可见格老化为已探索。
/// 目录已登记且当前地图匹配时,层注册表、雾场、地面圈任一缺失即抛;层必须事先登记。
/// 没有 Vision/scripted_fog_source.json 时不装配。排在 <see cref="VisionSystem"/> 之后的同一固定步,
/// 这样本步写入的可见格不会被同一步开头的老化清掉。
/// </summary>
internal sealed class ScriptedFogSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ScriptedFogSource _source;
    private float _elapsedSeconds;
    private FogLayerDefinition _layerDefinition;
    private bool _layerResolved;
    private bool _seedsApplied;

    public ScriptedFogSourceSystem(GameEngine engine, ScriptedFogSource source)
    {
        _engine = engine;
        _source = source;
    }

    public static void TryInstall(GameEngine engine)
    {
        var catalog = engine.ConfigCatalog;
        if (catalog == null || !catalog.TryGet(ScriptedFogSource.CatalogPath, out var entry)) return;
        var node = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in entry, engine.ConfigConflictReport)
            ?? throw new InvalidOperationException($"{ScriptedFogSource.CatalogPath} 存在目录登记但合并为空。");
        ScriptedFogSource source = ScriptedFogSource.Parse(node);
        engine.SetService(CoreServiceKeys.FogDecalPalette, source.Palette);
        engine.RegisterSystem(
            new ScriptedFogSourceSystem(engine, source),
            SystemGroup.PostMovement);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, _source.MapId, StringComparison.OrdinalIgnoreCase)) return;

        _elapsedSeconds += dt;
        (float sourceX, float sourceY) = _source.PositionAt(_elapsedSeconds);
        WriteFogField(sourceX, sourceY);
        UpsertVisionRing(sourceX, sourceY);
    }

    private void WriteFogField(float sourceXCm, float sourceYCm)
    {
        FogLayerRegistry layers = _engine.GetService(CoreServiceKeys.VisionFogLayerRegistry)
            ?? throw new InvalidOperationException($"{ScriptedFogSource.CatalogPath} 地图匹配但缺少 VisionFogLayerRegistry。");
        FogFieldStore fields = _engine.GetService(CoreServiceKeys.VisionFogFieldStore)
            ?? throw new InvalidOperationException($"{ScriptedFogSource.CatalogPath} 地图匹配但缺少 VisionFogFieldStore。");
        if (!_layerResolved)
        {
            FogLayerId layerId = layers.GetId(_source.LayerId);
            if (layerId.Value <= 0)
            {
                throw new InvalidOperationException(
                    $"{ScriptedFogSource.CatalogPath} 雾层 \"{_source.LayerId}\" 未登记。脚本源不登记层。");
            }

            _layerDefinition = layers.Get(layerId);
            _layerResolved = true;
        }

        FogField field = fields.GetOrCreate(_source.ScopeKeyId, _layerDefinition);
        if (!_seedsApplied)
        {
            for (int i = 0; i < _source.SeedExplored.Length; i++)
            {
                FogSeedCell seed = _source.SeedExplored[i];
                field.SetExplored(new FogCell(seed.X, seed.Y));
            }

            _seedsApplied = true;
        }

        if (_source.AgeVisibleToExplored) field.AgeVisibleToExplored();
        int cellSize = field.CellSizeCm;
        int cellX = (int)MathF.Floor(sourceXCm / cellSize);
        int cellY = (int)MathF.Floor(sourceYCm / cellSize);
        int radiusCells = (int)MathF.Floor(_source.VisionRadiusCm / cellSize);
        int radiusSq = radiusCells * radiusCells;
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                if ((dx * dx) + (dy * dy) > radiusSq) continue;
                field.SetVisible(new FogCell(cellX + dx, cellY + dy));
            }
        }
    }

    private void UpsertVisionRing(float sourceXCm, float sourceYCm)
    {
        GroundOverlayBuffer overlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException($"{ScriptedFogSource.CatalogPath} 地图匹配但缺少 GroundOverlayBuffer。");
        Vector3 center = WorldPlane2D.LogicCmToVisualMeters(sourceXCm, sourceYCm, _source.RingHeight);
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = _source.OverlayStableId,
            Shape = GroundOverlayShape.Circle,
            Center = center,
            Radius = _source.VisionRadiusCm / 100f,
            Angle = 0f,
            Rotation = 0f,
            Length = _source.VisionRadiusCm / 100f,
            Width = 1f,
            FillColor = _source.Fill,
            BorderColor = _source.Border,
            BorderWidth = _source.BorderWidth,
        });
    }
}

internal sealed class ScriptedFogSource
{
    public const string CatalogPath = "Vision/scripted_fog_source.json";
    public const string EllipseMotion = "ellipse";

    public required string MapId { get; init; }
    public required string LayerId { get; init; }
    public int ScopeKeyId { get; init; }
    public int OverlayStableId { get; init; }
    public float CenterXCm { get; init; }
    public float CenterYCm { get; init; }
    public float VisionRadiusCm { get; init; }
    public bool AgeVisibleToExplored { get; init; }
    public required FogSeedCell[] SeedExplored { get; init; }
    public required FogEllipseMotion Motion { get; init; }
    public float RingHeight { get; init; }
    public float BorderWidth { get; init; }
    public Vector4 Fill { get; init; }
    public Vector4 Border { get; init; }
    public required FogDecalPalette Palette { get; init; }

    public (float X, float Y) PositionAt(float elapsedSeconds)
    {
        float x = CenterXCm + (Motion.RadiusXCm * MathF.Cos(elapsedSeconds * Motion.AngularSpeedX));
        float y = CenterYCm + (Motion.RadiusYCm * MathF.Sin(elapsedSeconds * Motion.AngularSpeedY));
        return (x, y);
    }

    public static ScriptedFogSource Parse(JsonNode node)
    {
        ScriptedFogSourceDto dto;
        try
        {
            dto = node.Deserialize<ScriptedFogSourceDto>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{CatalogPath} 反序列化为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{CatalogPath} 严格反序列化失败: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(dto.MapId))
            throw new InvalidOperationException($"{CatalogPath} mapId 必填。");
        if (string.IsNullOrWhiteSpace(dto.LayerId))
            throw new InvalidOperationException($"{CatalogPath} layerId 必填。");
        if (dto.Motion == null)
            throw new InvalidOperationException($"{CatalogPath} motion 必填。");
        if (!string.Equals(dto.Motion.Kind, EllipseMotion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CatalogPath} motion.kind \"{dto.Motion.Kind}\" 需为 {EllipseMotion}。");
        }

        if (dto.SeedExplored == null)
            throw new InvalidOperationException($"{CatalogPath} seedExplored 必填。");
        var seeds = new FogSeedCell[dto.SeedExplored.Length];
        for (int i = 0; i < dto.SeedExplored.Length; i++)
        {
            FogSeedCellDto seed = dto.SeedExplored[i]
                ?? throw new InvalidOperationException($"{CatalogPath} seedExplored[{i}] 为空。");
            seeds[i] = new FogSeedCell(seed.X, seed.Y);
        }

        return new ScriptedFogSource
        {
            MapId = dto.MapId,
            LayerId = dto.LayerId,
            ScopeKeyId = dto.ScopeKeyId,
            OverlayStableId = dto.OverlayStableId,
            CenterXCm = dto.CenterXCm,
            CenterYCm = dto.CenterYCm,
            VisionRadiusCm = dto.VisionRadiusCm,
            AgeVisibleToExplored = dto.AgeVisibleToExplored,
            SeedExplored = seeds,
            Motion = new FogEllipseMotion(dto.Motion.RadiusXCm, dto.Motion.RadiusYCm, dto.Motion.AngularSpeedX, dto.Motion.AngularSpeedY),
            RingHeight = dto.RingHeight,
            BorderWidth = dto.BorderWidth,
            Fill = Color(dto.Fill, "fill"),
            Border = Color(dto.Border, "border"),
            Palette = PaletteOf(dto.FogDecal),
        };
    }

    private static Vector4 Color(float[]? value, string name)
    {
        if (value == null || value.Length != 4)
            throw new InvalidOperationException($"{CatalogPath} {name} 需要 4 个分量。");
        return new Vector4(value[0], value[1], value[2], value[3]);
    }

    private static FogDecalPalette PaletteOf(FogDecalDto? colors)
    {
        if (colors == null)
        {
            throw new InvalidOperationException($"{CatalogPath} 缺少 fogDecal。");
        }

        return new FogDecalPalette(
            Channel(colors.Unseen, "unseen"),
            Channel(colors.Explored, "explored"),
            Channel(colors.Visible, "visible"),
            Channel(colors.Denied, "denied"));
    }

    private static FogDecalColor Channel(int[]? rgba, string name)
    {
        if (rgba == null || rgba.Length != 4)
        {
            throw new InvalidOperationException($"{CatalogPath} fogDecal.{name} 需要 4 个 0..255 的整数。");
        }

        for (int i = 0; i < 4; i++)
        {
            if ((uint)rgba[i] > 255)
            {
                throw new InvalidOperationException($"{CatalogPath} fogDecal.{name}[{i}] 超出 0..255。");
            }
        }

        return new FogDecalColor((byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3]);
    }
}

internal readonly struct FogSeedCell
{
    public FogSeedCell(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}

internal readonly struct FogEllipseMotion
{
    public FogEllipseMotion(float radiusXCm, float radiusYCm, float angularSpeedX, float angularSpeedY)
    {
        RadiusXCm = radiusXCm;
        RadiusYCm = radiusYCm;
        AngularSpeedX = angularSpeedX;
        AngularSpeedY = angularSpeedY;
    }

    public float RadiusXCm { get; }
    public float RadiusYCm { get; }
    public float AngularSpeedX { get; }
    public float AngularSpeedY { get; }
}

internal sealed class ScriptedFogSourceDto
{
    [JsonRequired] public string MapId { get; set; } = string.Empty;
    [JsonRequired] public string LayerId { get; set; } = string.Empty;
    [JsonRequired] public int ScopeKeyId { get; set; }
    [JsonRequired] public int OverlayStableId { get; set; }
    [JsonRequired] public float CenterXCm { get; set; }
    [JsonRequired] public float CenterYCm { get; set; }
    [JsonRequired] public float VisionRadiusCm { get; set; }
    [JsonRequired] public bool AgeVisibleToExplored { get; set; }
    [JsonRequired] public FogSeedCellDto[] SeedExplored { get; set; } = Array.Empty<FogSeedCellDto>();
    [JsonRequired] public FogMotionDto Motion { get; set; } = new();
    [JsonRequired] public float RingHeight { get; set; }
    [JsonRequired] public float BorderWidth { get; set; }
    [JsonRequired] public float[] Fill { get; set; } = Array.Empty<float>();
    [JsonRequired] public float[] Border { get; set; } = Array.Empty<float>();
    [JsonRequired] public FogDecalDto FogDecal { get; set; } = new();
}

internal sealed class FogDecalDto
{
    [JsonRequired] public int[] Unseen { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] Explored { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] Visible { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] Denied { get; set; } = Array.Empty<int>();
}

internal sealed class FogSeedCellDto
{
    [JsonRequired] public int X { get; set; }
    [JsonRequired] public int Y { get; set; }
}

internal sealed class FogMotionDto
{
    [JsonRequired] public string Kind { get; set; } = string.Empty;
    [JsonRequired] public float RadiusXCm { get; set; }
    [JsonRequired] public float RadiusYCm { get; set; }
    [JsonRequired] public float AngularSpeedX { get; set; }
    [JsonRequired] public float AngularSpeedY { get; set; }
}
