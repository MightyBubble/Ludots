using System;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Navigation.AgentProfiles;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 流场重建合同：共享障碍排斥场、按包围盒栅格化、障碍不变不重建、分片刷新必须走完整轮，
/// 结果都必须与“逐格扫全部障碍 + 逐格扫 9x9 邻域”的原始算法逐位一致。
/// </summary>
[TestFixture]
public sealed class MassNavigationFlowFieldRebuildTests
{
    private const int RandomSeed = 20260928;

    [TestCase(false)]
    [TestCase(true)]
    public void FlowFields_MatchFullGridReference_AcrossObstacleChanges(bool crowdStampEnabled)
    {
        MassNavigationFlowSolverState flow = CreateScenarioFlow();
        var tuning = new MassNavigationFlowTuning { Enabled = crowdStampEnabled, IterationsPerStep = crowdStampEnabled ? 64 : 0 };
        var random = new Random(RandomSeed);
        MassNavigationObstacleSnapshot[] obstacles = CreateObstacles(random, count: 48);

        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        flow.AdvanceFlowPipeline(tuning, refreshFlow: true, refreshCrowd: true);
        AssertFlowFieldsMatchReference(flow, obstacles, "initial obstacles");

        for (int i = 0; i < 6; i++)
        {
            int index = random.Next(obstacles.Length);
            obstacles[index] = new MassNavigationObstacleSnapshot(
                obstacles[index].WorldXCm + 730f,
                obstacles[index].WorldYCm - 410f,
                obstacles[index].RadiusCm);
        }

        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        flow.AdvanceFlowPipeline(tuning, refreshFlow: false, refreshCrowd: false);
        AssertFlowFieldsMatchReference(flow, obstacles, "moved obstacles");

        MassNavigationObstacleSnapshot[] none = Array.Empty<MassNavigationObstacleSnapshot>();
        flow.ResetRuntimeObstaclesFromWorld(none);
        flow.AdvanceFlowPipeline(tuning, refreshFlow: false, refreshCrowd: false);
        AssertFlowFieldsMatchReference(flow, none, "cleared obstacles");
    }

    [Test]
    public void UnchangedObstacles_DoNotRebuildStaticCostOrFlow()
    {
        MassNavigationFlowSolverState flow = CreateScenarioFlow();
        var tuning = new MassNavigationFlowTuning { Enabled = false, IterationsPerStep = 0 };
        MassNavigationObstacleSnapshot[] obstacles = CreateObstacles(new Random(RandomSeed), count: 16);
        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        flow.AdvanceFlowPipeline(tuning, refreshFlow: false, refreshCrowd: false);
        int revision = flow.StaticObstacleCostRevision;

        flow.ResetRuntimeObstaclesFromWorld((MassNavigationObstacleSnapshot[])obstacles.Clone());

        Assert.That(flow.AdvanceFlowPipeline(tuning, refreshFlow: false, refreshCrowd: false), Is.False,
            "重新提交一模一样的障碍，不应产生任何流场工作。");
        Assert.That(flow.StaticObstacleCostRevision, Is.EqualTo(revision));

        obstacles[0] = new MassNavigationObstacleSnapshot(obstacles[0].WorldXCm + 200f, obstacles[0].WorldYCm, obstacles[0].RadiusCm);
        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        Assert.That(flow.AdvanceFlowPipeline(tuning, refreshFlow: false, refreshCrowd: false), Is.True);
        Assert.That(flow.StaticObstacleCostRevision, Is.EqualTo(revision + 1));
    }

    [Test]
    public void SlicedPipeline_FinishesEveryPass_WhenRefreshIsRequestedEveryStep()
    {
        const int sliceCount = 4;
        MassNavigationFlowSolverState flow = CreateScenarioFlow();
        Assert.That(flow.FlowStateCount, Is.GreaterThan(1), "需要多个 flow state 才能覆盖分片续跑。");
        var tuning = new MassNavigationFlowTuning { Enabled = false, IterationsPerStep = 0 };
        var random = new Random(RandomSeed);
        MassNavigationObstacleSnapshot[] obstacles = CreateObstacles(random, count: 24);
        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        flow.AdvanceFlowPipeline(tuning, refreshFlow: true, refreshCrowd: false);

        obstacles = CreateObstacles(random, count: 24);
        flow.ResetRuntimeObstaclesFromWorld(obstacles);
        for (int step = 0; step < sliceCount * 3; step++)
        {
            flow.RequestFlowRebuild();
            flow.AdvanceFlowPipeline(
                tuning,
                refreshFlow: true,
                refreshCrowd: true,
                agentSliceIndex: step % sliceCount,
                agentSliceCount: sliceCount);
        }

        AssertFlowFieldsMatchReference(flow, obstacles, "sliced refresh under continuous invalidation");
    }

    [Test]
    public void AuthoredTeamsWithoutTarget_DoNotAllocateFlowFields()
    {
        MassNavigationFlowSolverState flow = CreateConfiguredFlow();
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        flow.ResetAuthoredAgents(new[]
        {
            CreateAuthoredSeed(teamId: 1, 1_000f, 1_000f, layer),
            CreateAuthoredSeed(teamId: 2, 2_000f, 2_000f, layer),
        });

        flow.AdvanceFlowPipeline(new MassNavigationFlowTuning(), refreshFlow: true, refreshCrowd: true);

        Assert.That(flow.FlowStateCount, Is.EqualTo(2));
        for (int i = 0; i < flow.FlowStateCount; i++)
        {
            Assert.That(flow.GetFlowField(i, out _, out _).Length, Is.Zero, $"flow state {i}");
        }
    }

    [Test]
    public void EnvironmentSignature_CountsCompoundBlockerOnceLikeRegistration()
    {
        using var world = World.Create();
        var projection = new MassNavigationFlowObstacleProjection();
        projection.SetPiece(0, ManifestationObstacleShape2D.Circle, 0, 0, 80);
        projection.SetPiece(1, ManifestationObstacleShape2D.Circle, 120, 0, 60);
        projection.SetPiece(2, ManifestationObstacleShape2D.Circle, -120, 40, 60);
        world.Create(WorldPositionCm.FromCm(500, 500), projection);

        var signature = MassNavigationEnvironmentBindingSystem.ComputeSignature(world);

        Assert.That(signature.BlockerCount, Is.EqualTo(1),
            "签名计数必须与 RegisterBlocker 一样按实体计，否则复合障碍每帧都会触发重绑。");
    }

    [Test]
    public void EnvironmentSignature_IgnoresQueryOrderButTracksGeometry()
    {
        using var world = World.Create();
        Entity first = world.Create(WorldPositionCm.FromCm(100, 100), CreateSinglePieceProjection(50));
        world.Create(WorldPositionCm.FromCm(900, 900), CreateSinglePieceProjection(70));
        var before = MassNavigationEnvironmentBindingSystem.ComputeSignature(world);

        world.Add(first, new MassNavigationBlockerProfile { RadiusCm = 50f });
        var afterArchetypeMove = MassNavigationEnvironmentBindingSystem.ComputeSignature(world);

        world.Set(first, WorldPositionCm.FromCm(160, 100));
        var afterMove = MassNavigationEnvironmentBindingSystem.ComputeSignature(world);

        Assert.Multiple(() =>
        {
            Assert.That(afterArchetypeMove, Is.EqualTo(before), "实体换 chunk 只改变遍历顺序，不应触发重绑。");
            Assert.That(afterMove.Hash, Is.Not.EqualTo(before.Hash), "障碍真的移动了，签名必须变化。");
        });
    }

    private static MassNavigationFlowObstacleProjection CreateSinglePieceProjection(int radiusCm)
    {
        var projection = new MassNavigationFlowObstacleProjection();
        projection.SetPiece(0, ManifestationObstacleShape2D.Circle, 0, 0, radiusCm);
        return projection;
    }

    private static MassNavigationObstacleSnapshot[] CreateObstacles(Random random, int count)
    {
        var obstacles = new MassNavigationObstacleSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            float x = -400f + (float)(random.NextDouble() * 10_800.0);
            float y = -400f + (float)(random.NextDouble() * 10_800.0);
            float radius = 60f + (float)(random.NextDouble() * 420.0);
            obstacles[i] = new MassNavigationObstacleSnapshot(x, y, radius);
        }

        return obstacles;
    }

    private static void AssertFlowFieldsMatchReference(
        MassNavigationFlowSolverState flow,
        ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles,
        string label)
    {
        float[] cost = ReferenceStaticCost(flow, obstacles);
        int checkedStates = 0;
        for (int i = 0; i < flow.FlowStateCount; i++)
        {
            ReadOnlySpan<float> actual = flow.GetFlowField(i, out float targetX, out float targetY);
            if (actual.Length == 0)
            {
                continue;
            }

            float[] expected = ReferenceFlow(flow, cost, targetX, targetY);
            int mismatches = 0;
            for (int k = 0; k < expected.Length; k++)
            {
                if (BitConverter.SingleToInt32Bits(actual[k]) != BitConverter.SingleToInt32Bits(expected[k]))
                {
                    mismatches++;
                }
            }

            Assert.That(mismatches, Is.Zero, $"{label}: flow state {i} differs from full-grid reference in {mismatches} components.");
            checkedStates++;
        }

        Assert.That(checkedStates, Is.GreaterThan(0), $"{label}: no targeted flow state was checked.");
    }

    private static float[] ReferenceStaticCost(MassNavigationFlowSolverState flow, ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles)
    {
        var semantics = flow.Semantics.Solver;
        var cost = new float[flow.GridWidth * flow.GridHeight];
        for (int y = 0; y < flow.GridHeight; y++)
        {
            for (int x = 0; x < flow.GridWidth; x++)
            {
                float wx = (x + 0.5f) * flow.FlowCellSizeCm;
                float wy = (y + 0.5f) * flow.FlowCellSizeCm;
                bool blocked = false;
                for (int i = 0; i < obstacles.Length; i++)
                {
                    float dx = wx - (obstacles[i].WorldXCm - flow.WorldOriginXCm);
                    float dy = wy - (obstacles[i].WorldYCm - flow.WorldOriginYcm);
                    float r2 = obstacles[i].RadiusCm * obstacles[i].RadiusCm;
                    if ((dx * dx) + (dy * dy) < r2)
                    {
                        blocked = true;
                        break;
                    }
                }

                cost[(y * flow.GridWidth) + x] = blocked ? semantics.FlowBlockedCellCost : 1f;
            }
        }

        return cost;
    }

    private static float[] ReferenceFlow(MassNavigationFlowSolverState flow, float[] cost, float targetX, float targetY)
    {
        var s = flow.Semantics.Solver;
        int width = flow.GridWidth;
        int height = flow.GridHeight;
        var result = new float[width * height * 2];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width) + x;
                int flowIndex = idx << 1;
                if (cost[idx] > s.FlowBlockedCellThreshold)
                {
                    continue;
                }

                float wx = (x + 0.5f) * flow.FlowCellSizeCm;
                float wy = (y + 0.5f) * flow.FlowCellSizeCm;
                float dx = targetX - wx;
                float dy = targetY - wy;
                float distSq = dx * dx + dy * dy;
                if (distSq < s.FlowTargetStopDistanceSq)
                {
                    continue;
                }

                float invDist = SafeInverseSqrt(s, distSq);
                dx *= invDist;
                dy *= invDist;
                float avoidX = 0f;
                float avoidY = 0f;
                int radius = s.FlowObstacleNeighborRadiusCells;
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int nx = x + offsetX;
                        int ny = y + offsetY;
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                        {
                            continue;
                        }

                        if (cost[(ny * width) + nx] > s.FlowBlockedCellThreshold)
                        {
                            float ovx = -offsetX;
                            float ovy = -offsetY;
                            float obstacleDistSq = (ovx * ovx) + (ovy * ovy);
                            if (obstacleDistSq > s.NormalizationEpsilonSq)
                            {
                                float invObstacleDist = SafeInverseSqrt(s, obstacleDistSq);
                                float obstacleDist = obstacleDistSq * invObstacleDist;
                                float obstacleWeight = s.FlowObstacleNeighborWeight / (obstacleDist * obstacleDist);
                                avoidX += (ovx * invObstacleDist) * obstacleWeight;
                                avoidY += (ovy * invObstacleDist) * obstacleWeight;
                            }
                        }
                    }
                }

                float flowX = dx + (avoidX * s.FlowObstacleAvoidanceWeight);
                float flowY = dy + (avoidY * s.FlowObstacleAvoidanceWeight);
                float flowLengthSq = flowX * flowX + flowY * flowY;
                if (flowLengthSq >= s.NormalizationEpsilonSq)
                {
                    float invFlow = SafeInverseSqrt(s, flowLengthSq);
                    result[flowIndex] = flowX * invFlow;
                    result[flowIndex + 1] = flowY * invFlow;
                }
            }
        }

        return result;
    }

    private static float SafeInverseSqrt(MassNavigationSolverSemantics semantics, float value)
    {
        return value < semantics.InverseSqrtMinValue ? 0f : 1f / MathF.Sqrt(value);
    }

    private static MassNavigationFlowSolverState CreateScenarioFlow()
    {
        MassNavigationFlowSolverState flow = CreateConfiguredFlow();
        var spawnLayout = new MassNavigationScenarioSpawnLayoutConfig
        {
            Kind = "QuadrantSpread",
            OrbitRadiusCm = 3_000f,
            RandomSeed = RandomSeed,
        };
        spawnLayout.Validate();
        flow.Reset(
            new[] { 1, 2, 3, 4 },
            unitsPerTeam: 8,
            CreateProfileSet(),
            new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u),
            spawnLayout);
        return flow;
    }

    private static MassNavigationFlowSolverState CreateConfiguredFlow()
    {
        MassNavigationConfig config = MassNavigationConfig.Load(
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

    private static MassNavigationAgentSeed CreateAuthoredSeed(int teamId, float localX, float localY, MassNavigationAgentLayer layer)
    {
        return new MassNavigationAgentSeed(
            relationshipDomainId: teamId,
            localPositionXCm: localX,
            localPositionYCm: localY,
            heavy: false,
            navMass: 1f,
            visualScale: 1f,
            bodyRadiusCm: 20f,
            speedCmPerSecond: 800f,
            layer);
    }

    private static MassNavigationAgentProfileSetConfig CreateProfileSet()
    {
        var profileSet = new MassNavigationAgentProfileSetConfig
        {
            DefaultProfileId = "light",
            Profiles = new[]
            {
                new MassNavigationAgentProfileConfig
                {
                    Id = "light",
                    Heavy = false,
                    VisualScale = 1f,
                    SpeedCmPerSecond = 800f,
                    EveryNth = 0,
                    NthOffset = 0,
                },
            },
        };
        profileSet.Validate();
        profileSet.BindAgentProfiles(new AgentProfileRegistry(new[]
        {
            new AgentProfileConfig
            {
                Id = "light",
                RadiusCm = 20,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0,
            },
        }));
        return profileSet;
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
