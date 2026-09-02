// M2.a Field2D 同步:内核装载后的 Map 格值(terrainType/areaId)灌进引擎地图会话的
// sango.* 字段层(assets/Fields/layers.json 声明,sango_default 地图启用)。
// 坐标系约定(与 TerrainExport 一致):内核格 x=北轴(row)、y=东轴(col);引擎世界
// 以地图中心为原点,格子尺寸 GridSize(米)×100 = 2000cm。灌入走 layer.Field.WorldToCell,
// 由 FieldGridSpec2D 完成中心对齐换算,不在本文件复算索引。

using System;
using Ludots.Core.Fields;
using Ludots.Platform.Abstractions;

namespace Sango.Runtime
{
    public static class SangoFieldLayers
    {
        public const string TerrainTypeLayerKey = "sango.terrainType";
        public const string AreaIdLayerKey = "sango.areaId";

        /// <summary>
        /// 把 <paramref name="scenario"/> 当前 Map 的逐格值写入 <paramref name="store"/> 的两层。
        /// 层值定义:terrainType = Cell.TerrainType.Id(表索引,bin 缺失表项时内核回落 id 0);
        /// areaId = Cell.BelongCity?.Id ?? 0(bin 的区域 ID,即城/关/港的 citySet 键)。
        /// 返回写入格数(两层同格数;store 缺任一层即抛错——地图启用面与内核必须一致)。
        /// </summary>
        public static int Populate(FieldSessionStore store, Sango.Core.Scenario scenario)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));

            if (!store.TryGetByKey(TerrainTypeLayerKey, out FieldLayerData? terrainData) ||
                terrainData is not DiscreteIdFieldLayerData terrainLayer)
                throw new InvalidOperationException($"Field layer '{TerrainTypeLayerKey}' is not enabled on the map session.");
            if (!store.TryGetByKey(AreaIdLayerKey, out FieldLayerData? areaData) ||
                areaData is not DiscreteIdFieldLayerData areaLayer)
                throw new InvalidOperationException($"Field layer '{AreaIdLayerKey}' is not enabled on the map session.");

            Sango.Core.Map map = scenario.Map;
            if (map?.CellSet?.GetCell(0, 0) == null)
                throw new InvalidOperationException("SangoFieldLayers requires a loaded kernel map (real bin or synthetic grid).");

            // 源单位:1 内核世界单位 = 1 m;地图世界以中心为原点(与 CHTM/HexGrid bounds 一致)。
            float cellMeters = map.GridSize;
            int halfWorldCm = (int)(map.Width * cellMeters * 100f / 2f);

            // 区域表对齐:discreteId 层的非零值必须落在 RegionIdRegistry 已注册区间
            // (FieldDiscreteVisualProjector 的硬校验)。Register 按注册顺序发号(1 起),
            // 故按值升序注册 1..max,使 region id == 层值(terrainType=表 id,areaId=城 id),
            // 未出现的中间值一并注册占位以保持对齐。
            int maxTerrainId = 0;
            int maxAreaId = 0;
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Sango.Core.Cell cell = map.CellSet.GetCell(x, y);
                    maxTerrainId = Math.Max(maxTerrainId, cell.TerrainType?.Id ?? 0);
                    maxAreaId = Math.Max(maxAreaId, cell.BelongCity?.Id ?? 0);
                }
            }

            for (int id = 1; id <= maxTerrainId; id++)
            {
                terrainLayer.Regions.Register($"sango.terrain.{id}");
            }
            for (int id = 1; id <= maxAreaId; id++)
            {
                areaLayer.Regions.Register($"sango.area.{id}");
            }

            int written = 0;
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Sango.Core.Cell cell = map.CellSet.GetCell(x, y);
                    var center = new WorldCmInt2(
                        (int)((y * cellMeters + cellMeters * 0.5f) * 100f) - halfWorldCm,
                        (int)((x * cellMeters + cellMeters * 0.5f) * 100f) - halfWorldCm);

                    terrainLayer.Field.Set(terrainLayer.Field.WorldToCell(center), cell.TerrainType?.Id ?? 0);
                    areaLayer.Field.Set(areaLayer.Field.WorldToCell(center), cell.BelongCity?.Id ?? 0);
                    written++;
                }
            }

            return written;
        }
    }
}
