using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using CameraShowcaseMod;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Client;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.ThreeC.Acceptance
{
    [TestFixture]
    public sealed class CameraShowcaseModTests
    {
        private const int BlendSettleFrames = 20;
        private const string TestInputBackendKey = "Tests.CameraShowcaseMod.InputBackend";
        private static readonly string[] CoreInputMods =
        {
            "LudotsCoreMod",
            "CoreInputMod"
        };

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "CameraBootstrapMod",
            "VirtualCameraShotsMod",
            "CameraShowcaseMod"
        };

        [Test]
        public void CoreInputMod_DefaultGameplay_ProvidesGenericCommandSourceAndViewModeActions()
        {
            using var engine = CreateEngine(CoreInputMods);

            var input = engine.GetService(CoreServiceKeys.InputHandler);
            Assert.That(input, Is.Not.Null);
            Assert.That(input!.HasContext("Default_Gameplay"), Is.True);
            Assert.That(input.HasAction("CommandSourceAcquire"), Is.True);
            Assert.That(input.HasAction("Command"), Is.True);
            Assert.That(input.HasAction("Cancel"), Is.True);
            Assert.That(input.HasAction("ViewModeNext"), Is.True);
            Assert.That(input.HasAction("ViewModePrev"), Is.True);
            Assert.That(input.HasAction("TabTarget"), Is.True);
        }

        [Test]
        public void CameraShowcaseMod_CommandSourceFollowMode_TracksCommandSourcePrimaryWithoutFallback()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.HubMapId);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry!.TryGet(CameraShowcaseIds.CommandSourceFollowProfileId, out var commandSourceProfile), Is.True);
            Assert.That(registry.TryGet(CameraShowcaseIds.RevealShotId, out var revealShot), Is.True);
            Assert.That(commandSourceProfile.Id, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));
            Assert.That(revealShot.Id, Is.EqualTo(CameraShowcaseIds.RevealShotId));

            Assert.That(engine.GlobalContext.TryGetValue("CoreInputMod.ViewModeManager", out var managerObj), Is.True);
            Assert.That(managerObj, Is.Not.Null);

            Assert.That(SwitchViewMode(managerObj!, CameraShowcaseIds.CommandSourceFollowModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            var brain = engine.AuthorityCamera().VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));
            Assert.That(engine.AuthorityCamera().State.IsFollowing, Is.False);
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.Null);
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(1200f, 800f)));

            Entity captain = FindEntityByName(engine.World, CameraShowcaseIds.CaptainName);
            Assert.That(captain, Is.Not.EqualTo(Entity.Null));

            SetCommandSource(engine, captain);
            Tick(engine, 3);
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.EqualTo(new Vector2(3200f, 2000f)));
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(3200f, 2000f)));

            ClearCommandSource(engine);
            Tick(engine, 3);
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.Null);
            Assert.That(engine.AuthorityCamera().State.IsFollowing, Is.False);
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(3200f, 2000f)));
        }

        [Test]
        public void CameraShowcaseMod_CommandSourceFollowHotkey_IsOnlyActiveOnShowcaseMaps()
        {
            using var engine = CreateEngine(ShowcaseMods);
            var input = engine.GetService(CoreServiceKeys.InputHandler);
            Assert.That(input, Is.Not.Null);
            var backend = GetInputBackend(engine);

            LoadMap(engine, "entry");
            PressButton(engine, backend, "<Keyboard>/f4");
            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.Not.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));

            LoadMap(engine, CameraShowcaseIds.HubMapId);
            PressButton(engine, backend, "<Keyboard>/f4");
            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));
        }

        [Test]
        public void CameraShowcaseMod_LeavingShowcaseMap_ClearsCommandSourceFollowModeOwnership()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.HubMapId);

            Assert.That(engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj), Is.True);
            Assert.That(SwitchViewMode(managerObj!, CameraShowcaseIds.CommandSourceFollowModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));

            LoadMap(engine, "entry");

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.Not.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));
            Assert.That(engine.GlobalContext.ContainsKey(ViewModeManager.ActiveModeIdKey), Is.False);
        }

        [Test]
        public void CameraShowcaseMod_StackMap_TaggedRevealShot_FallsBackToFollowProfile()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.StackMapId);
            Tick(engine, BlendSettleFrames);

            var brain = engine.AuthorityCamera().VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.True);
            Assert.That(brain.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.RevealShotId));
            Assert.That(brain.IsActive(CameraShowcaseIds.FollowProfileId), Is.True);
            Assert.That(engine.AuthorityCamera().State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(3200f, 2000f)));

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Clear = true
            });
            TickUntil(engine, () => engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId == CameraShowcaseIds.FollowProfileId);
            Tick(engine, BlendSettleFrames);

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.FollowProfileId));
            Assert.That(engine.AuthorityCamera().State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
        }

        [Test]
        public void CameraShowcaseMod_CommandSourceFollowMap_DefaultProfile_FollowsCommandSourceWithoutFallback()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.CommandSourceFollowMapId);

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.Null);
            Assert.That(engine.AuthorityCamera().State.IsFollowing, Is.False);
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(1200f, 800f)));

            Entity captain = FindEntityByName(engine.World, CameraShowcaseIds.CaptainName);
            Assert.That(captain, Is.Not.EqualTo(Entity.Null));

            SetCommandSource(engine, captain);
            TickCamera(engine, 3);
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.EqualTo(new Vector2(3400f, 2200f)));
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(3400f, 2200f)));

            ClearCommandSource(engine);
            TickCamera(engine, 3);
            Assert.That(engine.AuthorityCamera().FollowTargetPositionCm, Is.Null);
            Assert.That(engine.AuthorityCamera().State.IsFollowing, Is.False);
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(3400f, 2200f)));
        }

        [Test]
        public void CameraShowcaseMod_BootstrapMap_CentersBoundsWithSharedBootstrap()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.BootstrapMapId);

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.TacticalProfileId));
            Assert.That(engine.AuthorityCamera().State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(new Vector2(4000f, 2500f)));
            Assert.That(engine.AuthorityCamera().State.DistanceCm, Is.EqualTo(8100f));
        }

        [Test]
        public void CameraShowcaseMod_PoseRequest_TargetsActiveVirtualCamera()
        {
            using var engine = CreateEngine(ShowcaseMods);
            LoadMap(engine, CameraShowcaseIds.CommandSourceFollowMapId);

            Assert.That(engine.AuthorityCamera().VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraShowcaseIds.CommandSourceFollowProfileId));

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = CameraShowcaseIds.CommandSourceFollowProfileId,
                Pitch = 55f,
                DistanceCm = 3600f,
                FovYDeg = 48f
            });
            Tick(engine, 1);

            Assert.That(engine.AuthorityCamera().State.Pitch, Is.EqualTo(55f).Within(0.001f));
            Assert.That(engine.AuthorityCamera().State.DistanceCm, Is.EqualTo(3600f).Within(0.001f));
            Assert.That(engine.AuthorityCamera().State.FovYDeg, Is.EqualTo(48f).Within(0.001f));
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
            // Bootstrap map has no Players; pass an explicit empty launch so cold-start seats are not injected.
            if (string.Equals(mapId, CameraShowcaseIds.BootstrapMapId, StringComparison.Ordinal))
            {
                engine.LoadMap(MapLoadRequest.FromMapId(mapId, new MapLaunchContext()));
            }
            else
            {
                // Seat maps need a present surface so LogicView cameras get ConfigureRuntime + PresentBinding metrics.
                if (engine.GetService(CoreServiceKeys.ViewController) == null)
                {
                    engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
                }

                engine.LoadMap(mapId);
            }

            Tick(engine, frames);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static void TickCamera(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.AuthorityCamera().Update(1f / 60f);
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

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            return ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity local) &&
                   engine.World.IsAlive(local)
                ? local
                : throw new InvalidOperationException("LocalPlayerEntity is missing.");
        }

        private static void SetCommandSource(GameEngine engine, params Entity[] entities)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            Entity local = GetLocalPlayer(engine);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: local,
                primaryEntity: entities.Length > 0 ? entities[0] : Entity.Null,
                title: "Camera showcase command source",
                summary: "Test-owned command source collection.");
            collections.Replace(local, in descriptor, entities, local);
        }

        private static void ClearCommandSource(GameEngine engine)
        {
            SetCommandSource(engine);
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

        private static bool SwitchViewMode(object manager, string modeId)
        {
            var switchTo = manager.GetType().GetMethod("SwitchTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null)
                ?? throw new InvalidOperationException("ViewModeManager.SwitchTo(string) was not found.");
            return switchTo.Invoke(manager, new object[] { modeId }) is bool value && value;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Test input backend is missing.");
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
        {
            backend.SetButton(path, true);
            Tick(engine, 1);
            backend.SetButton(path, false);
            Tick(engine, BlendSettleFrames);
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly System.Collections.Generic.Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution => new(1920f, 1080f);
            public float Fov => 60f;
            public float AspectRatio => 16f / 9f;
        }
    }
}
