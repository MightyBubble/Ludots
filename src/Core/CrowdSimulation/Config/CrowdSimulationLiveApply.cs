using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 调试参数、调试视图、生产配置重建的门闩。校验失败不调用提交。
/// </summary>
public static class CrowdSimulationLiveApply
{
    public static CrowdSimulationEditorResult ApplyDebug(JsonObject document, Action commit)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commit);
        try
        {
            CrowdSimulationDebugConfig.Load(document);
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex));
        }

        commit();
        return CrowdSimulationEditorResult.Succeeded();
    }

    public static CrowdSimulationEditorResult ApplyDebugView(JsonObject document, Action commit)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commit);
        try
        {
            CrowdSimulationDebugViewConfig.Load(document, "CrowdSimulationDebugView.json");
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex));
        }

        commit();
        return CrowdSimulationEditorResult.Succeeded();
    }

    public static CrowdSimulationEditorResult ApplyRebuild(
        Func<IReadOnlyList<CrowdSimulationAuthoringIssue>> validate,
        Action rebuild)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(rebuild);
        IReadOnlyList<CrowdSimulationAuthoringIssue> issues = validate();
        if (issues.Count > 0)
        {
            return CrowdSimulationEditorResult.Failed(issues);
        }

        rebuild();
        return CrowdSimulationEditorResult.Succeeded(rebuilt: true);
    }
}
