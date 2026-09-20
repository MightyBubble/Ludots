namespace NavGateShowcaseMod;

internal static class NavGateIds
{
    public const string MapId = "nav_gate_valley";

    public const int CampAXcm = 1500;
    public const int CampAYcm = 1500;
    public const int CampBXcm = 5500;
    public const int CampBYcm = 5500;
    public const int GateXcm = 3600;
    public const int GateYcm = 3600;
    public const int GateRadiusCm = 1100;

    // 全场戏收敛在西南角 tile(0,0) 内：当前引擎的跨瓦 navmesh 查询存在
    // 南向焊接与深目标投影缺陷（见 README 反向 API 审计"后续"清单），
    // 单瓦舞台先保证演示真实闭环；跨瓦修复后可放大布场。
    public const int SquadCount = 8;
    public const int SquadRingRadiusCm = 420;
    public const int MarchSpeedCmPerSecond = 700;
    public const int WaypointReachCm = 220;
    public const int RepathIntervalTicks = 45;
    public const int BriefingTicks = 150;
    public const int SealedCooldownTicks = 150;
    public const int ArrivedRestTicks = 180;

    public const int GateTriggerDistanceCm = 1500;
    public const int ArriveDistanceCm = 600;

    // 首程兜底：初始瓦片上的路径可能绕开触发圈（离线瓦片与运行时重烤的布线差异），
    // 行军超过该时长仍未触发就强制落门，保证第一圈就出现惊喜时刻。
    public const int MarchGateFallbackTicks = 300;

    // NAV-R2 稳定性熔断：runtime recast 落门/抬门重烤存在同线程长阻塞与
    // BuildPolyDetail 活锁（见 artifacts/techdebt/2608-nav-runtime-bake-livelock.md），
    // 自动巡演最多完整落门 N 圈，之后停止自动落门并显式提示。
    public const int MaxAutoGateCycles = 3;

    public static readonly int[] ManualObstacleRadiusCm = { 1200, 2400, 4800 };
    public static readonly float[] PaceMultipliers = { 0.6f, 1.0f, 1.8f };

    // GroundOverlay / WorldHud stable ids（每帧 Upsert，Id 稳定即无泄漏）
    public const int OverlayGateRing = 9001;
    public const int OverlayGateFill = 9002;
    public const int OverlayCampA = 9011;
    public const int OverlayCampB = 9012;
    public const int OverlayDirtyTileBase = 9100; // +tileIndex*4
    public const int OverlayUnitBase = 9500;      // +agentIndex
    public const int OverlayPathBase = 10000;     // +agentIndex*64 +segment

    public const int HudPhaseBanner = 99001;
    public const int HudMetricsBlock = 99002;
    public const int HudLegend = 99003;
    public const int HudCampA = 99011;
    public const int HudCampB = 99012;
    public const int HudGate = 99013;
}
