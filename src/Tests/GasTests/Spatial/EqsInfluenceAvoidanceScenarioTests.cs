using System;
using Ludots.Platform.Abstractions;
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
    [TestFixture]
    public class EqsInfluenceAvoidanceScenarioTests
    {
        [Test]
        public void Scenario_AvoidThreatWhileNearGoal_SelectsSafeProximityCandidate()
        {
            using World world = World.Create();

            Entity actor = world.Create(WorldPositionCm.FromCm(0, 0));

            WorldCmInt2 goal = new WorldCmInt2(500, 0);

            WorldCmInt2 threat = new WorldCmInt2(300, 0);

            FieldGridSpec2D grid = new FieldGridSpec2D(cellSizeCm: 50, chunkSizeCells: 8);
            InfluenceFieldRegistry registry = new InfluenceFieldRegistry();
            InfluenceField threatField = registry.GetOrCreate("threat", grid);
            threatField.Stamp(threat, radiusCm: 200, peak: 10f, FalloffKind.Linear);

            // Verify threat influence exists near threat center (cell quantization at 50cm density
            // means the sampled cell center is offset from the exact stamp center, so peak is ~8.2 not 10).
            float threatAtCenter = threatField.Sample(threat);
            Assert.That(threatAtCenter, Is.GreaterThan(8f), "Threat should be near peak at center (cell-quantized)");

            // Ring surrounds the goal.
            RingGenerator generator = new RingGenerator(radiusCm: 400, count: 16);

            DistanceTest distTest = new DistanceTest(
                preferNear: true,
                weight: 1f,
                reference: goal);

            InfluenceTest influenceTest = new InfluenceTest(
                fieldKey: "threat",
                preferLow: true,
                weight: 2f,       // double weight: safety > proximity
                normalizeScale: 10f);

            EqsQuery query = new EqsQuery(generator, distTest, influenceTest);
            EqsContext ctx = new EqsContext(
                origin: new WorldCmInt2(0, 0),
                world: world,
                influenceFields: registry);

            Span<EqsItem> buffer = stackalloc EqsItem[16];
            bool found = query.RunBest(in ctx, buffer, out EqsItem best);

            Assert.That(found, Is.True, "EQS should find at least one candidate");

            long distSq = DistanceSq(new WorldCmInt2(0, 0), best.Position);
            long expectedRadiusSq = 400L * 400L;
            Assert.That(distSq, Is.InRange(expectedRadiusSq - 10000, expectedRadiusSq + 10000),
                $"Best candidate should be on ~400cm ring, got distance² {distSq}");

            float influenceAtBest = threatField.Sample(best.Position);
            Assert.That(influenceAtBest, Is.LessThan(3f),
                $"Best candidate should have low threat influence, got {influenceAtBest}");

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
