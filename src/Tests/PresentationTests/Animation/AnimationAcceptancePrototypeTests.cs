using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AnimationAcceptanceMod;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class AnimationAcceptancePrototypeTests
    {
        private static readonly string[] PrototypeMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "AnimationAcceptanceMod"
        };

        [Test]
        public void AnimationAcceptanceMap_EmitsLayeredSkinnedSnapshot_ForTankAndHumanoid()
        {
            using var engine = CreateEngine(PrototypeMods);
            LoadMap(engine, AnimationAcceptanceIds.StartupMapId, frames: 12);

            PrimitiveDrawBuffer? snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
            SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            AnimationProfileRegistry? profileRegistry = engine.GetService(CoreServiceKeys.AnimationProfileRegistry);
            AnimationClipRegistry? clipRegistry = engine.GetService(CoreServiceKeys.AnimationClipRegistry);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(skinnedBatch, Is.Not.Null);
            Assert.That(profileRegistry, Is.Not.Null);
            Assert.That(clipRegistry, Is.Not.Null);

            int locomotionId = AnimationChannelRegistry.Register(AnimationChannelRegistry.Locomotion);
            int aimYawId = AnimationChannelRegistry.Register(AnimationChannelRegistry.AimYaw);
            int recoilId = AnimationChannelRegistry.Register(AnimationChannelRegistry.Recoil);

            int tankCount = 0;
            int humanoidCount = 0;
            int staticCount = 0;
            var traceLines = new List<string>();

            foreach (ref readonly var item in snapshot!.GetSpan())
            {
                if (item.RenderPath.IsSkinnedLane())
                {
                    Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0));
                    Assert.That(item.StableId, Is.GreaterThan(0));
                    Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
                    Assert.That(item.AnimationProfileId, Is.GreaterThan(0));
                    Assert.That(item.AnimationOverlay.BaseClip.ChannelId, Is.EqualTo(locomotionId));
                    Assert.That(item.AnimationOverlay.LayerClip.ChannelId, Is.EqualTo(aimYawId));
                    Assert.That(item.AnimationOverlay.OverlayClip.ChannelId, Is.EqualTo(recoilId));

                    Assert.That(
                        profileRegistry!.TryResolveStateClipId(item.AnimationProfileId, item.Animator.GetPrimaryStateIndex(), out int stateClipAssetId),
                        Is.True);

                    bool isTank = item.Animator.GetPrimaryStateIndex() is >= 31 and <= 33;
                    int baseClipAssetId = clipRegistry!.GetId(isTank
                        ? AnimationAcceptanceIds.TankLocomotionClipKey
                        : AnimationAcceptanceIds.HumanoidLocomotionClipKey);
                    int layerClipAssetId = clipRegistry.GetId(isTank
                        ? AnimationAcceptanceIds.TankAimClipKey
                        : AnimationAcceptanceIds.HumanoidAimClipKey);
                    int overlayClipAssetId = clipRegistry.GetId(isTank
                        ? AnimationAcceptanceIds.TankRecoilClipKey
                        : AnimationAcceptanceIds.HumanoidRecoilClipKey);
                    Assert.That(baseClipAssetId, Is.GreaterThan(0));
                    Assert.That(layerClipAssetId, Is.GreaterThan(0));
                    Assert.That(overlayClipAssetId, Is.GreaterThan(0));

                    Assert.That(
                        clipRegistry!.TryResolveLocator(stateClipAssetId, AnimationAcceptanceIds.RaylibBackendId, out var stateRaylibLocator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(stateClipAssetId, AnimationAcceptanceIds.Ue5BackendId, out var stateUe5Locator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(baseClipAssetId, AnimationAcceptanceIds.RaylibBackendId, out var baseRaylibLocator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(baseClipAssetId, AnimationAcceptanceIds.Ue5BackendId, out var baseUe5Locator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(layerClipAssetId, AnimationAcceptanceIds.RaylibBackendId, out var layerRaylibLocator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(layerClipAssetId, AnimationAcceptanceIds.Ue5BackendId, out var layerUe5Locator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(overlayClipAssetId, AnimationAcceptanceIds.RaylibBackendId, out var overlayRaylibLocator),
                        Is.True);
                    Assert.That(
                        clipRegistry.TryResolveLocator(overlayClipAssetId, AnimationAcceptanceIds.Ue5BackendId, out var overlayUe5Locator),
                        Is.True);

                    if (item.Animator.GetPrimaryStateIndex() is >= 31 and <= 33)
                    {
                        tankCount++;
                        Assert.That(item.AnimationProfileId, Is.EqualTo(profileRegistry.GetId(AnimationAcceptanceIds.TankProfileKey)));
                        Assert.That(item.AnimationOverlay.LayerClip.Weight01, Is.EqualTo(1f).Within(0.001f));
                        Assert.That(MathF.Abs(item.AnimationOverlay.LayerClip.Scalar0), Is.GreaterThan(0.01f));
                    }
                    else if (item.Animator.GetPrimaryStateIndex() is >= 41 and <= 44)
                    {
                        humanoidCount++;
                        Assert.That(item.AnimationProfileId, Is.EqualTo(profileRegistry.GetId(AnimationAcceptanceIds.HumanoidProfileKey)));
                        Assert.That(item.AnimationOverlay.LayerClip.Weight01, Is.GreaterThan(0.1f));
                        Assert.That(MathF.Abs(item.AnimationOverlay.LayerClip.Scalar0), Is.GreaterThan(0.01f));
                    }

                    traceLines.Add(JsonSerializer.Serialize(new
                    {
                        stable_id = item.StableId,
                        template_id = item.TemplateId,
                        profile_id = item.AnimationProfileId,
                        lane = item.RenderPath.ToString(),
                        controller_id = item.Animator.GetControllerId(),
                        primary_state = item.Animator.GetPrimaryStateIndex(),
                        state_clip_asset = clipRegistry.GetName(stateClipAssetId),
                        state_raylib_asset = stateRaylibLocator.AssetRef,
                        state_ue5_asset = stateUe5Locator.AssetRef,
                        base_clip = AnimationChannelRegistry.GetName(item.AnimationOverlay.BaseClip.ChannelId),
                        base_clip_asset = clipRegistry.GetName(baseClipAssetId),
                        base_raylib_asset = baseRaylibLocator.AssetRef,
                        base_ue5_asset = baseUe5Locator.AssetRef,
                        base_time = item.AnimationOverlay.BaseClip.NormalizedTime01,
                        base_weight = item.AnimationOverlay.BaseClip.Weight01,
                        layer_clip = AnimationChannelRegistry.GetName(item.AnimationOverlay.LayerClip.ChannelId),
                        layer_clip_asset = clipRegistry.GetName(layerClipAssetId),
                        layer_raylib_asset = layerRaylibLocator.AssetRef,
                        layer_ue5_asset = layerUe5Locator.AssetRef,
                        layer_weight = item.AnimationOverlay.LayerClip.Weight01,
                        layer_scalar0 = item.AnimationOverlay.LayerClip.Scalar0,
                        overlay_clip = AnimationChannelRegistry.GetName(item.AnimationOverlay.OverlayClip.ChannelId),
                        overlay_clip_asset = clipRegistry.GetName(overlayClipAssetId),
                        overlay_raylib_asset = overlayRaylibLocator.AssetRef,
                        overlay_ue5_asset = overlayUe5Locator.AssetRef,
                        overlay_weight = item.AnimationOverlay.OverlayClip.Weight01,
                        overlay_time = item.AnimationOverlay.OverlayClip.NormalizedTime01,
                    }));
                }
                else if (item.RenderPath.IsStaticInstanceLane())
                {
                    staticCount++;
                    Assert.That(item.AnimationProfileId, Is.EqualTo(0));
                }
            }

            Assert.That(tankCount, Is.EqualTo(1), "Exactly one tank prototype should be visible in the acceptance map.");
            Assert.That(humanoidCount, Is.EqualTo(1), "Exactly one humanoid upper-body prototype should be visible in the acceptance map.");
            Assert.That(staticCount, Is.EqualTo(1), "Acceptance map should keep one static baseline entity for lane separation.");
            Assert.That(skinnedBatch!.Count, Is.EqualTo(2), "Skinned visual batch contract should contain the tank and humanoid once each.");
            foreach (ref readonly var item in skinnedBatch.GetSpan())
            {
                Assert.That(item.AnimationProfileId, Is.GreaterThan(0));
            }

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "animation-layered-prototypes");
            Directory.CreateDirectory(artifactDir);

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), string.Join(Environment.NewLine, traceLines));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(tankCount, humanoidCount, staticCount));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathArtifact());
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames)
        {
            engine.LoadMap(mapId);
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static string BuildBattleReport(int tankCount, int humanoidCount, int staticCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: animation-layered-prototypes");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario name: raylib layered tank + humanoid prototype acceptance");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic fixture / animation_acceptance_entry / 12 ticks @ 60 Hz");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine("- [T+012] Tank prototype resolves profile -> state clip -> raylib/ue5 locators for the vehicle surrogate.");
            sb.AppendLine("- [T+012] Humanoid prototype resolves the same chain for layered locomotion + aim + recoil.");
            sb.AppendLine("- [T+012] Static baseline entity remains on static lane with profile id 0.");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success/failure decision: success");
            sb.AppendLine("- failed assertions: none");
            sb.AppendLine("- reason codes: layered_tank_visible, layered_humanoid_visible, static_lane_separate, profile_locator_chain_valid");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- total actions: {tankCount + humanoidCount + staticCount}");
            sb.AppendLine($"- layered tank visuals: {tankCount}");
            sb.AppendLine($"- layered humanoid visuals: {humanoidCount}");
            sb.AppendLine($"- static baseline visuals: {staticCount}");
            return sb.ToString();
        }

        private static string BuildPathArtifact()
        {
            return
                """
                flowchart TD
                    A[start animation acceptance map] --> B[tick prototype driver system]
                    B --> C[tank base locomotion updates packed state]
                    B --> D[humanoid base locomotion updates packed state]
                    C --> E[tank profile resolves packed state clip assets]
                    D --> F[humanoid profile resolves packed state clip assets]
                    E --> G[snapshot emits skinned tank prototype item]
                    F --> H[snapshot emits skinned humanoid prototype item]
                    A --> I[static baseline remains on static lane]
                    B --> M[named overlay channels locomotion / aim_yaw / recoil]
                    E --> J{clip locator exists for raylib and ue5}
                    F --> K{clip locator exists for raylib and ue5}
                    J -->|no| L[fail acceptance]
                    K -->|no| L
                """;
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

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
