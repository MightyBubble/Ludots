using System;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface 与 CrowdSimulationConfig 的地形类型表逐字对齐
/// （栅格编号 = 表中下标，顺序错一位全图错位）。
/// 板是否正方形、宽高能否被 profiles.cellSizeCm 整除，在配置装载时检查，那时 .navsurface 和 .height 还不存在。
/// <see cref="ValidateWorldSize"/> 只对照装载结果里的导航格、.navsurface 和 .height。
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
    }

    public static void ValidateWorldSize(
        int boardWidthCm,
        int boardHeightCm,
        int navCellsPerSide,
        int navCellSizeCm,
        NavSurfaceAsset surface,
        string surfaceAssetPath,
        ContinuousHeightmapAsset height,
        string heightAssetPath)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(heightAssetPath);

        string derived = $"{navCellsPerSide}×{navCellsPerSide}";
        string facts =
            $"板 {boardWidthCm}×{boardHeightCm} cm，profiles.cellSizeCm {navCellSizeCm} 推出 {derived} 格，" +
            $"{surfaceAssetPath} 头 {surface.NavCellsX}×{surface.NavCellsY}，" +
            $"{heightAssetPath} 包围盒 {height.Bounds.Width}×{height.Bounds.Height} cm、采样 {height.SampleColumns}×{height.SampleRows}";

        if (surface.NavCellsX != navCellsPerSide || surface.NavCellsY != navCellsPerSide)
        {
            throw new InvalidOperationException(
                $"{surfaceAssetPath}: 格数 {surface.NavCellsX}×{surface.NavCellsY} 与板 {boardWidthCm}×{boardHeightCm} cm / profiles.cellSizeCm {navCellSizeCm} 推出的 {derived} 格不符。{facts}。");
        }

        if (surface.NavCellSizeCm != navCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{surfaceAssetPath}: navCellSizeCm = {surface.NavCellSizeCm}，与 profiles.cellSizeCm = {navCellSizeCm} 不符。{facts}。");
        }

        if (height.Bounds.Width != boardWidthCm || height.Bounds.Height != boardHeightCm)
        {
            throw new InvalidOperationException(
                $"{heightAssetPath}: 包围盒 {height.Bounds.Width}×{height.Bounds.Height} cm 与板 {boardWidthCm}×{boardHeightCm} cm 不符。{facts}。");
        }

        int samplesX = height.SampleColumns;
        int samplesY = height.SampleRows;
        if (samplesX != samplesY || samplesX < navCellsPerSide || samplesX % navCellsPerSide != 0)
        {
            throw new InvalidOperationException(
                $"{heightAssetPath}: 采样 {samplesX}×{samplesY} 必须是正方形，且每边 ≥ 并整除导航格数 {derived}。{facts}。");
        }
    }

    private static string assetPath(Config.CrowdSimulationRuntimeConfig config) => config.SurfaceAsset;
}
