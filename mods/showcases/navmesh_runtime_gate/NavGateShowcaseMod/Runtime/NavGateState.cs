using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace NavGateShowcaseMod.Runtime;

public enum NavGatePhase
{
    Briefing,
    MarchToB,
    Sealed,
    ArrivedAtB,
    ReturnToA,
    RestAtA,
}

public sealed class NavGateAgent
{
    public Entity Entity;
    public Arch.Core.World World = null!;
    public WorldPositionCm Position => World.Get<WorldPositionCm>(Entity);
    public int[] PathXcm = System.Array.Empty<int>();
    public int[] PathZcm = System.Array.Empty<int>();
    public int PathCursor;
    public bool HasPath;
    public bool DetourPath;
    public int RepathCountdown;
    public bool Arrived;
}

/// <summary>
/// showcase 共享状态：由 Entry 创建、注入各系统。所有数字都来自真实管线
/// （navmesh store / rebuild queue / TryFindPath），HUD 只做读出。
/// </summary>
public sealed class NavGateState
{
    public NavGatePhase Phase = NavGatePhase.Briefing;
    public int PhaseTicks;

    public readonly List<NavGateAgent> Agents = new(NavGateIds.SquadCount);
    public int GoalXcm = NavGateIds.CampBXcm;
    public int GoalYcm = NavGateIds.CampBYcm;

    public bool GateDropped;
    public Entity GateEntity;
    public int GateCycleCount;
    public bool GateFuseAnnounced;
    public bool Frozen;
    public bool OverlayEnabled = true;
    public int ManualRadiusIndex;
    public int PaceIndex = 1;

    public uint LastSeenStoreRevision;
    public int ArrivedCount;
    public int TotalPathLenCm;
    public double LastBatchElapsedMs;
    public int PendingTiles;

    public float Pace => NavGateIds.PaceMultipliers[PaceIndex];
    public string PhaseLabel => Phase switch
    {
        NavGatePhase.Briefing => "集结 · 小队整备于 A 营",
        NavGatePhase.MarchToB => "行军 · A 营 → B 营",
        NavGatePhase.Sealed => "隘口封锁 · 增量重烤中",
        NavGatePhase.ArrivedAtB => "抵达 B 营 · 观察绕行结果",
        NavGatePhase.ReturnToA => "返程 · B 营 → A 营",
        NavGatePhase.RestAtA => "休整 · 即将再次出发",
        _ => "未知",
    };

    public static Fix64 DistanceCm(int axCm, int ayCm, int bxCm, int byCm)
    {
        long dx = axCm - bxCm;
        long dy = ayCm - byCm;
        return Fix64.FromInt((int)System.Math.Sqrt((dx * dx) + (dy * dy)));
    }
}
