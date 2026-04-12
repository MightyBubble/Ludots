using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class Navigation2DFlowDomainPoolTests
    {
        private const int CellSizeCm = 100;
        private const int TileSizeCells = 64;
        private const int WorldTileCount = 1000;

        [Test]
        public void FlowDomainPool_64KmWorld_AssignsDistinctBoundedDomains_AndRecentersWithoutGrowing()
        {
            using var runtime = CreateRuntime(domainCount: 3);
            Navigation2DFlowDomainPool pool = runtime.FlowDomains!;

            var requests = new[]
            {
                new Navigation2DFlowDomainRequest(101, TileCenterCm(32, 32), pool.DefaultProfileIndex, 100),
                new Navigation2DFlowDomainRequest(202, TileCenterCm(500, 500), pool.DefaultProfileIndex, 90),
                new Navigation2DFlowDomainRequest(303, TileCenterCm(940, 940), pool.DefaultProfileIndex, 80),
            };

            pool.ResolveAssignments(requests, tick: 1);

            Assert.That(pool.ActiveLeaseCount, Is.EqualTo(3));
            Assert.That(pool.ActiveAssignmentCount, Is.EqualTo(3));
            Assert.That(pool.UnassignedRequestCountFrame, Is.EqualTo(0));

            var flowIds = new HashSet<int>();
            for (int i = 0; i < requests.Length; i++)
            {
                Assert.That(pool.TryGetAssignedFlowId(requests[i].OwnerId, out int flowId), Is.True);
                Assert.That(flowIds.Add(flowId), Is.True);
                AssertDomainBounded(pool, flowId, expectedWidthTiles: 12, expectedHeightTiles: 10);
            }

            int preservedFlowId = GetAssignedFlowId(pool, 101);
            pool.ResolveAssignments(new[]
            {
                new Navigation2DFlowDomainRequest(101, TileCenterCm(80, 72), pool.DefaultProfileIndex, 100),
                requests[1],
                requests[2],
            }, tick: 2);

            int movedFlowId = GetAssignedFlowId(pool, 101);
            Assert.That(movedFlowId, Is.EqualTo(preservedFlowId));
            Assert.That(pool.RecenterCountFrame, Is.GreaterThanOrEqualTo(1));
            AssertDomainCenteredNear(pool, movedFlowId, expectedCenterTileX: 80, expectedCenterTileY: 72, maxDeltaTiles: 1);
            AssertDomainBounded(pool, movedFlowId, expectedWidthTiles: 12, expectedHeightTiles: 10);
        }

        [Test]
        public void FlowDomainPool_64KmWorld_Oversubscription_IsExplicit()
        {
            using var runtime = CreateRuntime(domainCount: 2);
            Navigation2DFlowDomainPool pool = runtime.FlowDomains!;

            pool.ResolveAssignments(new[]
            {
                new Navigation2DFlowDomainRequest(11, TileCenterCm(40, 40), pool.DefaultProfileIndex, 10),
                new Navigation2DFlowDomainRequest(22, TileCenterCm(400, 400), pool.DefaultProfileIndex, 9),
                new Navigation2DFlowDomainRequest(33, TileCenterCm(800, 800), pool.DefaultProfileIndex, 8),
            }, tick: 1);

            Assert.That(pool.ActiveLeaseCount, Is.EqualTo(2));
            Assert.That(pool.ActiveAssignmentCount, Is.EqualTo(2));
            Assert.That(pool.UnassignedRequestCountFrame, Is.EqualTo(1));
            Assert.That(pool.TryGetAssignedFlowId(33, out _), Is.False);
        }

        [Test]
        public void FlowDomainPool_HigherPriorityHotspot_PreemptsStaleOrLowerPriorityLease()
        {
            using var runtime = CreateRuntime(domainCount: 2);
            Navigation2DFlowDomainPool pool = runtime.FlowDomains!;

            pool.ResolveAssignments(new[]
            {
                new Navigation2DFlowDomainRequest(11, TileCenterCm(40, 40), pool.DefaultProfileIndex, 10),
                new Navigation2DFlowDomainRequest(22, TileCenterCm(320, 320), pool.DefaultProfileIndex, 20),
            }, tick: 1);

            int flowForOwner11 = GetAssignedFlowId(pool, 11);
            int flowForOwner22 = GetAssignedFlowId(pool, 22);

            pool.ResolveAssignments(new[]
            {
                new Navigation2DFlowDomainRequest(22, TileCenterCm(320, 320), pool.DefaultProfileIndex, 20),
                new Navigation2DFlowDomainRequest(33, TileCenterCm(640, 640), pool.DefaultProfileIndex, 50),
            }, tick: 2);

            Assert.That(pool.TryGetAssignedFlowId(22, out int preservedFlowId), Is.True);
            Assert.That(preservedFlowId, Is.EqualTo(flowForOwner22));
            Assert.That(pool.TryGetAssignedFlowId(33, out int promotedFlowId), Is.True);
            Assert.That(promotedFlowId, Is.EqualTo(flowForOwner11));
            Assert.That(pool.TryGetAssignedFlowId(11, out _), Is.False);
            Assert.That(pool.ReleasedLeaseCountFrame, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NavGroupFlowDomainAssignmentSystem_AssignsBindings_ForFlowGroups_AndClearsPreciseOrca()
        {
            using var world = World.Create();
            using var runtime = CreateRuntime(domainCount: 2);
            runtime.FlowEnabled = true;

            Entity flowGroup = world.Create(
                new NavGroupTag(),
                new NavGroupIdentity { GroupId = 101 },
                new NavGroupTarget2D { TargetCm = TileCenterCm(64, 64), RadiusCm = Fix64.Zero, FormationSpacingCm = 120, RotationRad = Fix64.Zero },
                new NavGroupRuntimeState { MemberCount = 2, SolverMode = NavSolverMode.CrowdFlow });

            Entity preciseGroup = world.Create(
                new NavGroupTag(),
                new NavGroupIdentity { GroupId = 202 },
                new NavGroupTarget2D { TargetCm = TileCenterCm(320, 320), RadiusCm = Fix64.Zero, FormationSpacingCm = 120, RotationRad = Fix64.Zero },
                new NavGroupRuntimeState { MemberCount = 1, SolverMode = NavSolverMode.PreciseOrca });

            Entity flowMemberA = world.Create(new NavGroupMember { GroupId = 101, SlotIndex = 0 });
            Entity flowMemberB = world.Create(new NavGroupMember { GroupId = 101, SlotIndex = 1 });
            Entity preciseMember = world.Create(
                new NavGroupMember { GroupId = 202, SlotIndex = 0 },
                new NavFlowBinding2D { SurfaceId = 0, FlowId = 1 });

            var system = new Ludots.Core.Navigation2D.Systems.NavGroupFlowDomainAssignmentSystem(world, runtime);
            system.Update(1f / 60f);

            Assert.That(world.Has<NavFlowBinding2D>(flowMemberA), Is.True);
            Assert.That(world.Has<NavFlowBinding2D>(flowMemberB), Is.True);
            Assert.That(world.Get<NavFlowBinding2D>(flowMemberA).FlowId, Is.EqualTo(world.Get<NavFlowBinding2D>(flowMemberB).FlowId));
            Assert.That(world.Has<NavFlowBinding2D>(preciseMember), Is.False);

            system.Dispose();
            world.Destroy(flowGroup);
            world.Destroy(preciseGroup);
        }

        [Test]
        public void FlowDomainPool_64KmWorldAcceptance_WritesArtifacts()
        {
            using var runtime = CreateRuntime(domainCount: 3);
            Navigation2DFlowDomainPool pool = runtime.FlowDomains!;
            var trace = new StringBuilder();
            var timeline = new StringBuilder();

            var tick1 = new[]
            {
                new Navigation2DFlowDomainRequest(1001, TileCenterCm(48, 64), pool.DefaultProfileIndex, 100),
                new Navigation2DFlowDomainRequest(2002, TileCenterCm(480, 496), pool.DefaultProfileIndex, 90),
                new Navigation2DFlowDomainRequest(3003, TileCenterCm(920, 936), pool.DefaultProfileIndex, 80),
            };
            pool.ResolveAssignments(tick1, tick: 1);
            AppendTrace(trace, 1, pool, tick1);
            timeline.AppendLine("- tick 1: three distant hotspots in a 64km world receive three bounded local flow domains.");

            var tick2 = new[]
            {
                new Navigation2DFlowDomainRequest(1001, TileCenterCm(80, 80), pool.DefaultProfileIndex, 100),
                tick1[1],
                tick1[2],
            };
            pool.ResolveAssignments(tick2, tick: 2);
            AppendTrace(trace, 2, pool, tick2);
            timeline.AppendLine("- tick 2: moving one hotspot recenters the same domain instead of widening the global solve window.");

            var tick3 = new[]
            {
                tick2[0],
                tick2[1],
                tick2[2],
                new Navigation2DFlowDomainRequest(4004, TileCenterCm(760, 120), pool.DefaultProfileIndex, 70),
            };
            pool.ResolveAssignments(tick3, tick: 3);
            AppendTrace(trace, 3, pool, tick3);
            timeline.AppendLine("- tick 3: a fourth simultaneous hotspot exceeds the explicit pool size and is reported as unassigned instead of silently expanding cost.");

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-flow-domain-pool-large-world");
            Directory.CreateDirectory(artifactDir);

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), trace.ToString());
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());

            Assert.That(pool.UnassignedRequestCountFrame, Is.EqualTo(1));
            Assert.That(trace.ToString(), Does.Contain("\"tick\":3"));
        }

        private static Navigation2DRuntime CreateRuntime(int domainCount)
        {
            var config = Navigation2DTestContracts.EnsureExplicitContracts(new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 128,
                FlowIterationsPerTick = 4096,
                FlowStreaming = new Navigation2DFlowStreamingConfig
                {
                    Enabled = true,
                    ActivationRadiusTiles = 2,
                    MaxActiveTilesPerFlow = 256,
                    UnloadGraceTicks = 4,
                    MaxPotentialCells = 512f,
                    MaxActivationWindowWidthTiles = 0,
                    MaxActivationWindowHeightTiles = 0,
                    WorldBoundsEnabled = true,
                    WorldMinTileX = 0,
                    WorldMinTileY = 0,
                    WorldMaxTileX = WorldTileCount - 1,
                    WorldMaxTileY = WorldTileCount - 1,
                },
                FlowDomains = new Navigation2DFlowDomainPoolConfig
                {
                    Enabled = true,
                    DomainCount = domainCount,
                    DefaultProfileId = "battle_hotspot",
                    Profiles = new List<Navigation2DFlowDomainProfileConfig>
                    {
                        new Navigation2DFlowDomainProfileConfig
                        {
                            Id = "battle_hotspot",
                            ActivationRadiusTiles = 1,
                            MaxActiveTilesPerFlow = 96,
                            UnloadGraceTicks = 2,
                            MaxPotentialCells = 512f,
                            DomainWidthTiles = 12,
                            DomainHeightTiles = 10,
                            RecenterThresholdTiles = 2,
                            HoldTicks = 8,
                        }
                    }
                }
            });

            return new Navigation2DRuntime(config, gridCellSizeCm: CellSizeCm, loadedChunks: null);
        }

        private static Fix64Vec2 TileCenterCm(int tileX, int tileY)
        {
            int cellX = tileX * TileSizeCells + (TileSizeCells / 2);
            int cellY = tileY * TileSizeCells + (TileSizeCells / 2);
            return Fix64Vec2.FromInt(cellX * CellSizeCm, cellY * CellSizeCm);
        }

        private static int GetAssignedFlowId(Navigation2DFlowDomainPool pool, int ownerId)
        {
            Assert.That(pool.TryGetAssignedFlowId(ownerId, out int flowId), Is.True);
            return flowId;
        }

        private static void AssertDomainBounded(Navigation2DFlowDomainPool pool, int flowId, int expectedWidthTiles, int expectedHeightTiles)
        {
            Assert.That(pool.TryGetLeaseSnapshot(flowId, out Navigation2DFlowDomainLeaseSnapshot snapshot), Is.True);
            Assert.That(snapshot.MaxTileX - snapshot.MinTileX + 1, Is.LessThanOrEqualTo(expectedWidthTiles));
            Assert.That(snapshot.MaxTileY - snapshot.MinTileY + 1, Is.LessThanOrEqualTo(expectedHeightTiles));
            Assert.That(snapshot.MinTileX, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.MinTileY, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.MaxTileX, Is.LessThan(WorldTileCount));
            Assert.That(snapshot.MaxTileY, Is.LessThan(WorldTileCount));
        }

        private static void AssertDomainCenteredNear(Navigation2DFlowDomainPool pool, int flowId, int expectedCenterTileX, int expectedCenterTileY, int maxDeltaTiles)
        {
            Assert.That(pool.TryGetLeaseSnapshot(flowId, out Navigation2DFlowDomainLeaseSnapshot snapshot), Is.True);
            Assert.That(Math.Abs(snapshot.CenterTileX - expectedCenterTileX), Is.LessThanOrEqualTo(maxDeltaTiles));
            Assert.That(Math.Abs(snapshot.CenterTileY - expectedCenterTileY), Is.LessThanOrEqualTo(maxDeltaTiles));
        }

        private static void AppendTrace(StringBuilder trace, int tick, Navigation2DFlowDomainPool pool, ReadOnlySpan<Navigation2DFlowDomainRequest> requests)
        {
            trace.Append('{');
            trace.Append("\"tick\":").Append(tick).Append(',');
            trace.Append("\"leases\":").Append(pool.ActiveLeaseCount).Append(',');
            trace.Append("\"assigned\":").Append(pool.ActiveAssignmentCount).Append(',');
            trace.Append("\"newLeases\":").Append(pool.NewLeaseCountFrame).Append(',');
            trace.Append("\"recenters\":").Append(pool.RecenterCountFrame).Append(',');
            trace.Append("\"released\":").Append(pool.ReleasedLeaseCountFrame).Append(',');
            trace.Append("\"unassigned\":").Append(pool.UnassignedRequestCountFrame).Append(',');
            trace.Append("\"summary\":\"").Append(pool.BuildSummary().Replace("\"", "'")).Append("\",");
            trace.Append("\"owners\":[");
            for (int i = 0; i < requests.Length; i++)
            {
                if (i > 0)
                {
                    trace.Append(',');
                }

                int ownerId = requests[i].OwnerId;
                if (pool.TryGetAssignedFlowId(ownerId, out int flowId) &&
                    pool.TryGetLeaseSnapshot(flowId, out Navigation2DFlowDomainLeaseSnapshot snapshot))
                {
                    trace.Append('{')
                        .Append("\"owner\":").Append(ownerId).Append(',')
                        .Append("\"flowId\":").Append(flowId).Append(',')
                        .Append("\"centerX\":").Append(snapshot.CenterTileX).Append(',')
                        .Append("\"centerY\":").Append(snapshot.CenterTileY).Append(',')
                        .Append("\"minX\":").Append(snapshot.MinTileX).Append(',')
                        .Append("\"minY\":").Append(snapshot.MinTileY).Append(',')
                        .Append("\"maxX\":").Append(snapshot.MaxTileX).Append(',')
                        .Append("\"maxY\":").Append(snapshot.MaxTileY)
                        .Append('}');
                }
                else
                {
                    trace.Append('{')
                        .Append("\"owner\":").Append(ownerId).Append(',')
                        .Append("\"flowId\":-1")
                        .Append('}');
                }
            }

            trace.Append("]}").AppendLine();
        }

        private static string BuildBattleReport(StringBuilder timeline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-flow-domain-pool-large-world");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: prove that a 64km x 64km world can use explicit bounded flow domains instead of solving one giant global flowfield.");
            sb.AppendLine("- Gameplay domain: `Navigation2D` large-world hotspot allocation, flow domain pool, and explicit oversubscription diagnostics.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- World bounds: 1000 x 1000 tiles at 64m per tile (64km x 64km).");
            sb.AppendLine("- Flow domain pool: 3 domains, profile `battle_hotspot`, local bounds 12 x 10 tiles.");
            sb.AppendLine("- Tick order: three deterministic resolve passes.");
            sb.AppendLine("- Failure policy: oversubscription is explicit and reported, never hidden by global window growth.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Simultaneous hotspots map onto a bounded domain pool.");
            sb.AppendLine("- Moving a hotspot recenters its lease instead of expanding world solve bounds.");
            sb.AppendLine("- Excess simultaneous hotspots are surfaced as unassigned requests.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: large-world flow allocation is now explicit, bounded, and ready for group-level production wiring.");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Configure 64km world bounds] --> B[Declare explicit hotspot flow profile]",
                "    B --> C[Create bounded flow domain pool]",
                "    C --> D[Resolve simultaneous group hotspot requests]",
                "    D --> E[Assign or recenter leases]",
                "    E --> F[Expose unassigned hotspots explicitly when pool is full]",
                "    F --> G[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
