using System.Numerics;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// 纯 HUD 镜像 presenter（WorldHud/WorldText + 挂父级固定偏移的闭式形状）的编译标记。
    /// 这类 presenter 的变换是 owner 插值位置的静态偏移，不需要逐行四元数/缩放/朝向重算：
    /// 变换同步走轻量 owner-join 通道（只更位置），其余合同（知识门、剔除 LOD、
    /// 保留式 emit 的稳定 ID 与参数）保持不变。
    /// </summary>
    public struct CompiledHudAnchor
    {
        /// <summary>锚相对 owner 插值后 VisualTransform 的固定偏移（视觉米空间）。</summary>
        public Vector3 VisualOffset;
    }
}
