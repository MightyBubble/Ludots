using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Components
{
    public struct Position
    {
        public IntVector2 GridPos;
    }

    public struct Velocity
    {
        public IntVector2 Value;
    }

    /// <summary>
    /// 世界位置（定点数厘米）- 逻辑层位置真相源。
    /// 
    /// 单位：厘米 (cm)
    /// 格式：Fix64Vec2 (Q31.32 定点数)
    /// 
    /// 对于有物理的实体，此组件由 Physics2D 系统更新。
    /// 对于无物理的实体，此组件直接作为位置真相源。
    /// 
    /// 渲染层通过 WorldToVisualSyncSystem 读取此组件并插值到 VisualTransform。
    /// </summary>
    public struct WorldPositionCm
    {
        public Fix64Vec2 Value;
        
        /// <summary>
        /// 从整数厘米创建
        /// </summary>
        public static WorldPositionCm FromCm(int x, int y) => new WorldPositionCm 
        { 
            Value = Fix64Vec2.FromInt(x, y) 
        };
        
        /// <summary>
        /// 从浮点厘米创建
        /// </summary>
        public static WorldPositionCm FromCmFloat(float x, float y) => new WorldPositionCm 
        { 
            Value = Fix64Vec2.FromFloat(x, y) 
        };
        
        /// <summary>
        /// 转换为整数厘米（四舍五入）
        /// </summary>
        public WorldCmInt2 ToWorldCmInt2() => Value.ToWorldCmInt2();
    }
    
    /// <summary>
    /// 上一帧的世界位置（定点数厘米），用于渲染插值。
    /// 在每个 FixedUpdate 开始时由 SavePreviousWorldPositionSystem 保存。
    /// </summary>
    public struct PreviousWorldPositionCm
    {
        public Fix64Vec2 Value;
    }
    
    /// <summary>
    /// 逻辑层面朝方向（弧度，0 = +X 方向，逆时针正方向）。
    /// 
    /// 可选组件：只有需要旋转的实体才挂载。
    /// WorldToVisualSyncSystem 检测到此组件后，会同步到 VisualTransform.Rotation。
    /// 
    /// 对于 2D 游戏，这表示在 XY 逻辑平面上的朝向角度，
    /// 同步到 3D XZ 视觉平面时转换为绕 Y 轴旋转。
    /// </summary>
    public struct FacingDirection
    {
        /// <summary>
        /// 面朝方向角度（弧度）。
        /// 0 = +X, π/2 = +Y(逻辑) → -Z(视觉)
        /// </summary>
        public float AngleRad;
    }
    
    public struct Health
    {
        public int Current;
        public int Max;
    }
    
    public struct Name
    {
        public string Value; // Note: String in component is not ideal for ECS performance but okay for identifiers
    }
}
