using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Vision;

/// <summary>
/// 配置驱动的脚本视野源:按椭圆轨迹把可见盘写入 FogField,并把此前可见格老化为已探索,
/// 同时投一圈地面标记。没有 Vision/scripted_fog_source.json 的目录登记时不装配。
/// </summary>
internal sealed class ScriptedFogSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ScriptedFogSource _source;
    private float _elapsedSeconds;
    private FogLayerRegistry? _layers;
    private FogLayerDefinition _layerDefinition;
    private bool _fullMapBoundsSeeded;

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
        engine.RegisterPresentationSystem(new ScriptedFogSourceSystem(engine, ScriptedFogSource.Parse(node)));
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
        float sourceX = _source.CenterXCm + (_source.RadiusXCm * MathF.Cos(_elapsedSeconds * _source.AngularSpeedX));
        float sourceY = _source.CenterYCm + (_source.RadiusYCm * MathF.Sin(_elapsedSeconds * _source.AngularSpeedY));
        WriteFogField(sourceX, sourceY);
        UpsertVisionRing(sourceX, sourceY);
    }

    private void WriteFogField(float sourceXCm, float sourceYCm)
    {
        FogLayerRegistry? layers = ResolveLayers();
        FogFieldStore? fields = _engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore? store)
            ? store
            : null;
        if (layers == null || fields == null) return;

        FogField field = fields.GetOrCreate(_source.ScopeKeyId, _layerDefinition);
        if (!_fullMapBoundsSeeded)
        {
            int mapCells = (int)(_source.MapExtentCm / field.CellSizeCm);
            field.SetExplored(new FogCell(0, 0));
            field.SetExplored(new FogCell(mapCells - 1, mapCells - 1));
            _fullMapBoundsSeeded = true;
        }

        field.AgeVisibleToExplored();
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

    private FogLayerRegistry? ResolveLayers()
    {
        if (_layers != null) return _layers;
        if (!_engine.TryGetService(CoreServiceKeys.VisionFogLayerRegistry, out FogLayerRegistry? layers) || layers == null)
        {
            return null;
        }

        FogLayerId layerId = layers.GetId(_source.LayerKey);
        if (layerId.Value <= 0)
        {
            layerId = layers.Register(_source.LayerKey, cellSizeCm: _source.CellSizeCm, updateHz: _source.UpdateHz);
        }

        _layers = layers;
        _layerDefinition = layers.Get(layerId);
        return _layers;
    }

    private void UpsertVisionRing(float sourceXCm, float sourceYCm)
    {
        if (!_engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer? overlays) || overlays == null)
        {
            return;
        }

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

    public required string MapId { get; init; }
    public required string LayerKey { get; init; }
    public int ScopeKeyId { get; init; }
    public int OverlayStableId { get; init; }
    public int CellSizeCm { get; init; }
    public int UpdateHz { get; init; }
    public float CenterXCm { get; init; }
    public float CenterYCm { get; init; }
    public float RadiusXCm { get; init; }
    public float RadiusYCm { get; init; }
    public float VisionRadiusCm { get; init; }
    public float MapExtentCm { get; init; }
    public float AngularSpeedX { get; init; }
    public float AngularSpeedY { get; init; }
    public float RingHeight { get; init; }
    public float BorderWidth { get; init; }
    public Vector4 Fill { get; init; }
    public Vector4 Border { get; init; }

    public static ScriptedFogSource Parse(JsonObject node)
    {
        return new ScriptedFogSource
        {
            MapId = Req(node, "mapId"),
            LayerKey = Req(node, "layerKey"),
            ScopeKeyId = Int(node, "scopeKeyId"),
            OverlayStableId = Int(node, "overlayStableId"),
            CellSizeCm = Int(node, "cellSizeCm"),
            UpdateHz = Int(node, "updateHz"),
            CenterXCm = Num(node, "centerXCm"),
            CenterYCm = Num(node, "centerYCm"),
            RadiusXCm = Num(node, "radiusXCm"),
            RadiusYCm = Num(node, "radiusYCm"),
            VisionRadiusCm = Num(node, "visionRadiusCm"),
            MapExtentCm = Num(node, "mapExtentCm"),
            AngularSpeedX = Num(node, "angularSpeedX"),
            AngularSpeedY = Num(node, "angularSpeedY"),
            RingHeight = Num(node, "ringHeight"),
            BorderWidth = Num(node, "borderWidth"),
            Fill = Color(node, "fill"),
            Border = Color(node, "border"),
        };
    }

    private static string Req(JsonObject node, string name) =>
        node[name]?.GetValue<string>() ?? throw new InvalidOperationException($"{CatalogPath} {name} 必填。");

    private static int Int(JsonObject node, string name) =>
        node[name]?.GetValue<int>() ?? throw new InvalidOperationException($"{CatalogPath} {name} 必填。");

    private static float Num(JsonObject node, string name) =>
        node[name]?.GetValue<float>() ?? throw new InvalidOperationException($"{CatalogPath} {name} 必填。");

    private static Vector4 Color(JsonObject node, string name)
    {
        var arr = node[name]?.AsArray() ?? throw new InvalidOperationException($"{CatalogPath} {name} 必填。");
        if (arr.Count != 4) throw new InvalidOperationException($"{CatalogPath} {name} 需要 4 个分量。");
        return new Vector4(arr[0]!.GetValue<float>(), arr[1]!.GetValue<float>(), arr[2]!.GetValue<float>(), arr[3]!.GetValue<float>());
    }
}
