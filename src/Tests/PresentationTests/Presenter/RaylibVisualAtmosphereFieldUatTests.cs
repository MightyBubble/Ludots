using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Particles;
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
    public sealed class RaylibVisualAtmosphereFieldUatTests
    {
        private string _repoRoot = string.Empty;
        private string _tempRoot = string.Empty;
        private string _coreRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = FindRepoRoot();
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_AtmosphereFieldUat", Guid.NewGuid().ToString("N"));
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
        public void DecalFieldGallery_AuthorScaleAndTintSurviveEmit()
        {
            using var fixture = AtmosphereFieldFixture.Create(_repoRoot, _coreRoot);

            PresentationVisualProxy small = fixture.EmitVisual("raylib_visual_atmosphere_decal_size_small_actor");
            PresentationVisualProxy large = fixture.EmitVisual("raylib_visual_atmosphere_decal_size_large_actor");
            Assert.That(small.AssetKind, Is.EqualTo(AssetKind.Decal));
            Assert.That(large.AssetKind, Is.EqualTo(AssetKind.Decal));
            Assert.That(small.Scale.X, Is.EqualTo(1.6f).Within(0.001f));
            Assert.That(large.Scale.X, Is.EqualTo(7.2f).Within(0.001f));
            Assert.That(large.Scale.X, Is.GreaterThan(small.Scale.X * 3f));

            PresentationVisualProxy thin = fixture.EmitVisual("raylib_visual_atmosphere_decal_thickness_thin_actor");
            PresentationVisualProxy thick = fixture.EmitVisual("raylib_visual_atmosphere_decal_thickness_thick_actor");
            Assert.That(thin.Scale.Y, Is.EqualTo(0.28f).Within(0.001f));
            Assert.That(thick.Scale.Y, Is.EqualTo(3.2f).Within(0.001f));

            PresentationVisualProxy white = fixture.EmitVisual("raylib_visual_atmosphere_decal_tint_white_actor");
            PresentationVisualProxy red = fixture.EmitVisual("raylib_visual_atmosphere_decal_tint_red_actor");
            Assert.That(white.Color, Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
            Assert.That(red.Color.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(red.Color.Y, Is.EqualTo(0.12f).Within(0.001f));
            Assert.That(red.Color.Z, Is.EqualTo(0.08f).Within(0.001f));
        }

        [Test]
        public void DecalFieldGallery_PlacementYawIsAuthoredPerStamp()
        {
            string path = Path.Combine(
                _repoRoot,
                "mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/assets/Presentation/decal_placements.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement placements = document.RootElement.GetProperty("placements");
            float along = YawFor(placements, "raylib_visual_atmosphere_decal_yaw_along");
            float across = YawFor(placements, "raylib_visual_atmosphere_decal_yaw_across");
            Assert.That(along, Is.EqualTo(10f).Within(0.01f));
            Assert.That(across, Is.EqualTo(100f).Within(0.01f));
            Assert.That(MathF.Abs(across - along), Is.GreaterThan(45f));
        }

        [Test]
        public void SplineRibbonGallery_WidthFillBorderAndControlPointsSurviveEmit()
        {
            using var fixture = AtmosphereFieldFixture.Create(_repoRoot, _coreRoot);

            SplineRibbonRequest narrow = fixture.EmitSpline("raylib_visual_atmosphere_ribbon_narrow_actor");
            SplineRibbonRequest wide = fixture.EmitSpline("raylib_visual_atmosphere_ribbon_wide_actor");
            SplineRibbonRequest bordered = fixture.EmitSpline("raylib_visual_atmosphere_ribbon_bordered_actor");

            Assert.That(narrow.Width, Is.EqualTo(0.48f).Within(0.001f));
            Assert.That(wide.Width, Is.EqualTo(3.15f).Within(0.001f));
            Assert.That(wide.Width, Is.GreaterThan(narrow.Width * 4f));

            Assert.That(narrow.FillColor.X, Is.EqualTo(0.82f).Within(0.001f));
            Assert.That(bordered.FillColor.Z, Is.EqualTo(0.78f).Within(0.001f));
            Assert.That(narrow.BorderWidth, Is.EqualTo(0f).Within(0.001f));
            Assert.That(bordered.BorderWidth, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(bordered.BorderColor.W, Is.EqualTo(0.98f).Within(0.001f));

            Assert.That(bordered.P0.X, Is.EqualTo(147f).Within(0.001f));
            Assert.That(bordered.P3.X, Is.EqualTo(161f).Within(0.001f));
            Assert.That(Vector3.Distance(bordered.P0, bordered.P3), Is.GreaterThan(1f));
        }

        [Test]
        public void CueFlashGallery_UsesLeafCueMesh_AndScaleColorAreAuthored()
        {
            using var fixture = AtmosphereFieldFixture.Create(_repoRoot, _coreRoot);
            int cueMeshId = fixture.Meshes.GetId("cue_marker");
            Assert.That(cueMeshId, Is.GreaterThan(0));

            PresentationVisualProxy small = fixture.EmitVisual("raylib_visual_atmosphere_cue_small_green_actor");
            PresentationVisualProxy large = fixture.EmitVisual("raylib_visual_atmosphere_cue_large_yellow_actor");
            Assert.That(small.MeshAssetId, Is.EqualTo(cueMeshId));
            Assert.That(large.MeshAssetId, Is.EqualTo(cueMeshId));
            Assert.That(small.Scale.X, Is.EqualTo(0.28f).Within(0.001f));
            Assert.That(large.Scale.X, Is.EqualTo(1.15f).Within(0.001f));
            Assert.That(small.Color.Y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(large.Color.X, Is.EqualTo(1f).Within(0.001f));
            Assert.That(large.Color.Y, Is.EqualTo(0.88f).Within(0.001f));
        }

        [Test]
        public void TransientMarkerBuffer_LifetimeMustBeAuthoredPositive()
        {
            var buffer = new TransientMarkerBuffer(8);
            var meshes = new MeshAssetRegistry();
            int cubeId = meshes.GetId(WellKnownMeshKeys.Cube);
            Assert.That(
                () => buffer.TryAddMesh(cubeId, Vector3.Zero, Vector3.One, Vector4.One, 0f),
                Throws.InvalidOperationException.With.Message.Contains("lifetimeSeconds"));
            Assert.That(
                () => buffer.TryAddAnchoredMesh(cubeId, Vector3.One, Vector4.One, -0.2f, Entity.Null, Vector3.Zero),
                Throws.InvalidOperationException.With.Message.Contains("lifetimeSeconds"));
            var world = World.Create();
            try
            {
                Assert.That(
                    () => buffer.TickAndRequest(new PresentationRequestBuffer(8), 0f, world),
                    Throws.InvalidOperationException.With.Message.Contains("dt must be > 0"));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void AtmosphereRules_CreatePresenterNotLegacyPerformer()
        {
            string path = Path.Combine(
                _repoRoot,
                "mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/assets/Presentation/presenters.json");
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Not.Contain("CreatePerformer"));
            Assert.That(json, Does.Not.Contain("DestroyPerformerScope"));
            Assert.That(json, Does.Contain("CreatePresenter"));
        }

        private static float YawFor(JsonElement placements, string templateId)
        {
            foreach (JsonElement item in placements.EnumerateArray())
            {
                if (item.GetProperty("templateId").GetString() == templateId)
                {
                    return item.GetProperty("yawDeg").GetSingle();
                }
            }

            throw new InvalidOperationException($"Placement '{templateId}' missing yawDeg.");
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

        private sealed class AtmosphereFieldFixture : IDisposable
        {
            private readonly World _world;
            private readonly ModLoader _modLoader;
            private readonly PresenterDefinitionRegistry _definitions;
            private readonly PresenterEntityRuntime _instances;
            private readonly PresentationRequestBuffer _requests;
            private readonly PresenterEmitSystem _emit;
            private int _nextStable = 8000;

            public MeshAssetRegistry Meshes { get; }

            private AtmosphereFieldFixture(
                World world,
                ModLoader modLoader,
                MeshAssetRegistry meshes,
                PresenterDefinitionRegistry definitions,
                PresenterEntityRuntime instances,
                PresentationRequestBuffer requests,
                PresenterEmitSystem emit)
            {
                _world = world;
                _modLoader = modLoader;
                Meshes = meshes;
                _definitions = definitions;
                _instances = instances;
                _requests = requests;
                _emit = emit;
            }

            public static AtmosphereFieldFixture Create(string repoRoot, string coreRoot)
            {
                int ResolveVfxAssetId(string key, MeshAssetRegistry meshes)
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

                var world = World.Create();
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", coreRoot);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadMods(RepoModPaths.ResolveExplicit(repoRoot, new[]
                {
                    "LudotsCoreMod",
                    "RaylibVisualAtmosphereShowcaseMod",
                }));

                var pipeline = new ConfigPipeline(vfs, modLoader);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var mapLoader = new MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
                mapLoader.LoadTemplates(catalog);

                var particleVfx = new ParticleVfxRegistry();
                new ParticleVfxConfigLoader(pipeline, particleVfx).Load(catalog);
                var meshAssets = new MeshAssetRegistry();
                new MeshAssetConfigLoader(pipeline, meshAssets, particleVfx).Load(catalog);
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
                        AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.Spline
                            => meshAssets.GetId(key),
                        AssetKind.VFX => ResolveVfxAssetId(key, meshAssets),
                        _ => throw new InvalidOperationException($"Unexpected asset {kind} '{key}'."),
                    }).Load(catalog);

                var instances = new PresenterEntityRuntime(world);
                var requests = new PresentationRequestBuffer(64);
                var emit = new PresenterEmitSystem(
                    world,
                    instances,
                    definitions,
                    requests,
                    new Dictionary<string, object>(),
                    animatorStates: null!,
                    soundRequests: null!);

                return new AtmosphereFieldFixture(world, modLoader, meshAssets, definitions, instances, requests, emit);
            }

            public PresentationVisualProxy EmitVisual(string definitionId)
            {
                _ = Emit(definitionId);
                PresentationRequest? last = null;
                for (int i = 0; i < _requests.Count; i++)
                {
                    PresentationRequest request = _requests.GetSpan()[i];
                    if (request.Kind == PresentationRequestKind.VisualProxy)
                    {
                        last = request;
                    }
                }

                if (last == null)
                {
                    throw new InvalidOperationException($"{definitionId} did not emit a visual proxy.");
                }

                return last.Value.VisualProxy;
            }

            public SplineRibbonRequest EmitSpline(string definitionId)
            {
                _ = Emit(definitionId);
                PresentationRequest? last = null;
                for (int i = 0; i < _requests.Count; i++)
                {
                    PresentationRequest request = _requests.GetSpan()[i];
                    if (request.Kind == PresentationRequestKind.SplineRibbon)
                    {
                        last = request;
                    }
                }

                if (last == null)
                {
                    throw new InvalidOperationException($"{definitionId} did not emit a spline ribbon request.");
                }

                return last.Value.SplineRibbon;
            }

            private PresentationRequest Emit(string definitionId)
            {
                int defId = _definitions.GetId(definitionId);
                Assert.That(defId, Is.GreaterThan(0), definitionId);
                Entity owner = _world.Create(
                    new PresentationStableId { Value = _nextStable++ },
                    VisualTransform.Default,
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                Entity presenter = _instances.Create(
                    defId,
                    owner,
                    0,
                    PresentationAnchorKind.WorldPosition,
                    new Vector3(2f, 3f, 4f),
                    _nextStable++,
                    Entity.Null,
                    default);
                PresenterDefinition definition = _definitions.Get(defId);
                _instances.SetParamDefault(definition, presenter);
                ref var state = ref _world.Get<PresenterState>(presenter);
                state.BehaviorActiveMask = uint.MaxValue;
                ref var rot = ref _world.Get<PresenterWorldRotation>(presenter);
                rot.Value = Quaternion.Identity;
                ref var scale = ref _world.Get<PresenterWorldScale>(presenter);
                scale.Value = Vector3.One;

                _requests.Clear();
                _emit.Update(0.016f);
                Assert.That(_requests.Count, Is.GreaterThan(0), $"{definitionId} emitted nothing.");
                return _requests.GetSpan()[0];
            }

            public void Dispose()
            {
                _emit.Dispose();
                _modLoader.UnloadAll();
                _world.Dispose();
            }
        }
    }
}
