using System;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface 与 CrowdSimulationConfig 的一致性校验：类型表逐字对齐
/// （栅格编号 = 表中下标，顺序错一位全图错位），尺寸与导航格推导值相等。
/// </summary>
public static class NavSurfaceContract
{
    public static void Validate(NavSurfaceAsset asset, Config.CrowdSimulationRuntimeConfig config)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(config);

        if (asset.TerrainTypeIds.Length != config.TerrainTypeIds.Count)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 地形类型表 {asset.TerrainTypeIds.Length} 项，与 maps.{config.MapId}.terrainAreas（{config.TerrainTypeIds.Count} 项）不一致。");
        }

        for (int i = 0; i < asset.TerrainTypeIds.Length; i++)
        {
            if (!string.Equals(asset.TerrainTypeIds[i], config.TerrainTypeIds[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{assetPath(config)}: 地形类型表第 {i} 项 \"{asset.TerrainTypeIds[i]}\" 与配置 \"{config.TerrainTypeIds[i]}\" 不一致（编号 = 表中下标，顺序必须相同）。");
            }
        }

        if (asset.NavCellsX != config.NavCellsPerSide || asset.NavCellsY != config.NavCellsPerSide)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 尺寸 {asset.NavCellsX} × {asset.NavCellsY} 与导航网格 {config.NavCellsPerSide} × {config.NavCellsPerSide} 不符。");
        }

        if (asset.NavCellSizeCm != config.NavCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: navCellSizeCm = {asset.NavCellSizeCm}，与 profiles.cellSizeCm = {config.NavCellSizeCm} 不符。");
        }
    }

    private static string assetPath(Config.CrowdSimulationRuntimeConfig config) => config.SurfaceAsset;
}
