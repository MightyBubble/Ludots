namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 右键点击下令时抄进指令载荷的形状和 magic box。null 表示不带 auto、形状用 box。
/// </summary>
public sealed class CrowdPlayerOrderPrefs
{
    public string ShapeId { get; set; } = "";
    public bool MagicBox { get; set; }
}
