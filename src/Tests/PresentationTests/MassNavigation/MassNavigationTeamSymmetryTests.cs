using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Navigation.AgentProfiles;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 两队对称性回归：arena 同款配置（2 队 × 48，OrbitOpposedTargets 2600，无场景障碍）
/// 验证 solver 对两队的处理几何镜像对称——归位/集结行为不应有引擎级偏差。
/// 若本测试通过而 arena 中红蓝归位不对称，则不对称来自场景障碍布局（crates/plate/door），
/// 而非 MassNavigation solver。
/// </summary>
[TestFixture]
public class MassNavigationTeamSymmetryTests
{
    private const int Teams = 2;
    private const int UnitsPerTeam = 48;
    private const float OrbitRadiusCm = 2_600f;
    private const float CenterX = 5_000f;
    private const float CenterY = 5_000f;
    private const float SymmetryToleranceCm = 80f;

    [Test]
    public void InitialSpawn_IsMirrorSymmetricAroundCenter()
    {
        MassNavigationFlowSolverState flow = CreateArenaFlow();
        flow.Reset(
            new[] { 1, 2 },
            UnitsPerTeam,
            CreateArenaProfileSet(),
            CreateAgentLayer(),
            CreateArenaSpawnLayout());

        // 每队第 i 个 agent（按队内 localIndex）应关于 (5000,5000) 镜像对称。
        // 容差覆盖 DeterministicSpawnJitterCm（jitter 含 TeamId，幅度 ±SpawnJitterCm=12）。
        float jitterTolerance = 12f * 2f + 1f;
        for (int team0Local = 0; team0Local < UnitsPerTeam; team0Local++)
        {
            int team0Index = team0Local;
            int team1Index = UnitsPerTeam + team0Local; // 第二队从 UnitCount/2 开始
            float t0x = flow.GetPositionX(team0Index);
            float t0y = flow.GetPositionY(team0Index);
            float t1x = flow.GetPositionX(team1Index);
            float t1y = flow.GetPositionY(team1Index);

            Assert.That(MathF.Abs((t0x - CenterX) - (-(t1x - CenterX))), Is.LessThanOrEqualTo(jitterTolerance),
                $"team0[{team0Local}] x={t0x} team1[{team0Local}] x={t1x} 关于中心 x 不对称");
            Assert.That(MathF.Abs((t0y - CenterY) - (-(t1y - CenterY))), Is.LessThanOrEqualTo(jitterTolerance),
                $"team0[{team0Local}] y={t0y} team1[{team0Local}] y={t1y} 关于中心 y 不对称");
        }
    }

    [Test]
    public void NoTargetSteering_KeepsBothTeamsMirrorSymmetric()
    {
        using var world = World.Create();
        MassNavigationFlowSolverState flow = CreateArenaFlow();
        flow.Reset(
            new[] { 1, 2 },
            UnitsPerTeam,
            CreateArenaProfileSet(),
            CreateAgentLayer(),
            CreateArenaSpawnLayout());
        TeamManager.LoadConfig(new TeamConfig
        {
            DefaultRelationship = "Friendly",
            Relationships = new List<RelationshipEntry>(),
        });
        var navGroups = new MassNavigationGroupRuntime(
            CreateArenaConfig().Semantics.Group,
            CreateRuntimeCapacity(agentCapacity: flow.UnitCount, groupMemberCapacity: flow.UnitCount));

        // 无显式单位目标：agent 由 solver 的 team-slot 目标驱动（集结/归位）。
        // 步进 90 帧（≈15Hz × 6s），比较两队质心关于中心的镜像对称性。
        for (int frame = 0; frame < 90; frame++)
        {
            flow.Step(
                dt: 1f / 15f,
                world,
                navGroups,
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: flow.UnitCount + 1);
        }

        (float cx0, float cy0) = ComputeTeamCentroid(flow, teamId: 1);
        (float cx1, float cy1) = ComputeTeamCentroid(flow, teamId: 2);

        Assert.That(MathF.Abs((cx0 - CenterX) - (-(cx1 - CenterX))), Is.LessThanOrEqualTo(SymmetryToleranceCm),
            $"team0 centroid x={cx0} team1 centroid x={cx1} 归位关于中心 x 不对称");
        Assert.That(MathF.Abs((cy0 - CenterY) - (-(cy1 - CenterY))), Is.LessThanOrEqualTo(SymmetryToleranceCm),
            $"team0 centroid y={cy0} team1 centroid y={cy1} 归位关于中心 y 不对称");
    }

    [Test]
    public void SymmetricShockwavePush_KeepsBothTeamsMirrorSymmetric()
    {
        using var world = World.Create();
        MassNavigationFlowSolverState flow = CreateArenaFlow();
        flow.Reset(
            new[] { 1, 2 },
            UnitsPerTeam,
            CreateArenaProfileSet(),
            CreateAgentLayer(),
            CreateArenaSpawnLayout());
        TeamManager.LoadConfig(new TeamConfig
        {
            DefaultRelationship = "Friendly",
            Relationships = new List<RelationshipEntry>(),
        });
        var navGroups = new MassNavigationGroupRuntime(
            CreateArenaConfig().Semantics.Group,
            CreateRuntimeCapacity(agentCapacity: flow.UnitCount, groupMemberCapacity: flow.UnitCount));

        // 让两队在无命令下集结一段时间（到达 team-slot 目标附近）。
        for (int frame = 0; frame < 120; frame++)
        {
            flow.Step(
                dt: 1f / 15f,
                world,
                navGroups,
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: flow.UnitCount + 1);
        }

        (float beforeX0, float _) = ComputeTeamCentroid(flow, teamId: 1);
        (float beforeX1, float _) = ComputeTeamCentroid(flow, teamId: 2);

        // 对称震波（震心在中心）：team0（西侧）向西推离 400cm，team1（东侧）向东推离 400cm。
        var team0Indices = new List<int>(UnitsPerTeam);
        var team1Indices = new List<int>(UnitsPerTeam);
        for (int i = 0; i < flow.UnitCount; i++)
        {
            if (flow.GetTeam(i) == 1)
            {
                team0Indices.Add(i);
            }
            else
            {
                team1Indices.Add(i);
            }
        }

        flow.ApplyExternalDisplacement(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(team0Indices), -400f, 0f);
        flow.ApplyExternalDisplacement(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(team1Indices), +400f, 0f);

        // 震波推离后归位（继续走 team-slot 目标），观察两队质心是否保持镜像对称。
        for (int frame = 0; frame < 90; frame++)
        {
            flow.Step(
                dt: 1f / 15f,
                world,
                navGroups,
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: flow.UnitCount + 1);
        }

        (float afterX0, float _) = ComputeTeamCentroid(flow, teamId: 1);
        (float afterX1, float _) = ComputeTeamCentroid(flow, teamId: 2);

        // 推离前对称性基线（容差放宽：推离后归位过程含分离/避障）。
        Assert.That(MathF.Abs((beforeX0 - CenterX) - (-(beforeX1 - CenterX))), Is.LessThanOrEqualTo(SymmetryToleranceCm),
            $"集结后基线不对称 beforeX0={beforeX0} beforeX1={beforeX1}");
        Assert.That(MathF.Abs((afterX0 - CenterX) - (-(afterX1 - CenterX))), Is.LessThanOrEqualTo(SymmetryToleranceCm * 2f),
            $"震波归位后不对称 afterX0={afterX0} afterX1={afterX1}（对称推离应保持镜像）");
    }

    private static (float cx, float cy) ComputeTeamCentroid(MassNavigationFlowSolverState flow, int teamId)
    {
        float sx = 0f;
        float sy = 0f;
        int count = 0;
        for (int i = 0; i < flow.UnitCount; i++)
        {
            if (flow.GetTeam(i) != teamId)
            {
                continue;
            }

            sx += flow.GetPositionX(i);
            sy += flow.GetPositionY(i);
            count++;
        }

        Assert.That(count, Is.EqualTo(UnitsPerTeam), $"team {teamId} 单位数不正确");
        return (sx / count, sy / count);
    }

    private static MassNavigationFlowSolverState CreateArenaFlow()
    {
        MassNavigationConfig config = CreateArenaConfig();
        var flow = new MassNavigationFlowSolverState(CreateSolverConfig());
        flow.ArrivalTuning.CopyFrom(config.Arrival);
        flow.AvoidanceTuning.CopyFrom(config.Avoidance);
        flow.Semantics.CopyFrom(config.Semantics);
        return flow;
    }

    private static MassNavigationConfig CreateArenaConfig()
    {
        return MassNavigationConfig.Load(
            ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
    }

    private static MassNavigationFlowSolverConfig CreateSolverConfig()
    {
        return new MassNavigationFlowSolverConfig
        {
            FieldWidthCm = 10_000,
            FieldHeightCm = 10_000,
            FlowCellSizeCm = 100,
            MaxObstacleCount = 64,
            ParallelWorkerCount = 1,
            SeparationHashCellSizeCm = 100,
            SeparationHashMinSearchRadiusCells = 2,
            HardResolveHashCellSizeCm = 50,
            HardResolveHashMinSearchRadiusCells = 1,
            PlayAreaMinXCm = 50f,
            PlayAreaMaxXCm = 9_950f,
            PlayAreaMinYCm = 50f,
            PlayAreaMaxYCm = 9_950f,
        };
    }

    private static MassNavigationAgentLayer CreateAgentLayer()
    {
        return new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
    }

    private static MassNavigationScenarioSpawnLayoutConfig CreateArenaSpawnLayout()
    {
        var spawnLayout = new MassNavigationScenarioSpawnLayoutConfig
        {
            Kind = "OrbitOpposedTargets",
            OrbitRadiusCm = OrbitRadiusCm,
            RandomSeed = 12648430,
        };
        spawnLayout.Validate();
        return spawnLayout;
    }

    private static MassNavigationAgentProfileSetConfig CreateArenaProfileSet()
    {
        var profileSet = new MassNavigationAgentProfileSetConfig
        {
            DefaultProfileId = "light",
            Profiles = new MassNavigationAgentProfileConfig[]
            {
                new()
                {
                    Id = "heavy",
                    Heavy = true,
                    VisualScale = 0.34f,
                    SpeedCmPerSecond = 800f,
                    EveryNth = 7,
                    NthOffset = 0,
                },
                new()
                {
                    Id = "light",
                    Heavy = false,
                    VisualScale = 0.22f,
                    SpeedCmPerSecond = 800f,
                    EveryNth = 0,
                    NthOffset = 0,
                },
            },
        };
        profileSet.Validate();
        profileSet.BindAgentProfiles(CreateArenaAgentProfiles());
        return profileSet;
    }

    private static AgentProfileRegistry CreateArenaAgentProfiles()
    {
        return new AgentProfileRegistry(new[]
        {
            new AgentProfileConfig
            {
                Id = "heavy",
                RadiusCm = 28,
                HeightCm = 200,
                ClearanceCm = 40,
                Mass = 2,
                Layer = 0,
            },
            new AgentProfileConfig
            {
                Id = "light",
                RadiusCm = 20,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0,
            },
        });
    }

    private static MassNavigationRuntimeCapacityConfig CreateRuntimeCapacity(
        int agentCapacity,
        int groupMemberCapacity)
    {
        return new MassNavigationRuntimeCapacityConfig
        {
            NavigationGroupCapacity = 8,
            GroupMembershipAgentCapacity = agentCapacity,
            GroupMemberCapacity = groupMemberCapacity,
            MovePlanExecutionGroupCapacity = 8,
            MovePlanExecutionMemberCapacity = groupMemberCapacity,
            RouteStateCapacity = 8,
            RouteMaxExpandedPerRequest = 128,
            RouteWaypointCapacityPerAgent = 64,
            LoadedChunkCapacity = 16,
            RelationshipDomainCapacity = 4,
            DisplacedAgentCapacity = 64,
        };
    }

    private static string MassNavigationModRoot()
    {
        string cwd = TestContext.CurrentContext.TestDirectory;
        return Path.GetFullPath(Path.Combine(cwd, "../../../../../../mods/capabilities/navigation/MassNavigationMod"));
    }

    private static JsonObject ReadObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
