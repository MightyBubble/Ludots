using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CameraAcceptancePresenterTreeTests
    {
        private string _repoRoot = string.Empty;
        private string _tempRoot = string.Empty;
        private string _coreRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = FindRepoRoot();
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_CameraAcceptancePresenterTree", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_tempRoot, "Core");
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Configs"));
            File.WriteAllText(
                Path.Combine(_coreRoot, "Configs", "config_catalog.json"),
                File.ReadAllText(Path.Combine(_repoRoot, "assets", "Configs", "config_catalog.json")));
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
            AttributeRegistry.Clear();
            AttributeRegistry.Register("Health");
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
            AttributeRegistry.Clear();
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
            }
        }

        [Test]
        public void ProjectionCueFixture_EmitsMeshDecalVfxAndSurfaceChildren()
        {
            var world = World.Create();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", _coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadMods(RepoModPaths.ResolveExplicit(_repoRoot, new[]
            {
                "LudotsCoreMod",
                "CoreInputMod",
                "SharedThreeCProfilesMod",
                "CameraAcceptanceMod",
            }));

            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            var mapLoader = new MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
            mapLoader.LoadTemplates(catalog);

            var meshAssets = new MeshAssetRegistry();
            new MeshAssetConfigLoader(pipeline, meshAssets).Load(catalog);
            var materialAssets = new PresentationMaterialRegistry();
            new PresentationMaterialConfigLoader(pipeline, materialAssets).Load(catalog);
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            var animatorControllers = new AnimatorControllerRegistry();
            new AnimatorControllerConfigLoader(pipeline, animatorControllers).Load(catalog);
            var animationClips = new AnimationClipRegistry();
            new AnimationClipConfigLoader(pipeline, animationClips).Load(catalog);
            var animationProfiles = new AnimationProfileRegistry();
            new AnimationProfileConfigLoader(pipeline, animationProfiles, animatorControllers, animationClips).Load(catalog);

            var definitions = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(
                pipeline,
                definitions,
                resolveAttributeName: AttributeRegistry.GetId,
                resolveMeshId: meshAssets.GetId,
                resolveTextTokenId: textCatalog.GetTokenId,
                resolveEntityTemplateKey: mapLoader.EntityTemplateKeys.GetId,
                resolveEffectTemplateId: _ => 0,
                resolveMaterialId: materialAssets.GetId,
                resolveAnimatorControllerId: animatorControllers.GetId,
                resolveAnimationProfileId: animationProfiles.GetId,
                resolveBehaviorAssetId: (kind, key) => kind switch
                {
                    AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Spline or AssetKind.Surface
                        => meshAssets.GetId(key),
                    _ => throw new InvalidOperationException($"Unexpected asset {kind} '{key}'."),
                }).Load(catalog);

            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);
            var requests = new PresentationRequestBuffer(64);
            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            int defId = definitions.GetId("camera_acceptance_projection_cue_fixture");
            Assert.That(defId, Is.GreaterThan(0), "camera_acceptance_projection_cue_fixture presenter is required.");
            Entity owner = world.Create(
                new PresentationStableId { Value = 9101 },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = instances.Create(
                defId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                new Vector3(2f, 3f, 4f),
                9102,
                Entity.Null,
                default);
            PresenterDefinition definition = definitions.Get(defId);
            instances.SetParamDefault(definition, presenter);
            var presenterQuery = new QueryDescription().WithAll<PresenterState, PresenterWorldRotation, PresenterWorldScale>();
            world.Query(in presenterQuery, (ref PresenterState childState, ref PresenterWorldRotation childRot, ref PresenterWorldScale childScale) =>
            {
                childState.BehaviorActiveMask = uint.MaxValue;
                childRot.Value = Quaternion.Identity;
                childScale.Value = Vector3.One;
            });

            requests.Clear();
            emit.Update(0.016f);

            var kinds = new HashSet<AssetKind>();
            for (int i = 0; i < requests.Count; i++)
            {
                PresentationRequest request = requests.GetSpan()[i];
                if (request.Kind == PresentationRequestKind.VisualProxy)
                {
                    kinds.Add(request.VisualProxy.AssetKind);
                }
            }

            Assert.That(kinds, Does.Contain(AssetKind.Mesh));
            Assert.That(kinds, Does.Contain(AssetKind.Decal));
            Assert.That(kinds, Does.Contain(AssetKind.VFX));
            Assert.That(kinds, Does.Contain(AssetKind.Surface));

            modLoader.UnloadAll();
            world.Dispose();
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

                current = Path.GetDirectoryName(current) ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
