namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 编辑器完整包的字段：scenario、globals、templates、traversal。
/// traversal 就是 CrowdSimulation/traversal.json 合并后的对象。
/// </summary>
public static class CrowdSimulationAuthoringBundle
{
    public const string ScenarioKey = "scenario";
    public const string GlobalsKey = "globals";
    public const string TemplatesKey = "templates";
    public const string TraversalKey = "traversal";

    public const string ScenarioLayer = "scenario";
    public const string GlobalsLayer = "globals";
    public const string TemplatesLayer = "templates";
    public const string DebugLayer = "debug";
    public const string DebugViewLayer = "debugView";
    public const string TraversalLayer = "traversal";

    public static readonly string[] BundleKeys = [ScenarioKey, GlobalsKey, TemplatesKey, TraversalKey];
}
