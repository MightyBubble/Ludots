using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.GraphRuntime;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Map.Hex;
using Ludots.Core.Registry;
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
        public void PresenterEmitSystem_SkinnedAsset_UsesAnimatorStateAndPerSlotStableId()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "hero.controller",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 12, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 24, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 91,
                            DurationSeconds = 0f,
                            ConsumeTrigger = true,
                        },
                    ],
                });

            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register(
                "presenter.hero.skinned",
                new PresenterDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.Animator,
                            ActiveByDefault = true,
                            Animator = new AnimatorConfig
                            {
                                AnimatorControllerId = controllerId,
                                AnimationProfileId = 77,
                                StateParamKey = 120,
                                SpeedParamKey = -1,
                            },
                        },
                        new BehaviorSlot
                        {
                            SlotIndex = 1,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.SkinnedMesh,
                                AssetId = 101,
                                MaterialId = 202,
                                RenderPath = VisualRenderPath.SkinnedMesh,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                AssetIdParamKey = -1,
                            },
                        },
                    ],
                });

            var instances = new PresenterEntityRuntime(world);
            var animatorStates = new PresenterAnimatorStateBuffer(4);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new PresentationStableId { Value = 501 },
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 3f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, scopeId: 42, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 7001, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = (1u << 0) | (1u << 1);
            instances.SetParam(presenter, 91, ParamLane.Int, 0f, 1, default);

            using var animatorSystem = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            using var emitSystem = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                new SoundRequestBuffer());

            animatorSystem.Update(0.1f);
            emitSystem.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            ref readonly PresentationRequest request = ref requests.GetSpan()[0];
            Assert.That(request.Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(request.VisualProxy.MeshAssetId, Is.EqualTo(101));
            Assert.That(request.VisualProxy.MaterialId, Is.EqualTo(202));
            Assert.That(request.VisualProxy.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
            Assert.That(request.VisualProxy.AnimationProfileId, Is.EqualTo(77));
            Assert.That(request.VisualProxy.Animator.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(request.VisualProxy.Animator.GetPrimaryStateIndex(), Is.EqualTo(24));
            Assert.That(request.VisualProxy.StableId, Is.GreaterThan(0));
            Assert.That(request.VisualProxy.StableId, Is.Not.EqualTo(7001), "Skinned asset output should derive a per-slot stable id.");
        }

        [Test]
        public void PresenterEmitSystem_StaticAsset_DoesNotCarryAnimatorPayload()
        {
            using var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register(
                "presenter.static.mesh",
                new PresenterDefinition
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
                                AssetId = 11,
                                MaterialId = 12,
                                RenderPath = VisualRenderPath.StaticMesh,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                AssetIdParamKey = -1,
                            },
                        },
                    ],
                });

            var instances = new PresenterEntityRuntime(world);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new PresentationStableId { Value = 1 },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(defId, owner, scopeId: 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), stableId: 2001, Entity.Null, default);
            ref var state = ref world.Get<PresenterState>(presenter);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PresenterWorldRotation>(presenter);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PresenterWorldScale>(presenter);
            scale.Value = new Vector3(1f, 2f, 3f);

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                new PresenterAnimatorStateBuffer(2),
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            ref readonly PresentationRequest request = ref requests.GetSpan()[0];
            Assert.That(request.VisualProxy.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(request.VisualProxy.Animator.GetControllerId(), Is.EqualTo(0), "Static presenter output must stay animator-free.");
            Assert.That(request.VisualProxy.Position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            Assert.That(request.VisualProxy.Scale, Is.EqualTo(new Vector3(1f, 2f, 3f)));
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
        public void AnimatorControllerConfigLoader_CompilesSemanticParameterIndex()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AnimatorSemanticParam", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animator_controllers.json"), """
[
  {
    "id": "semantic.controller",
    "defaultStateIndex": 0,
    "states": [
      { "packedStateIndex": 1, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true },
      { "packedStateIndex": 2, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true }
    ],
    "transitions": [
      {
        "fromStateIndex": 0,
        "toStateIndex": 1,
        "conditionKind": "FloatGreaterOrEqual",
        "parameterIndex": "semantic.anim.speed",
        "threshold": 0.2,
        "durationSeconds": 0.1,
        "durationMode": "Seconds",
        "consumeTrigger": false,
        "hasExitTime": false,
        "exitTime": 0.0,
        "interruptSource": "None",
        "orderedInterruption": false
      }
    ]
  }
]
""");

            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var controllers = new Ludots.Core.Presentation.Assets.AnimatorControllerRegistry();

            new AnimatorControllerConfigLoader(pipeline, controllers).Load(catalog);

            Assert.That(controllers.TryGet(controllers.GetId("semantic.controller"), out var controller), Is.True);
            Assert.That(controller.Transitions[0].ParameterIndex, Is.EqualTo(PresenterParamKeyRegistry.Register("semantic.anim.speed")));

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failures in test teardown.
            }
        }

        [Test]
        public void AnimatorControllerConfigLoader_RejectsWrongCaseConditionKind()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AnimatorStrictCondition", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animator_controllers.json"), """
[
  {
    "id": "strict.controller",
    "defaultStateIndex": 0,
    "states": [
      { "packedStateIndex": 1, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true },
      { "packedStateIndex": 2, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true }
    ],
    "transitions": [
      {
        "fromStateIndex": 0,
        "toStateIndex": 1,
        "conditionKind": "floatGreaterOrEqual",
        "parameterIndex": "semantic.anim.speed",
        "threshold": 0.2,
        "durationSeconds": 0.1,
        "durationMode": "Seconds",
        "consumeTrigger": false,
        "hasExitTime": false,
        "exitTime": 0.0,
        "interruptSource": "None",
        "orderedInterruption": false
      }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var controllers = new Ludots.Core.Presentation.Assets.AnimatorControllerRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new AnimatorControllerConfigLoader(pipeline, controllers).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("conditionKind"));
            Assert.That(ex.Message, Does.Contain("floatGreaterOrEqual"));
        }

        [Test]
        public void AnimatorControllerConfigLoader_RejectsNumericParameterIndex()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AnimatorNumericParam", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animator_controllers.json"), """
[
  {
    "id": "numeric.controller",
    "defaultStateIndex": 0,
    "states": [
      { "packedStateIndex": 1, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true },
      { "packedStateIndex": 2, "durationSeconds": 1.0, "playbackSpeed": 1.0, "loop": true }
    ],
    "transitions": [
      {
        "fromStateIndex": 0,
        "toStateIndex": 1,
        "conditionKind": "FloatGreaterOrEqual",
        "parameterIndex": 0,
        "threshold": 0.2,
        "durationSeconds": 0.1,
        "durationMode": "Seconds",
        "consumeTrigger": false,
        "hasExitTime": false,
        "exitTime": 0.0,
        "interruptSource": "None",
        "orderedInterruption": false
      }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var controllers = new Ludots.Core.Presentation.Assets.AnimatorControllerRegistry();

            Assert.That(
                () => new AnimatorControllerConfigLoader(pipeline, controllers).Load(catalog),
                Throws.InvalidOperationException.With.Message.Contains("must be a semantic string"));
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
            var presenterRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.PresenterDefinitionRegistry);
            var profileRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationProfileRegistry);
            var clipRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationClipRegistry);

            Assert.That(controllerRegistry, Is.Not.Null);
            Assert.That(presenterRegistry, Is.Not.Null);
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
            Assert.That(profileRegistry.TryResolveStateClipId(humanoidProfileId, 43, out int humanoidRunClipId), Is.True);

            int tankRecoilClipId = clipRegistry!.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.TankRecoilClipKey);
            int humanoidAimClipId = clipRegistry.GetId(AnimationAcceptanceMod.AnimationAcceptanceIds.HumanoidAimClipKey);
            Assert.That(tankRecoilClipId, Is.GreaterThan(0));
            Assert.That(humanoidAimClipId, Is.GreaterThan(0));

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

            int healthBarDefId = presenterRegistry!.GetId("entity_health_bar");
            int worldTextDefId = presenterRegistry.GetId("entity_world_text");
            Assert.That(presenterRegistry.TryGet(healthBarDefId, out var healthBar), Is.True);
            Assert.That(presenterRegistry.TryGet(worldTextDefId, out var worldText), Is.True);
            Assert.That(healthBar.Behaviors.Length, Is.GreaterThan(0));
            Assert.That(worldText.Behaviors.Length, Is.GreaterThan(0));
        }

        [Test]
        public void AnimationProfileConfigLoader_RejectsRemovedBuiltinClipsField()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AnimationBuiltinClips", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animation_profiles.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animation_profiles.json"), """
[
  {
    "id": "legacy.profile",
    "animatorControllerId": "legacy.controller",
    "stateClips": [],
    "builtinClips": [
      { "builtinClipId": "LocomotionCycle", "clipAssetId": "legacy.clip" }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var profiles = new AnimationProfileRegistry();
            var controllers = new AnimatorControllerRegistry();
            var clips = new AnimationClipRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new AnimationProfileConfigLoader(pipeline, profiles, controllers, clips).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("builtinClips"));
            Assert.That(ex.Message, Does.Contain("stateClips"));
        }

        [Test]
        public void RepositoryPresenters_Definitions_MustHaveValidIds()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory.GetFiles(Path.Combine(repoRoot, "mods"), "presenters.json", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(file));
                Assert.That(root, Is.TypeOf<JsonArray>(), $"Presenter file must contain a JSON array: {file}");

                foreach (JsonNode? item in (JsonArray)root!)
                {
                    if (item is not JsonObject obj)
                    {
                        continue;
                    }

                    string definitionId = obj["id"]?.GetValue<string>() ?? string.Empty;
                    Assert.That(
                        string.IsNullOrWhiteSpace(definitionId),
                        Is.False,
                        $"Presenter definition in '{file}' must define a non-empty id.");
                }
            }
        }

        [Test]
        public void PresenterEmitSystem_AndTransientMarkers_PopulateSharedVisualProxyAndSkinnedBatchContracts()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var requests = new PresentationRequestBuffer();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "presenter.skinned",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States = [ new AnimatorStateDefinition { PackedStateIndex = 3, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true } ],
                    Transitions = Array.Empty<AnimatorTransitionDefinition>(),
                });

            var definitions = new PresenterDefinitionRegistry();
            int definitionId = definitions.Register(
                "presenter.skinned.marker",
                new PresenterDefinition
                {
                    Behaviors =
                    [
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.Animator,
                            ActiveByDefault = true,
                            Animator = new AnimatorConfig
                            {
                                AnimatorControllerId = controllerId,
                                AnimationProfileId = 9,
                                StateParamKey = 40,
                                SpeedParamKey = -1,
                            },
                        },
                        new BehaviorSlot
                        {
                            SlotIndex = 1,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.SkinnedMesh,
                                AssetId = 7,
                                MaterialId = 9,
                                RenderPath = VisualRenderPath.SkinnedMesh,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                AssetIdParamKey = -1,
                            },
                        },
                    ],
                });

            var instances = new PresenterEntityRuntime(world);
            var animatorStates = new PresenterAnimatorStateBuffer(4);
            Entity owner = world.Create(
                new PresentationStableId { Value = 501 },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 2f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(definitionId, owner, scopeId: 7, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 3001, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = (1u << 0) | (1u << 1);

            using var animatorSystem = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            using var emitSystem = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates,
                new SoundRequestBuffer());

            animatorSystem.Update(0.016f);
            emitSystem.Update(0.016f);

            var markers = new TransientMarkerBuffer();
            Assert.That(markers.TryAddMesh(99, new Vector3(3f, 0.25f, 4f), Vector3.One, Vector4.One, 0.2f), Is.True);
            markers.TickAndRequest(requests, 0.016f, world);
            Assert.That(requests.Count, Is.EqualTo(2));

            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                new StableDrawCache(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                proxyBuffer,
                skinnedBatchBuffer);
            flush.Update(0.016f);

            Assert.That(proxyBuffer.Count, Is.EqualTo(2));
            Assert.That(skinnedBatchBuffer.Count, Is.EqualTo(1));
            Assert.That(proxyBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(snapshotBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(skinnedBatchBuffer.GetSpan()[0].AnimationProfileId, Is.EqualTo(9));
            Assert.That(skinnedBatchBuffer.GetSpan()[0].Animator.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.EqualTo(TransientMarkerIdentity.ComposeStableId(1)));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.GreaterThan(0));
        }

        [Test]
        public void PresenterEmitSystem_MixedSkinnedAndSelectionMesh_UsesDirectSkinnedBatch()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            var skinnedBatch = new SkinnedVisualBatchBuffer(8);
            const int selectionVisibleParam = 701;

            int definitionId = definitions.Register(
                "presenter.mass_navigation_flow.agent",
                new PresenterDefinition
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
                                AssetKind = AssetKind.SkinnedMesh,
                                AssetId = 10,
                                MaterialId = 20,
                                RenderPath = VisualRenderPath.GpuSkinnedInstance,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                AssetIdParamKey = -1,
                            },
                        },
                        new BehaviorSlot
                        {
                            SlotIndex = 1,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.Mesh,
                                AssetId = 11,
                                MaterialId = 21,
                                RenderPath = VisualRenderPath.InstancedStaticMesh,
                                Mobility = VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                VisibilityParamKey = selectionVisibleParam,
                                AssetIdParamKey = -1,
                            },
                        },
                    ],
                    ParamDefaults =
                    [
                        new ParamDefault
                        {
                            ParamKey = selectionVisibleParam,
                            Lane = ParamLane.Int,
                            IntValue = 0,
                        },
                    ],
                });

            var instances = new PresenterEntityRuntime(world);
            Entity owner = world.Create(
                new PresentationStableId { Value = 901 },
                new VisualTransform
                {
                    Position = new Vector3(2f, 0f, 4f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(definitionId, owner, scopeId: 1, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 9001, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = (1u << 0) | (1u << 1);
            instances.SetParam(presenter, selectionVisibleParam, ParamLane.Int, 0f, 0, Vector4.Zero);

            using var emitSystem = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!,
                skinnedVisualBatchBuffer: skinnedBatch);

            emitSystem.Update(0.016f);

            Assert.That(skinnedBatch.Count, Is.EqualTo(1), "Skinned body should use the production direct GPU skinned batch even when the same presenter has a visibility-param selection mesh.");
            Assert.That(requests.Count, Is.EqualTo(1), "Only the hidden selection mesh should need a visibility snapshot request.");
            Assert.That(requests.Get(0).Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(requests.Get(0).VisualProxy.MeshAssetId, Is.EqualTo(11));
            Assert.That(requests.Get(0).VisualProxy.Visibility, Is.EqualTo(VisualVisibility.Hidden));
            Assert.That(skinnedBatch.GetSpan()[0].MeshAssetId, Is.EqualTo(10));
            Assert.That(skinnedBatch.GetSpan()[0].RenderPath, Is.EqualTo(VisualRenderPath.GpuSkinnedInstance));
        }

        [Test]
        public void PresenterEmitSystem_InstanceScopedMarker_UsesAllocatedStableId()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
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
                "presenter.entity.marker",
                new PresenterDefinition
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
                                AssetIdParamKey = -1,
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
            instances.BindDefinitions(definitions);
            Entity presenter = instances.Create(
                    definitionId,
                    owner,
                    scopeId: 9001,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    stableId: 7001,
                    Entity.Null,
                    default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var behaviorSystem = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                soundRequests);
            using var system = new PresenterEmitSystem(
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
                new MeshAssetRegistry(),
                new StableDrawCache(),
                drawBuffer,
                groundOverlays,
                worldHud,
                new SplineRibbonBuffer(),
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
        public void WorldHudPresentBehavior_ProjectsKnownAudienceFromKnowledgeNotTeamRelationship()
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

                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 8);
                var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
                UpsertPresenterKnowledge(projectionStore, ownerAudience, owner, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);
                UpsertPresenterKnowledge(projectionStore, hostileAudience, owner, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);

                var behavior = new WorldHudPresentBehavior();
                var ownerGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = ownerAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };
                var hostileGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = hostileAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                bool ownerVisible = behavior.TryResolveProjection(world, ownerGlobals, owner, LODLevel.High, out PresentPhaseResult ownerPhase);
                bool hostileVisible = behavior.TryResolveProjection(world, hostileGlobals, owner, LODLevel.High, out PresentPhaseResult hostilePhase);

                Assert.That(ownerVisible, Is.True);
                Assert.That(ownerPhase.IsOwnedByAudience, Is.True);
                Assert.That(ownerPhase.ShouldPresent, Is.True);
                Assert.That(ownerPhase.AllowWorldHudProjection, Is.True);

                Assert.That(hostileVisible, Is.True, "Knowledge projection is the HUD readability authority; team hostility is styling-only.");
                Assert.That(hostilePhase.IsHostile, Is.True);
                Assert.That(hostilePhase.ShouldPresent, Is.True);
                Assert.That(hostilePhase.AllowWorldHudProjection, Is.True);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void WorldHudPresentBehavior_ProjectsVisibleTransientWorldTextForHostileAudience()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                Entity target = world.Create(
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                Entity hostileAudience = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);

                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 8);
                var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
                UpsertPresenterKnowledge(projectionStore, hostileAudience, target, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);

                var behavior = new WorldHudPresentBehavior();
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = hostileAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                bool transientTextProjected = behavior.TryResolveProjection(
                    world,
                    globals,
                    target,
                    LODLevel.High,
                    WorldHudItemKind.Text,
                    ReadOnlySpan<int>.Empty,
                    out PresentPhaseResult transientTextPhase);
                ReadOnlySpan<int> requiredAttributes = stackalloc int[1] { 7 };
                bool attributeTextProjected = behavior.TryResolveProjection(
                    world,
                    globals,
                    target,
                    LODLevel.High,
                    WorldHudItemKind.Text,
                    requiredAttributes,
                    out PresentPhaseResult attributeTextPhase);

                Assert.That(transientTextProjected, Is.True);
                Assert.That(transientTextPhase.IsHostile, Is.True);
                Assert.That(transientTextPhase.ShouldPresent, Is.True);
                Assert.That(transientTextPhase.AllowWorldHudProjection, Is.True);

                Assert.That(attributeTextProjected, Is.False);
                Assert.That(attributeTextPhase.RequiresAttributeProjection, Is.True);
                Assert.That(attributeTextPhase.AllowWorldHudProjection, Is.False);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void WorldHudPresentBehavior_UsesConfiguredViewerForKnowledgeProjection()
        {
            using var world = World.Create();

            Entity target = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity configuredViewer = world.Create();

            var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            UpsertPresenterKnowledge(
                projectionStore,
                configuredViewer,
                target,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live);
            var behavior = new WorldHudPresentBehavior();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = configuredViewer,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };

            bool projected = behavior.TryResolveProjection(world, globals, target, LODLevel.High, out PresentPhaseResult phase);

            Assert.That(projected, Is.True);
            Assert.That(phase.HasKnowledgeProjection, Is.True);
            Assert.That(phase.ShouldPresent, Is.True);
        }

        [Test]
        public void WorldHudPresentBehavior_ProjectsAllyAudienceFromProjectionAndTeamRelationship()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                Entity owner = world.Create(
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                Entity allyAudience = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Friendly);
                var projectionStore = new KnowledgeProjectionStore();
                var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
                UpsertPresenterKnowledge(projectionStore, allyAudience, owner, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);

                var behavior = new WorldHudPresentBehavior();
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = allyAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                bool projected = behavior.TryResolveProjection(world, globals, owner, LODLevel.High, out PresentPhaseResult phase);

                Assert.That(projected, Is.True);
                Assert.That(phase.IsFriendly, Is.True);
                Assert.That(phase.HasKnowledgeProjection, Is.True);
                Assert.That(phase.ShouldPresent, Is.True);
                Assert.That(phase.AllowWorldHudProjection, Is.True);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void WorldHudPresentBehavior_SuppressesKnownAudienceWithoutRequiredAttributeProjection()
        {
            using var world = World.Create();

            const int healthAttributeId = 7;
            Entity owner = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity audience = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 });

            var projectionStore = new KnowledgeProjectionStore();
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            UpsertPresenterKnowledge(
                projectionStore,
                audience,
                owner,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty);
            var behavior = new WorldHudPresentBehavior();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = audience,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };
            ReadOnlySpan<int> requiredAttributes = stackalloc int[1] { healthAttributeId };

            bool projected = behavior.TryResolveProjection(world, globals, owner, LODLevel.High, requiredAttributes, out PresentPhaseResult phase);

            Assert.That(projected, Is.False);
            Assert.That(phase.HasKnowledgeProjection, Is.True);
            Assert.That(phase.RequiresAttributeProjection, Is.True);
            Assert.That(phase.HasAttributeProjection, Is.False);
            Assert.That(phase.ShouldPresent, Is.True);
            Assert.That(phase.AllowWorldHudProjection, Is.False);
        }

        [Test]
        public void WorldHudPresentBehavior_SuppressesUnknownAudienceWithoutKnowledgeProjection()
        {
            using var world = World.Create();

            Entity owner = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity audience = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 });

            var projectionStore = new KnowledgeProjectionStore();
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            var behavior = new WorldHudPresentBehavior();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = audience,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };

            bool projected = behavior.TryResolveProjection(world, globals, owner, LODLevel.High, out PresentPhaseResult phase);

            Assert.That(projected, Is.False);
            Assert.That(phase.ShouldPresent, Is.False);
            Assert.That(phase.HasVision, Is.False);
            Assert.That(phase.AllowWorldHudProjection, Is.False);
        }

        [Test]
        public void WorldHudPresentBehavior_DefaultAudienceSuppressesWorldHudWithoutKnowledgeViewer()
        {
            using var world = World.Create();

            const int healthAttributeId = 7;
            Entity ownerWithAttributes = world.Create(
                new AttributeBuffer(),
                new CullState { IsVisible = true, LOD = LODLevel.High });
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(ownerWithAttributes);
            attributes.SetCurrent(healthAttributeId, 100f);

            var behavior = new WorldHudPresentBehavior();
            var globals = new Dictionary<string, object>();
            ReadOnlySpan<int> requiredAttributes = stackalloc int[1] { healthAttributeId };

            bool projectedWithoutAttributes = behavior.TryResolveProjection(
                world,
                globals,
                ownerWithAttributes,
                LODLevel.High,
                WorldHudItemKind.Text,
                ReadOnlySpan<int>.Empty,
                out PresentPhaseResult noAttributePhase);
            bool projectedWithAttributes = behavior.TryResolveProjection(
                world,
                globals,
                ownerWithAttributes,
                LODLevel.High,
                WorldHudItemKind.Bar,
                requiredAttributes,
                out PresentPhaseResult attributePhase);

            Assert.That(projectedWithoutAttributes, Is.False);
            Assert.That(noAttributePhase.ShouldPresent, Is.False);
            Assert.That(noAttributePhase.HasVision, Is.False);
            Assert.That(noAttributePhase.AllowWorldHudProjection, Is.False);

            Assert.That(projectedWithAttributes, Is.False);
            Assert.That(attributePhase.ShouldPresent, Is.False);
            Assert.That(attributePhase.HasVision, Is.False);
            Assert.That(attributePhase.AllowWorldHudProjection, Is.False);
        }

        [Test]
        public void WorldHudPresentBehavior_ProjectionChecksAllocateZeroAfterWarmup()
        {
            using var world = World.Create();

            const int healthAttributeId = 7;
            Entity owner = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity audience = world.Create(
                new Team { Id = 10 },
                new PlayerOwner { PlayerId = 10 });

            var projectionStore = new KnowledgeProjectionStore();
            var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
            UpsertPresenterKnowledge(
                projectionStore,
                audience,
                owner,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty.WithId(healthAttributeId));
            var behavior = new WorldHudPresentBehavior();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = audience,
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
            };
            int[] requiredAttributes = [healthAttributeId];

            Assert.That(behavior.TryResolveProjection(world, globals, owner, LODLevel.High, requiredAttributes, out _), Is.True);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            int projectedCount = 0;
            for (int i = 0; i < 128; i++)
            {
                if (behavior.TryResolveProjection(world, globals, owner, LODLevel.High, requiredAttributes, out _))
                {
                    projectedCount++;
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(projectedCount, Is.EqualTo(128));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void PresenterEmitSystem_WorldBar_UsesWorldHudPresentBehaviorProjection()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                var instances = new PresenterEntityRuntime(world);
                var definitions = new PresenterDefinitionRegistry();
                var requests = new PresentationRequestBuffer();
                var soundRequests = new SoundRequestBuffer();

                int definitionId = definitions.Register(
                    "presenter.entity.worldbar",
                    new PresenterDefinition
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
                                    AssetIdParamKey = -1,
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

                instances.BindDefinitions(definitions);
                Entity presenter = instances.Create(definitionId, owner, scopeId: 9101, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 8101, Entity.Null, default);
                world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 8);
                var projectionResolver = new KnowledgeProjectionResolver(projectionStore);
                UpsertPresenterKnowledge(projectionStore, ownerAudience, owner, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);
                UpsertPresenterKnowledge(projectionStore, hostileAudience, owner, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live);

                var ownerGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = ownerAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };
                using var behaviorSystem = new PresenterBehaviorSystem(
                    world,
                    instances,
                    definitions,
                    new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                    new PresentationOwnerChangeBuffer(8),
                    soundRequests);
                behaviorSystem.Update(0f);

                using (var ownerSystem = new PresenterEmitSystem(
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

                // Owner emit clears retained dirty; re-mark so the hostile audience pass re-evaluates projection.
                instances.MarkTransformDrivenEmitDirty(presenter);

                var hostileGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = hostileAudience,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                using (var hostileSystem = new PresenterEmitSystem(
                           world,
                           instances,
                           definitions,
                           requests,
                           hostileGlobals))
                {
                    hostileSystem.Update(0.016f);
                }

                Assert.That(
                    requests.Count,
                    Is.EqualTo(1),
                    "Known hostile audience still receives world HUD when knowledge projection authorizes readability.");
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void PresenterEmitSystem_WorldBar_UsesRelationGrantedAttributeProjection()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                const int durabilityAttributeId = 7;
                const string publicMapObjectsKey = "collection.public_map_objects";
                var instances = new PresenterEntityRuntime(world);
                var definitions = new PresenterDefinitionRegistry();
                var requests = new PresentationRequestBuffer();
                var soundRequests = new SoundRequestBuffer();

                int definitionId = definitions.Register(
                    "presenter.entity.relation-granted-worldbar",
                    new PresenterDefinition
                    {
                        Behaviors =
                        [
                            new BehaviorSlot
                            {
                                SlotIndex = 0,
                                Kind = BehaviorKind.AttributeBinding,
                                ActiveByDefault = true,
                                AttributeBinding = new AttributeBindingConfig
                                {
                                    AttributeId = durabilityAttributeId,
                                    TargetParamKey = WellKnownPresenterParamKeys.BarFillRatio,
                                    Mode = ValueSourceKind.AttributeRatio,
                                    Thresholds = Array.Empty<ThresholdMapping>(),
                                },
                            },
                            new BehaviorSlot
                            {
                                SlotIndex = 1,
                                Kind = BehaviorKind.AssetBinding,
                                ActiveByDefault = true,
                                AssetBinding = new AssetBindingConfig
                                {
                                    AssetKind = AssetKind.WorldHud,
                                    Mobility = VisualMobility.Movable,
                                    LocalScale = new Vector3(40f, 6f, 1f),
                                    MaterialParamKey = -1,
                                    AssetIdParamKey = -1,
                                },
                            },
                        ],
                    });

                var ownerAttributes = new AttributeBuffer();
                ownerAttributes.SetBase(durabilityAttributeId, 100f);
                ownerAttributes.SetCurrent(durabilityAttributeId, 100f);
                Entity owner = world.Create(
                    ownerAttributes,
                    new PresentationStableId { Value = 701 },
                    new VisualTransform
                    {
                        Position = new Vector3(1f, 2f, 3f),
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                Entity viewer = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });
                Entity grantSource = world.Create();
                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);

                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
                var projectionResolver = CreateRelationGrantedProjectionResolver(
                    world,
                    viewer,
                    grantSource,
                    owner,
                    durabilityAttributeId,
                    "PublicMap",
                    publicMapObjectsKey,
                    projectionStore);
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                instances.BindDefinitions(definitions);
                Entity presenter = instances.Create(
                    definitionId,
                    owner,
                    scopeId: 9201,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    stableId: 8201,
                    Entity.Null,
                    default);
                world.Get<PresenterState>(presenter).BehaviorActiveMask = (1u << 0) | (1u << 1);

                using var behaviorSystem = new PresenterBehaviorSystem(
                    world,
                    instances,
                    definitions,
                    new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                    new PresentationOwnerChangeBuffer(8),
                    soundRequests);
                behaviorSystem.Update(0f);

                using var emitSystem = new PresenterEmitSystem(
                    world,
                    instances,
                    definitions,
                    requests,
                    globals);
                emitSystem.Update(0.016f);

                Assert.That(requests.Count, Is.EqualTo(1));
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void WorldHudPresentBehavior_ProjectsAttributeHudFromRelationGrantedKnowledge()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                const int durabilityAttributeId = 7;
                const string publicMapObjectsKey = "collection.public_map_objects";

                Entity viewer = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });
                Entity grantSource = world.Create();
                var mapObjectAttributes = new AttributeBuffer();
                mapObjectAttributes.SetCurrent(durabilityAttributeId, 100f);
                Entity mapObject = world.Create(
                    mapObjectAttributes,
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);
                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
                var projectionResolver = CreateRelationGrantedProjectionResolver(
                    world,
                    viewer,
                    grantSource,
                    mapObject,
                    durabilityAttributeId,
                    "PublicMap",
                    publicMapObjectsKey,
                    projectionStore);
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                var behavior = new WorldHudPresentBehavior();
                ReadOnlySpan<int> requiredAttributes = stackalloc int[1] { durabilityAttributeId };

                bool projected = behavior.TryResolveProjection(
                    world,
                    globals,
                    mapObject,
                    LODLevel.High,
                    WorldHudItemKind.Bar,
                    requiredAttributes,
                    out PresentPhaseResult phase);

                Assert.That(projected, Is.True);
                Assert.That(phase.IsHostile, Is.True, "Team hostility remains a styling fact.");
                Assert.That(phase.HasKnowledgeProjection, Is.True);
                Assert.That(phase.RequiresAttributeProjection, Is.True);
                Assert.That(phase.HasAttributeProjection, Is.True);
                Assert.That(phase.AllowWorldHudProjection, Is.True);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void WorldHudPresentBehavior_RelationGrantCompletesExistingProjectionAttributeMask()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                const int durabilityAttributeId = 7;
                const string publicMapObjectsKey = "collection.public_map_objects";

                Entity viewer = world.Create(
                    new Team { Id = 20 },
                    new PlayerOwner { PlayerId = 20 });
                Entity grantSource = world.Create();
                var mapObjectAttributes = new AttributeBuffer();
                mapObjectAttributes.SetCurrent(durabilityAttributeId, 100f);
                Entity mapObject = world.Create(
                    mapObjectAttributes,
                    new Team { Id = 10 },
                    new PlayerOwner { PlayerId = 10 },
                    new CullState { IsVisible = true, LOD = LODLevel.High });

                TeamManager.SetRelationshipSymmetric(10, 20, TeamRelationship.Hostile);
                var projectionStore = new KnowledgeProjectionStore(initialCapacity: 4);
                UpsertPresenterKnowledge(
                    projectionStore,
                    viewer,
                    mapObject,
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty);
                var projectionResolver = CreateRelationGrantedProjectionResolver(
                    world,
                    viewer,
                    grantSource,
                    mapObject,
                    durabilityAttributeId,
                    "PublicMap",
                    publicMapObjectsKey,
                    projectionStore);
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
                    [CoreServiceKeys.KnowledgeProjectionResolver.Name] = projectionResolver,
                };

                var behavior = new WorldHudPresentBehavior();
                ReadOnlySpan<int> requiredAttributes = stackalloc int[1] { durabilityAttributeId };

                bool projected = behavior.TryResolveProjection(
                    world,
                    globals,
                    mapObject,
                    LODLevel.High,
                    WorldHudItemKind.Bar,
                    requiredAttributes,
                    out PresentPhaseResult phase);

                Assert.That(projected, Is.True);
                Assert.That(phase.HasKnowledgeProjection, Is.True);
                Assert.That(phase.HasAttributeProjection, Is.True);
                Assert.That(phase.AllowWorldHudProjection, Is.True);
            }
            finally
            {
                TeamManager.Clear();
            }
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
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            Entity entity = world.Create(
                new PresentationStableId { Value = 99 },
                new EntityTemplateKeyRef { TemplateKeyId = 1234 });

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
            world.Add(entity, new PresentationDestroyPending());

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
        public void WorldToVisualSyncSystem_AndPresenterEmitSystem_SnapshotCarriesSyncedTransformRotationAndIdentity()
        {
            using var world = World.Create();
            world.Create(PresentationFrameState.Default);

            var definitions = new PresenterDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(definitions, "presenter.synced.static", assetId: 7, materialId: 9);
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);

            Entity owner = world.Create(
                WorldPositionCm.FromCm(250, 500),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(100, 200) },
                VisualTransform.Default,
                new FacingDirection { AngleRad = MathF.PI * 0.5f },
                new PresentationStableId { Value = 501 },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity presenter = instances.Create(definitionId, owner, scopeId: 0, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 6001, Entity.Null, default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var sync = new WorldToVisualSyncSystem(world);
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            using var emit = new PresenterEmitSystem(world, instances, definitions, requests, new Dictionary<string, object>(), null!, null!);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                new StableDrawCache(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            sync.Update(0.016f);
            ref var pos = ref world.Get<PresenterWorldPosition>(presenter);
            pos.Value = world.Get<VisualTransform>(owner).Position;
            ref var rot = ref world.Get<PresenterWorldRotation>(presenter);
            rot.Value = world.Get<VisualTransform>(owner).Rotation;
            ref var scale = ref world.Get<PresenterWorldScale>(presenter);
            scale.Value = world.Get<VisualTransform>(owner).Scale;
            emit.Update(0.016f);
            flush.Update(0.016f);

            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));

            var item = snapshotBuffer.GetSpan()[0];
            Assert.That(item.TemplateId, Is.EqualTo(definitionId));
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

            using var system = new TerrainHeightSyncSystem(world, globals);
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
                new VisualHeightmapSampleState(),
                new VisualTransform
                {
                    Position = new Vector3(1f, 2f, 5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });

            var projector = new UnavailableGroundProjector();
            var heightmap = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new Ludots.Platform.Abstractions.WorldAabbCm(0, 0, 1000, 1000),
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
        public void TerrainHeightSyncSystem_DoesNotSampleDynamicEntityWithoutExplicitHeightmapState()
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

            var heightmap = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new Ludots.Platform.Abstractions.WorldAabbCm(0, 0, 1000, 1000),
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
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            system.Update(0.016f);

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.Y, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void PresenterEmitSystem_WritesVisibilityIdentityAndTransformToSnapshot_WithoutChangingDrawBufferFiltering()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            var stableDrawCache = new StableDrawCache();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 16);
            int visibleDef = RegisterStaticVisualDefinition(definitions, "visible", assetId: 10, materialId: 20);
            int hiddenDef = RegisterStaticVisualDefinition(definitions, "hidden", assetId: 11, materialId: 21, visibilityParamKey: 500);
            int culledDef = RegisterStaticVisualDefinition(definitions, "culled", assetId: 12, materialId: 22, renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);

            Quaternion visibleRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);
            Quaternion hiddenRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            Quaternion culledRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f);

            Entity visibleOwner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity hiddenOwner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity culledOwner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Culled });

            Entity visiblePresenter = instances.Create(visibleDef, visibleOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(1f, 2f, 3f), 101, Entity.Null, default);
            Entity hiddenPresenter = instances.Create(hiddenDef, hiddenOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 202, Entity.Null, default);
            Entity culledPresenter = instances.Create(culledDef, culledOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 8f, 9f), 303, Entity.Null, default);

            world.Get<PresenterState>(visiblePresenter).BehaviorActiveMask = 1u;
            world.Get<PresenterWorldRotation>(visiblePresenter).Value = visibleRotation;
            world.Get<PresenterWorldScale>(visiblePresenter).Value = new Vector3(2f, 3f, 4f);

            world.Get<PresenterState>(hiddenPresenter).BehaviorActiveMask = 1u;
            world.Get<PresenterWorldRotation>(hiddenPresenter).Value = hiddenRotation;
            world.Get<PresenterWorldScale>(hiddenPresenter).Value = new Vector3(1f, 2f, 3f);
            instances.SetParam(hiddenPresenter, 500, ParamLane.Int, 0f, 0, default);

            world.Get<PresenterState>(culledPresenter).BehaviorActiveMask = 1u;
            world.Get<PresenterWorldRotation>(culledPresenter).Value = culledRotation;
            world.Get<PresenterWorldScale>(culledPresenter).Value = new Vector3(3f, 2f, 1f);

            using var system = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                null!,
                null!,
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            system.Update(0.016f);
            flush.Update(0.016f);

            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Visible draw buffer should still contain only currently drawable presenter visuals.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(3), "Adapter-facing snapshot must retain hidden and culled presenter visuals with explicit visibility.");

            var snapshotsByTemplateId = new Dictionary<int, PrimitiveDrawItem>();
            foreach (ref readonly var item in snapshotBuffer.GetSpan())
            {
                snapshotsByTemplateId[item.TemplateId] = item;
            }

            Assert.That(snapshotsByTemplateId[visibleDef].Visibility, Is.EqualTo(VisualVisibility.Visible));
            Assert.That(snapshotsByTemplateId[visibleDef].Scale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            AssertQuaternionEquivalent(snapshotsByTemplateId[visibleDef].Rotation, visibleRotation);

            Assert.That(snapshotsByTemplateId[hiddenDef].Visibility, Is.EqualTo(VisualVisibility.Hidden));
            Assert.That(snapshotsByTemplateId[hiddenDef].Scale, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            AssertQuaternionEquivalent(snapshotsByTemplateId[hiddenDef].Rotation, hiddenRotation);

            Assert.That(snapshotsByTemplateId[culledDef].Visibility, Is.EqualTo(VisualVisibility.Culled));
            Assert.That(snapshotsByTemplateId[culledDef].LOD, Is.EqualTo(LODLevel.Culled));
            Assert.That(snapshotsByTemplateId[culledDef].Scale, Is.EqualTo(new Vector3(3f, 2f, 1f)));
            AssertQuaternionEquivalent(snapshotsByTemplateId[culledDef].Rotation, culledRotation);

            var drawnItem = drawBuffer.GetSpan()[0];
            Assert.That(drawnItem.TemplateId, Is.EqualTo(visibleDef));
            Assert.That(drawnItem.Visibility, Is.EqualTo(VisualVisibility.Visible));
            AssertQuaternionEquivalent(drawnItem.Rotation, visibleRotation);
        }

        [Test]
        public void PresenterEntityRuntime_TracksActiveCountAndOwnerPayloadRefsIncrementally()
        {
            using var world = World.Create();
            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int rootDefA = definitions.Register("runtime.payload.root.a", new PresenterDefinition());
            int childDefA = definitions.Register("runtime.payload.child.a", new PresenterDefinition());
            int rootDefB = definitions.Register("runtime.payload.root.b", new PresenterDefinition());
            instances.BindDefinitions(definitions);

            Assert.That(instances.ActiveCount, Is.EqualTo(0));
            Assert.That(instances.HasOwnerPayload(ownerA), Is.False);

            Entity rootA = instances.Create(rootDefA, ownerA, scopeId: 1);
            Entity childA = instances.Create(childDefA, ownerA, scopeId: 1, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 1001, rootA, definitions.Get(childDefA));
            Entity rootB = instances.Create(rootDefB, ownerB, scopeId: 2);

            Assert.That(instances.ActiveCount, Is.EqualTo(3));
            Assert.That(instances.HasOwnerPayload(ownerA), Is.True);
            Assert.That(instances.HasOwnerPayload(ownerB), Is.True);

            instances.Destroy(rootA);
            Assert.That(instances.ActiveCount, Is.EqualTo(1), "Destroying a parent should recursively destroy descendants.");
            Assert.That(instances.HasOwnerPayload(ownerA), Is.False);
            Assert.That(instances.HasOwnerPayload(ownerB), Is.True);

            instances.Destroy(rootB);
            Assert.That(instances.ActiveCount, Is.EqualTo(0));
            Assert.That(instances.HasOwnerPayload(ownerB), Is.False);
        }

        [Test]
        public void PresenterEntityRuntime_OwnerPayloadRootIgnoresWorldPositionOverlayRoots()
        {
            using var world = World.Create();
            Entity owner = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new VisualTransform
                {
                    Position = new Vector3(10f, 0f, 20f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int entityRootDef = definitions.Register("runtime.payload.entity.root", new PresenterDefinition());
            int overlayRootDef = definitions.Register("runtime.payload.overlay.root", new PresenterDefinition());
            instances.BindDefinitions(definitions);

            Entity entityRoot = instances.Create(entityRootDef, owner, scopeId: 1);
            Entity overlayRoot = instances.Create(
                overlayRootDef,
                owner,
                scopeId: 2,
                PresentationAnchorKind.WorldPosition,
                new Vector3(4f, 0f, 6f),
                stableId: 2002,
                Entity.Null,
                definitions.Get(overlayRootDef));

            Assert.That(world.TryGet(owner, out PresentationOwnerHasPresenterPayload payload), Is.True);
            Assert.That(payload.Count, Is.EqualTo(2));
            Assert.That(payload.RootCount, Is.EqualTo(1), "World-position overlay roots share owner scope but must not break the entity-root fast path.");
            Assert.That(payload.SingleRootPresenter, Is.EqualTo(entityRoot));
            Assert.That(payload.SingleRootTransformSync, Is.EqualTo(1));

            world.Get<WorldPositionCm>(owner).Value = WorldPositionCm.FromCm(2500, -700).Value;
            world.Get<VisualTransform>(owner).Position = new Vector3(25f, 0f, -7f);

            using var sync = new PresenterEntityTransformSyncSystem(world, instances, definitions);
            sync.Update(0.016f);

            Assert.That(world.Get<PresenterWorldPosition>(entityRoot).Value, Is.EqualTo(new Vector3(25f, 0f, -7f)));
            Assert.That(world.Get<PresenterWorldPosition>(overlayRoot).Value, Is.EqualTo(new Vector3(4f, 0f, 6f)));
        }

        [Test]
        public void PresentationVisualProxyEmitter_Throws_WhenSnapshotBufferOverflows()
        {
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer);

            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 1,
                TemplateId = 101,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.StaticMesh,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            };

            emitter.Emit(proxy);
            proxy.StableId = 2;
            proxy.TemplateId = 102;
            var ex = Assert.Throws<InvalidOperationException>(() => emitter.Emit(proxy));
            Assert.That(ex!.Message, Does.Contain("overflowed"));
        }

        [Test]
        public void PresentationRequestFlushSystem_StableDrawCache_UsesLatestProxyAsSingleTruth()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var stableDrawCache = new StableDrawCache();

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 9001,
                TemplateId = 100,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = new Vector4(1f, 0f, 0f, 1f),
                RenderPath = VisualRenderPath.StaticMesh,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            }));
            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 11,
                MaterialId = 21,
                StableId = 9001,
                TemplateId = 101,
                Position = new Vector3(4f, 5f, 6f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(2f, 2f, 2f),
                Color = new Vector4(0f, 1f, 0f, 1f),
                RenderPath = VisualRenderPath.StaticMesh,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.Medium,
            }));

            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                proxyBuffer,
                skinnedBatchBuffer);

            flush.Update(0.016f);

            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(proxyBuffer.Count, Is.EqualTo(1));

            ref readonly PrimitiveDrawItem snapshot = ref snapshotBuffer.GetSpan()[0];
            Assert.That(snapshot.StableId, Is.EqualTo(9001));
            Assert.That(snapshot.MeshAssetId, Is.EqualTo(11));
            Assert.That(snapshot.MaterialId, Is.EqualTo(21));
            Assert.That(snapshot.TemplateId, Is.EqualTo(101));
            Assert.That(snapshot.Position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            Assert.That(snapshot.Scale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
            Assert.That(snapshot.LOD, Is.EqualTo(LODLevel.Medium));
        }

        [Test]
        public void PresentationRequestFlushSystem_StableDrawCache_WritesSnapshotRevisionFromContentChanges()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var stableDrawCache = new StableDrawCache();
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 9002,
                TemplateId = 200,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            }));

            flush.Update(0.016f);
            int firstRevision = snapshotBuffer.Revision;
            Assert.That(firstRevision, Is.GreaterThan(0));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));

            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Revision, Is.EqualTo(firstRevision));
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Stable visuals should stay projected when content revision is unchanged.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "Snapshot buffer must not be cleared on unchanged stable content.");

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 9002,
                TemplateId = 200,
                Position = new Vector3(5f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            }));

            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Revision, Is.GreaterThan(firstRevision));
        }

        [Test]
        public void PresentationRequestFlushSystem_TargetGenerationChange_ReprojectsRetainedStableContentWithoutContentRevisionChange()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var stableDrawCache = new StableDrawCache();
            var targetGeneration = new PresentationTargetGeneration();
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                proxyBuffer,
                new SkinnedVisualBatchBuffer(),
                targetGeneration: targetGeneration);

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 9200,
                TemplateId = 300,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Static,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            }));

            flush.Update(0.016f);
            int contentRevision = stableDrawCache.ContentRevision;
            int projectionGeneration = snapshotBuffer.ProjectionGeneration;
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(proxyBuffer.Count, Is.EqualTo(1));

            snapshotBuffer.Clear();
            drawBuffer.Clear();
            proxyBuffer.Clear();
            targetGeneration.MarkReady();

            flush.Update(0.016f);

            Assert.That(stableDrawCache.ContentRevision, Is.EqualTo(contentRevision));
            Assert.That(snapshotBuffer.Revision, Is.EqualTo(contentRevision));
            Assert.That(snapshotBuffer.ProjectionGeneration, Is.GreaterThan(projectionGeneration));
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "Target generation changes must replay retained snapshot content.");
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Target generation changes must replay visible retained draw content.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(1), "Target generation changes must replay proxy content for adapter snapshot consumers.");
            Assert.That(snapshotBuffer.GetSpan()[0].StableId, Is.EqualTo(9200));
        }

        [Test]
        public void PresentationVisualCapabilityValidator_RequiresTargetGeneration_WhenAdapterDeclaresExternalTargetLifecycle()
        {
            var requests = new PresentationVisualRequestBuffer();
            var capabilities = new PresentationAdapterCapabilities(PresentationVisualCapabilities.ExternalTargetLifecycle);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => PresentationVisualCapabilityValidator.Validate(requests, capabilities, targetGeneration: null))!;

            Assert.That(ex.Message, Does.Contain("external target lifecycle"));
            Assert.That(ex.Message, Does.Contain(nameof(PresentationTargetGeneration)));
        }

        [Test]
        public void RegisterPresentationAdapterCapabilities_ValidatesExternalTargetLifecycleWiring()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            var capabilities = new PresentationAdapterCapabilities(PresentationVisualCapabilities.ExternalTargetLifecycle);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => engine.RegisterPresentationAdapterCapabilities(capabilities))!;
            Assert.That(ex.Message, Does.Contain(nameof(PresentationTargetGeneration)));

            var targetGeneration = new PresentationTargetGeneration();
            engine.SetService(CoreServiceKeys.PresentationTargetGeneration, targetGeneration);

            engine.RegisterPresentationAdapterCapabilities(capabilities);

            Assert.That(engine.GetService(CoreServiceKeys.PresentationAdapterCapabilities), Is.SameAs(capabilities));
        }

        [Test]
        public void PresentationRequestFlushSystem_ClearsTransientProjection_WhenFrameStopsEmittingMovableProxy()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var stableDrawCache = new StableDrawCache();
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                proxyBuffer,
                new SkinnedVisualBatchBuffer());

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = 9100,
                TemplateId = 200,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Movable,
                Visibility = VisualVisibility.Visible,
                LOD = LODLevel.High,
            }));

            flush.Update(0.016f);
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(proxyBuffer.Count, Is.EqualTo(1));

            flush.Update(0.016f);
            Assert.That(drawBuffer.Count, Is.EqualTo(0),
                "Movable presenter projection must be cleared when the presenter stops emitting.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(0),
                "Proxy projection must not retain the previous movable presenter frame.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(0));
        }

        [Test]
        public void PresenterEmitSystem_StableCache_RetainsVisual_WhenOwnerBecomesCulled()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(
                definitions,
                "stable.cull.retained",
                assetId: 31,
                materialId: 41,
                renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var stableDrawCache = new StableDrawCache();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 16);
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = instances.Create(
                definitionId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                new Vector3(3f, 4f, 5f),
                404,
                Entity.Null,
                default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                null!,
                null!,
                null,
                stableDrawCache,
                visualStableIds: visualStableIds);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            emit.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            Assert.That(visualStableIds.TryGet(
                PresenterBehaviorRuntimeUtility.ComposeVisualStableKey(404, 0, AssetKind.Mesh, definitionId),
                out int visualStableId), Is.True);
            Assert.That(stableDrawCache.Contains(visualStableId), Is.True);
            int initialRevision = stableDrawCache.ContentRevision;
            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.GetSpan()[0].StableId, Is.EqualTo(visualStableId));
            Assert.That(snapshotBuffer.GetSpan()[0].Visibility, Is.EqualTo(VisualVisibility.Visible));

            ref CullState ownerCull = ref world.Get<CullState>(owner);
            ownerCull.IsVisible = false;
            ownerCull.LOD = LODLevel.Culled;
            instances.SyncCullVisibility();

            emit.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            Assert.That(stableDrawCache.Contains(visualStableId), Is.True);
            Assert.That(stableDrawCache.ContentRevision, Is.GreaterThan(initialRevision));
            drawBuffer.Clear();
            snapshotBuffer.Clear();
            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(0));
            Assert.That(snapshotBuffer.GetSpan()[0].StableId, Is.EqualTo(visualStableId));
            Assert.That(snapshotBuffer.GetSpan()[0].Visibility, Is.EqualTo(VisualVisibility.Culled));
            Assert.That(snapshotBuffer.GetSpan()[0].LOD, Is.EqualTo(LODLevel.Culled));

            ownerCull.IsVisible = true;
            ownerCull.LOD = LODLevel.High;
            instances.SyncCullVisibility();

            emit.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            Assert.That(stableDrawCache.Contains(visualStableId), Is.True);
            drawBuffer.Clear();
            snapshotBuffer.Clear();
            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
            Assert.That(snapshotBuffer.GetSpan()[0].StableId, Is.EqualTo(visualStableId));
            Assert.That(snapshotBuffer.GetSpan()[0].Visibility, Is.EqualTo(VisualVisibility.Visible));
        }

        [Test]
        public void PresenterEmitSystem_StableVisual_DoesNotReemit_WhenInputsStayUnchanged()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PresenterDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(
                definitions,
                "stable.no.reemit",
                assetId: 61,
                materialId: 71,
                renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var stableDrawCache = new StableDrawCache();
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 16);
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = instances.Create(
                definitionId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                new Vector3(6f, 7f, 8f),
                505,
                Entity.Null,
                default);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;

            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                null!,
                null!,
                null,
                stableDrawCache,
                visualStableIds: visualStableIds);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new SplineRibbonBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            emit.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0));
            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            int revisionAfterFirstEmit = stableDrawCache.ContentRevision;
            flush.Update(0.016f);

            emit.Update(0.016f);
            Assert.That(requests.Count, Is.EqualTo(0), "Unchanged stable visuals should not enqueue fresh visual proxies every tick.");
            Assert.That(stableDrawCache.ContentRevision, Is.EqualTo(revisionAfterFirstEmit), "Unchanged stable visuals should not rewrite stable cache content every tick.");
            flush.Update(0.016f);

            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
        }

        private static int RegisterStaticVisualDefinition(
            PresenterDefinitionRegistry definitions,
            string key,
            int assetId,
            int materialId,
            VisualRenderPath renderPath = VisualRenderPath.StaticMesh,
            int visibilityParamKey = -1)
        {
            return definitions.Register(
                key,
                new PresenterDefinition
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
                                AssetId = assetId,
                                MaterialId = materialId,
                                RenderPath = renderPath,
                                Mobility = renderPath == VisualRenderPath.InstancedStaticMesh
                                    ? VisualMobility.Static
                                    : VisualMobility.Movable,
                                LocalScale = Vector3.One,
                                VisibilityParamKey = visibilityParamKey,
                                AssetIdParamKey = -1,
                            },
                        },
                    ],
                });
        }

        private static void UpsertPresenterKnowledge(
            KnowledgeProjectionStore store,
            Entity viewer,
            Entity target,
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            KnowledgeIdMask256 attributeMask = default)
        {
            store.Upsert(
                viewer,
                target,
                new KnowledgeDisclosureRecord(
                    presence,
                    position,
                    attributeMask,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 1));
        }

        private static KnowledgeProjectionResolver CreateRelationGrantedProjectionResolver(
            World world,
            Entity viewer,
            Entity grantSource,
            Entity target,
            int attributeId,
            string relationshipType,
            string collectionKey,
            KnowledgeProjectionStore projectionStore)
        {
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
            int relationshipTypeId = relationshipTypes.Register(relationshipType);
            collectionKeys.Register(collectionKey);

            relationships.EnsureLink(viewer, grantSource, relationshipTypeId);
            collections.Replace(
                grantSource,
                EntityCollectionDescriptor.Create(
                    collectionKey,
                    EntityCollectionSourceKind.RelationDerived,
                    EntityCollectionRoleKind.Display),
                new[] { target });

            var catalogRuntime = RelationshipCatalogRuntime.Compile(
                new RelationshipCatalogConfig
                {
                    KnowledgeGrants =
                    {
                        new RelationshipKnowledgeGrantConfig
                        {
                            Id = $"{relationshipType}.{collectionKey}",
                            TypeId = relationshipType,
                            CollectionKey = collectionKey,
                            Presence = KnowledgePresence.LiveVisible,
                            Position = KnowledgePositionAccess.Live,
                            AttributeIds = { attributeId },
                            ObservedTick = 1,
                            ExpiryTick = 0,
                            ConfidencePermille = 1000,
                        },
                    },
                },
                relationshipTypes,
                new RelationshipMetricRegistry(),
                collections);

            var projector = new KnowledgeRelationCollectionProjector(
                relationships,
                collections,
                catalogRuntime,
                projectionStore);
            return new KnowledgeProjectionResolver(projectionStore, projector);
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
