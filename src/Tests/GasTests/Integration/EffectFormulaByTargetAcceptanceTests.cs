using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Q4 acceptance: one effect template settles with different formulas per target.
    /// The same Effect.Q4.Bolt hits a light dummy (armor 0, flat base damage) and a heavy
    /// dummy (armor 150, base × 100/(100+armor)); both formulas live in the OnApply main
    /// graph as pure data — no per-target effect variants, no code.
    /// </summary>
    [TestFixture]
    public sealed class EffectFormulaByTargetAcceptanceTests
    {
        [Test]
        public void SameEffect_LightAndHeavyTargets_DifferentFormulas()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(
                    repoRoot,
                    new[] { "LudotsCoreMod", "EffectFormulaByTargetAcceptanceMod" }),
                Path.Combine(repoRoot, "assets"));

            int templateId = EffectTemplateIdRegistry.GetId("Effect.Q4.Bolt");
            Assert.That(templateId, Is.GreaterThan(0), "Fixture effect must load from mod data.");
            Assert.That(GraphIdRegistry.GetId("Graph.Q4.BoltByArmor"), Is.GreaterThan(0), "Formula graph must compile from mod data.");

            int healthId = AttributeRegistry.GetId("Q4.Health");
            int armorId = AttributeRegistry.GetId("Q4.Armor");
            Assert.That(healthId, Is.GreaterThan(0), "Q4.Health must be registered by the fixture's attribute_constraints.json.");
            Assert.That(armorId, Is.GreaterThan(0), "Q4.Armor must be registered by the fixture's attribute_constraints.json.");

            Entity caster = engine.World.Create();
            Entity light = engine.World.Create(
                CreateAttributes(healthId, 1000f, armorId, 0f),
                new ActiveEffectContainer(),
                new DirtyFlags());
            Entity heavy = engine.World.Create(
                CreateAttributes(healthId, 1000f, armorId, 150f),
                new ActiveEffectContainer(),
                new DirtyFlags());

            EffectRequestQueue requests = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue service is missing.");
            requests.Publish(new EffectRequest { RootId = 1, Source = caster, Target = light, TemplateId = templateId });
            requests.Publish(new EffectRequest { RootId = 2, Source = caster, Target = heavy, TemplateId = templateId });

            engine.Start();
            for (int frame = 0; frame < 4; frame++)
            {
                engine.Tick(1f / 60f);
            }

            float lightHealth = engine.World.Get<AttributeBuffer>(light).GetCurrent(healthId);
            float heavyHealth = engine.World.Get<AttributeBuffer>(heavy).GetCurrent(healthId);

            TestContext.Out.WriteLine($"[Q4] light(armor 0)   1000 -> {lightHealth}  (flat formula: -50)");
            TestContext.Out.WriteLine($"[Q4] heavy(armor 150) 1000 -> {heavyHealth}  (mitigated formula: -50*100/250 = -20)");

            Assert.That(lightHealth, Is.EqualTo(950f).Within(0.001f),
                "Light target settles with the flat formula (base damage).");
            Assert.That(heavyHealth, Is.EqualTo(980f).Within(0.001f),
                "Heavy target settles with the armor-mitigated formula from the same effect template.");
        }

        private static AttributeBuffer CreateAttributes(int healthId, float health, int armorId, float armor)
        {
            var attributes = new AttributeBuffer();
            attributes.SetBase(healthId, health);
            attributes.SetBase(armorId, armor);
            return attributes;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
