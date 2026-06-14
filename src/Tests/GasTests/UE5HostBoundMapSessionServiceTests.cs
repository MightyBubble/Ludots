using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Adapter.UE5;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public class UE5HostBoundMapSessionServiceTests
    {
        private const string OuterMapId = "issue110_host_outer";
        private const string InnerMapId = "issue110_host_inner";
        private const string FailedMapId = "issue110_host_fail";

        [Test]
        public void LoadMap_InstallerWiring_DefersMapLoadedUntilHostWorldAndEntitiesAreReady()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Opening,
                "/Game/Maps/Outer",
                string.Empty,
                string.Empty,
                string.Empty)));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out IHostBoundMapSessionService sessionService);
            Assert.That(engine.GetService(CoreServiceKeys.MapLoadCompletionGate), Is.SameAs(sessionService));
            Assert.That(engine.GetService(CoreServiceKeys.FocusedMapLoadStateSink), Is.SameAs(sessionService));

            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(OuterMapId);

            HostBoundMapSessionSnapshot pending = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(OuterMapId));
            Assert.That(pending.HasExplicitBinding, Is.True);
            Assert.That(pending.IsPending, Is.True);
            Assert.That(pending.HasPendingReturn, Is.False);
            Assert.That(pending.Navigation.State, Is.EqualTo(HostLevelNavigationState.Opening));
            Assert.That(loadedMaps, Is.Empty, "MapLoaded must wait for host completion.");
            Assert.That(HasSuspendedMapEntity(engine, OuterMapId), Is.True, "Entities must stay suspended while host load is pending.");

            engine.Tick(1f / 60f);
            Assert.That(loadedMaps, Is.Empty);

            navigator.SetSnapshot(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty));

            engine.Tick(1f / 60f);

            HostBoundMapSessionSnapshot ready = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(loadedMaps, Is.EqualTo(new[] { OuterMapId }));
            Assert.That(ready.IsReady, Is.True);
            Assert.That(ready.LoadStatus, Is.EqualTo(MapLoadStatus.DeferredSuccess));
            Assert.That(HasSuspendedMapEntity(engine, OuterMapId), Is.False, "Entities must resume only after host load completes.");
        }

        [Test]
        public void UE5HostComposer_Compose_RegistersHostLoadLifecycleServices()
        {
            using var environment = new HostLoadTestEnvironment();
            UE5HostSetup setup = environment.ComposeHostSetup();
            try
            {
                IHostBoundMapSessionService service = setup.Engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionService);
                Assert.That(service, Is.Not.Null);
                Assert.That(setup.Engine.GetService(CoreServiceKeys.MapLoadCompletionGate), Is.SameAs(service));
                Assert.That(setup.Engine.GetService(CoreServiceKeys.FocusedMapLoadStateSink), Is.SameAs(service));
            }
            finally
            {
                setup.Engine.Dispose();
            }
        }

        [Test]
        public void PushMap_AndPopMap_ReuseFormalNestedMapLifecycleAndPendingReturn()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
                [InnerMapId] = CreateBinding("PreviewWorld", "/Game/Maps/Preview", HostLevelTransitionMode.PreviewMod),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty)));
            navigator.QueueLoadResult("/Game/Maps/Preview", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Active,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));
            navigator.QueueExitPreviewResult(HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Returning,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _);
            var suspendedMaps = CaptureMapEvents(engine, GameEvents.MapSuspended);
            var resumedMaps = CaptureMapEvents(engine, GameEvents.MapResumed);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(OuterMapId);
            Assert.That(loadedMaps, Does.Contain(OuterMapId));
            loadedMaps.Clear();

            engine.PushMap(InnerMapId);

            HostBoundMapSessionSnapshot activeInner = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(InnerMapId));
            Assert.That(activeInner.IsReady, Is.True);
            Assert.That(activeInner.HasPendingReturn, Is.True, "Pending return must come from the formal nested map stack.");
            Assert.That(suspendedMaps, Is.EqualTo(new[] { OuterMapId }));
            Assert.That(loadedMaps, Is.EqualTo(new[] { InnerMapId }));
            loadedMaps.Clear();

            engine.PopMap();

            HostBoundMapSessionSnapshot pendingOuter = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(navigator.ExitPreviewCalls, Is.EqualTo(1), "Preview pop must cancel through the navigator.");
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(OuterMapId));
            Assert.That(resumedMaps, Is.Empty, "MapResumed must wait for host return completion.");
            Assert.That(pendingOuter.FocusedMapId, Is.EqualTo(OuterMapId));
            Assert.That(pendingOuter.HasPendingReturn, Is.False);
            Assert.That(pendingOuter.IsPending, Is.True);
            Assert.That(loadedMaps, Is.Empty, "Canceled inner map must not publish MapLoaded.");

            navigator.SetSnapshot(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty));

            engine.Tick(1f / 60f);

            HostBoundMapSessionSnapshot resumedOuter = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(resumedMaps, Is.EqualTo(new[] { OuterMapId }));
            Assert.That(resumedOuter.IsReady, Is.True);
        }

        [Test]
        public void LoadMap_WhenHostNavigationFails_KeepsEntitiesSuspendedAndPublishesFailedStatus()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [FailedMapId] = CreateBinding("FailedWorld", "/Game/Maps/Failed", HostLevelTransitionMode.DirectOpenLevel),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Failed", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Opening,
                "/Game/Maps/Failed",
                string.Empty,
                string.Empty,
                string.Empty)));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(FailedMapId);

            navigator.SetSnapshot(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Failed,
                "/Game/Maps/Failed",
                string.Empty,
                string.Empty,
                "Host world failed to initialize required entities."));

            engine.Tick(1f / 60f);

            HostBoundMapSessionSnapshot failed = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(loadedMaps, Is.Empty, "Failed host completion must not publish MapLoaded.");
            Assert.That(failed.LoadStatus.Failed, Is.True);
            Assert.That(failed.LoadStatus.ErrorMessage, Does.Contain("required entities"));
            Assert.That(HasSuspendedMapEntity(engine, FailedMapId), Is.True);
        }

        [Test]
        public void LoadMap_WhenPendingDirectOpenLosesFocus_CancelsHostNavigation()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
                [FailedMapId] = CreateBinding("NextWorld", "/Game/Maps/Next", HostLevelTransitionMode.DirectOpenLevel),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Opening,
                "/Game/Maps/Outer",
                string.Empty,
                string.Empty,
                string.Empty)));
            navigator.QueueLoadResult("/Game/Maps/Next", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Next",
                "/Game/Maps/Next",
                "NextWorld",
                string.Empty)));
            navigator.SetCancelPendingLoadResult(HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Idle,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty)));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(OuterMapId);
            Assert.That(loadedMaps, Is.Empty);

            engine.LoadMap(FailedMapId);

            Assert.That(navigator.CancelPendingLoadCalls, Is.EqualTo(1), "Losing focus must formally cancel the host-side pending load.");
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(FailedMapId));
            Assert.That(loadedMaps, Is.EqualTo(new[] { FailedMapId }));
        }

        [Test]
        public void PushMap_WithNonPreviewHostBinding_FailsInsteadOfInventingReturnLifecycle()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
                [InnerMapId] = CreateBinding("InnerWorld", "/Game/Maps/InnerDirect", HostLevelTransitionMode.DirectOpenLevel),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty)));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(OuterMapId);
            loadedMaps.Clear();

            engine.PushMap(InnerMapId);

            HostBoundMapSessionSnapshot failed = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(InnerMapId));
            Assert.That(failed.LoadStatus.Failed, Is.True);
            Assert.That(failed.LoadStatus.ErrorMessage, Does.Contain("PreviewMod"));
            Assert.That(loadedMaps, Is.Empty);
            Assert.That(HasSuspendedMapEntity(engine, InnerMapId), Is.True);
        }

        [Test]
        public void LoadMap_WithExternalSessionBinding_WithoutHandler_FailsExplicitly()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [FailedMapId] = CreateBinding(string.Empty, string.Empty, HostLevelTransitionMode.ExternalSession),
            });

            using GameEngine engine = environment.CreateEngine(resolver, new ScriptedNavigator(), out _);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);

            engine.LoadMap(FailedMapId);

            HostBoundMapSessionSnapshot failed = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(failed.LoadStatus.Failed, Is.True);
            Assert.That(failed.LoadStatus.ErrorMessage, Does.Contain(nameof(IExternalSessionTransitionHandler)));
            Assert.That(loadedMaps, Is.Empty);
            Assert.That(HasSuspendedMapEntity(engine, FailedMapId), Is.True);
        }

        [Test]
        public void PushMap_AndPopMap_WithExternalSessionBinding_UseTypedExternalTransitionHooks()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [InnerMapId] = CreateBinding(string.Empty, string.Empty, HostLevelTransitionMode.ExternalSession),
            });
            var navigator = new ScriptedNavigator();
            var externalHandler = new ScriptedExternalSessionTransitionHandler();

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _, externalHandler);
            var loadedMaps = CaptureMapEvents(engine, GameEvents.MapLoaded);
            var resumedMaps = CaptureMapEvents(engine, GameEvents.MapResumed);

            engine.LoadMap(OuterMapId);
            loadedMaps.Clear();

            engine.PushMap(InnerMapId);

            HostBoundMapSessionSnapshot pendingInner = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(externalHandler.LaunchCalls, Is.EqualTo(1));
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(InnerMapId));
            Assert.That(pendingInner.IsPending, Is.True);
            Assert.That(loadedMaps, Is.Empty);

            externalHandler.CompleteLaunch(MapLoadCompletionResult.Ready());
            engine.Tick(1f / 60f);

            HostBoundMapSessionSnapshot activeInner = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(activeInner.IsReady, Is.True);
            Assert.That(loadedMaps, Is.EqualTo(new[] { InnerMapId }));
            loadedMaps.Clear();

            engine.PopMap();

            HostBoundMapSessionSnapshot pendingOuter = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(externalHandler.ReturnCalls, Is.EqualTo(1));
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(OuterMapId));
            Assert.That(pendingOuter.IsPending, Is.True);
            Assert.That(resumedMaps, Is.Empty);

            externalHandler.CompleteReturn(MapLoadCompletionResult.Ready());
            engine.Tick(1f / 60f);

            HostBoundMapSessionSnapshot resumedOuter = engine.GetService(UE5AdapterServiceKeys.HostBoundMapSessionState);
            Assert.That(resumedOuter.IsReady, Is.True);
            Assert.That(resumedMaps, Is.EqualTo(new[] { OuterMapId }));
        }

        [Test]
        public void UnloadMap_WhenFocusedMapResumeIsPending_CancelsFormalResumeGate()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>());
            var navigator = new ScriptedNavigator();
            GameEngine? engine = null;
            try
            {
                engine = environment.CreateEngine(resolver, navigator, out _);
                var gate = new TrackingMapLoadCompletionGate();
                engine.SetService(CoreServiceKeys.MapLoadCompletionGate, gate);

                engine.LoadMap(OuterMapId);
                engine.PushMap(InnerMapId);
                engine.PopMap();

                Assert.That(gate.ResumePendingLoad, Is.Not.Null);
                Assert.That(gate.ResumePendingLoad!.CancelCalls, Is.EqualTo(0));

                engine.UnloadMap(OuterMapId);

                Assert.That(gate.ResumePendingLoad.CancelCalls, Is.EqualTo(1));
            }
            finally
            {
                engine?.Dispose();
            }
        }

        [Test]
        public void UnloadMap_WhenPreviewReturnIsPending_CancelsHostPendingReturn()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
                [InnerMapId] = CreateBinding("PreviewWorld", "/Game/Maps/Preview", HostLevelTransitionMode.PreviewMod),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty)));
            navigator.QueueLoadResult("/Game/Maps/Preview", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Active,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));
            navigator.QueueExitPreviewResult(HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Returning,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));
            navigator.SetCancelPendingReturnResult(HostLevelNavigationResult.Ok(HostLevelNavigationSnapshot.Empty));

            using GameEngine engine = environment.CreateEngine(resolver, navigator, out _);

            engine.LoadMap(OuterMapId);
            engine.PushMap(InnerMapId);
            engine.PopMap();

            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(OuterMapId));
            Assert.That(navigator.CancelPendingReturnCalls, Is.EqualTo(0));

            engine.UnloadMap(OuterMapId);

            Assert.That(navigator.CancelPendingReturnCalls, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenPreviewReturnIsPending_CancelsHostPendingReturn()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>
            {
                [OuterMapId] = CreateBinding("OuterWorld", "/Game/Maps/Outer", HostLevelTransitionMode.DirectOpenLevel),
                [InnerMapId] = CreateBinding("PreviewWorld", "/Game/Maps/Preview", HostLevelTransitionMode.PreviewMod),
            });
            var navigator = new ScriptedNavigator();
            navigator.QueueLoadResult("/Game/Maps/Outer", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Outer",
                "/Game/Maps/Outer",
                "OuterWorld",
                string.Empty)));
            navigator.QueueLoadResult("/Game/Maps/Preview", HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Active,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));
            navigator.QueueExitPreviewResult(HostLevelNavigationResult.Ok(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.PreviewMod,
                HostLevelNavigationState.Returning,
                "/Game/Maps/Preview",
                "/Game/Maps/Preview",
                "PreviewWorld",
                string.Empty)));
            navigator.SetCancelPendingReturnResult(HostLevelNavigationResult.Ok(HostLevelNavigationSnapshot.Empty));

            GameEngine? engine = null;
            try
            {
                engine = environment.CreateEngine(resolver, navigator, out _);

                engine.LoadMap(OuterMapId);
                engine.PushMap(InnerMapId);
                engine.PopMap();

                Assert.That(navigator.CancelPendingReturnCalls, Is.EqualTo(0));

                engine.Dispose();
                engine = null;

                Assert.That(navigator.CancelPendingReturnCalls, Is.EqualTo(1));
            }
            finally
            {
                engine?.Dispose();
            }
        }

        [Test]
        public void Dispose_WhenMapResumeIsPending_CancelsFormalResumeGate()
        {
            using var environment = new HostLoadTestEnvironment();
            var resolver = new MapBindingResolver(new Dictionary<string, ExplicitHostMapBinding>());
            var navigator = new ScriptedNavigator();
            GameEngine? engine = null;
            try
            {
                engine = environment.CreateEngine(resolver, navigator, out _);
                var gate = new TrackingMapLoadCompletionGate();
                engine.SetService(CoreServiceKeys.MapLoadCompletionGate, gate);

                engine.LoadMap(OuterMapId);
                engine.PushMap(InnerMapId);
                engine.PopMap();

                Assert.That(gate.ResumePendingLoad, Is.Not.Null);
                Assert.That(gate.ResumePendingLoad!.CancelCalls, Is.EqualTo(0));

                engine.Dispose();
                engine = null;

                Assert.That(gate.ResumePendingLoad.CancelCalls, Is.EqualTo(1));
            }
            finally
            {
                engine?.Dispose();
            }
        }

        private static List<string> CaptureMapEvents(GameEngine engine, EventKey eventKey)
        {
            var maps = new List<string>();
            engine.TriggerManager.RegisterEventHandler(eventKey, ctx =>
            {
                maps.Add(ctx.Get(CoreServiceKeys.MapId).Value);
                return Task.CompletedTask;
            });
            return maps;
        }

        private static ExplicitHostMapBinding CreateBinding(
            string hostWorldName,
            string levelPath,
            HostLevelTransitionMode transitionMode)
        {
            return new ExplicitHostMapBinding(
                hostWorldName,
                levelPath,
                transitionMode,
                false,
                null,
                null);
        }

        private static bool HasSuspendedMapEntity(GameEngine engine, string mapId)
        {
            bool suspended = false;
            var query = new QueryDescription().WithAll<MapEntity, SuspendedTag>();
            engine.World.Query(in query, (Entity _, ref MapEntity mapEntity, ref SuspendedTag _) =>
            {
                if (mapEntity.MapId == new MapId(mapId))
                {
                    suspended = true;
                }
            });
            return suspended;
        }

        private sealed class HostLoadTestEnvironment : IDisposable
        {
            private readonly string _repoRoot;
            private readonly string _assetsRoot;
            private readonly string _tempModRoot;
            private readonly string _tempBootstrapRoot;

            public HostLoadTestEnvironment()
            {
                _repoRoot = FindRepoRoot();
                _assetsRoot = Path.Combine(_repoRoot, "assets");
                _tempModRoot = Path.Combine(_repoRoot, ".tmp", "issue110_host_tests", Guid.NewGuid().ToString("N"));
                _tempBootstrapRoot = Path.Combine(_tempModRoot, "bootstrap");
                Directory.CreateDirectory(_tempModRoot);
                WriteTempMod();
            }

            public GameEngine CreateEngine(
                IExplicitHostMapBindingResolver resolver,
                IHostLevelNavigator navigator,
                out IHostBoundMapSessionService sessionService,
                IExternalSessionTransitionHandler externalTransitionHandler = null)
            {
                var modPaths = RepoModPaths.ResolveExplicit(_repoRoot, new[] { "LudotsCoreMod" });
                modPaths.Add(_tempModRoot);

                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(modPaths, _assetsRoot);
                InstallInput(engine);
                engine.SetService(UE5AdapterServiceKeys.ExplicitHostMapBindingResolver, resolver);
                engine.SetService(UE5AdapterServiceKeys.HostLevelNavigator, navigator);
                if (externalTransitionHandler != null)
                {
                    engine.SetService(UE5AdapterServiceKeys.ExternalSessionTransitionHandler, externalTransitionHandler);
                }

                sessionService = UE5HostBoundMapSessionInstaller.Install(engine);
                engine.Start();
                return engine;
            }

            public UE5HostSetup ComposeHostSetup()
            {
                string coreModRoot = RepoModPaths.ResolveExplicit(_repoRoot, new[] { "LudotsCoreMod", "CoreInputMod" })[0];
                string coreInputModRoot = RepoModPaths.ResolveExplicit(_repoRoot, new[] { "LudotsCoreMod", "CoreInputMod" })[1];
                Directory.CreateDirectory(_tempBootstrapRoot);
                string graphPath = Path.Combine(_tempBootstrapRoot, "launch.graph.json");
                File.WriteAllText(Path.Combine(_tempBootstrapRoot, "launcher.runtime.json"), """
{
  "LaunchGraphPath": "launch.graph.json",
  "PlanFingerprint": "issue110-host-tests",
  "PlanSchemaVersion": 1,
  "PlanGeneratedAtUtc": "2026-04-09T00:00:00Z"
}
""");
                File.WriteAllText(graphPath, $$"""
{
  "schemaVersion": 1,
  "generatedAtUtc": "2026-04-09T00:00:00Z",
  "planFingerprint": "issue110-host-tests",
  "orderedModIds": ["LudotsCoreMod", "CoreInputMod", "Issue110HostLoadTestMod"],
  "plannedMods": [
    { "id": "LudotsCoreMod", "rootPath": "{{coreModRoot.Replace("\\", "\\\\")}}" },
    { "id": "CoreInputMod", "rootPath": "{{coreInputModRoot.Replace("\\", "\\\\")}}" },
    { "id": "Issue110HostLoadTestMod", "rootPath": "{{_tempModRoot.Replace("\\", "\\\\")}}" }
  ]
}
""");

                return UE5HostComposer.Compose(Path.Combine(_tempBootstrapRoot, "launcher.runtime.json"));
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(_tempModRoot))
                    {
                        Directory.Delete(_tempModRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }

            private void WriteTempMod()
            {
                File.WriteAllText(Path.Combine(_tempModRoot, "mod.json"), """
{
  "name": "Issue110HostLoadTestMod",
  "version": "1.0.0",
  "description": "Host load lifecycle integration tests.",
  "priority": 100,
  "dependencies": {
    "LudotsCoreMod": "^1.0.0"
  }
}
""");

                string assetsDir = Path.Combine(_tempModRoot, "assets");
                string mapsDir = Path.Combine(assetsDir, "Maps");
                Directory.CreateDirectory(mapsDir);

                File.WriteAllText(Path.Combine(assetsDir, "game.json"), """
{
  "startupMapId": "issue110_host_outer",
  "startupInputContexts": []
}
""");

                WriteMap(mapsDir, OuterMapId, "OuterEntity");
                WriteMap(mapsDir, InnerMapId, "InnerEntity");
                WriteMap(mapsDir, FailedMapId, "FailedEntity");
            }

            private static void WriteMap(string mapsDir, string mapId, string name)
            {
                File.WriteAllText(Path.Combine(mapsDir, $"{mapId}.json"), $$"""
{
  "Id": "{{mapId}}",
  "Entities": [
    {
      "Template": "moba_dummy",
      "Overrides": {
        "Name": { "Value": "{{name}}" }
      }
    }
  ]
}
""");
            }

            private static void InstallInput(GameEngine engine)
            {
                var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
                var backend = new NullInputBackend();
                var inputHandler = new PlayerInputHandler(backend, inputConfig);
                for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
                {
                    inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
                }

                engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
                engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
                engine.SetService(CoreServiceKeys.UiCaptured, false);
            }

            private static string FindRepoRoot()
            {
                string? dir = TestContext.CurrentContext.TestDirectory;
                while (!string.IsNullOrWhiteSpace(dir))
                {
                    if (Directory.Exists(Path.Combine(dir, "assets")) &&
                        Directory.Exists(Path.Combine(dir, "mods")))
                    {
                        return dir;
                    }

                    dir = Directory.GetParent(dir)?.FullName;
                }

                throw new DirectoryNotFoundException("Repository root not found from test directory.");
            }
        }

        private sealed class MapBindingResolver : IExplicitHostMapBindingResolver
        {
            private readonly IReadOnlyDictionary<string, ExplicitHostMapBinding> _bindingsByMapId;

            public MapBindingResolver(IReadOnlyDictionary<string, ExplicitHostMapBinding> bindingsByMapId)
            {
                _bindingsByMapId = bindingsByMapId;
            }

            public bool TryResolve(MapSession focusedSession, out ExplicitHostMapBinding binding)
            {
                return _bindingsByMapId.TryGetValue(focusedSession.MapId.Value, out binding);
            }
        }

        private sealed class ScriptedNavigator : IHostLevelNavigator
        {
            private readonly Dictionary<string, Queue<HostLevelNavigationResult>> _loadResultsByLevelPath =
                new(StringComparer.Ordinal);
            private readonly Queue<HostLevelNavigationResult> _exitPreviewResults = new();

            public HostLevelNavigationSnapshot Snapshot { get; private set; } = HostLevelNavigationSnapshot.Empty;

            public int ExitPreviewCalls { get; private set; }
            public int CancelPendingLoadCalls { get; private set; }
            public int CancelPendingReturnCalls { get; private set; }
            public HostLevelNavigationResult CancelPendingLoadResult { get; private set; } =
                HostLevelNavigationResult.Ok(HostLevelNavigationSnapshot.Empty);
            public HostLevelNavigationResult CancelPendingReturnResult { get; private set; } =
                HostLevelNavigationResult.Ok(HostLevelNavigationSnapshot.Empty);

            public void QueueLoadResult(string levelPath, params HostLevelNavigationResult[] results)
            {
                _loadResultsByLevelPath[levelPath] = new Queue<HostLevelNavigationResult>(results);
            }

            public void QueueExitPreviewResult(params HostLevelNavigationResult[] results)
            {
                for (int i = 0; i < results.Length; i++)
                {
                    _exitPreviewResults.Enqueue(results[i]);
                }
            }

            public void SetCancelPendingLoadResult(HostLevelNavigationResult result)
            {
                CancelPendingLoadResult = result;
            }

            public void SetCancelPendingReturnResult(HostLevelNavigationResult result)
            {
                CancelPendingReturnResult = result;
            }

            public void SetSnapshot(HostLevelNavigationSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public HostLevelNavigationResult Load(in HostLevelLoadRequest request)
            {
                if (_loadResultsByLevelPath.TryGetValue(request.LevelPath, out Queue<HostLevelNavigationResult>? results) &&
                    results.Count > 0)
                {
                    HostLevelNavigationResult result = results.Count > 1 ? results.Dequeue() : results.Peek();
                    Snapshot = result.Snapshot;
                    return result;
                }

                Snapshot = new HostLevelNavigationSnapshot(
                    request.TransitionMode,
                    HostLevelNavigationState.Active,
                    request.LevelPath,
                    request.LevelPath,
                    string.Empty,
                    string.Empty);
                return HostLevelNavigationResult.Ok(Snapshot);
            }

            public HostLevelNavigationResult CancelPendingLoad()
            {
                CancelPendingLoadCalls++;
                Snapshot = CancelPendingLoadResult.Snapshot;
                return CancelPendingLoadResult;
            }

            public HostLevelNavigationResult CancelPendingReturn()
            {
                CancelPendingReturnCalls++;
                Snapshot = CancelPendingReturnResult.Snapshot;
                return CancelPendingReturnResult;
            }

            public HostLevelNavigationResult ExitPreview()
            {
                ExitPreviewCalls++;
                if (_exitPreviewResults.Count > 0)
                {
                    HostLevelNavigationResult result = _exitPreviewResults.Count > 1 ? _exitPreviewResults.Dequeue() : _exitPreviewResults.Peek();
                    Snapshot = result.Snapshot;
                    return result;
                }

                Snapshot = HostLevelNavigationSnapshot.Empty;
                return HostLevelNavigationResult.Ok(Snapshot);
            }
        }

        private sealed class ScriptedExternalSessionTransitionHandler : IExternalSessionTransitionHandler
        {
            public int LaunchCalls { get; private set; }
            public int ReturnCalls { get; private set; }

            private readonly MutablePendingMapLoad _launchPendingLoad = new();
            private readonly MutablePendingMapLoad _returnPendingLoad = new();

            public IPendingMapLoad BeginLaunch(in ExternalSessionLaunchRequest request)
            {
                LaunchCalls++;
                _launchPendingLoad.Reset();
                return _launchPendingLoad;
            }

            public IPendingMapLoad BeginReturn(in ExternalSessionReturnRequest request)
            {
                ReturnCalls++;
                _returnPendingLoad.Reset();
                return _returnPendingLoad;
            }

            public void CompleteLaunch(MapLoadCompletionResult result)
            {
                _launchPendingLoad.SetResult(result);
            }

            public void CompleteReturn(MapLoadCompletionResult result)
            {
                _returnPendingLoad.SetResult(result);
            }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;

            public bool GetButton(string devicePath) => false;

            public Vector2 GetMousePosition() => Vector2.Zero;

            public float GetMouseWheel() => 0f;

            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;
        }

        private sealed class TrackingMapLoadCompletionGate : IMapLoadCompletionGate
        {
            public TrackingPendingMapLoad? ResumePendingLoad { get; private set; }

            public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
            {
                return new CompletedPendingMapLoad(MapLoadCompletionResult.Ready());
            }

            public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
            {
                ResumePendingLoad = new TrackingPendingMapLoad();
                return ResumePendingLoad;
            }
        }

        private sealed class TrackingPendingMapLoad : IPendingMapLoad
        {
            public int CancelCalls { get; private set; }

            public MapLoadCompletionResult Poll()
            {
                return MapLoadCompletionResult.Pending();
            }

            public void Cancel()
            {
                CancelCalls++;
            }
        }

        private sealed class MutablePendingMapLoad : IPendingMapLoad
        {
            private MapLoadCompletionResult _result = MapLoadCompletionResult.Pending();

            public void Reset()
            {
                _result = MapLoadCompletionResult.Pending();
            }

            public void SetResult(MapLoadCompletionResult result)
            {
                _result = result;
            }

            public MapLoadCompletionResult Poll()
            {
                return _result;
            }

            public void Cancel()
            {
            }
        }

        private sealed class CompletedPendingMapLoad : IPendingMapLoad
        {
            private readonly MapLoadCompletionResult _result;

            public CompletedPendingMapLoad(MapLoadCompletionResult result)
            {
                _result = result;
            }

            public MapLoadCompletionResult Poll()
            {
                return _result;
            }

            public void Cancel()
            {
            }
        }
    }
}
