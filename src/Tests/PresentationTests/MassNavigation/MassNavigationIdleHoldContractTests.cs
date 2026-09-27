using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 无令驻停合同：authored 队无场景目标时，agent 以出生位为锚驻停——
/// 不做自由体漂移（软分离/障碍推不再位移它），被求解器内力推过
/// arrival-recovery 阈值后走回锚点；编组到位判定中已 settled 的
/// 掉队成员不再阻断整单完成。
/// </summary>
[TestFixture]
public class MassNavigationIdleHoldContractTests
{
    private const float AnchorToleranceCm = 80f;

    [Test]
    public void IdleAnchoredAgents_StepWithoutOrders_HoldSpawnPosition()
    {
        using var world = World.Create();
        LoadFriendlyTeamConfig();
        MassNavigationFlowSolverState flow = CreateFlow();
        var navGroups = CreateGroupRuntime(flow);
        float[] spawnX = { 4_950f, 5_050f };
        float[] spawnY = { 5_000f, 5_000f };
        ResetWithSeeds(flow, spawnX, spawnY);

        for (int frame = 0; frame < 90; frame++)
        {
            Step(flow, world, navGroups);
        }

        for (int i = 0; i < spawnX.Length; i++)
        {
            Assert.That(flow.HasUnitTarget(i), Is.True, $"agent {i} 应持有驻停锚点");
            Assert.That(DistanceTo(flow, i, spawnX[i], spawnY[i]), Is.LessThanOrEqualTo(1f),
                $"agent {i} 无令步进后漂离出生位");
            Assert.That(flow.GetVelocityCmPerSecond(i).Length(), Is.EqualTo(0f),
                $"agent {i} 驻停速度应为零");
        }
    }

    [Test]
    public void IdleAnchoredAgent_PushedPastWakeThreshold_WalksBackToAnchor()
    {
        using var world = World.Create();
        LoadFriendlyTeamConfig();
        MassNavigationFlowSolverState flow = CreateFlow();
        var navGroups = CreateGroupRuntime(flow);
        float[] spawnX = { 4_950f, 5_050f };
        float[] spawnY = { 5_000f, 5_000f };
        ResetWithSeeds(flow, spawnX, spawnY);
        Step(flow, world, navGroups);

        // 模拟硬解算/穿越编组的推挤后果：位置被改写而锚点不动（外部位移
        // 会连带平移锚点，不适用此处）。400cm > wakePushDistanceCm=80。
        flow.SetUnitPositionForTests(0, spawnX[0] + 400f, spawnY[0]);

        for (int frame = 0; frame < 90; frame++)
        {
            Step(flow, world, navGroups);
        }

        Assert.That(DistanceTo(flow, 0, spawnX[0], spawnY[0]), Is.LessThanOrEqualTo(AnchorToleranceCm),
            "被推离锚点的驻停 agent 应回到锚点附近");
    }

    [Test]
    public void GroupArrival_StragglerSettlesFarFromSlot_CompletesOrder()
    {
        MassNavigationProfileRegistry.Reset();
        using World world = World.Create();
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.NavigationGroupCapacity = 2;
        config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 2;
        config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 2;

        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));

        int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.idleHoldArrival");
        Entity walker = world.Create(new MassNavigationAgent { ProfileId = profileId });
        Entity straggler = world.Create(new MassNavigationAgent { ProfileId = profileId });
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        simulation.RebuildFromAuthoredAgents(
            world,
            new[] { walker, straggler },
            new[]
            {
                new MassNavigationAgentSeed(1, 1_000f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
                new MassNavigationAgentSeed(1, 1_200f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
            },
            new[] { true, true });

        int[] members = { 0, 1 };
        MassNavigationOrderChainTests.CommitPreparedOrderMove(
            simulation,
            orderToken: 101,
            members,
            teamId: 1,
            destinationWorldCm: new Vector2(4_000f, 4_000f));

        // 正常推进到 walker 到位；straggler 半途被钉住（settled），距自身
        // 槽位永远超出 200cm——对应卡死/放弃的求解器终态。
        bool walkerSettled = false;
        for (int frame = 0; frame < 240 && !walkerSettled; frame++)
        {
            simulation.NavGroupRuntime.UpdateTargets(simulation.MassNavigationFlow, simulation.FrameIndex);
            simulation.StepNavigationForTests(world, 0.05f, runHardResolve: true);
            walkerSettled = simulation.GetFlowSolverForTests().IsUnitSettled(0);
        }

        Assert.That(walkerSettled, Is.True, "walker 应在预算帧内到达槽位");
        simulation.GetFlowSolverForTests().HoldUnitAtCurrentPosition(1);

        simulation.NavGroupRuntime.UpdateTargets(simulation.MassNavigationFlow, simulation.FrameIndex);

        Assert.That(simulation.NavGroupRuntime.TryGetOrderGroup(101, out bool arrived), Is.True);
        Assert.That(arrived, Is.True, "settled 的掉队成员不应阻断编组到位");
    }

    private static void ResetWithSeeds(MassNavigationFlowSolverState flow, float[] spawnX, float[] spawnY)
    {
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        var seeds = new MassNavigationAgentSeed[spawnX.Length];
        for (int i = 0; i < seeds.Length; i++)
        {
            seeds[i] = new MassNavigationAgentSeed(
                relationshipDomainId: 1,
                localPositionXCm: spawnX[i],
                localPositionYCm: spawnY[i],
                heavy: false,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                layer);
        }

        flow.ResetAuthoredAgents(seeds);
    }

    private static void Step(
        MassNavigationFlowSolverState flow,
        World world,
        MassNavigationGroupRuntime navGroups)
    {
        flow.Step(
            dt: 1f / 15f,
            world,
            navGroups,
            runHardResolve: false,
            hardResolveCandidateThresholdAgents: flow.UnitCount + 1);
    }

    private static float DistanceTo(MassNavigationFlowSolverState flow, int index, float xCm, float yCm)
    {
        float dx = flow.GetPositionX(index) - xCm;
        float dy = flow.GetPositionY(index) - yCm;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static void LoadFriendlyTeamConfig()
    {
        TeamManager.LoadConfig(new TeamConfig
        {
            DefaultRelationship = "Friendly",
            Relationships = new System.Collections.Generic.List<RelationshipEntry>(),
        });
    }

    private static MassNavigationGroupRuntime CreateGroupRuntime(MassNavigationFlowSolverState flow)
    {
        MassNavigationConfig config = MassNavigationConfig.Load(
            ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
        return new MassNavigationGroupRuntime(
            config.Semantics.Group,
            new MassNavigationRuntimeCapacityConfig
            {
                NavigationGroupCapacity = 8,
                GroupMembershipAgentCapacity = flow.UnitCount,
                GroupMemberCapacity = flow.UnitCount,
                MovePlanExecutionGroupCapacity = 8,
                MovePlanExecutionMemberCapacity = flow.UnitCount,
                RouteStateCapacity = 8,
                RouteMaxExpandedPerRequest = 128,
                RouteWaypointCapacityPerAgent = 64,
                LoadedChunkCapacity = 16,
                RelationshipDomainCapacity = 4,
                DisplacedAgentCapacity = 64,
            });
    }

    private static MassNavigationFlowSolverState CreateFlow()
    {
        var config = MassNavigationConfig.Load(
            ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
        var flow = new MassNavigationFlowSolverState(new MassNavigationFlowSolverConfig
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
        });
        flow.ArrivalTuning.CopyFrom(config.Arrival);
        flow.AvoidanceTuning.CopyFrom(config.Avoidance);
        flow.Semantics.CopyFrom(config.Semantics);
        return flow;
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
