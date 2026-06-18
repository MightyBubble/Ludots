using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using NUnit.Framework;

namespace GasTests.Physics2D
{
    [TestFixture]
    public sealed class Physics2DScaleBenchmarkTests
    {
        [SetUp]
        public void SetUp()
        {
            ShapeDataStorage2D.Clear();
        }

        [Explicit("Hardware-sensitive Physics2D scale benchmark for 30k dynamic + 100k static bodies.")]
        [Test]
        public void Scale_ThirtyKDynamicHundredKStatic_WritesBenchmarkArtifacts()
        {
            const int dynamicCount = 30_000;
            const int staticCount = 100_000;
            const int fixedHz = 60;
            const int physicsHz = 15;
            const int warmupFixedTicks = 8;
            const int measuredFixedTicks = 30;

            BenchmarkResult sweep = RunScenario(
                Physics2DBroadphaseStrategyKind.SortAndSweep,
                cellSizeCm: 512,
                dynamicCount,
                staticCount,
                fixedHz,
                physicsHz,
                warmupFixedTicks,
                measuredFixedTicks);
            BenchmarkResult grid = RunScenario(
                Physics2DBroadphaseStrategyKind.UniformGrid,
                cellSizeCm: 512,
                dynamicCount,
                staticCount,
                fixedHz,
                physicsHz,
                warmupFixedTicks,
                measuredFixedTicks);

            string artifactRoot = Path.Combine(FindRepoRoot(), "artifacts", "benchmarks", "physics2d-scale");
            Directory.CreateDirectory(artifactRoot);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string reportPath = Path.Combine(artifactRoot, $"physics2d-scale-{timestamp}.md");
            File.WriteAllText(reportPath, BuildReport(sweep, grid), Encoding.UTF8);

            TestContext.Out.WriteLine(reportPath);
            Assert.That(grid.AveragePhysicsUpdateMs, Is.LessThanOrEqualTo(sweep.AveragePhysicsUpdateMs * 1.10d));
        }

        private static BenchmarkResult RunScenario(
            Physics2DBroadphaseStrategyKind strategy,
            int cellSizeCm,
            int dynamicCount,
            int staticCount,
            int fixedHz,
            int physicsHz,
            int warmupFixedTicks,
            int measuredFixedTicks)
        {
            using var world = World.Create();
            int dynamicShape = ShapeDataStorage2D.RegisterCircle(Fix64.FromInt(18));
            int staticShape = ShapeDataStorage2D.RegisterBox(Fix64.FromInt(32), Fix64.FromInt(32));

            SpawnDynamicGrid(world, dynamicCount, dynamicShape);
            SpawnStaticGrid(world, staticCount, staticShape);

            var tickPolicy = new Physics2DTickPolicy(physicsHz, maxStepsPerFixedTick: 8);
            var broadphasePolicy = new Physics2DBroadphasePolicy(new Physics2DBroadphaseConfig
            {
                Strategy = strategy,
                CellSizeCm = cellSizeCm
            });
            var simulation = new Physics2DSimulationSystem(world, new DiscreteClock(), tickPolicy, broadphasePolicy);
            simulation.Initialize();

            float fixedDeltaTime = 1f / fixedHz;
            for (int i = 0; i < warmupFixedTicks; i++)
            {
                simulation.Update(fixedDeltaTime);
            }

            double totalPhysicsMs = 0d;
            int measuredPhysicsSteps = 0;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < measuredFixedTicks; i++)
            {
                simulation.Update(fixedDeltaTime);
                Physics2DPerfStats stats = CaptureStats(world);
                totalPhysicsMs += stats.PhysicsUpdateMs;
                measuredPhysicsSteps += stats.PhysicsStepsLastFixedTick;
            }
            stopwatch.Stop();

            Physics2DPerfStats lastStats = CaptureStats(world);
            return new BenchmarkResult(
                strategy,
                cellSizeCm,
                dynamicCount,
                staticCount,
                measuredFixedTicks,
                measuredPhysicsSteps,
                totalPhysicsMs / measuredFixedTicks,
                stopwatch.Elapsed.TotalMilliseconds,
                lastStats.PotentialPairs,
                lastStats.ContactPairs,
                lastStats.DroppedPairs);
        }

        private static void SpawnDynamicGrid(World world, int count, int shapeIndex)
        {
            var collider = new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = shapeIndex };
            var mass = Mass2D.FromFloat(1f, 1f);
            for (int i = 0; i < count; i++)
            {
                int x = (i % 300) * 180 - 27_000;
                int y = (i / 300) * 180 - 9_000;
                int vx = (i & 1) == 0 ? 12 : -12;
                world.Create(
                    new Position2D { Value = Fix64Vec2.FromInt(x, y) },
                    new Velocity2D { Linear = Fix64Vec2.FromInt(vx, 0), Angular = Fix64.Zero },
                    mass,
                    collider,
                    PhysicsMaterial2D.Default);
            }
        }

        private static void SpawnStaticGrid(World world, int count, int shapeIndex)
        {
            var collider = new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = shapeIndex };
            for (int i = 0; i < count; i++)
            {
                int x = (i % 500) * 220 - 55_000;
                int y = (i / 500) * 220 - 22_000;
                world.Create(
                    new Position2D { Value = Fix64Vec2.FromInt(x, y) },
                    Velocity2D.Zero,
                    Mass2D.Static,
                    collider,
                    PhysicsMaterial2D.Default);
            }
        }

        private static Physics2DPerfStats CaptureStats(World world)
        {
            Physics2DPerfStats result = default;
            var query = new QueryDescription().WithAll<Physics2DPerfStats>();
            world.Query(in query, (ref Physics2DPerfStats stats) => result = stats);
            return result;
        }

        private static string BuildReport(BenchmarkResult sweep, BenchmarkResult grid)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Physics2D Scale Benchmark");
            sb.AppendLine();
            sb.AppendLine("| Strategy | Dynamic | Static | Cell cm | Fixed ticks | Physics steps | Avg physics update ms | Wall ms | Potential pairs | Contacts | Dropped |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            AppendRow(sb, sweep);
            AppendRow(sb, grid);
            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, BenchmarkResult result)
        {
            sb.Append("| ");
            sb.Append(result.Strategy);
            sb.Append(" | ");
            sb.Append(result.DynamicBodies);
            sb.Append(" | ");
            sb.Append(result.StaticBodies);
            sb.Append(" | ");
            sb.Append(result.CellSizeCm);
            sb.Append(" | ");
            sb.Append(result.MeasuredFixedTicks);
            sb.Append(" | ");
            sb.Append(result.MeasuredPhysicsSteps);
            sb.Append(" | ");
            sb.Append(result.AveragePhysicsUpdateMs.ToString("0.###"));
            sb.Append(" | ");
            sb.Append(result.TotalWallMs.ToString("0.###"));
            sb.Append(" | ");
            sb.Append(result.PotentialPairs);
            sb.Append(" | ");
            sb.Append(result.ContactPairs);
            sb.Append(" | ");
            sb.Append(result.DroppedPairs);
            sb.AppendLine(" |");
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "src")) &&
                    Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Unable to locate repository root from test directory.");
        }

        private readonly record struct BenchmarkResult(
            Physics2DBroadphaseStrategyKind Strategy,
            int CellSizeCm,
            int DynamicBodies,
            int StaticBodies,
            int MeasuredFixedTicks,
            int MeasuredPhysicsSteps,
            double AveragePhysicsUpdateMs,
            double TotalWallMs,
            int PotentialPairs,
            int ContactPairs,
            int DroppedPairs);
    }
}
