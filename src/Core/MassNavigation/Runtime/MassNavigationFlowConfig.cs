using System.Text.Json.Serialization;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationFlowConfig
{
    [JsonRequired] public bool CrowdCostEnabled { get; set; }
    [JsonRequired] public int CrowdStampBudgetAgentsPerRefresh { get; set; }

    public void Validate()
    {
        if (CrowdStampBudgetAgentsPerRefresh < 0)
        {
            throw new System.InvalidOperationException(
                "MassNavigation flow requires CrowdStampBudgetAgentsPerRefresh >= 0.");
        }

        if (CrowdCostEnabled && CrowdStampBudgetAgentsPerRefresh == 0)
        {
            throw new System.InvalidOperationException(
                "MassNavigation flow requires a positive CrowdStampBudgetAgentsPerRefresh when crowd cost is enabled.");
        }
    }
}
