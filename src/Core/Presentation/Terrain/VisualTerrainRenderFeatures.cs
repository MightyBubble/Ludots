using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    [Flags]
    public enum VisualTerrainSurfaceFlags : byte
    {
        None = 0,
        Water = 1 << 0,
        Ramp = 1 << 1,
        Blocked = 1 << 2
    }

    public readonly struct VisualTerrainRenderCell
    {
        public VisualTerrainRenderCell(
            byte heightLevel,
            int surfaceHeightCm,
            byte waterHeightLevel,
            int waterHeightCm,
            VisualTerrainSurfaceFlags surfaceFlags,
            byte areaId)
        {
            HeightLevel = heightLevel;
            SurfaceHeightCm = surfaceHeightCm;
            WaterHeightLevel = waterHeightLevel;
            WaterHeightCm = waterHeightCm;
            SurfaceFlags = surfaceFlags;
            AreaId = areaId;
        }

        public byte HeightLevel { get; }

        public int SurfaceHeightCm { get; }

        public byte WaterHeightLevel { get; }

        public int WaterHeightCm { get; }

        public VisualTerrainSurfaceFlags SurfaceFlags { get; }

        public byte AreaId { get; }

        public bool IsRamp => (SurfaceFlags & VisualTerrainSurfaceFlags.Ramp) != 0;

        public bool IsBlocked => (SurfaceFlags & VisualTerrainSurfaceFlags.Blocked) != 0;

        public bool HasWater => (SurfaceFlags & VisualTerrainSurfaceFlags.Water) != 0 || WaterHeightLevel > 0;
    }

    /// <summary>
    /// Optional terrain render metadata for sources whose authoring model has gameplay cell fields.
    /// Height sampling remains owned by IVisualHeightmap; this contract only exposes WYSIWYG styling data.
    /// </summary>
    public interface IVisualTerrainRenderFeatureSource
    {
        WorldAabbCm FeatureBounds { get; }

        int FeatureCellColumns { get; }

        int FeatureCellRows { get; }

        bool TryReadFeatureCell(int cellX, int cellY, out VisualTerrainRenderCell cell);
    }
}
