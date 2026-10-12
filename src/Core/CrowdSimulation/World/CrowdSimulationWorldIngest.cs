using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// 群体进图读入 .navsurface 和 .height 之后的唯一装配：先做尺寸对照，再生成地表栅格和高度场。
/// 板是否正方形、宽高能否被 profiles.cellSizeCm 整除，在 <see cref="CrowdSimulationConfigLoader"/> 里检查，那时资产还不存在。
/// </summary>
public static class CrowdSimulationWorldIngest
{
    public readonly record struct Assets(SurfaceGrid Grid, NavHeightField Heights);

    public static Assets Build(
        CrowdSimulationRuntimeConfig config,
        int boardWidthCm,
        int boardHeightCm,
        NavSurfaceAsset surface,
        string surfaceAssetPath,
        ContinuousHeightmapAsset height,
        string heightAssetPath,
        IReadOnlyList<BlockerFootprint> blockers)
    {
        ArgumentNullException.ThrowIfNull(config);
        NavSurfaceContract.ValidateWorldSize(
            boardWidthCm,
            boardHeightCm,
            config.NavCellsPerSide,
            config.NavCellSizeCm,
            surface,
            surfaceAssetPath,
            height,
            heightAssetPath);
        return new Assets(
            SurfaceGrid.Build(config, surface, blockers),
            NavHeightField.FromHeightmap(height, config.NavCellsPerSide, config.NavCellSizeCm));
    }
}
