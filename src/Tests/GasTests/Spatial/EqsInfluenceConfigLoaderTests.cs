using System;
using Arch.Core;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Config;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public class EqsInfluenceConfigLoaderTests
    {
        private const string FieldsJson = """
            [
              {
                "id": "threat",
                "cellSizeCm": 50,
                "chunkSizeCells": 8,
                "sources": [
                  { "xCm": 300, "yCm": 0, "radiusCm": 200, "peak": 10, "falloff": "Linear" }
                ]
              }
            ]
            """;

        private const string QueriesJson = """
            [
              {
                "id": "avoid_threat_near_goal",
                "generator": { "kind": "Ring", "radiusCm": 400, "count": 16 },
                "tests": [
                  { "kind": "Distance", "preferNear": true, "weight": 1, "reference": { "xCm": 500, "yCm": 0 } },
                  { "kind": "Influence", "fieldKey": "threat", "preferLow": true, "weight": 2, "normalizeScale": 10 }
                ],
                "selection": { "kind": "Best" }
              }
            ]
            """;

        private const string ScenariosJson = """
            [
              {
                "id": "avoid_threat_demo",
                "origin": { "xCm": 0, "yCm": 0 },
                "queryId": "avoid_threat_near_goal",
                "influenceFieldIds": ["threat"],
                "presentation": {
                  "influenceFieldId": "threat",
                  "drawCandidates": true,
                  "drawBest": true,
                  "normalizePeak": 10
                }
              }
            ]
            """;

        [Test]
        public void Load_Materialize_AndSelectBest_AvoidsThreatLine()
        {
            EqsInfluenceConfigDocument doc = EqsInfluenceConfigLoader.LoadFromJson(FieldsJson, QueriesJson, ScenariosJson);
            InfluenceFieldRegistry registry = EqsInfluenceConfigLoader.MaterializeFields(doc, new[] { "threat" });
            Assert.That(registry.TryGet("threat", out InfluenceField? threat) && threat != null);
            Assert.That(threat!.Sample(new WorldCmInt2(300, 0)), Is.GreaterThan(8f));

            EqsQuery query = EqsInfluenceConfigLoader.CreateQuery(
                EqsInfluenceConfigLoader.RequireQuery(doc, "avoid_threat_near_goal"));

            using World world = World.Create();
            var ctx = new EqsContext(new WorldCmInt2(0, 0), world, influenceFields: registry);
            Span<EqsItem> buffer = stackalloc EqsItem[16];
            Assert.That(query.RunBest(in ctx, buffer, out EqsItem best), Is.True);
            Assert.That(threat.Sample(best.Position), Is.LessThan(3f));
            Assert.That(Math.Abs(best.Position.Y), Is.GreaterThan(50));
        }

        [Test]
        public void Load_UnknownFieldReference_Throws()
        {
            string badScenarios = """
                [
                  {
                    "id": "bad",
                    "origin": { "xCm": 0, "yCm": 0 },
                    "queryId": "avoid_threat_near_goal",
                    "influenceFieldIds": ["missing"],
                    "presentation": { "influenceFieldId": "threat", "normalizePeak": 10 }
                  }
                ]
                """;

            Assert.Throws<InvalidOperationException>(() =>
                EqsInfluenceConfigLoader.LoadFromJson(FieldsJson, QueriesJson, badScenarios));
        }

        [Test]
        public void Projector_WritesInfluenceKindRecords()
        {
            EqsInfluenceConfigDocument doc = EqsInfluenceConfigLoader.LoadFromJson(FieldsJson, QueriesJson, ScenariosJson);
            InfluenceFieldRegistry registry = EqsInfluenceConfigLoader.MaterializeFields(doc);
            registry.PresentationNormalizePeak = 10f;

            var buffer = new GlobalFieldVisualBuffer();
            buffer.BeginFrame();
            var projector = new InfluenceGlobalFieldVisualProjector { NormalizePeak = 10f };
            projector.Project(registry, buffer);

            Assert.That(projector.LastProjectedFieldCount, Is.EqualTo(1));
            Assert.That(projector.LastProjectedCellCount, Is.GreaterThan(0));
            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(1));
            Assert.That(buffer.GetRecords()[0].Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Influence));
        }
    }
}
