using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Formal ConfigPipeline/GameEngine validation contracts for the presentation VFX
    /// showcase configs: StaticPresenter30k smoke, Raylib Visual Atmosphere blend-mode
    /// glows, and the Raylib VFX Forge. VFX mesh assets must resolve particleVfxId data
    /// through the strict load chain, and game.json must stay on presenter-era keys.
    /// </summary>
    [TestFixture]
    public sealed class VfxShowcaseConfigContractTests
    {
        private string _repoRoot = string.Empty;
        private string _tempRoot = string.Empty;
        private string _coreRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = FindRepoRoot();
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_VfxShowcaseConfigContracts", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_tempRoot, "Core");
            Directory.CreateDirectory(_coreRoot);
            File.WriteAllText(
                Path.Combine(_coreRoot, "config_catalog.json"),
                File.ReadAllText(Path.Combine(_repoRoot, "assets", "config_catalog.json")));
            File.WriteAllText(
                Path.Combine(_coreRoot, "game.json"),
                File.ReadAllText(Path.Combine(_repoRoot, "assets", "game.json")));
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
            AttributeRegistry.Clear();
            AttributeRegistry.Register("Health");
            AttributeRegistry.Register("Durability");
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
        public void StaticPresenter30kShowcase_VfxMeshAssetsResolveThroughFormalPipeline()
        {
            using var fixture = ShowcaseConfigFixture.Create(
                _repoRoot,
                _coreRoot,
                new[]
                {
                    ModMount("LudotsCoreMod"),
                    ModMount("CoreInputMod"),
                    ModMount("CapabilityStandardStaticPresenter30kMod", "showcases", "capability_standard"),
                });

            fixture.Config.Presentation.Validate();

            AssertVfxAsset(fixture, "capability_static_presenter.smoke.billboard", "capability_static_performer.smoke");
            Assert.That(fixture.Definitions.GetId("capability_static_presenter_chimney_smoke_vfx"), Is.GreaterThan(0));
        }

        [Test]
        public void RaylibVisualAtmosphereShowcase_VfxMeshAssetsResolveThroughFormalPipeline()
        {
            using var fixture = ShowcaseConfigFixture.Create(
                _repoRoot,
                _coreRoot,
                new[]
                {
                    ModMount("LudotsCoreMod"),
                    ModMount("RaylibVisualAtmosphereShowcaseMod", "showcases", "raylib_visual_atmosphere"),
                });

            fixture.Config.Presentation.Validate();

            AssertVfxAsset(fixture, "raylib_visual_atmosphere.vfx_blend", "raylib_visual_atmosphere.vfx_blend");
            AssertVfxAsset(fixture, "raylib_visual_atmosphere.vfx_additive", "raylib_visual_atmosphere.vfx_additive");
            Assert.That(fixture.Definitions.GetId("raylib_visual_atmosphere_vfx_blend_actor"), Is.GreaterThan(0));
            Assert.That(fixture.Definitions.GetId("raylib_visual_atmosphere_vfx_additive_actor"), Is.GreaterThan(0));
        }

        [Test]
        public void VfxForgeShowcase_MergesPresenterEraCapacitiesAndResolvesVfxAssets()
        {
            using var fixture = ShowcaseConfigFixture.Create(
                _repoRoot,
                _coreRoot,
                new[]
                {
                    ModMount("LudotsCoreMod"),
                    ModMount("VfxForgeRaylibShowcaseMod", "showcases", "vfx_forge_raylib"),
                });

            fixture.Config.Presentation.Validate();
            Assert.That(
                fixture.Config.Presentation.PresenterInstanceCapacity,
                Is.EqualTo(4096),
                "VFX Forge must author the presenter-era presenterInstanceCapacity override.");
            Assert.That(
                fixture.Config.Presentation.PresenterCommandCapacity,
                Is.EqualTo(8192),
                "VFX Forge must author the presenter-era presenterCommandCapacity override.");

            string[] effectAssets =
            {
                "vfx_forge.spark_column.effect",
                "vfx_forge.energy_orbit.effect",
                "vfx_forge.trail_arc.effect",
                "vfx_forge.ember_rain.effect",
                "vfx_forge.shield_dome.effect",
                "vfx_forge.gravity_well.effect",
                "vfx_forge.flame_flipbook.effect",
                "vfx_forge.smoke_flipbook.effect",
                "vfx_forge.stretched_sparks.effect",
            };
            string[] particleVfxKeys =
            {
                "vfx_forge.spark_column",
                "vfx_forge.energy_orbit",
                "vfx_forge.trail_arc",
                "vfx_forge.ember_rain",
                "vfx_forge.shield_dome",
                "vfx_forge.gravity_well",
                "vfx_forge.flame_flipbook",
                "vfx_forge.smoke_flipbook",
                "vfx_forge.stretched_sparks",
            };
            for (int i = 0; i < effectAssets.Length; i++)
            {
                AssertVfxAsset(fixture, effectAssets[i], particleVfxKeys[i]);
            }
        }

        [Test]
        public void VfxForgeShowcase_AuthoringHasNoDuplicatePresenterIds()
        {
            string path = Path.Combine(
                _repoRoot,
                "mods",
                "showcases",
                "vfx_forge_raylib",
                "VfxForgeRaylibShowcaseMod",
                "assets",
                "Presentation",
                "presenters.json");
            var array = JsonNode.Parse(File.ReadAllText(path))?.AsArray()
                ?? throw new InvalidOperationException("VFX Forge presenters.json must be an array.");
            var ids = array
                .Select(node => node?["id"]?.GetValue<string>())
                .Where(id => id != null)
                .ToList();
            var duplicates = ids
                .GroupBy(id => id)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            Assert.That(
                duplicates,
                Is.Empty,
                "Duplicate presenter ids are silently overwritten by PresenterDefinitionRegistry; authored config must stay unique.");
        }

        private static (string ModId, string RootPath) ModMount(
            string modId,
            params string[] relativeSegments)
        {
            var segments = new List<string> { "mods" };
            segments.AddRange(relativeSegments);
            segments.Add(modId);
            return (modId, Path.Combine(segments.ToArray()));
        }

        private static void AssertVfxAsset(
            ShowcaseConfigFixture fixture,
            string meshAssetKey,
            string particleVfxKey)
        {
            int meshId = fixture.Meshes.GetId(meshAssetKey);
            Assert.That(meshId, Is.GreaterThan(0), $"Mesh asset '{meshAssetKey}' should be registered.");
            Assert.That(fixture.Meshes.TryGetDescriptor(meshId, out MeshAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.VfxData.IsValid, Is.True, $"Mesh asset '{meshAssetKey}' must declare VFX particle data.");
            int particleId = fixture.Particles.GetId(particleVfxKey);
            Assert.That(particleId, Is.GreaterThan(0), $"Particle VFX '{particleVfxKey}' should be registered.");
            Assert.That(descriptor.VfxData.ParticleVfxAssetId, Is.EqualTo(particleId));
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "mods")) &&
                    File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        /// <summary>
        /// Runs the strict GameEngine presentation load chain (merged game.json,
        /// particle VFX, mesh assets, materials, animators, presenters) against the
        /// authored mod configs without requiring the showcase code assemblies.
        /// </summary>
        private sealed class ShowcaseConfigFixture : IDisposable
        {
            private readonly World _world;
            private readonly ModLoader _modLoader;

            public GameConfig Config { get; }
            public MeshAssetRegistry Meshes { get; }
            public ParticleVfxRegistry Particles { get; }
            public PresenterDefinitionRegistry Definitions { get; }

            private ShowcaseConfigFixture(
                World world,
                ModLoader modLoader,
                GameConfig config,
                MeshAssetRegistry meshes,
                ParticleVfxRegistry particles,
                PresenterDefinitionRegistry definitions)
            {
                _world = world;
                _modLoader = modLoader;
                Config = config;
                Meshes = meshes;
                Particles = particles;
                Definitions = definitions;
            }

            public static ShowcaseConfigFixture Create(
                string repoRoot,
                string coreRoot,
                IReadOnlyList<(string ModId, string RootPath)> mods)
            {
                var world = World.Create();
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", coreRoot);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                for (int i = 0; i < mods.Count; i++)
                {
                    string modRoot = Path.Combine(repoRoot, mods[i].RootPath);
                    vfs.Mount(mods[i].ModId, modRoot);
                    modLoader.LoadedModIds.Add(mods[i].ModId);
                }

                var pipeline = new ConfigPipeline(vfs, modLoader);
                GameConfig config = pipeline.MergeGameConfig();
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var mapLoader = new MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
                mapLoader.LoadTemplates(catalog);

                var particles = new ParticleVfxRegistry();
                new ParticleVfxConfigLoader(pipeline, particles).Load(catalog);
                var meshes = new MeshAssetRegistry();
                new MeshAssetConfigLoader(pipeline, meshes, particles).Load(catalog);
                var materials = new PresentationMaterialRegistry();
                new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);
                var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
                var animatorControllers = new AnimatorControllerRegistry();
                new AnimatorControllerConfigLoader(pipeline, animatorControllers).Load(catalog);
                var animationClips = new AnimationClipRegistry();
                new AnimationClipConfigLoader(pipeline, animationClips).Load(catalog);
                var animationProfiles = new AnimationProfileRegistry();
                new AnimationProfileConfigLoader(pipeline, animationProfiles, animatorControllers, animationClips).Load(catalog);

                var entityCollectionKeys = new StringIntRegistry(
                    capacity: 64,
                    startId: 1,
                    invalidId: 0,
                    comparer: StringComparer.Ordinal);
                RegisterBuiltInEntityCollectionKeys(entityCollectionKeys);

                var definitions = new PresenterDefinitionRegistry();
                new PresenterDefinitionConfigLoader(
                    pipeline,
                    definitions,
                    resolveAttributeName: AttributeRegistry.GetId,
                    resolveMeshId: meshes.GetId,
                    resolveTextTokenId: textCatalog.GetTokenId,
                    resolveEntityTemplateKey: mapLoader.EntityTemplateKeys.GetId,
                    resolveEffectTemplateId: _ => 0,
                    resolveMaterialId: materials.GetId,
                    resolveAnimatorControllerId: animatorControllers.GetId,
                    resolveAnimationProfileId: animationProfiles.GetId,
                    resolveBehaviorAssetId: (kind, key) => kind switch
                    {
                        AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.Spline
                            => meshes.GetId(key),
                        AssetKind.VFX => ResolveVfxAssetId(key, meshes),
                        AssetKind.Sound => meshes.GetId(key),
                        AssetKind.GroundOverlay => ResolveGroundOverlayShapeId(key),
                        _ => 0,
                    },
                    resolveInstancedBatchAssetId: _ => 0,
                    resolveEntityCollectionKeyId: entityCollectionKeys.Register).Load(catalog);
                definitions.RebuildCompiledViews();

                return new ShowcaseConfigFixture(world, modLoader, config, meshes, particles, definitions);
            }

            private static int ResolveVfxAssetId(string key, MeshAssetRegistry meshes)
            {
                int assetId = meshes.GetId(key);
                if (assetId <= 0)
                {
                    return 0;
                }

                if (!meshes.TryGetDescriptor(assetId, out MeshAssetDescriptor descriptor) ||
                    !descriptor.VfxData.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Presenter behavior VFX asset '{key}' must declare VFX particle data.");
                }

                return assetId;
            }

            private static int ResolveGroundOverlayShapeId(string key)
            {
                if (Enum.TryParse<GroundOverlayShape>(key, ignoreCase: false, out var shape))
                {
                    return (int)shape;
                }

                throw new InvalidOperationException(
                    $"GroundOverlay AssetBinding references unknown shape '{key}'.");
            }

            private static void RegisterBuiltInEntityCollectionKeys(StringIntRegistry registry)
            {
                registry.Register(EntityCollectionKeys.UiCommandAcquisition);
                registry.Register(EntityCollectionKeys.HoveredEntity);
                registry.Register(EntityCollectionKeys.AbilityAimHover);
                registry.Register(EntityCollectionKeys.AbilityAimAffected);
                registry.Register(EntityCollectionKeys.EntityInfoExplicit);
                registry.Register(EntityCollectionKeys.CommandSource);
                registry.Register(EntityCollectionKeys.UiCastRaw);
                registry.Register(EntityViewKeys.ControlPlaneCommand);
                registry.Register(EntityViewKeys.CommandDeckFiltered);
            }

            public void Dispose()
            {
                _modLoader.UnloadAll();
                _world.Dispose();
            }
        }
    }
}
