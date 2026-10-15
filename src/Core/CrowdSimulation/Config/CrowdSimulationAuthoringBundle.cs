namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 编辑器完整包的结构版本。只出现在 { scenario, globals, templates, costMatrix, version } 交换包上。
/// CrowdSimulationConfig.json 拒绝 version 键，完整包的版本不写进那份文件。
/// </summary>
public static class CrowdSimulationAuthoringBundle
{
    public const int SchemaVersion = 2;

    public const string ScenarioKey = "scenario";
    public const string GlobalsKey = "globals";
    public const string TemplatesKey = "templates";
    public const string CostMatrixKey = "costMatrix";
    public const string VersionKey = "version";

    public const string ScenarioLayer = "scenario";
    public const string GlobalsLayer = "globals";
    public const string TemplatesLayer = "templates";
    public const string DebugLayer = "debug";
    public const string DebugViewLayer = "debugView";
    public const string CostMatrixLayer = "costMatrix";
}
