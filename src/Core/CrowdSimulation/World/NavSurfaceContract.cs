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
/// 链接种类表对照当前配置；烘焙输入指纹在 <see cref="ValidateBakeInputs"/> 里对照，那里才有地图条带和高度场。
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
    }

    /// <summary>
    /// 进图时用当前配置、地图上层条带实体和高度场重算 <see cref="BakeInputsDigest"/>，和资产头比。
    /// 任何一项烘焙输入改了而资产没重烘，都在这里报错。
    /// </summary>
    public static void ValidateBakeInputs(
        NavSurfaceAsset asset,
        CrowdSimulationRuntimeConfig config,
        IReadOnlyList<LayeredPathRecord> layeredPaths,
        NavHeightField heights)
    {
        ArgumentNullException.ThrowIfNull(asset);
        uint current = BakeInputsDigest(config, layeredPaths, heights);
        if (asset.BakeInputsDigest != current)
        {
            throw new InvalidOperationException(
                $"{assetPath(config)}: 烘焙输入和当前配置不一致，重烘（bakeInputsDigest 资产 0x{asset.BakeInputsDigest:X8}，按当前输入 0x{current:X8}）。");
        }
    }

    /// <summary>
    /// .navsurface 烘焙输入的 FNV-1a 32 指纹，整数一律小端。先写 i32 navCellSizeCm。
    /// 没有链接种类时到此为止：格字节只来自地编网格，条带和高度不影响产物。
    /// 有链接种类时依次写：
    /// 每个种类按表序 utf8 id、0x00、i32 maxGapCm、i32 linkSpacingCm、endpointMaxSlopeDeg 的 IEEE 754 双精度位、
    /// 按地形表序每地形一字节（bit0 端点，bit1 硬缝）；
    /// i32 条带数，每条按地图实体序 utf8 模板 id、0x00、i32 fromXCm、fromYCm、toXCm、toYCm、widthCm，
    /// 再写模板效果一字节、utf8 区域 id、0x00、i32 portalReachNavCells；
    /// 最后按格序每格 i64 高度、i64 坡度（Fix64 原始值）。
    /// </summary>
    public static uint BakeInputsDigest(
        CrowdSimulationRuntimeConfig config,
        IReadOnlyList<LayeredPathRecord> layeredPaths,
        NavHeightField heights)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(layeredPaths);
        ArgumentNullException.ThrowIfNull(heights);
        uint hash = BakeInputsDigestWithoutLinks(config.NavCellSizeCm);
        var bakes = config.LinkCandidateBakes;
        if (bakes.Count == 0)
        {
            return hash;
        }

        foreach (RuntimeLinkCandidateBake bake in bakes)
        {
            if (bake.EndpointTerrain.Length != bake.HardGapTerrain.Length)
            {
                throw new ArgumentException($"链接种类 \"{bake.KindId}\" 的端点掩码和硬缝掩码长度不同。", nameof(config));
            }

            hash = MixString(hash, bake.KindId);
            hash = MixInt32(hash, bake.MaxGapCm);
            hash = MixInt32(hash, bake.LinkSpacingCm);
            hash = MixInt64(hash, BitConverter.DoubleToInt64Bits(bake.EndpointMaxSlopeDeg));
            for (int t = 0; t < bake.EndpointTerrain.Length; t++)
            {
                int flags = (bake.EndpointTerrain[t] ? 1 : 0) | (bake.HardGapTerrain[t] ? 2 : 0);
                hash = MixByte(hash, (byte)flags);
            }
        }

        hash = MixInt32(hash, layeredPaths.Count);
        foreach (LayeredPathRecord path in layeredPaths)
        {
            RuntimeStructureTemplate template = config.Structures.Templates[
                config.Structures.RequireMapTemplate(path.TemplateId, CrowdStructureEffectKind.Surface)];
            CrowdSimulationPathSpan span = path.Span;
            hash = MixString(hash, path.TemplateId);
            hash = MixInt32(hash, span.FromXCm);
            hash = MixInt32(hash, span.FromYCm);
            hash = MixInt32(hash, span.ToXCm);
            hash = MixInt32(hash, span.ToYCm);
            hash = MixInt32(hash, span.WidthCm);
            hash = MixByte(hash, (byte)template.Effect);
            hash = MixString(hash, config.NavAreas[template.AreaIndex!.Value].Id);
            hash = MixInt32(hash, template.PortalReachNavCells);
        }

        int cells = config.NavCellsPerSide * config.NavCellsPerSide;
        if (heights.NavCellsPerSide != config.NavCellsPerSide || heights.HeightsCm.Length != cells || heights.Slope.Length != cells)
        {
            throw new ArgumentException(
                $"高度场 {heights.NavCellsPerSide} 格每边，导航格 {config.NavCellsPerSide} 格每边。", nameof(heights));
        }

        for (int i = 0; i < cells; i++)
        {
            hash = MixInt64(hash, heights.HeightsCm[i].RawValue);
            hash = MixInt64(hash, heights.Slope[i].RawValue);
        }

        return hash;
    }

    /// <summary>没有链接种类的地图的指纹：只含 i32 navCellSizeCm。</summary>
    public static uint BakeInputsDigestWithoutLinks(int navCellSizeCm) => MixInt32(FnvOffset, navCellSizeCm);

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

    private static uint MixString(uint hash, string value)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash = MixByte(hash, b);
        }

        return MixByte(hash, 0);
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
