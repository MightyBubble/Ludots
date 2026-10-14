namespace Ludots.Core.CrowdSimulation.Presentation;

public enum CrowdCommandCardState : byte
{
    None = 0,
    Planning = 1,
    Moving = 2,
    Arrived = 3,
    Partial = 4,
}

/// <summary>
/// 指令状态卡：只看计数。规划中无人移动是规划；有人移动且无人到达是移动；
/// 全部到达是到达；到达和移动、不可达或仍在规划混在一起是部分到达。
/// </summary>
public static class CrowdCommandStatusCard
{
    public static CrowdCommandCardState Classify(int planning, int moving, int arrived, int other)
    {
        int total = planning + moving + arrived + other;
        if (total <= 0) return CrowdCommandCardState.None;
        if (arrived == total) return CrowdCommandCardState.Arrived;
        if (arrived > 0) return CrowdCommandCardState.Partial;
        if (moving > 0) return CrowdCommandCardState.Moving;
        if (planning > 0 && other == 0) return CrowdCommandCardState.Planning;
        return CrowdCommandCardState.Partial;
    }
}
