using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
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
        public void VisualRenderPathSemantics_KeepStaticAndSkinnedLanesSeparated()
        {
            Assert.That(VisualRenderPath.StaticMesh.IsStaticInstanceLane(), Is.True);
            Assert.That(VisualRenderPath.InstancedStaticMesh.IsStaticInstanceLane(), Is.True);
            Assert.That(VisualRenderPath.HierarchicalInstancedStaticMesh.IsStaticInstanceLane(), Is.True);
            Assert.That(VisualRenderPath.SkinnedMesh.IsStaticInstanceLane(), Is.False);
            Assert.That(VisualRenderPath.GpuSkinnedInstance.IsStaticInstanceLane(), Is.False);

            Assert.That(VisualRenderPath.SkinnedMesh.IsSkinnedLane(), Is.True);
            Assert.That(VisualRenderPath.GpuSkinnedInstance.IsSkinnedLane(), Is.True);
            Assert.That(VisualRenderPath.StaticMesh.IsSkinnedLane(), Is.False);
            Assert.That(VisualRenderPath.GpuSkinnedInstance.SupportsAnimatorPackedState(), Is.True);
        }

        [Test]
        public void VisualRuntimeState_Create_RejectsSkinnedPathWithoutAnimatorController()
        {
            Assert.That(
                () => VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 3,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.SkinnedMesh),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("animatorControllerId"));

            Assert.That(
                () => VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 3,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.GpuSkinnedInstance),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("animatorControllerId"));
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
        public void PresentationAuthoringContext_ApplyAnimator_RejectsStaticRenderPath()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
            var performers = new PerformerDefinitionRegistry();
            var animators = new AnimatorControllerRegistry();
            var stableIds = new PresentationStableIdAllocator();

            visualTemplates.Register(
                "static.template",
                new VisualTemplateDefinition
                {
                    MeshAssetId = 101,
                    MaterialId = 202,
                    BaseScale = 1f,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Movable,
                    VisibleByDefault = true,
                });

            var context = new PresentationAuthoringContext(visualTemplates, performers, animators, stableIds);
            JsonNode authoring = JsonNode.Parse(
                """
                {
                  "visualTemplateId": "static.template",
                  "animator": {
                    "controllerId": "hero.controller",
                    "primaryStateIndex": 12
                  }
                }
                """)!;

            Assert.That(
                () => context.Apply(entity, authoring),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("reserved for skinned lanes"));
        }

        [Test]
        public void EntityVisualEmitSystem_RejectsAnimatorPayloadOnStaticLane()
        {
            using var world = World.Create();
            var entity = world.Create();
            entity.Add(VisualTransform.Default);
            entity.Add(VisualRuntimeState.Create(
                meshAssetId: 11,
                materialId: 12,
                baseScale: 1f,
                renderPath: VisualRenderPath.StaticMesh));
            entity.Add(AnimatorPackedState.Create(3));

            var drawBuffer = new PrimitiveDrawBuffer();
            using var system = new EntityVisualEmitSystem(world, drawBuffer);

            Assert.That(
                () => system.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("stay separate from skinned runtime sync"));
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

        [Test]
        public void WorldToVisualSyncSystem_AndEntityVisualEmitSystem_SnapshotCarriesSyncedTransformRotationAndIdentity()
        {
            using var world = World.Create();
            world.Create(PresentationFrameState.Default);

            world.Create(
                WorldPositionCm.FromCm(250, 500),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(100, 200) },
                VisualTransform.Default,
                new FacingDirection { AngleRad = MathF.PI * 0.5f },
                new VisualTemplateRef { TemplateId = 42 },
                VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 9,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.StaticMesh),
                new PresentationStableId { Value = 501 });

            using var sync = new WorldToVisualSyncSystem(world);
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            using var emit = new EntityVisualEmitSystem(world, drawBuffer, snapshotBuffer);

            sync.Update(0.016f);
            emit.Update(0.016f);

            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));

            var item = snapshotBuffer.GetSpan()[0];
            Assert.That(item.StableId, Is.EqualTo(501));
            Assert.That(item.TemplateId, Is.EqualTo(42));
            Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
            Assert.That(item.Position.X, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(item.Position.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(item.Position.Z, Is.EqualTo(5f).Within(0.001f));
            AssertQuaternionEquivalent(item.Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI * 0.5f));
        }

        [Test]
        public void EntityVisualEmitSystem_WritesVisibilityIdentityAndTransformToSnapshot_WithoutChangingDrawBufferFiltering()
        {
            using var world = World.Create();
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();

            Quaternion visibleRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);
            Quaternion hiddenRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            Quaternion culledRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f);

            world.Create(
                new PresentationStableId { Value = 101 },
                new VisualTemplateRef { TemplateId = 1001 },
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 3f),
                    Rotation = visibleRotation,
                    Scale = new Vector3(2f, 3f, 4f),
                },
                VisualRuntimeState.Create(
                    meshAssetId: 10,
                    materialId: 20,
                    baseScale: 1.5f,
                    renderPath: VisualRenderPath.StaticMesh));

            world.Create(
                new PresentationStableId { Value = 202 },
                new VisualTemplateRef { TemplateId = 2002 },
                new VisualTransform
                {
                    Position = new Vector3(4f, 5f, 6f),
                    Rotation = hiddenRotation,
                    Scale = new Vector3(1f, 2f, 3f),
                },
                VisualRuntimeState.Create(
                    meshAssetId: 11,
                    materialId: 21,
                    baseScale: 2f,
                    renderPath: VisualRenderPath.StaticMesh,
                    visible: false));

            world.Create(
                new PresentationStableId { Value = 303 },
                new VisualTemplateRef { TemplateId = 3003 },
                new VisualTransform
                {
                    Position = new Vector3(7f, 8f, 9f),
                    Rotation = culledRotation,
                    Scale = new Vector3(3f, 2f, 1f),
                },
                VisualRuntimeState.Create(
                    meshAssetId: 12,
                    materialId: 22,
                    baseScale: 0.5f,
                    renderPath: VisualRenderPath.InstancedStaticMesh),
                new CullState { IsVisible = false });

            using var system = new EntityVisualEmitSystem(world, drawBuffer, snapshotBuffer);
            system.Update(0.016f);

            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Legacy draw buffer should still contain only currently drawable visuals.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(3), "Adapter-facing snapshot must retain hidden and culled visuals with explicit visibility.");

            var snapshotsByStableId = new System.Collections.Generic.Dictionary<int, Ludots.Core.Presentation.Rendering.PrimitiveDrawItem>();
            foreach (ref readonly var item in snapshotBuffer.GetSpan())
            {
                snapshotsByStableId[item.StableId] = item;
            }

            Assert.That(snapshotsByStableId[101].Visibility, Is.EqualTo(VisualVisibility.Visible));
            Assert.That(snapshotsByStableId[101].TemplateId, Is.EqualTo(1001));
            Assert.That(snapshotsByStableId[101].Scale, Is.EqualTo(new Vector3(3f, 4.5f, 6f)));
            AssertQuaternionEquivalent(snapshotsByStableId[101].Rotation, visibleRotation);

            Assert.That(snapshotsByStableId[202].Visibility, Is.EqualTo(VisualVisibility.Hidden));
            Assert.That(snapshotsByStableId[202].TemplateId, Is.EqualTo(2002));
            Assert.That(snapshotsByStableId[202].Scale, Is.EqualTo(new Vector3(2f, 4f, 6f)));
            AssertQuaternionEquivalent(snapshotsByStableId[202].Rotation, hiddenRotation);

            Assert.That(snapshotsByStableId[303].Visibility, Is.EqualTo(VisualVisibility.Culled));
            Assert.That(snapshotsByStableId[303].TemplateId, Is.EqualTo(3003));
            Assert.That(snapshotsByStableId[303].Scale, Is.EqualTo(new Vector3(1.5f, 1f, 0.5f)));
            AssertQuaternionEquivalent(snapshotsByStableId[303].Rotation, culledRotation);

            var drawnItem = drawBuffer.GetSpan()[0];
            Assert.That(drawnItem.StableId, Is.EqualTo(101));
            Assert.That(drawnItem.Visibility, Is.EqualTo(VisualVisibility.Visible));
            AssertQuaternionEquivalent(drawnItem.Rotation, visibleRotation);
        }

        [Test]
        public void EntityVisualEmitSystem_Throws_WhenRenderableVisualIsMissingPresentationStableId()
        {
            using var world = World.Create();
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();

            world.Create(
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 3f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                VisualRuntimeState.Create(
                    meshAssetId: 10,
                    materialId: 20,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.StaticMesh));

            using var system = new EntityVisualEmitSystem(world, drawBuffer, snapshotBuffer);

            var ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f));
            Assert.That(ex!.Message, Does.Contain("PresentationStableId"));
        }

        [Test]
        public void EntityVisualEmitSystem_Throws_WhenSnapshotBufferOverflows()
        {
            using var world = World.Create();
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer(capacity: 1);

            world.Create(
                new PresentationStableId { Value = 1 },
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 3f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                VisualRuntimeState.Create(
                    meshAssetId: 10,
                    materialId: 20,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.StaticMesh));

            world.Create(
                new PresentationStableId { Value = 2 },
                new VisualTransform
                {
                    Position = new Vector3(4f, 5f, 6f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                VisualRuntimeState.Create(
                    meshAssetId: 11,
                    materialId: 21,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.StaticMesh));

            using var system = new EntityVisualEmitSystem(world, drawBuffer, snapshotBuffer);

            var ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f));
            Assert.That(ex!.Message, Does.Contain("overflowed"));
        }

        private static void AssertQuaternionEquivalent(Quaternion actual, Quaternion expected, float epsilon = 0.0001f)
        {
            Quaternion normalizedActual = Quaternion.Normalize(actual);
            Quaternion normalizedExpected = Quaternion.Normalize(expected);
            float similarity = MathF.Abs(Quaternion.Dot(normalizedActual, normalizedExpected));
            Assert.That(similarity, Is.GreaterThanOrEqualTo(1f - epsilon));
        }
    }
}
