using System;
using System.Collections.Generic;
using System.Text;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface 与 maps.&lt;mapId&gt;.terrainTypes 逐字对齐
/// （栅格编号 = 表中下标，顺序错一位全图错位）。
/// 板是否正方形、宽高能否被 地图 navCellSizeCm 整除，在配置装载时检查，那时 .navsurface 和 .height 还不存在。
/// <see cref="ValidateWorldSize"/> 只对照装载结果里的导航格、.navsurface 和 .height。
/// 链接种类表和 linkBakeDigest 对照当前配置：矩阵或几何参数改了而资产没重烘，装载就报错。
/// </summary>
public static class NavSurfaceContract
{
    public static void Validate(NavSurfaceAsset asset, CrowdSimulationRuntimeConfig config)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(config);

        if (asset.TerrainTypeIds.Length != config.TerrainTypeIds.Count)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 地形类型表 {asset.TerrainTypeIds.Length} 项，与 maps.{config.MapId}.terrainTypes（{config.TerrainTypeIds.Count} 项）不一致。");
        }

        for (int i = 0; i < asset.TerrainTypeIds.Length; i++)
        {
            if (!string.Equals(asset.TerrainTypeIds[i], config.TerrainTypeIds[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{assetPath(config)}: 地形类型表第 {i} 项 \"{asset.TerrainTypeIds[i]}\" 与配置 \"{config.TerrainTypeIds[i]}\" 不一致（编号 = 表中下标，顺序必须相同）。");
            }
        }

        var bakes = config.LinkCandidateBakes;
        bool sameKinds = asset.LinkKindIds.Length == bakes.Count;
        for (int i = 0; sameKinds && i < bakes.Count; i++)
        {
            sameKinds = string.Equals(asset.LinkKindIds[i], bakes[i].KindId, StringComparison.Ordinal);
        }

        if (!sameKinds)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 链接种类表 [{string.Join(", ", asset.LinkKindIds)}] 与 maps.{config.MapId}.linkCandidateBake 按 linkKinds 顺序得出的 [{KindList(bakes)}] 不一致，重烘。");
        }

        if (asset.LinkBakeDigest != config.LinkBakeDigest)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 链接候选和配置不一致，重烘（linkBakeDigest 资产 0x{asset.LinkBakeDigest:X8}，按当前配置 0x{config.LinkBakeDigest:X8}）。");
        }
    }

    /// <summary>
    /// FNV-1a 32：i32 navCellSizeCm；每个种类按表序写 utf8 id、0x00、i32 maxGapCm、i32 linkSpacingCm、
    /// endpointMaxSlopeDeg 的 IEEE 754 双精度位、再按地形表序每地形一字节（bit0 端点，bit1 硬缝）。整数一律小端。
    /// </summary>
    public static uint LinkBakeDigest(int navCellSizeCm, IReadOnlyList<RuntimeLinkCandidateBake> bakes)
    {
        ArgumentNullException.ThrowIfNull(bakes);
        uint hash = FnvOffset;
        hash = MixInt32(hash, navCellSizeCm);
        foreach (RuntimeLinkCandidateBake bake in bakes)
        {
            if (bake.EndpointTerrain.Length != bake.HardGapTerrain.Length)
            {
                throw new ArgumentException($"链接种类 \"{bake.KindId}\" 的端点掩码和硬缝掩码长度不同。", nameof(bakes));
            }

            foreach (byte b in Encoding.UTF8.GetBytes(bake.KindId))
            {
                hash = MixByte(hash, b);
            }

            hash = MixByte(hash, 0);
            hash = MixInt32(hash, bake.MaxGapCm);
            hash = MixInt32(hash, bake.LinkSpacingCm);
            hash = MixInt64(hash, BitConverter.DoubleToInt64Bits(bake.EndpointMaxSlopeDeg));
            for (int t = 0; t < bake.EndpointTerrain.Length; t++)
            {
                int flags = (bake.EndpointTerrain[t] ? 1 : 0) | (bake.HardGapTerrain[t] ? 2 : 0);
                hash = MixByte(hash, (byte)flags);
            }
        }

        return hash;
    }

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

    private static uint MixByte(uint hash, byte value) => (hash ^ value) * FnvPrime;

    private static uint MixInt32(uint hash, int value)
    {
        uint bits = unchecked((uint)value);
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash = MixByte(hash, (byte)(bits >> shift));
        }

        return hash;
    }

    private static uint MixInt64(uint hash, long value)
    {
        hash = MixInt32(hash, unchecked((int)value));
        return MixInt32(hash, unchecked((int)(value >> 32)));
    }

    private static string KindList(IReadOnlyList<RuntimeLinkCandidateBake> bakes)
    {
        var ids = new string[bakes.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = bakes[i].KindId;
        }

        return string.Join(", ", ids);
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
            $"板 {boardWidthCm}×{boardHeightCm} cm，地图 navCellSizeCm {navCellSizeCm} 推出 {derived} 格，" +
            $"{surfaceAssetPath} 头 {surface.NavCellsX}×{surface.NavCellsY}，" +
            $"{heightAssetPath} 包围盒 {height.Bounds.Width}×{height.Bounds.Height} cm、采样 {height.SampleColumns}×{height.SampleRows}";

        if (surface.NavCellsX != navCellsPerSide || surface.NavCellsY != navCellsPerSide)
        {
            throw new InvalidOperationException(
                $"{surfaceAssetPath}: 格数 {surface.NavCellsX}×{surface.NavCellsY} 与板 {boardWidthCm}×{boardHeightCm} cm / 地图 navCellSizeCm {navCellSizeCm} 推出的 {derived} 格不符。{facts}。");
        }

        if (surface.NavCellSizeCm != navCellSizeCm)
        {
            throw new InvalidOperationException(
                $"{surfaceAssetPath}: navCellSizeCm = {surface.NavCellSizeCm}，与 地图 navCellSizeCm = {navCellSizeCm} 不符。{facts}。");
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

    private static string assetPath(CrowdSimulationRuntimeConfig config) => config.SurfaceAsset;
}
