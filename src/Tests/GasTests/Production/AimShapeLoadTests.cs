using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    public sealed class AimShapeLoadTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void Ability_ReferencingUndeclaredShape_FailsAtMount()
        {
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(
                7,
                new AbilityDefinition
                {
                    TriggerGraphs = new List<string> { "graph.aim.shape.missing" },
                });

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerGraphMounting.BuildAbilityMountTriggers(
                    new GraphProgramRegistry(),
                    abilities,
                    new CustomEventNameRegistry(),
                    "AimShapeLoad"));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("graph.aim.shape.missing"));
            Assert.That(ex.Message, Does.Contain("not registered"));
        }
    }
}
