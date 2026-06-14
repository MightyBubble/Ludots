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
        public void PerformerEmitSystem_SkinnedAsset_UsesAnimatorStateAndPerSlotStableId()
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

            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register(
                "performer.hero.skinned",
                new PerformerDefinition
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

            var instances = new PerformerEntityRuntime(world);
            var animatorStates = new PerformerAnimatorStateBuffer(4);
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
            Entity performer = instances.Create(defId, owner, scopeId: 42, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 7001, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = (1u << 0) | (1u << 1);
            instances.SetParam(performer, 91, ParamLane.Int, 0f, 1, default);

            using var animatorSystem = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            using var emitSystem = new PerformerEmitSystem(
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
        public void PerformerEmitSystem_StaticAsset_DoesNotCarryAnimatorPayload()
        {
            using var world = World.Create();
            var definitions = new PerformerDefinitionRegistry();
            int defId = definitions.Register(
                "performer.static.mesh",
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

            var instances = new PerformerEntityRuntime(world);
            var requests = new PresentationRequestBuffer();
            Entity owner = world.Create(
                new PresentationStableId { Value = 1 },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            instances.BindDefinitions(definitions);
            Entity performer = instances.Create(defId, owner, scopeId: 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), stableId: 2001, Entity.Null, default);
            ref var state = ref world.Get<PerformerState>(performer);
            state.BehaviorActiveMask = 1u;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = Quaternion.Identity;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
            scale.Value = new Vector3(1f, 2f, 3f);

            using var system = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                new PerformerAnimatorStateBuffer(2),
                new SoundRequestBuffer());

            system.Update(0.016f);

            Assert.That(requests.Count, Is.EqualTo(1));
            ref readonly PresentationRequest request = ref requests.GetSpan()[0];
            Assert.That(request.VisualProxy.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(request.VisualProxy.Animator.GetControllerId(), Is.EqualTo(0), "Static performer output must stay animator-free.");
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
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            File.WriteAllText(Path.Combine(core, "Configs", "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "animator_controllers.json"), """
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
        "consumeTrigger": false
      }
    ]
  }
]
""");

            PerformerParamKeyRegistry.ClearCustomKeysForTests();
            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var controllers = new Ludots.Core.Presentation.Assets.AnimatorControllerRegistry();

            new AnimatorControllerConfigLoader(pipeline, controllers).Load(catalog);

            Assert.That(controllers.TryGet(controllers.GetId("semantic.controller"), out var controller), Is.True);
            Assert.That(controller.Transitions[0].ParameterIndex, Is.EqualTo(PerformerParamKeyRegistry.Register("semantic.anim.speed")));

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
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            File.WriteAllText(Path.Combine(core, "Configs", "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "animator_controllers.json"), """
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
        "consumeTrigger": false
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
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(core, "Configs", "Presentation"));
            File.WriteAllText(Path.Combine(core, "Configs", "config_catalog.json"), """
[
  { "Path": "Presentation/animator_controllers.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Configs", "Presentation", "animator_controllers.json"), """
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
        "consumeTrigger": false
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
            var performerRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.PerformerDefinitionRegistry);
            var profileRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationProfileRegistry);
            var clipRegistry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AnimationClipRegistry);

            Assert.That(controllerRegistry, Is.Not.Null);
            Assert.That(performerRegistry, Is.Not.Null);
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

            int healthBarDefId = performerRegistry!.GetId("entity_health_bar");
            int worldTextDefId = performerRegistry.GetId("entity_world_text");
            Assert.That(performerRegistry.TryGet(healthBarDefId, out var healthBar), Is.True);
            Assert.That(performerRegistry.TryGet(worldTextDefId, out var worldText), Is.True);
            Assert.That(healthBar.Behaviors.Length, Is.GreaterThan(0));
            Assert.That(worldText.Behaviors.Length, Is.GreaterThan(0));
        }

        [Test]
        public void RepositoryPerformers_Definitions_MustHaveValidIds()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory.GetFiles(Path.Combine(repoRoot, "mods"), "performers.json", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(file));
                Assert.That(root, Is.TypeOf<JsonArray>(), $"Performer file must contain a JSON array: {file}");

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
                        $"Performer definition in '{file}' must define a non-empty id.");
                }
            }
        }

        [Test]
        public void PerformerEmitSystem_AndTransientMarkers_PopulateSharedVisualProxyAndSkinnedBatchContracts()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var proxyBuffer = new PresentationVisualProxyBuffer();
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer();
            var requests = new PresentationRequestBuffer();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "performer.skinned",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States = [ new AnimatorStateDefinition { PackedStateIndex = 3, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true } ],
                });

            var definitions = new PerformerDefinitionRegistry();
            int definitionId = definitions.Register(
                "performer.skinned.marker",
                new PerformerDefinition
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

            var instances = new PerformerEntityRuntime(world);
            var animatorStates = new PerformerAnimatorStateBuffer(4);
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
            Entity performer = instances.Create(definitionId, owner, scopeId: 7, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 3001, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = (1u << 0) | (1u << 1);

            using var animatorSystem = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            using var emitSystem = new PerformerEmitSystem(
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
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                new StableDrawCache(),
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
            Assert.That(skinnedBatchBuffer.GetSpan()[0].Animator.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.EqualTo(TransientMarkerIdentity.ComposeStableId(1)));
            Assert.That(proxyBuffer.GetSpan()[1].StableId, Is.GreaterThan(0));
        }

        [Test]
        public void PerformerEmitSystem_MixedSkinnedAndSelectionMesh_UsesDirectSkinnedBatch()
        {
            using var world = World.Create();
            var requests = new PresentationRequestBuffer();
            var definitions = new PerformerDefinitionRegistry();
            var skinnedBatch = new SkinnedVisualBatchBuffer(8);
            const int selectionVisibleParam = 701;

            int definitionId = definitions.Register(
                "performer.massflow.agent",
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

            var instances = new PerformerEntityRuntime(world);
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
            Entity performer = instances.Create(definitionId, owner, scopeId: 1, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 9001, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = (1u << 0) | (1u << 1);
            instances.SetParam(performer, selectionVisibleParam, ParamLane.Int, 0f, 0, Vector4.Zero);

            using var emitSystem = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!,
                skinnedVisualBatchBuffer: skinnedBatch);

            emitSystem.Update(0.016f);

            Assert.That(skinnedBatch.Count, Is.EqualTo(1), "Skinned body should use the production direct GPU skinned batch even when the same performer has a visibility-param selection mesh.");
            Assert.That(requests.Count, Is.EqualTo(1), "Only the hidden selection mesh should need a visibility snapshot request.");
            Assert.That(requests.Get(0).Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(requests.Get(0).VisualProxy.MeshAssetId, Is.EqualTo(11));
            Assert.That(requests.Get(0).VisualProxy.Visibility, Is.EqualTo(VisualVisibility.Hidden));
            Assert.That(skinnedBatch.GetSpan()[0].MeshAssetId, Is.EqualTo(10));
            Assert.That(skinnedBatch.GetSpan()[0].RenderPath, Is.EqualTo(VisualRenderPath.GpuSkinnedInstance));
        }

        [Test]
        public void PerformerEmitSystem_InstanceScopedMarker_UsesAllocatedStableId()
        {
            using var world = World.Create();
            var instances = new PerformerEntityRuntime(world);
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
            Entity performer = instances.Create(
                    definitionId,
                    owner,
                    scopeId: 9001,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    stableId: 7001,
                    Entity.Null,
                    default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var behaviorSystem = new PerformerBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
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
                new StableDrawCache(),
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
                var instances = new PerformerEntityRuntime(world);
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
                Entity performer = instances.Create(definitionId, owner, scopeId: 9101, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 8101, Entity.Null, default);
                world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

                var ownerGlobals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.LocalPlayerEntity.Name] = ownerAudience,
                };
                using var behaviorSystem = new PerformerBehaviorSystem(
                    world,
                    instances,
                    definitions,
                    new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                    new PresentationOwnerChangeBuffer(8),
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
                new StableDrawCache(),
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
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
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
        public void WorldToVisualSyncSystem_AndPerformerEmitSystem_SnapshotCarriesSyncedTransformRotationAndIdentity()
        {
            using var world = World.Create();
            world.Create(PresentationFrameState.Default);

            var definitions = new PerformerDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(definitions, "performer.synced.static", assetId: 7, materialId: 9);
            var instances = new PerformerEntityRuntime(world);
            instances.BindDefinitions(definitions);

            Entity owner = world.Create(
                WorldPositionCm.FromCm(250, 500),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(100, 200) },
                VisualTransform.Default,
                new FacingDirection { AngleRad = MathF.PI * 0.5f },
                new PresentationStableId { Value = 501 },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            Entity performer = instances.Create(definitionId, owner, scopeId: 0, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 6001, Entity.Null, default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var sync = new WorldToVisualSyncSystem(world);
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            using var emit = new PerformerEmitSystem(world, instances, definitions, requests, new Dictionary<string, object>(), null!, null!);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                new StableDrawCache(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            sync.Update(0.016f);
            ref var pos = ref world.Get<PerformerWorldPosition>(performer);
            pos.Value = world.Get<VisualTransform>(owner).Position;
            ref var rot = ref world.Get<PerformerWorldRotation>(performer);
            rot.Value = world.Get<VisualTransform>(owner).Rotation;
            ref var scale = ref world.Get<PerformerWorldScale>(performer);
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
            };

            using var system = new TerrainHeightSyncSystem(world, globals);
            system.Update(0.016f);

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Position.Y, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void PerformerEmitSystem_WritesVisibilityIdentityAndTransformToSnapshot_WithoutChangingDrawBufferFiltering()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PerformerDefinitionRegistry();
            int visibleDef = RegisterStaticVisualDefinition(definitions, "visible", assetId: 10, materialId: 20);
            int hiddenDef = RegisterStaticVisualDefinition(definitions, "hidden", assetId: 11, materialId: 21, visibilityParamKey: 500);
            int culledDef = RegisterStaticVisualDefinition(definitions, "culled", assetId: 12, materialId: 22, renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PerformerEntityRuntime(world);
            instances.BindDefinitions(definitions);

            Quaternion visibleRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.25f);
            Quaternion hiddenRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            Quaternion culledRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f);

            Entity visibleOwner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity hiddenOwner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity culledOwner = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Culled });

            Entity visiblePerformer = instances.Create(visibleDef, visibleOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(1f, 2f, 3f), 101, Entity.Null, default);
            Entity hiddenPerformer = instances.Create(hiddenDef, hiddenOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(4f, 5f, 6f), 202, Entity.Null, default);
            Entity culledPerformer = instances.Create(culledDef, culledOwner, 0, PresentationAnchorKind.WorldPosition, new Vector3(7f, 8f, 9f), 303, Entity.Null, default);

            world.Get<PerformerState>(visiblePerformer).BehaviorActiveMask = 1u;
            world.Get<PerformerWorldRotation>(visiblePerformer).Value = visibleRotation;
            world.Get<PerformerWorldScale>(visiblePerformer).Value = new Vector3(2f, 3f, 4f);

            world.Get<PerformerState>(hiddenPerformer).BehaviorActiveMask = 1u;
            world.Get<PerformerWorldRotation>(hiddenPerformer).Value = hiddenRotation;
            world.Get<PerformerWorldScale>(hiddenPerformer).Value = new Vector3(1f, 2f, 3f);
            instances.SetParam(hiddenPerformer, 500, ParamLane.Int, 0f, 0, default);

            world.Get<PerformerState>(culledPerformer).BehaviorActiveMask = 1u;
            world.Get<PerformerWorldRotation>(culledPerformer).Value = culledRotation;
            world.Get<PerformerWorldScale>(culledPerformer).Value = new Vector3(3f, 2f, 1f);

            using var system = new PerformerEmitSystem(world, instances, definitions, requests, new Dictionary<string, object>(), null!, null!);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                new StableDrawCache(),
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            system.Update(0.016f);
            flush.Update(0.016f);

            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Visible draw buffer should still contain only currently drawable performer visuals.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(3), "Adapter-facing snapshot must retain hidden and culled performer visuals with explicit visibility.");

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
        public void PerformerEntityRuntime_TracksActiveCountAndOwnerPayloadRefsIncrementally()
        {
            using var world = World.Create();
            Entity ownerA = world.Create();
            Entity ownerB = world.Create();
            var instances = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int rootDefA = definitions.Register("runtime.payload.root.a", new PerformerDefinition());
            int childDefA = definitions.Register("runtime.payload.child.a", new PerformerDefinition());
            int rootDefB = definitions.Register("runtime.payload.root.b", new PerformerDefinition());
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
        public void PresentationVisualProxyEmitter_Throws_WhenSnapshotBufferOverflows()
        {
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer);

            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
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
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                proxyBuffer,
                new SkinnedVisualBatchBuffer(),
                targetGeneration: targetGeneration);

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                proxyBuffer,
                new SkinnedVisualBatchBuffer());

            requests.Add(PresentationRequest.FromVisualProxy(Entity.Null, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
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
                "Movable performer projection must be cleared when the performer stops emitting.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(0),
                "Proxy projection must not retain the previous movable performer frame.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(0));
        }

        [Test]
        public void PerformerEmitSystem_StableCache_RemovesVisual_WhenOwnerBecomesCulled()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(
                definitions,
                "stable.cull.removal",
                assetId: 31,
                materialId: 41,
                renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PerformerEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var stableDrawCache = new StableDrawCache();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity performer = instances.Create(
                definitionId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                new Vector3(3f, 4f, 5f),
                404,
                Entity.Null,
                default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var emit = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                null!,
                null!,
                null,
                stableDrawCache);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
                snapshotBuffer,
                new PresentationVisualProxyBuffer(),
                new SkinnedVisualBatchBuffer());

            emit.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(1));
            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));

            ref CullState ownerCull = ref world.Get<CullState>(owner);
            ownerCull.IsVisible = false;
            ownerCull.LOD = LODLevel.Culled;
            instances.SyncCullVisibility();

            emit.Update(0.016f);
            Assert.That(stableDrawCache.Count, Is.EqualTo(0));
            drawBuffer.Clear();
            snapshotBuffer.Clear();
            flush.Update(0.016f);
            Assert.That(snapshotBuffer.Count, Is.EqualTo(0));
            Assert.That(drawBuffer.Count, Is.EqualTo(0));
        }

        [Test]
        public void PerformerEmitSystem_StableVisual_DoesNotReemit_WhenInputsStayUnchanged()
        {
            using var world = World.Create();
            var drawBuffer = new PrimitiveDrawBuffer();
            var snapshotBuffer = new PrimitiveDrawBuffer();
            var requests = new PresentationRequestBuffer();
            var definitions = new PerformerDefinitionRegistry();
            int definitionId = RegisterStaticVisualDefinition(
                definitions,
                "stable.no.reemit",
                assetId: 61,
                materialId: 71,
                renderPath: VisualRenderPath.InstancedStaticMesh);
            var instances = new PerformerEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var stableDrawCache = new StableDrawCache();
            Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity performer = instances.Create(
                definitionId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                new Vector3(6f, 7f, 8f),
                505,
                Entity.Null,
                default);
            world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

            using var emit = new PerformerEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                null!,
                null!,
                null,
                stableDrawCache);
            using var flush = new PresentationRequestFlushSystem(
                world,
                requests,
                new PrefabRegistry(),
                new MeshAssetRegistry(),
                stableDrawCache,
                drawBuffer,
                new GroundOverlayBuffer(),
                new WorldHudBatchBuffer(),
                new RoadSplineBuffer(),
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
            PerformerDefinitionRegistry definitions,
            string key,
            int assetId,
            int materialId,
            VisualRenderPath renderPath = VisualRenderPath.StaticMesh,
            int visibilityParamKey = -1)
        {
            return definitions.Register(
                key,
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

        private static void AssertQuaternionEquivalent(Quaternion actual, Quaternion expected, float epsilon = 0.0001f)
        {
            Quaternion normalizedActual = Quaternion.Normalize(actual);
            Quaternion normalizedExpected = Quaternion.Normalize(expected);
            float similarity = MathF.Abs(Quaternion.Dot(normalizedActual, normalizedExpected));
            Assert.That(similarity, Is.GreaterThanOrEqualTo(1f - epsilon));
        }
    }
}

