using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map.Hex;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using System.Collections.Generic;

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
        public void VisualRuntimeState_Create_RejectsAnimationProfileOnStaticLane()
        {
            Assert.That(
                () => VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 3,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.StaticMesh,
                    animationProfileId: 5),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("animationProfileId"));
        }

        [Test]
        public void PresentationAuthoringContext_Apply_AssignsStableIdVisualAndAnimatorState()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
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
                    AnimationProfileId = 77,
                    BaseScale = 1.25f,
                    RenderPath = VisualRenderPath.SkinnedMesh,
                    Mobility = VisualMobility.Movable,
                    VisibleByDefault = true,
                });

            var context = new PresentationAuthoringContext(visualTemplates, animators, stableIds);
            JsonNode authoring = JsonNode.Parse(
                """
                {
                  "visualTemplateId": "hero.template",
                  "visible": false,
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
            Assert.That(entity.Has<AnimatorRuntimeState>(), Is.True);
            Assert.That(entity.Has<AnimationOverlayRequest>(), Is.True);
            Assert.That(entity.Has<AnimatorFeedbackBuffer>(), Is.True);
            Assert.That(entity.Has<ModelPerformBinding>(), Is.False, "Skinned model ownership remains legacy until animator ownership migrates.");

            int stableId = entity.Get<PresentationStableId>().Value;
            Assert.That(stableId, Is.GreaterThan(0));
            Assert.That(entity.Get<VisualTemplateRef>().TemplateId, Is.EqualTo(templateId));

            var visual = entity.Get<VisualRuntimeState>();
            Assert.That(visual.MeshAssetId, Is.EqualTo(101));
            Assert.That(visual.MaterialId, Is.EqualTo(202));
            Assert.That(visual.BaseScale, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(visual.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
            Assert.That(visual.AnimatorControllerId, Is.EqualTo(controllerId));
            Assert.That(visual.AnimationProfileId, Is.EqualTo(77));
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
            var overlay = entity.Get<AnimationOverlayRequest>();
            Assert.That(overlay.HasAnyClip, Is.False);
            Assert.That(overlay.BaseClip.ClipId, Is.EqualTo(AnimatorBuiltinClipId.None));
            Assert.That(overlay.LayerClip.ClipId, Is.EqualTo(AnimatorBuiltinClipId.None));
            Assert.That(overlay.OverlayClip.ClipId, Is.EqualTo(AnimatorBuiltinClipId.None));
            Assert.That(entity.Get<AnimatorFeedbackBuffer>().Count, Is.EqualTo(0));

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
        public void PresentationAuthoringContext_Apply_RejectsStartupPerformerIds()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
            var animators = new AnimatorControllerRegistry();
            var stableIds = new PresentationStableIdAllocator();
            var context = new PresentationAuthoringContext(visualTemplates, animators, stableIds);

            JsonNode authoring = JsonNode.Parse(
                """
                {
                  "startupPerformerIds": ["performer.cast_marker"]
                }
                """)!;

            var ex = Assert.Throws<InvalidOperationException>(() => context.Apply(entity, authoring));
            Assert.That(ex!.Message, Does.Contain("startupPerformerIds"));
        }

        [Test]
        public void PresentationAuthoringContext_ApplyAnimator_RejectsStaticRenderPath()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
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

            var context = new PresentationAuthoringContext(visualTemplates, animators, stableIds);
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
            entity.Add(new PresentationStableId { Value = 1 });
            entity.Add(VisualTransform.Default);
            entity.Add(VisualRuntimeState.Create(
                meshAssetId: 11,
                materialId: 12,
                baseScale: 1f,
                renderPath: VisualRenderPath.StaticMesh));
            entity.Add(AnimatorPackedState.Create(3));
            entity.Add(new AnimationOverlayRequest
            {
                BaseClip = AnimatorBuiltinClipState.Create(AnimatorBuiltinClipId.LocomotionCycle, 0.5f, 1f),
            });

            var requests = new PresentationRequestBuffer();
            using var system = new EntityVisualEmitSystem(world, requests);

            Assert.That(
                () => system.Update(0.016f),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("stay separate from skinned runtime sync"));
        }

        [Test]
        public void AnimatorControllerConfigLoader_LoadsDefinitionsFromConfigPipeline()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "fixtures", "animation", "AnimationAcceptanceMod"),
            };

            using var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

            var registry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimatorControllerRegistry);
            Assert.That(registry, Is.Not.Null);

            int tankId = registry!.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.TankControllerKey);
            int humanoidId = registry.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.HumanoidControllerKey);
            Assert.That(tankId, Is.GreaterThan(0));
            Assert.That(humanoidId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(tankId, out var tank), Is.True);
            Assert.That(registry.TryGet(humanoidId, out var humanoid), Is.True);
            Assert.That(tank.States.Length, Is.EqualTo(3));
            Assert.That(humanoid.Transitions.Length, Is.EqualTo(8));
        }

        [Test]
        public void AnimationProfileConfigLoaders_LoadProfileClipAndTemplateResolutionChain()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "fixtures", "animation", "AnimationAcceptanceMod"),
            };

            using var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

            var controllerRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimatorControllerRegistry);
            var templateRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.PresentationVisualTemplateRegistry);
            var profileRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationProfileRegistry);
            var clipRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationClipRegistry);

            Assert.That(controllerRegistry, Is.Not.Null);
            Assert.That(templateRegistry, Is.Not.Null);
            Assert.That(profileRegistry, Is.Not.Null);
            Assert.That(clipRegistry, Is.Not.Null);

            int tankProfileId = profileRegistry!.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.TankProfileKey);
            int humanoidProfileId = profileRegistry.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.HumanoidProfileKey);
            Assert.That(tankProfileId, Is.GreaterThan(0));
            Assert.That(humanoidProfileId, Is.GreaterThan(0));

            Assert.That(profileRegistry.TryGet(tankProfileId, out var tankProfile), Is.True);
            Assert.That(profileRegistry.TryGet(humanoidProfileId, out var humanoidProfile), Is.True);
            Assert.That(
                tankProfile.AnimatorControllerId,
                Is.EqualTo(controllerRegistry!.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.TankControllerKey)));
            Assert.That(
                humanoidProfile.AnimatorControllerId,
                Is.EqualTo(controllerRegistry.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.HumanoidControllerKey)));

            Assert.That(profileRegistry.TryResolveStateClipId(tankProfileId, 32, out int tankCruiseClipId), Is.True);
            Assert.That(profileRegistry.TryResolveBuiltinClipId(tankProfileId, AnimatorBuiltinClipId.RecoilPulse, out int tankRecoilClipId), Is.True);
            Assert.That(profileRegistry.TryResolveStateClipId(humanoidProfileId, 43, out int humanoidRunClipId), Is.True);
            Assert.That(profileRegistry.TryResolveBuiltinClipId(humanoidProfileId, AnimatorBuiltinClipId.AimYawOffset, out int humanoidAimClipId), Is.True);

            Assert.That(
                clipRegistry!.TryResolveLocator(tankCruiseClipId, AnimationAcceptanceMod.AnimationAcceptanceIds.RaylibBackendId, out var tankCruiseRaylib),
                Is.True);
            Assert.That(
                clipRegistry.TryResolveLocator(tankRecoilClipId, AnimationAcceptanceMod.AnimationAcceptanceIds.Ue5BackendId, out var tankRecoilUe5),
                Is.True);
            Assert.That(
                clipRegistry.TryResolveLocator(humanoidRunClipId, AnimationAcceptanceMod.AnimationAcceptanceIds.RaylibBackendId, out var humanoidRunRaylib),
                Is.True);
            Assert.That(
                clipRegistry.TryResolveLocator(humanoidAimClipId, AnimationAcceptanceMod.AnimationAcceptanceIds.Ue5BackendId, out var humanoidAimUe5),
                Is.True);

            Assert.That(tankCruiseRaylib.AssetRef, Does.Contain("tank_cruise"));
            Assert.That(tankRecoilUe5.AssetRef, Does.Contain("Tank_RecoilPulse"));
            Assert.That(humanoidRunRaylib.AssetRef, Does.Contain("humanoid_run"));
            Assert.That(humanoidAimUe5.AssetRef, Does.Contain("Humanoid_AimYawOffset"));

            int tankTemplateId = templateRegistry!.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.TankVisualTemplateId);
            int humanoidTemplateId = templateRegistry.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.HumanoidVisualTemplateId);
            Assert.That(templateRegistry.TryGet(tankTemplateId, out var tankTemplate), Is.True);
            Assert.That(templateRegistry.TryGet(humanoidTemplateId, out var humanoidTemplate), Is.True);
            Assert.That(tankTemplate.AnimationProfileId, Is.EqualTo(tankProfileId));
            Assert.That(humanoidTemplate.AnimationProfileId, Is.EqualTo(humanoidProfileId));
            Assert.That(tankTemplate.AnimatorControllerId, Is.EqualTo(tankProfile.AnimatorControllerId));
            Assert.That(humanoidTemplate.AnimatorControllerId, Is.EqualTo(humanoidProfile.AnimatorControllerId));
        }

        [Test]
        public void RepositoryVisualTemplates_SkinnedEntries_MustBindThroughAnimationProfiles()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory.GetFiles(Path.Combine(repoRoot, "mods"), "visual_templates.json", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(file));
                Assert.That(root, Is.TypeOf<JsonArray>(), $"Visual template file must contain a JSON array: {file}");

                foreach (JsonNode? item in (JsonArray)root!)
                {
                    if (item is not JsonObject obj)
                    {
                        continue;
                    }

                    string templateId = obj["id"]?.GetValue<string>() ?? "<missing>";
                    string renderPath = obj["renderPath"]?.GetValue<string>() ?? string.Empty;
                    if (!string.Equals(renderPath, nameof(VisualRenderPath.SkinnedMesh), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(renderPath, nameof(VisualRenderPath.GpuSkinnedInstance), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string animationProfileId = obj["animationProfileId"]?.GetValue<string>() ?? string.Empty;
                    Assert.That(
                        string.IsNullOrWhiteSpace(animationProfileId),
                        Is.False,
                        $"Skinned visual template '{templateId}' in '{file}' must define animationProfileId.");
                }
            }
        }

        [Test]
        public void EntityVisualEmitSystem_AndTransientMarkers_PopulateSharedVisualProxyAndSkinnedBatchContracts()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var requests = new PresentationRequestBuffer();

            world.Create(
                new PresentationStableId { Value = 501 },
                new VisualTemplateRef { TemplateId = 42 },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 2f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 9,
                    baseScale: 1f,
                    renderPath: VisualRenderPath.SkinnedMesh,
                    animatorControllerId: 3,
                    animationProfileId: 9),
                AnimatorPackedState.Create(3),
                new AnimationOverlayRequest
                {
                    BaseClip = AnimatorBuiltinClipState.Create(AnimatorBuiltinClipId.LocomotionCycle, 0.25f, 0.8f),
                });

            using (var entityEmit = new EntityVisualEmitSystem(world, requests))
            {
                entityEmit.Update(0.016f);
            }

            var markers = new TransientMarkerBuffer();
            Assert.That(markers.TryAddMesh(99, new Vector3(3f, 0.25f, 4f), Vector3.One, Vector4.One, 0.2f), Is.True);
            markers.TickAndRequest(requests, 0.016f, world);
            Assert.That(requests.Count, Is.EqualTo(2));

            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                proxyBuffer,
                skinnedBatchBuffer);
            flush.Update(0.016f);

            Assert.That(proxyBuffer.Count, Is.EqualTo(2));
            Assert.That(skinnedBatchBuffer.Count, Is.EqualTo(1));
            Assert.That(proxyBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(snapshotBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(skinnedBatchBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(skinnedBatchBuffer.GetSpan()[0].AnimationOverlay.BaseClip.ClipId, Is.EqualTo(AnimatorBuiltinClipId.LocomotionCycle));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.EqualTo(TransientMarkerIdentity.ComposeStableId(1)));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.GreaterThan(0));
        }

        [Test]
        public void PresentationAuthoringContext_Apply_StaticTemplate_BindsEntityToModelPerformer()
        {
            using var world = World.Create();
            var entity = world.Create();

            var visualTemplates = new VisualTemplateRegistry();
            var animators = new AnimatorControllerRegistry();
            var stableIds = new PresentationStableIdAllocator();

            int templateId = visualTemplates.Register(
                "crate.template",
                new VisualTemplateDefinition
                {
                    MeshAssetId = 17,
                    MaterialId = 23,
                    BaseScale = 1.1f,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Movable,
                    VisibleByDefault = true,
                });

            var context = new PresentationAuthoringContext(visualTemplates, animators, stableIds);
            JsonNode authoring = JsonNode.Parse(
                """
                {
                  "visualTemplateId": "crate.template"
                }
                """)!;

            context.Apply(entity, authoring);

            Assert.That(entity.Has<ModelPerformBinding>(), Is.True);
            var binding = entity.Get<ModelPerformBinding>();
            Assert.That(binding.TemplateId, Is.EqualTo(templateId));
        }

        [Test]
        public void EntityVisualEmitSystem_SkipsDuplicateModelPerformBinding_StaticSlice()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();

            int templateId = 42;
            world.Create(
                new PresentationStableId { Value = 501 },
                new VisualTemplateRef { TemplateId = templateId },
                new ModelPerformBinding { TemplateId = templateId },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 2f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One * 2f,
                },
                VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 9,
                    baseScale: 1.5f,
                    renderPath: VisualRenderPath.StaticMesh));

            using (var legacyEmit = new EntityVisualEmitSystem(world, requests))
            {
                legacyEmit.Update(0.016f);
            }

            Assert.That(requests.Count, Is.EqualTo(0), "Legacy entity emit must stop duplicating model-bound entities once performer owns the static model slice.");
        }

        [Test]
        public void PerformerEmitSystem_InstanceScopedMarker_UsesAllocatedStableId()
        {
            using var world = World.Create();
            var instances = new PerformerInstanceBuffer();
            var definitions = new PerformerDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var worldHud = new WorldHudBatchBuffer();
            var groundOverlays = new GroundOverlayBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var globals = new Dictionary<string, object>();
            var soundRequests = new SoundRequestBuffer();

            int definitionId = definitions.Register(
                "performer.entity.marker",
                new PerformerDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.Mesh,
                                AssetId = 77,
                                RenderPath = VisualRenderPath.StaticMesh,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                            },
                        },
                    ],
                });

            Entity owner = world.Create(
                new PresentationStableId { Value = 501 },
                new VisualTransform
                {
                    Position = new Vector3(2f, 0.5f, 3f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
            Assert.That(
                instances.TryAllocate(
                    definitionId,
                    owner,
                    scopeId: 9001,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    stableId: 7001,
                    out _),
                Is.True);
            instances.Get(0).BehaviorActiveMask = 1u;

            using var behaviorSystem = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(),
                soundRequests);
            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                globals);

            behaviorSystem.Update(0f);
            system.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(1));

            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                drawBuffer,
                groundOverlays,
                worldHud,
                new RoadSplineBuffer(),
                snapshotBuffer,
                proxyBuffer,
                skinnedBatchBuffer);
            flush.Update(0.016f);

            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            ref readonly var item = ref snapshotBuffer.GetSpan()[0];
            Assert.That(item.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(item.StableId, Is.GreaterThan(0));
            Assert.That(item.StableId, Is.Not.EqualTo(7001), "AssetBinding snapshots now derive a per-slot stable id.");
            Assert.That(item.StableId, Is.GreaterThan(0));
        }

        [Test]
        public void WorldHudPerformBehavior_ProjectsOwnerAudienceAndSuppressesHostileAudience()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                Entity owner = world.Create(
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                Entity ownerAudience = world.Create(
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 });
                Entity hostileAudience = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);

                var behavior = new WorldHudPerformBehavior();
                var ownerGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = ownerAudience,
                };
                var hostileGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = hostileAudience,
                };

                bool ownerVisible = behavior.TryResolveProjection(world, ownerGlobals, owner, LODLevel.High, out PerformPhaseResult ownerPhase);
                bool hostileVisible = behavior.TryResolveProjection(world, hostileGlobals, owner, LODLevel.High, out PerformPhaseResult hostilePhase);

                Assert.That(ownerVisible, Is.True);
                Assert.That(ownerPhase.IsOwnedByAudience, Is.True);
                Assert.That(ownerPhase.ShouldPresent, Is.True);
                Assert.That(ownerPhase.AllowWorldHudProjection, Is.True);

                Assert.That(hostileVisible, Is.False);
                Assert.That(hostilePhase.IsHostile, Is.True);
                Assert.That(hostilePhase.ShouldPresent, Is.True, "Phase result remains valid for the viewer.");
                Assert.That(hostilePhase.AllowWorldHudProjection, Is.False, "Projection policy must already be resolved in PerformPhaseResult.");
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void PerformerEmitSystem_WorldBar_UsesWorldHudPerformBehaviorProjection()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                var instances = new PerformerInstanceBuffer();
                var definitions = new PerformerDefinitionRegistry();
                var requests = new PresentationRequestBuffer();
                var soundRequests = new SoundRequestBuffer();

                int definitionId = definitions.Register(
                    "performer.entity.worldbar",
                    new PerformerDefinition
                    {
                        Behaviors =
                        [
                            new BehaviorSlot
                            {
                                SlotIndex = 0,
                                Kind = BehaviorKind.AssetBinding,
                                ActiveByDefault = true,
                                AssetBinding = new AssetBindingConfig
                                {
                                    AssetKind = AssetKind.WorldHud,
                                    Mobility = VisualMobility.Movable,
                                    LocalScale = new Vector3(40f, 6f, 1f),
                                },
                            },
                        ],
                    });

                Entity owner = world.Create(
                    new PresentationStableId { Value = 601 },
                    new VisualTransform
                    {
                        Position = new Vector3(1f, 2f, 3f),
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                Entity ownerAudience = world.Create(
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 });
                Entity hostileAudience = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);

                Assert.That(
                    instances.TryAllocate(
                        definitionId,
                        owner,
                        scopeId: 9101,
                        PresentationAnchorKind.Entity,
                        Vector3.Zero,
                        stableId: 8101,
                        out _),
                    Is.True);
                instances.Get(0).BehaviorActiveMask = 1u;

                var ownerGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = ownerAudience,
                };
                using var behaviorSystem = new PerformerBehaviorSystem(
                    world,
                    instances,
                    definitions,
                    new PresentationEventStream(),
                    soundRequests);
                behaviorSystem.Update(0f);

                using (var ownerSystem = new PerformerEmitSystem(
                           world,
                           instances,
                           definitions,
                           requests,
                           ownerGlobals))
                {
                    ownerSystem.Update(0.016f);
                }

                Assert.That(requests.Count, Is.EqualTo(1), "Owner audience should receive projected world HUD output.");
                requests.Clear();

                var hostileGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = hostileAudience,
                };

                using (var hostileSystem = new PerformerEmitSystem(
                           world,
                           instances,
                           definitions,
                           requests,
                           hostileGlobals))
                {
                    hostileSystem.Update(0.016f);
                }

                Assert.That(requests.Count, Is.EqualTo(0), "Hostile audience should be suppressed by the first world HUD behavior projection slice.");
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void PresentationRequestFlushSystem_PrefabRequest_PreservesPrefabRootProxyWithoutSilentLeafFiltering()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var prefabs = new PrefabRegistry();
            var meshes = new MeshAssetRegistry();

            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            int typedPrefabMeshId = meshes.Register(
                "test.prefab.typed_root",
                MeshAssetDescriptor.Prefab(
                    0,
                    PrefabPart.Default(cubeId),
                    PrefabPart.Decal(materialId: 17, size: new Vector2(2f, 3f)),
                    PrefabPart.Vfx(effectAssetId: 23, spawnMode: PrefabVfxSpawnMode.Loop),
                    PrefabPart.Surface(cubeId, materialId: 31, tiling: new Vector2(2f, 2f))));
            int prefabId = prefabs.Register(
                "test.prefab.typed_root",
                new PrefabDefinition
                {
                    MeshAssetId = typedPrefabMeshId,
                    BaseScale = 1f,
                });

            requests.Add(PresentationRequest.FromPrefab(
                Entity.Null,
                prefabId,
                stableId: 7001,
                position: new Vector3(3f, 0.5f, 4f),
                rotation: Quaternion.Identity,
                scale: new Vector3(1.25f, 1.25f, 1.25f),
                color: new Vector4(0.25f, 0.8f, 1f, 0.9f),
                lod: LODLevel.High,
                context: PrefabFinalizationContext.Empty));

            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                prefabs,
                meshes,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                proxyBuffer,
                skinnedBatchBuffer);
            flush.Update(0.016f);

            Assert.That(proxyBuffer.Count, Is.EqualTo(1),
                "Prefab requests should survive flush as one prefab-root proxy instead of being pre-filtered into mesh-only leaves.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1),
                "Adapter-facing snapshot should retain the prefab root asset so downstream adapter finalization preserves typed visual semantics.");

            ref readonly var proxy = ref proxyBuffer.GetSpan()[0];
            ref readonly var snapshot = ref snapshotBuffer.GetSpan()[0];
            Assert.That(proxy.MeshAssetId, Is.EqualTo(typedPrefabMeshId));
            Assert.That(snapshot.MeshAssetId, Is.EqualTo(typedPrefabMeshId));
            Assert.That(proxy.StableId, Is.EqualTo(7001));
            Assert.That(snapshot.StableId, Is.EqualTo(7001));
            Assert.That(proxy.Position, Is.EqualTo(new Vector3(3f, 0.5f, 4f)));
            Assert.That(snapshot.Position, Is.EqualTo(new Vector3(3f, 0.5f, 4f)));
            Assert.That(proxy.Scale, Is.EqualTo(new Vector3(1.25f, 1.25f, 1.25f)));
            Assert.That(snapshot.Scale, Is.EqualTo(new Vector3(1.25f, 1.25f, 1.25f)));
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private static Vector3 AxialToWorld(float q, float r)
        {
            float x = HexCoordinates.EdgeLength * 1.7320508f * (q + r / 2.0f);
            float z = HexCoordinates.EdgeLength * 1.5f * r;
            return new Vector3(x, 0f, z);
        }

        private sealed class RecordingGroundProjector : IVisualGroundProjector
        {
            private readonly ProjectCallback _project;

            public delegate void ProjectCallback(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, Span<float> outHeights);

            public RecordingGroundProjector(ProjectCallback project)
            {
                _project = project;
            }

            public int InvocationCount { get; private set; }

            public float[] LastXs { get; private set; } = Array.Empty<float>();

            public float[] LastYs { get; private set; } = Array.Empty<float>();

            public bool TryProjectHeights(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm)
            {
                InvocationCount++;
                LastXs = worldXCm.ToArray();
                LastYs = worldYCm.ToArray();
                _project(worldXCm, worldYCm, outHeightCm);
                return true;
            }
        }

        private sealed class UnavailableGroundProjector : IVisualGroundProjector
        {
            public int InvocationCount { get; private set; }

            public bool TryProjectHeights(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm)
            {
                InvocationCount++;
                return false;
            }
        }

        [Test]
        public void PresentationEntityLifecycleSystem_PublishesSpawnAndDestroyFacts_AndFinalizesEntity()
        {
            using var world = World.Create();
            var events = new PresentationEventStream();
            Entity entity = world.Create(
                new PresentationStableId { Value = 99 },
                new EntityTemplateKeyCm { TemplateKeyId = 1234 });

            using var lifecycle = new PresentationEntityLifecycleSystem(world, events);
            using var finalize = new PresentationEntityFinalizeDestroySystem(world);

            lifecycle.Update(0.016f);

            var firstPass = events.GetSpan();
            Assert.That(firstPass.Length, Is.EqualTo(1));
            Assert.That(firstPass[0].Kind, Is.EqualTo(PresentationEventKind.EntitySpawned));
            Assert.That(firstPass[0].KeyId, Is.EqualTo(1234));
            Assert.That(firstPass[0].PayloadA, Is.EqualTo(99));
            Assert.That(world.Has<PresentationLifecycleState>(entity), Is.True);

            events.Clear();
            var state = world.Get<PresentationLifecycleState>(entity);
            state.PendingDestroy = true;
            world.Set(entity, state);

            lifecycle.Update(0.016f);

            var secondPass = events.GetSpan();
            Assert.That(secondPass.Length, Is.EqualTo(1));
            Assert.That(secondPass[0].Kind, Is.EqualTo(PresentationEventKind.EntityDestroyed));
            Assert.That(secondPass[0].KeyId, Is.EqualTo(1234));
            Assert.That(secondPass[0].PayloadA, Is.EqualTo(99));

            finalize.Update(0.016f);
            Assert.That(world.IsAlive(entity), Is.False);
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
            var requests = new PresentationRequestBuffer();
            using var emit = new EntityVisualEmitSystem(world, requests);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer);

            sync.Update(0.016f);
            emit.Update(0.016f);
            flush.Update(0.016f);

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
        public void TerrainHeightSyncSystem_DoesNotUseLegacyProjector_WhenVisualHeightmapIsMissing()
        {
            using var world = World.Create();
            world.Create(
                new PresentationFrameState
                {
                    InterpolationAlpha = 0.25f,
                    Enabled = true,
                },
                new PresentationFrameStateTag());

            Entity entity = world.Create(
                WorldPositionCm.FromCm(400, 800),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(0, 400) },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var projector = new RecordingGroundProjector((xs, ys, heights) =>
            {
                heights[0] = xs[0] + ys[0];
            });

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.VisualGroundProjector.Name] = projector,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            system.Update(0.016f);

            Assert.That(projector.InvocationCount, Is.EqualTo(0));

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(visual.Position.Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(visual.Position.Z, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void TerrainHeightSyncSystem_DoesNotUseVertexMapFallback_WhenVisualHeightmapIsMissing()
        {
            using var world = World.Create();
            var vertexMap = new VertexMap();
            vertexMap.Initialize(widthInChunks: 4, heightInChunks: 4);
            vertexMap.SetHeight(0, 0, 0);
            vertexMap.SetHeight(1, 0, 10);
            vertexMap.SetHeight(0, 1, 20);

            Vector3 p0 = AxialToWorld(q: 0f, r: 0f);
            Vector3 p1 = AxialToWorld(q: 1f, r: 0f);
            Vector3 p2 = AxialToWorld(q: 0f, r: 1f);
            Vector3 worldPos = (p0 + p1 + p2) / 3f;

            Entity entity = world.Create(
                WorldPositionCm.FromCmFloat(worldPos.X * 100f, worldPos.Z * 100f),
                new VisualTransform
                {
                    Position = worldPos,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.VertexMap.Name] = vertexMap,
            };

            using var system = new TerrainHeightSyncSystem(world, globals)
            {
                HeightScale = 1f,
            };
            system.Update(0.016f);

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.Y, Is.EqualTo(worldPos.Y).Within(0.001f));
        }

        [Test]
        public void TerrainHeightSyncSystem_UsesVisualHeightmapSingleTruth_WhenRegistered()
        {
            using var world = World.Create();
            Entity entity = world.Create(
                WorldPositionCm.FromCm(400, 800),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(300, 700) },
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var projector = new UnavailableGroundProjector();
            var heightmap = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new Ludots.Core.Mathematics.WorldAabbCm(0, 0, 1000, 1000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 100,
                        100, 200,
                    }));
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.VisualHeightmap.Name] = heightmap,
                [CoreServiceKeys.VisualGroundProjector.Name] = projector,
            };

            using var system = new TerrainHeightSyncSystem(world, globals);

            Assert.DoesNotThrow(() => system.Update(0.016f));
            Assert.That(projector.InvocationCount, Is.EqualTo(0));

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(visual.Position.Y, Is.EqualTo(120f * 0.01f).Within(0.001f));
            Assert.That(visual.Position.Z, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void EntityVisualEmitSystem_WritesVisibilityIdentityAndTransformToSnapshot_WithoutChangingDrawBufferFiltering()
        {
            using var world = World.Create();
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();

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
                new CullState { IsVisible = false, LOD = LODLevel.Culled });

            using var system = new EntityVisualEmitSystem(world, requests);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer);
            system.Update(0.016f);
            flush.Update(0.016f);

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
            Assert.That(snapshotsByStableId[303].LOD, Is.EqualTo(LODLevel.Culled));
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
            var requests = new PresentationRequestBuffer();

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

            using var system = new EntityVisualEmitSystem(world, requests);

            var ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f));
            Assert.That(ex!.Message, Does.Contain("PresentationStableId"));
        }

        [Test]
        public void EntityVisualEmitSystem_Throws_WhenSnapshotBufferOverflows()
        {
            using var world = World.Create();
            var drawBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer();
            var snapshotBuffer = new Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer(capacity: 1);
            var requests = new PresentationRequestBuffer();

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

            using var system = new EntityVisualEmitSystem(world, requests);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer);

            system.Update(0.016f);
            var ex = Assert.Throws<InvalidOperationException>(() => flush.Update(0.016f));
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

