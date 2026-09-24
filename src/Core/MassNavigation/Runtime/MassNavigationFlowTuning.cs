namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationFlowTuning
{
    /// <summary>
    /// 每次 flow 场刷新为流场计入 crowd 成本的 agent 采样预算（0 = 流场只反映静态障碍与目标，
    /// 不反映人群本身）。采样游标在轮次内推进，跨轮次覆盖全体。
    /// </summary>
    public int CrowdStampBudgetUnits { get; set; }

    public void Validate()
    {
        if (CrowdStampBudgetUnits < 0)
        {
            throw new System.InvalidOperationException(
                "MassNavigation flow requires CrowdStampBudgetUnits >= 0.");
        }
    }
}
