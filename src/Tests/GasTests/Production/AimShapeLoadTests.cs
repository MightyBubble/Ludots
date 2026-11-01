using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
        private const string ShapeGraph = "graph.aim.shape.circle";
        private const string RadiusFields = "targetQuery.radius|unitCreation.placementRadiusCm";
        private static int SearchEffect;
        private static int SummonEffect;
        private static int OtherSearchEffect;

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            SearchEffect = EffectTemplateIdRegistry.Register("Effect.Shape.Search");
            SummonEffect = EffectTemplateIdRegistry.Register("Effect.Shape.Summon");
            OtherSearchEffect = EffectTemplateIdRegistry.Register("Effect.Shape.OtherSearch");
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
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
        public void ShapeGraph_ExecEffectCarriesField_Mounts()
        {
            List<Trigger> triggers = Mount(Ability(SearchEffect), Effects(searchRadius: 300), RadiusFields);

            Assert.That(triggers, Is.Not.Empty);
        }

        [Test]
        public void ShapeGraph_NoExecEffectCarriesField_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(SearchEffect), Effects(searchRadius: 0), RadiusFields));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.AbsentFieldError));
            Assert.That(ex.Message, Does.Contain(ShapeGraph));
            Assert.That(ex.Message, Does.Contain("Effect.Shape.Search"));
        }

        [Test]
        public void ShapeGraph_EffectOutsideExec_IsNotRead()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(), Effects(searchRadius: 300), RadiusFields));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.AbsentFieldError));
            Assert.That(ex.Message, Does.Contain("Executed effects: none"));
        }

        [Test]
        public void ShapeGraph_UnknownField_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(SearchEffect), Effects(searchRadius: 300), "targetQuery.radius|not.a.field"));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.UnknownFieldError));
            Assert.That(ex.Message, Does.Contain("not.a.field"));
        }

        [Test]
        public void ShapeGraph_SameFieldOnTwoExecEffects_FailsAtMount()
        {
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() =>
                Mount(Ability(SearchEffect, OtherSearchEffect), Effects(searchRadius: 300, otherSearchRadius: 200), RadiusFields));

            Assert.That(ex!.Message, Does.Contain(EffectTemplateNamedFloat.AmbiguousFieldError));
            Assert.That(ex.Message, Does.Contain("Effect.Shape.Search"));
            Assert.That(ex.Message, Does.Contain("Effect.Shape.OtherSearch"));
        }

        [Test]
        public void GraphWithoutEffectRead_MountsWithoutExecEffects()
        {
            List<Trigger> triggers = Mount(Ability(), Effects(searchRadius: 0), fields: null);

            Assert.That(triggers, Is.Not.Empty);
        }

        [Test]
        public void FieldList_FirstCarriedFieldWins()
        {
            AbilityDefinition definition = Ability(SearchEffect, SummonEffect);

            float radius = EffectTemplateNamedFloat.RequireFromExec(
                in definition, Effects(searchRadius: 600, placementRadius: 200), RadiusFields, "Ability.Test");

            Assert.That(radius, Is.EqualTo(600f));
        }

        [Test]
        public void FieldList_FallsThroughToNextField()
        {
            AbilityDefinition definition = Ability(SummonEffect);

            float radius = EffectTemplateNamedFloat.RequireFromExec(
                in definition, Effects(searchRadius: 600, placementRadius: 280), RadiusFields, "Ability.Test");

            Assert.That(radius, Is.EqualTo(280f));
        }

        [Test]
        public void FieldList_ValidatesEveryName()
        {
            Assert.That(EffectTemplateNamedFloat.ValidateFieldList(RadiusFields), Is.Null);
            Assert.That(EffectTemplateNamedFloat.ValidateFieldList(""), Does.Contain(EffectTemplateNamedFloat.UnknownFieldError));
            Assert.That(EffectTemplateNamedFloat.ValidateFieldList("targetQuery.radius|"), Does.Contain(EffectTemplateNamedFloat.UnknownFieldError));
            Assert.That(EffectTemplateNamedFloat.ValidateFieldList("radius"), Does.Contain("'radius'"));
        }

        private static List<Trigger> Mount(AbilityDefinition definition, EffectTemplateRegistry effects, string? fields)
        {
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(7, definition);
            var programs = new GraphProgramRegistry();
            int graphId = GraphIdRegistry.Register(ShapeGraph);
            GraphInstruction[] program = fields != null
                ? new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadAbilityEffectFloat, Imm = 0 },
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
                fields != null ? new[] { fields } : null,
                new[] { new TriggerGraphEntry("press", "EntityAliveCountChanged", 0, false) });
            return TriggerGraphMounting.BuildAbilityMountTriggers(
                programs,
                abilities,
                new CustomEventNameRegistry(),
                "AimShapeLoad",
                effects);
        }

        private static AbilityDefinition Ability(params int[] execEffects)
        {
            var exec = default(AbilityExecSpec);
            exec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 1, tagId: 1);
            for (int i = 0; i < execEffects.Length; i++)
            {
                ExecItemKind kind = i % 2 == 0 ? ExecItemKind.EffectSignal : ExecItemKind.EffectClip;
                exec.SetItem(i + 1, kind, tick: 0, durationTicks: 1, templateId: execEffects[i]);
            }

            exec.SetItem(execEffects.Length + 1, ExecItemKind.End, tick: 1);
            return new AbilityDefinition
            {
                TriggerGraphs = new List<string> { ShapeGraph },
                ExecSpec = exec,
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig { CastRangeCm = 900f },
            };
        }

        private static EffectTemplateRegistry Effects(int searchRadius, int placementRadius = 0, int otherSearchRadius = 0)
        {
            var effects = new EffectTemplateRegistry();
            effects.Register(SearchEffect, Search(searchRadius));
            effects.Register(OtherSearchEffect, Search(otherSearchRadius));
            effects.Register(SummonEffect, new EffectTemplateData
            {
                UnitCreation = new UnitCreationDescriptor { PlacementRadiusCm = placementRadius },
            });
            return effects;
        }

        private static EffectTemplateData Search(int radius)
        {
            return new EffectTemplateData
            {
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor { RadiusCm = radius },
                },
            };
        }
    }
}
