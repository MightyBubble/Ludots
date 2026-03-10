using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using CameraAcceptanceMod;
using CameraProfilesMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;
using VirtualCameraShotsMod;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class CameraCapabilityModTests
    {
        private const string ViewModeManagerGlobalKey = "CoreInputMod.ViewModeManager";
        private const string ActiveViewModeIdGlobalKey = "CoreInputMod.ActiveViewModeId";
        private const int BlendSettleFrames = 20;

        private static readonly string[] CameraCapabilityMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "CameraBootstrapMod",
            "VirtualCameraShotsMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void CameraAcceptanceMod_EntryMap_ActivatesIntroFocusShot()
        {
            using var engine = CreateEngine(CameraCapabilityMods);
            LoadMap(engine, CameraAcceptanceIds.EntryMapId);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null, "VirtualCameraRegistry should be available from GameEngine services.");
            Assert.That(registry!.TryGet(VirtualCameraShotIds.IntroFocus, out var definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo(VirtualCameraShotIds.IntroFocus));

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.True);
            Assert.That(brain.ActiveCameraId, Is.EqualTo(VirtualCameraShotIds.IntroFocus));
            Assert.That(brain.IsActive(CameraProfileIds.FollowCamera), Is.True);
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(6400f, 3200f)));
        }

        [Test]
        public void CameraAcceptanceMod_ClearRequest_RestoresFollowCameraAndHeroTarget()
        {
            using var engine = CreateEngine(CameraCapabilityMods);
            LoadMap(engine, CameraAcceptanceIds.EntryMapId);

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Clear = true
            });
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraProfileIds.FollowCamera);

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.True);
            Assert.That(brain.ActiveCameraId, Is.EqualTo(CameraProfileIds.FollowCamera));
            Assert.That(brain.IsActive(VirtualCameraShotIds.IntroFocus), Is.False);

            Tick(engine, frames: BlendSettleFrames);

            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.Get<WorldPositionCm>(hero).Value.ToVector2(), Is.EqualTo(new Vector2(1200f, 800f)));
        }

        [Test]
        public void CameraProfilesMod_ViewModeSwitch_AppliesVirtualCameraAuthority()
        {
            using var engine = CreateEngine(CameraCapabilityMods);
            LoadMap(engine, CameraAcceptanceIds.EntryMapId);

            var manager = engine.GlobalContext.TryGetValue(ViewModeManagerGlobalKey, out var managerObj)
                ? managerObj
                : null;
            Assert.That(manager, Is.Not.Null, "ViewModeManager should be registered by CoreInputMod.");
            Assert.That(GetActiveMode(manager!), Is.Null, "Shared camera profiles should register without forcing an initial mode.");

            Assert.That(SwitchViewMode(manager!, CameraProfileIds.TacticalMode), Is.True);
            Tick(engine, frames: 1);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.HasActiveCamera, Is.True);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(VirtualCameraShotIds.IntroFocus));
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.IsActive(CameraProfileIds.TacticalCamera), Is.True);
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.GlobalContext[ActiveViewModeIdGlobalKey], Is.EqualTo(CameraProfileIds.TacticalMode));

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Clear = true
            });
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraProfileIds.TacticalCamera);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraProfileIds.TacticalCamera));

            Tick(engine, frames: BlendSettleFrames);
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);

            Assert.That(SwitchViewMode(manager!, CameraProfileIds.FollowMode), Is.True);
            Tick(engine, frames: BlendSettleFrames);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraProfileIds.FollowCamera));
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);

            Assert.That(SwitchViewMode(manager!, CameraProfileIds.InspectMode), Is.True);
            Tick(engine, frames: BlendSettleFrames);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraProfileIds.InspectCamera));
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);
            Assert.That(engine.GlobalContext[ActiveViewModeIdGlobalKey], Is.EqualTo(CameraProfileIds.InspectMode));
        }

        [Test]
        public void VirtualCameraShotsMod_Loads_All_ShotTemplates_And_AllowsActivation()
        {
            using var engine = CreateEngine("LudotsCoreMod", "VirtualCameraShotsMod");
            LoadMap(engine, engine.MergedConfig.StartupMapId);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry!.TryGet(VirtualCameraShotIds.IntroFocus, out var introFocus), Is.True);
            Assert.That(registry.TryGet(VirtualCameraShotIds.SelectionLock, out var selectionLock), Is.True);
            Assert.That(registry.TryGet(VirtualCameraShotIds.InspectSweep, out var inspectSweep), Is.True);
            Assert.That(introFocus.Id, Is.EqualTo(VirtualCameraShotIds.IntroFocus));
            Assert.That(selectionLock.Id, Is.EqualTo(VirtualCameraShotIds.SelectionLock));
            Assert.That(inspectSweep.Id, Is.EqualTo(VirtualCameraShotIds.InspectSweep));

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = VirtualCameraShotIds.InspectSweep,
                BlendDurationSeconds = 0f
            });
            Tick(engine, frames: 1);

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.True);
            Assert.That(brain.ActiveCameraId, Is.EqualTo(VirtualCameraShotIds.InspectSweep));
            Assert.That(brain.AllowsInput, Is.True);
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(3200f, 2000f)));
        }

        [Test]
        public void CameraBootstrapMod_TaggedMap_CentersEntityBounds()
        {
            using var engine = CreateEngine(CameraCapabilityMods);
            LoadMap(engine, CameraAcceptanceIds.BootstrapMapId);

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.HasActiveCamera, Is.True);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraProfileIds.TacticalCamera));
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(3000f, 2000f)));
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(5400f));
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Tick(engine, frames);
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

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames = 30)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                engine.Tick(1f / 60f);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static object? GetActiveMode(object manager)
        {
            return manager.GetType().GetProperty("ActiveMode", BindingFlags.Instance | BindingFlags.Public)?.GetValue(manager);
        }

        private static bool SwitchViewMode(object manager, string modeId)
        {
            var switchTo = manager.GetType().GetMethod("SwitchTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null)
                ?? throw new InvalidOperationException("ViewModeManager.SwitchTo(string) was not found.");
            return switchTo.Invoke(manager, new object[] { modeId }) is bool value && value;
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
