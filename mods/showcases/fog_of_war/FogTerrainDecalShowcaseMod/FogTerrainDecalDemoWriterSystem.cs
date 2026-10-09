using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;

namespace FogTerrainDecalShowcaseMod;

/// <summary>
/// 迷雾三态演示写入器:presentation 侧按帧手工填充 FogField(当前视野盘=Visible,
/// 此前轨迹经 AgeVisibleToExplored 落为 Explored,其余保持 Unseen),并投影视野源
/// 地面圈作视觉锚点。写入的是渲染底座的真实数据结构(FogFieldStore/FogField);
/// 内核视野组(F02)落地后由真实视野数据接管同一通道,本写入器即可移除。
/// </summary>
internal sealed class FogTerrainDecalDemoWriterSystem : ISystem<float>
{
    private const string MapId = "fog_terrain_decal_showcase";
    private const string LayerKey = "fog-terrain-decal-ground";
    private const int ScopeKeyId = 316;
    private const int OverlayStableId = 7316;
    private const float PathCenterXCm = 204_800f;
    private const float PathCenterYCm = 204_800f;
    private const float PathRadiusXCm = 60_000f;
    private const float PathRadiusYCm = 45_000f;
    private const float VisionRadiusCm = 24_000f;
    private const float PathAngularSpeedX = 0.09f;
    private const float PathAngularSpeedY = 0.13f;

    private readonly GameEngine _engine;
    private float _elapsedSeconds;
    private FogLayerRegistry? _layers;
    private FogFieldStore? _fields;
    private FogLayerDefinition _layerDefinition;
    private bool _fullMapBoundsSeeded;

    public FogTerrainDecalDemoWriterSystem(GameEngine engine)
    {
        _engine = engine;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _elapsedSeconds += dt;
        float sourceX = PathCenterXCm + (PathRadiusXCm * MathF.Cos(_elapsedSeconds * PathAngularSpeedX));
        float sourceY = PathCenterYCm + (PathRadiusYCm * MathF.Sin(_elapsedSeconds * PathAngularSpeedY));
        WriteFogField(sourceX, sourceY);
        UpsertVisionRing(sourceX, sourceY);
    }

    private void WriteFogField(float sourceXCm, float sourceYCm)
    {
        FogLayerRegistry? layers = ResolveLayers();
        FogFieldStore? fields = _engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore? store)
            ? store
            : null;
        if (layers == null || fields == null)
        {
            return;
        }

        FogField field = fields.GetOrCreate(ScopeKeyId, _layerDefinition);
        // 迷雾贴花的覆盖域=场非默认格的包围盒:先在对角各放一个已探索格,
        // 把包围盒钉到全图,视野轨迹之外的整张地图都以"不可见=黑"呈现
        if (!_fullMapBoundsSeeded)
        {
            int mapCells = (int)(409_600 / field.CellSizeCm);
            field.SetExplored(new FogCell(0, 0));
            field.SetExplored(new FogCell(mapCells - 1, mapCells - 1));
            _fullMapBoundsSeeded = true;
        }

        field.AgeVisibleToExplored();
        int cellSize = field.CellSizeCm;
        int cellX = (int)MathF.Floor(sourceXCm / cellSize);
        int cellY = (int)MathF.Floor(sourceYCm / cellSize);
        int radiusCells = (int)MathF.Floor(VisionRadiusCm / cellSize);
        int radiusSq = radiusCells * radiusCells;
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                if ((dx * dx) + (dy * dy) > radiusSq)
                {
                    continue;
                }

                field.SetVisible(new FogCell(cellX + dx, cellY + dy));
            }
        }
    }

    private FogLayerRegistry? ResolveLayers()
    {
        if (_layers != null)
        {
            return _layers;
        }

        if (!_engine.TryGetService(CoreServiceKeys.VisionFogLayerRegistry, out FogLayerRegistry? layers) ||
            layers == null)
        {
            return null;
        }

        FogLayerId layerId = layers.GetId(LayerKey);
        if (layerId.Value <= 0)
        {
            layerId = layers.Register(LayerKey, cellSizeCm: 400, updateHz: 10);
        }

        _layers = layers;
        _layerDefinition = layers.Get(layerId);
        return _layers;
    }

    private void UpsertVisionRing(float sourceXCm, float sourceYCm)
    {
        if (!_engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer? overlays) ||
            overlays == null)
        {
            return;
        }

        Vector3 center = WorldPlane2D.LogicCmToVisualMeters(sourceXCm, sourceYCm, 1.2f);
        overlays.Upsert(new GroundOverlayItem
        {
            StableId = OverlayStableId,
            Shape = GroundOverlayShape.Circle,
            Center = center,
            Radius = VisionRadiusCm / 100f,
            Angle = 0f,
            Rotation = 0f,
            Length = VisionRadiusCm / 100f,
            Width = 1f,
            FillColor = new Vector4(0.15f, 0.65f, 1f, 0.10f),
            BorderColor = new Vector4(0.25f, 0.85f, 1f, 0.75f),
            BorderWidth = 0.06f
        });
    }
}
