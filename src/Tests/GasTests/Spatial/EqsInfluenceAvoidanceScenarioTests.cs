using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Generators;
using Ludots.Core.Spatial.Eqs.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    /// <summary>
    /// Headless E2E scenario: AI actor avoids threat while staying near goal using EQS + influence.
    /// Minimal scenario per ludots-feature-delivery skill.
    /// </summary>
    [TestFixture]
    public class EqsInfluenceAvoidanceScenarioTests
    {
        [Test]
        public void Scenario_AvoidThreatWhileNearGoal_SelectsSafeProximityCandidate()
        {
            // --- ARRANGE: minimal world ---
            using World world = World.Create();

            // Actor at origin
            Entity actor = world.Create(WorldPositionCm.FromCm(0, 0));

            // Goal at (500cm, 0)
            WorldCmInt2 goal = new WorldCmInt2(500, 0);

            // Threat source at (300cm, 0) — between actor and goal
            WorldCmInt2 threat = new WorldCmInt2(300, 0);

            // --- ACT: project threat influence ---
            FieldGridSpec2D grid = new FieldGridSpec2D(cellSizeCm: 50, chunkSizeCells: 8);
            InfluenceFieldRegistry registry = new InfluenceFieldRegistry();
            InfluenceField threatField = registry.GetOrCreate("threat", grid);
            threatField.Stamp(threat, radiusCm: 200, peak: 10f, FalloffKind.Linear);

            // Verify threat influence exists near threat center (cell quantization at 50cm density
            // means the sampled cell center is offset from the exact stamp center, so peak is ~8.2 not 10).
            float threatAtCenter = threatField.Sample(threat);
            Assert.That(threatAtCenter, Is.GreaterThan(8f), "Threat should be near peak at center (cell-quantized)");

            // --- ACT: EQS query with ring generator + distance + influence tests ---
            // Generate ring of 16 candidates around actor at 400cm radius (surrounds goal)
            RingGenerator generator = new RingGenerator(radiusCm: 400, count: 16);

            // Test 1: prefer near goal (target reference point)
            DistanceTest distTest = new DistanceTest(
                preferNear: true,
                weight: 1f,
                reference: goal);

            // Test 2: prefer low threat (avoid danger)
            InfluenceTest influenceTest = new InfluenceTest(
                fieldKey: "threat",
                preferLow: true,  // lower threat = higher score
                weight: 2f,       // double weight: safety > proximity
                normalizeScale: 10f);

            EqsQuery query = new EqsQuery(generator, distTest, influenceTest);
            EqsContext ctx = new EqsContext(
                origin: new WorldCmInt2(0, 0),
                world: world,
                influenceFields: registry);

            Span<EqsItem> buffer = stackalloc EqsItem[16];
            bool found = query.RunBest(in ctx, buffer, out EqsItem best);

            // --- ASSERT: best candidate avoids threat while staying near goal ---
            Assert.That(found, Is.True, "EQS should find at least one candidate");

            // Best position should be on the ring (400cm from origin)
            long distSq = DistanceSq(new WorldCmInt2(0, 0), best.Position);
            long expectedRadiusSq = 400L * 400L;
            Assert.That(distSq, Is.InRange(expectedRadiusSq - 10000, expectedRadiusSq + 10000),
                $"Best candidate should be on ~400cm ring, got distance² {distSq}");

            // Best position should be far from threat (low influence)
            float influenceAtBest = threatField.Sample(best.Position);
            Assert.That(influenceAtBest, Is.LessThan(3f),
                $"Best candidate should have low threat influence, got {influenceAtBest}");

            // Best position should NOT be on the direct actor-goal line where threat sits
            // Check that best.Position is not within ±50cm Y of the threat line (Y=0)
            Assert.That(Math.Abs(best.Position.Y), Is.GreaterThan(50),
                $"Best candidate should avoid the threat line (Y=0), got Y={best.Position.Y}");

            TestContext.WriteLine($"✓ Best candidate: ({best.Position.X}, {best.Position.Y}) with score {best.Score:F2}");
            TestContext.WriteLine($"✓ Threat influence at best: {influenceAtBest:F2} (peak was 10.0)");
            TestContext.WriteLine($"✓ Distance from goal: {Math.Sqrt(DistanceSq(goal, best.Position)):F0}cm");
        }

        private static long DistanceSq(WorldCmInt2 a, WorldCmInt2 b)
        {
            long dx = b.X - a.X;
            long dy = b.Y - a.Y;
            return dx * dx + dy * dy;
        }
    }
}
