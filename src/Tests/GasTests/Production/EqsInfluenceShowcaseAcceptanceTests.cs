using System;
using Arch.Core;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// Headless acceptance for eqs_influence showcase config + presentation projection contract.
    /// </summary>
    [TestFixture]
    public sealed class EqsInfluenceShowcaseAcceptanceTests
    {
        [Test]
        public void EqsInfluenceShowcase_ConfigDrivenScenario_ProjectsFieldAndSelectsSafeCandidate()
        {
            string root = FindShowcaseConfigRoot();
            EqsInfluenceConfigDocument document = EqsInfluenceConfigLoader.LoadFromDirectory(root);
            EqsScenarioConfig scenario = EqsInfluenceConfigLoader.RequireScenario(document, "avoid_threat_demo");

            InfluenceFieldRegistry registry = EqsInfluenceConfigLoader.MaterializeFields(document, scenario.InfluenceFieldIds);
            registry.PresentationNormalizePeak = scenario.Presentation.NormalizePeak;
            Assert.That(registry.TryGet("threat", out InfluenceField? threat) && threat != null);

            EqsQuery query = EqsInfluenceConfigLoader.CreateQuery(
                EqsInfluenceConfigLoader.RequireQuery(document, scenario.QueryId));

            using World world = World.Create();
            var ctx = new EqsContext(scenario.Origin, world, influenceFields: registry);
            Span<EqsItem> buffer = stackalloc EqsItem[32];
            Assert.That(query.RunBest(in ctx, buffer, out EqsItem best), Is.True);

            Assert.That(threat!.Sample(best.Position), Is.LessThan(3f));
            Assert.That(Math.Abs(best.Position.Y), Is.GreaterThan(50));

            var visual = new GlobalFieldVisualBuffer();
            visual.BeginFrame();
            var projector = new InfluenceGlobalFieldVisualProjector
            {
                NormalizePeak = scenario.Presentation.NormalizePeak
            };
            projector.Project(registry, visual);
            Assert.That(projector.LastProjectedFieldCount, Is.EqualTo(1));
            Assert.That(visual.GetRecords()[0].Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Influence));
        }

        private static string FindShowcaseConfigRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(
                    current.FullName,
                    "mods",
                    "showcases",
                    "eqs_influence",
                    "EqsInfluenceShowcaseMod",
                    "assets",
                    "Configs");
                if (Directory.Exists(Path.Combine(candidate, "Spatial")))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate eqs_influence showcase Configs directory.");
        }
    }
}
