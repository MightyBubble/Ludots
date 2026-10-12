using System;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface 与 CrowdSimulationConfig 的一致性校验：类型表逐字对齐
/// （栅格编号 = 表中下标，顺序错一位全图错位），尺寸与导航格推导值相等。
/// <see cref="ValidateWorldSize"/> 在板、.navsurface 和 .height 都已读入后做同一次尺寸对照。
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

    public static void ValidateWorldSize(
        int boardWidthCm,
        int boardHeightCm,
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

        bool dividesX = navCellSizeCm > 0 && boardWidthCm % navCellSizeCm == 0;
        bool dividesY = navCellSizeCm > 0 && boardHeightCm % navCellSizeCm == 0;
        int derivedX = dividesX ? boardWidthCm / navCellSizeCm : 0;
        int derivedY = dividesY ? boardHeightCm / navCellSizeCm : 0;
        string derived = dividesX && dividesY ? $"{derivedX}×{derivedY}" : "不能整除";
        string facts =
            $"板 {boardWidthCm}×{boardHeightCm} cm，profiles.cellSizeCm {navCellSizeCm} 推出 {derived} 格，" +
            $"{surfaceAssetPath} 头 {surface.NavCellsX}×{surface.NavCellsY}，" +
            $"{heightAssetPath} 包围盒 {height.Bounds.Width}×{height.Bounds.Height} cm、采样 {height.SampleColumns}×{height.SampleRows}";

        if (!dividesX || !dividesY)
        {
            throw new InvalidOperationException($"地图尺寸不一致。{facts}。板宽高必须能被 profiles.cellSizeCm 整除。");
        }

        if (surface.NavCellsX != derivedX || surface.NavCellsY != derivedY)
        {
            throw new InvalidOperationException(
                $"{surfaceAssetPath}: 格数 {surface.NavCellsX}×{surface.NavCellsY} 与板 {boardWidthCm}×{boardHeightCm} cm / profiles.cellSizeCm {navCellSizeCm} 推出的 {derivedX}×{derivedY} 格不符。{facts}。");
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
        if (samplesX != samplesY
            || samplesX < derivedX || samplesX % derivedX != 0
            || samplesY < derivedY || samplesY % derivedY != 0)
        {
            throw new InvalidOperationException(
                $"{heightAssetPath}: 采样 {samplesX}×{samplesY} 必须是正方形，且每边 ≥ 并整除导航格数 {derivedX}×{derivedY}。{facts}。");
        }
    }

    private static string assetPath(Config.CrowdSimulationRuntimeConfig config) => config.SurfaceAsset;
}
