using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;
using Ludots.Core.NodeLibraries.GASGraph;
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

        [Test]
        public void ShapeGraph_MissingEffectField_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(range: 300f, field: null), Effects(radius: 170, halfAngle: 40), withOp: true));

            Assert.That(ex!.Message, Does.Contain("effectFields"));
            Assert.That(ex.Message, Does.Contain(ShapeGraph));
        }

        [Test]
        public void ShapeGraph_ZeroEffectField_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(range: 300f, field: EffectTemplateNamedFloat.TargetQueryRadius), Effects(radius: 0, halfAngle: 40), withOp: true));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.AbsentFieldError));
            Assert.That(ex.Message, Does.Contain(EffectTemplateNamedFloat.TargetQueryRadius));
        }

        [Test]
        public void ShapeGraph_UnknownEffectField_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(range: 300f, field: "not.a.field"), Effects(radius: 170, halfAngle: 40), withOp: true));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.UnknownFieldError));
            Assert.That(ex.Message, Does.Contain("not.a.field"));
        }

        [Test]
        public void ShapeGraph_ZeroCastRange_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(range: 0f, field: EffectTemplateNamedFloat.TargetQueryHalfAngle), Effects(radius: 300, halfAngle: 40), withOp: true));

            Assert.That(ex!.Message, Does.Contain("castRangeCm"));
        }

        [Test]
        public void EffectField_OnGraphThatDoesNotReadOne_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(range: 300f, field: EffectTemplateNamedFloat.TargetQueryHalfAngle), Effects(radius: 300, halfAngle: 40), withOp: false));

            Assert.That(ex!.Message, Does.Contain("does not read an effect float"));
        }

        [Test]
        public void ShapeGraph_PositiveLiteral_MountsWithoutAnEffectField()
        {
            List<Trigger> triggers = Mount(AbilityLiteral(range: 800f, literal: 90f), Effects(radius: 0, halfAngle: 0), withOp: true);

            Assert.That(triggers, Is.Not.Empty);
        }

        [Test]
        public void ShapeGraph_ZeroLiteral_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(AbilityLiteral(range: 800f, literal: 0f), Effects(radius: 0, halfAngle: 0), withOp: true));

            Assert.That(ex!.Message, Does.Contain("greater than 0"));
        }

        [Test]
        public void ShapeGraph_PositiveEffectField_Mounts()
        {
            List<Trigger> triggers = Mount(
                Ability(range: 300f, field: EffectTemplateNamedFloat.TargetQueryHalfAngle),
                Effects(radius: 300, halfAngle: 40),
                withOp: true);

            Assert.That(triggers, Is.Not.Empty);
        }

        private const string ShapeGraph = "graph.aim.shape.cone";

        private static List<Trigger> Mount(AbilityDefinition definition, EffectTemplateRegistry effects, bool withOp)
        {
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(7, definition);
            var programs = new GraphProgramRegistry();
            int graphId = GraphIdRegistry.Register(ShapeGraph);
            GraphInstruction[] program = withOp
                ? new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadAbilityEffectFloat },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }
                : new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                };
            programs.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                null,
                new[] { new TriggerGraphEntry("press", "EntityAliveCountChanged", 0, false) });
            return TriggerGraphMounting.BuildAbilityMountTriggers(
                programs,
                abilities,
                new CustomEventNameRegistry(),
                "AimShapeLoad",
                effects);
        }

        private static AbilityDefinition AbilityLiteral(float range, float literal)
        {
            return new AbilityDefinition
            {
                TriggerGraphs = new List<string> { ShapeGraph },
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = range,
                    ImpactEffectTemplateId = 3,
                },
                TriggerGraphEffectLiterals = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [ShapeGraph] = literal,
                },
            };
        }

        private static AbilityDefinition Ability(float range, string? field)
        {
            var definition = new AbilityDefinition
            {
                TriggerGraphs = new List<string> { ShapeGraph },
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = range,
                    ImpactEffectTemplateId = 3,
                },
            };
            if (field != null)
            {
                definition.TriggerGraphEffectFields = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ShapeGraph] = field,
                };
            }

            return definition;
        }

        private static EffectTemplateRegistry Effects(int radius, int halfAngle)
        {
            var effects = new EffectTemplateRegistry();
            effects.Register(3, new EffectTemplateData
            {
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor
                    {
                        RadiusCm = radius,
                        HalfAngleDeg = halfAngle,
                    },
                },
            });
            return effects;
        }
    }
}
