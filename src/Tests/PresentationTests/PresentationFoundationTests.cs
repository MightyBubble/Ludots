using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationFoundationTests
    {
        [Test]
        public void AnimatorPackedState_RoundTripsControllerStatesFlagsAndBits()
        {
            var packed = AnimatorPackedState.Create(7);

            packed.SetPrimaryStateIndex(12);
            packed.SetSecondaryStateIndex(3);
            packed.SetNormalizedTime01(0.5f);
            packed.SetTransitionProgress01(0.25f);
            packed.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping | AnimatorPackedStateFlags.InTransition);
            packed.SetParameterBit(1, true);
            packed.SetParameterBit(7, true);
            packed.SetParameterBit(63, true);

            Assert.That(packed.GetControllerId(), Is.EqualTo(7));
            Assert.That(packed.GetPrimaryStateIndex(), Is.EqualTo(12));
            Assert.That(packed.GetSecondaryStateIndex(), Is.EqualTo(3));
            Assert.That(packed.GetNormalizedTime01(), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(packed.GetTransitionProgress01(), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(
                packed.GetFlags(),
                Is.EqualTo(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping | AnimatorPackedStateFlags.InTransition));
            Assert.That(packed.GetParameterBit(1), Is.True);
            Assert.That(packed.GetParameterBit(7), Is.True);
            Assert.That(packed.GetParameterBit(63), Is.True);
            Assert.That(packed.GetParameterBit(2), Is.False);
            Assert.That(
                () => packed.SetParameterBit(AnimatorPackedState.MaxParameterBits, true),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void PresentationAuthoringContext_Apply_AssignsStableIdVisualAnimatorAndStartupPerformers()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
            var performers = new PerformerDefinitionRegistry();
            var animators = new AnimatorControllerRegistry();
            var stableIds = new PresentationStableIdAllocator();

            int controllerId = animators.Register("hero.controller");
            int templateId = visualTemplates.Register(
                "hero.template",
                new VisualTemplateDefinition
                {
                    MeshAssetId = 101,
                    MaterialId = 202,
                    AnimatorControllerId = controllerId,
                    BaseScale = 1.25f,
                    RenderPath = VisualRenderPath.SkinnedMesh,
                    Mobility = VisualMobility.Movable,
                    VisibleByDefault = true,
                });

            int markerId = performers.Register("performer.cast_marker", new PerformerDefinition { VisualKind = PerformerVisualKind.Marker3D });
            int barId = performers.Register("performer.health_bar", new PerformerDefinition { VisualKind = PerformerVisualKind.WorldBar });

            var context = new PresentationAuthoringContext(visualTemplates, performers, animators, stableIds);
            JsonNode authoring = JsonNode.Parse(
                """
                {
                  "visualTemplateId": "hero.template",
                  "visible": false,
                  "startupPerformerIds": ["performer.cast_marker", "performer.health_bar"],
                  "animator": {
                    "primaryStateIndex": 12,
                    "secondaryStateIndex": 3,
                    "normalizedTime": 0.5,
                    "transitionProgress": 0.25,
                    "flags": ["Active", "Looping", "InTransition"],
                    "parameterBits": [1, 7, 63]
                  }
                }
                """)!;

            context.Apply(entity, authoring);

            Assert.That(entity.Has<PresentationStableId>(), Is.True);
            Assert.That(entity.Has<VisualTemplateRef>(), Is.True);
            Assert.That(entity.Has<VisualRuntimeState>(), Is.True);
            Assert.That(entity.Has<AnimatorPackedState>(), Is.True);
            Assert.That(entity.Has<PresentationStartupPerformers>(), Is.True);
            Assert.That(entity.Has<PresentationStartupState>(), Is.True);

            int stableId = entity.Get<PresentationStableId>().Value;
            Assert.That(stableId, Is.GreaterThan(0));
            Assert.That(entity.Get<VisualTemplateRef>().TemplateId, Is.EqualTo(templateId));

            var visual = entity.Get<VisualRuntimeState>();
            Assert.That(visual.MeshAssetId, Is.EqualTo(101));
            Assert.That(visual.MaterialId, Is.EqualTo(202));
            Assert.That(visual.BaseScale, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(visual.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
            Assert.That(visual.AnimatorControllerId, Is.EqualTo(controllerId));
            Assert.That(visual.IsVisibleRequested, Is.False);
            Assert.That(visual.HasAnimator, Is.True);

            var animator = entity.Get<AnimatorPackedState>();
            Assert.That(animator.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(animator.GetPrimaryStateIndex(), Is.EqualTo(12));
            Assert.That(animator.GetSecondaryStateIndex(), Is.EqualTo(3));
            Assert.That(animator.GetNormalizedTime01(), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(animator.GetTransitionProgress01(), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(
                animator.GetFlags(),
                Is.EqualTo(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping | AnimatorPackedStateFlags.InTransition));
            Assert.That(animator.GetParameterBit(1), Is.True);
            Assert.That(animator.GetParameterBit(7), Is.True);
            Assert.That(animator.GetParameterBit(63), Is.True);

            var startupPerformers = entity.Get<PresentationStartupPerformers>();
            Assert.That(startupPerformers.Count, Is.EqualTo(2));
            Assert.That(startupPerformers.Get(0), Is.EqualTo(markerId));
            Assert.That(startupPerformers.Get(1), Is.EqualTo(barId));
            Assert.That(entity.Get<PresentationStartupState>().Initialized, Is.False);

            context.Apply(
                entity,
                JsonNode.Parse(
                    """
                    {
                      "animator": {
                        "controllerId": "hero.controller",
                        "primaryStateIndex": 7
                      }
                    }
                    """)!);

            Assert.That(entity.Get<PresentationStableId>().Value, Is.EqualTo(stableId), "Reapplying presentation authoring must preserve stable ids.");
            Assert.That(entity.Get<AnimatorPackedState>().GetPrimaryStateIndex(), Is.EqualTo(7));
        }

        [Test]
        public void PresentationStartupPerformerSystem_UsesStableIdScope_AndRunsOnlyOnce()
        {
            using var world = World.Create();
            var entity = world.Create();
            var commands = new PresentationCommandBuffer();
            var startup = default(PresentationStartupPerformers);
            startup.Count = 2;
            startup.Set(0, 11);
            startup.Set(1, 22);

            entity.Add(new PresentationStableId { Value = 99 });
            entity.Add(startup);
            entity.Add(new PresentationStartupState { Initialized = false });

            using var system = new PresentationStartupPerformerSystem(world, commands);
            system.Update(0.016f);

            var firstPass = commands.GetSpan();
            Assert.That(firstPass.Length, Is.EqualTo(2));
            Assert.That(firstPass[0].Kind, Is.EqualTo(PresentationCommandKind.CreatePerformer));
            Assert.That(firstPass[0].AnchorKind, Is.EqualTo(PresentationAnchorKind.Entity));
            Assert.That(firstPass[0].IdA, Is.EqualTo(11));
            Assert.That(firstPass[0].IdB, Is.EqualTo(99));
            Assert.That(firstPass[0].Source, Is.EqualTo(entity));
            Assert.That(firstPass[1].IdA, Is.EqualTo(22));
            Assert.That(firstPass[1].IdB, Is.EqualTo(99));
            Assert.That(entity.Get<PresentationStartupState>().Initialized, Is.True);

            commands.Clear();
            system.Update(0.016f);

            Assert.That(commands.Count, Is.EqualTo(0), "Startup performers should only be emitted on the first update.");
        }
    }
}
