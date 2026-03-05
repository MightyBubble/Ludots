using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Platform.Abstractions;

namespace MobaDemoMod.Utils
{
    /// <summary>
    /// 委托给 Core 的 GroundRaycastUtil。保留此文件以避免大范围 using 变更。
    /// </summary>
    internal static class GroundRaycast
    {
        public static bool TryGetGroundWorldCm(in ScreenRay ray, out WorldCmInt2 worldCm)
            => GroundRaycastUtil.TryGetGroundWorldCm(in ray, out worldCm);
    }
}
