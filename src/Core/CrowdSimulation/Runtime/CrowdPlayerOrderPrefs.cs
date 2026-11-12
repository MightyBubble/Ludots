namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 右键点击下令时抄进指令载荷的形状、magic box 和乐观迷雾。null 表示不带 auto、形状用 box；
/// 会话已装迷雾且没有偏好时，fogTerrain 用配置里的开局值。
/// </summary>
public sealed class CrowdPlayerOrderPrefs
{
    public string ShapeId { get; set; } = "";
    public bool MagicBox { get; set; }
    /// <summary>下一笔令要不要把未探索地形当成可通行。Bind 时从 fog.optimisticTerrain 抄入。</summary>
    public bool OptimisticTerrain { get; set; }
}
