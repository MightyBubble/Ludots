using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace MobaDemoMod.Utils
{
    /// <summary>
    /// 委托给 Core 的 GroundRaycastUtil。保留此文件以避免大范围 using 变更。
    /// </summary>
    internal static class GroundRaycast
    {
        public static bool TryGetGroundWorldCm(in ScreenRay ray, in WorldSizeSpec worldSize, out WorldCmInt2 worldCm)
            => GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, worldSize, out worldCm);
    }
}
