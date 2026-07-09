using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ArtifactFolderName = "rfc0065-showcase-workflow";
        private const string LauncherBindingName = "interaction_showcase";
        private const string LauncherTargetPath = "mods/showcases/interaction/InteractionShowcaseMod";
        private const string ManualGuiLaunchCommand = ".\\scripts\\run-mod-launcher.cmd cli launch interaction_showcase --adapter raylib";
        private const string DefaultSchemeId = "scheme.default";
        private const string WasdSchemeId = "scheme.wasd_move";
        private const string DefaultIntentId = "intent.command.default";
        private const string AllTogetherDispatchId = InteractionShowcaseIds.BlinkDispatchAllTogetherProfileId;
        private const string OneByOneDispatchId = InteractionShowcaseIds.BlinkDispatchOneByOneProfileId;
        private const string NearestTopNDispatchId = InteractionShowcaseIds.BlinkDispatchNearestTopNProfileId;

        private static readonly JsonSerializerOptions TraceJsonOptions = new(JsonSerializerDefaults.Web);

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "InteractionShowcaseMod"
        };

        [Test]
        public void Show5Show6Workflow_RightClickCommandRoutesThroughIntentDispatchAndOrderBuffer()
        {
            string repoRoot = FindRepoRoot();
            AssertLauncherBinding(repoRoot);
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
            Directory.CreateDirectory(artifactDir);

            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend);
            AssertStaticInteractionShowcaseCamera(engine);
            engine.LoadMap(InteractionShowcaseIds.HubMapId);
            Tick(engine, 8);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null), "interaction showcase must publish the local player command owner.");
            Entity arcweaver = FindEntityByName(engine.World, InteractionShowcaseIds.ArcweaverName);
            Entity vanguard = FindEntityByName(engine.World, InteractionShowcaseIds.VanguardName);
            Entity commander = FindEntityByName(engine.World, InteractionShowcaseIds.CommanderName);
            Assert.That(arcweaver, Is.Not.EqualTo(Entity.Null));
            Assert.That(vanguard, Is.Not.EqualTo(Entity.Null));
            Assert.That(commander, Is.Not.EqualTo(Entity.Null));

            var schemes = engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException("ControlSchemeRuntime service is missing.");
            int schemeId = schemes.SchemeIdRegistry.GetId(DefaultSchemeId);
            Assert.That(schemes.ActiveSchemeId, Is.EqualTo(schemeId), "SHOW-6 requires scheme.default to be active from production startup.");

            var stack = engine.GetService(CoreServiceKeys.InteractionContextStack)
                ?? throw new InvalidOperationException("InteractionContextStack service is missing.");
            Assert.That(stack.TryPeek(out InteractionContextFrame frame), Is.True);
            Assert.That(stack.CollectionKeyRegistry.GetName(frame.ActiveCollectionKeyId), Is.EqualTo(EntityCollectionKeys.CommandSource));
            Assert.That(
                stack.CommandIntentProfileIdRegistry.GetName(CommandIntentArbiter.ResolveActiveCommandIntent(stack, schemes)),
                Is.EqualTo(DefaultIntentId));

            var intents = engine.GetService(CoreServiceKeys.CommandIntentProfileRegistry)
                ?? throw new InvalidOperationException("CommandIntentProfileRegistry service is missing.");
            int intentProfileId = intents.ProfileIdRegistry.GetId(DefaultIntentId);
            Assert.That(intents.IsInstalled(intentProfileId), Is.True);

            var dispatch = engine.GetService(CoreServiceKeys.CastDispatchProfileRegistry)
                ?? throw new InvalidOperationException("CastDispatchProfileRegistry service is missing.");
            int dispatchProfileId = dispatch.ProfileIdRegistry.GetId(AllTogetherDispatchId);
            Assert.That(dispatch.IsInstalled(dispatchProfileId), Is.True);

            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            Entity[] actors = { arcweaver, vanguard, commander };
            Assert.That(
                collections.TryGet(localPlayer, EntityCollectionKeys.CommandSource, out EntityCollectionHandle sourceHandle),
                Is.True,
                "Interaction showcase startup must seed collection.command.source directly for command routing.");
            Assert.That(CopyCollection(collections, sourceHandle), Is.EquivalentTo(actors));
            PublishHoveredEntity(collections, localPlayer, vanguard);
            Assert.That(Ludots.Tests.EntityCollectionTestAccess.TryGetHoveredEntity(engine, out Entity hovered), Is.True);
            Assert.That(hovered, Is.EqualTo(vanguard));

            Assert.That(engine.GetService(CoreServiceKeys.ActiveInputOrderMapping), Is.Not.Null,
                "InteractionShowcaseLocalOrderSourceSystem must create the production InputOrderMappingSystem.");

            Vector2 targetWorldCm = new(2080f, 1080f);
            DispatchVariantEvidence[] dispatchVariants = AssertDispatchVariants(dispatch, actors, engine.World, targetWorldCm);
            RightClickCommandWorld(engine, backend, targetWorldCm);
            TickUntil(
                engine,
                () => TryReadSharedMoveOrders(engine, actors, out _),
                maxFrames: 48,
                describeFailure: () => BuildOrderDiagnostics(engine, actors));

            Assert.That(TryReadSharedMoveOrders(engine, actors, out Order[] orders), Is.True);
            Assert.That(orders.Length, Is.EqualTo(actors.Length));
            int sharedOrderId = orders[0].OrderId;
            Assert.That(sharedOrderId, Is.GreaterThan(0));
            Assert.That(orders.Select(order => order.OrderId), Is.All.EqualTo(sharedOrderId),
                "dispatch.all_together must fan out with one shared order id.");
            Assert.That(orders.Select(order => order.Actor), Is.EquivalentTo(actors));

            int moveToId = engine.GetService(CoreServiceKeys.OrderTypeRegistry)!.GetId("moveTo");
            for (int i = 0; i < orders.Length; i++)
            {
                Assert.That(orders[i].OrderTypeId, Is.EqualTo(moveToId));
                Assert.That(orders[i].PlayerId, Is.EqualTo(1));
                Assert.That(engine.World.IsAlive(orders[i].Target), Is.False);
                Assert.That(orders[i].Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
                Assert.That(orders[i].Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.Single));
                Assert.That(orders[i].Args.Spatial.WorldCm.X, Is.EqualTo(targetWorldCm.X).Within(0.001f));
                Assert.That(orders[i].Args.Spatial.WorldCm.Z, Is.EqualTo(targetWorldCm.Y).Within(0.001f));
            }

            Entity[] commandSource = CopyCollection(collections, sourceHandle);
            WriteAcceptanceArtifacts(
                artifactDir,
                localPlayer,
                actors,
                hovered,
                commandSource,
                orders,
                dispatchVariants,
                targetWorldCm,
                intentProfileId,
                dispatchProfileId,
                schemeId);
        }

        [Test]
        public void Show6VisibleUatTimeline_SwitchesDefaultSchemeToWasdScheme()
        {
            string? previous = Environment.GetEnvironmentVariable("LUDOTS_INTERACTION_SHOWCASE_AUTO_SCHEME_TIMELINE");
            Environment.SetEnvironmentVariable("LUDOTS_INTERACTION_SHOWCASE_AUTO_SCHEME_TIMELINE", "1");
            try
            {
                string repoRoot = FindRepoRoot();
                AssertLauncherBinding(repoRoot);

                var backend = new TestInputBackend();
                using var engine = CreateEngine(repoRoot, backend);
                engine.LoadMap(InteractionShowcaseIds.HubMapId);
                Tick(engine, 8);

                var schemes = engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                    ?? throw new InvalidOperationException("ControlSchemeRuntime service is missing.");
                int defaultSchemeId = schemes.SchemeIdRegistry.GetId(DefaultSchemeId);
                int wasdSchemeId = schemes.SchemeIdRegistry.GetId(WasdSchemeId);
                Assert.That(schemes.ActiveSchemeId, Is.EqualTo(defaultSchemeId));
                Assert.That(schemes.TryGetActiveAxisMove(out _), Is.False);

                Tick(engine, 90);

                Assert.That(schemes.ActiveSchemeId, Is.EqualTo(wasdSchemeId));
                Assert.That(schemes.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding), Is.True);
                Assert.That(binding.ActionId, Is.EqualTo("Move"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUDOTS_INTERACTION_SHOWCASE_AUTO_SCHEME_TIMELINE", previous);
            }
        }

        [Test]
        public void Show6VisibleUatBlinkTimeline_PublishesDispatchEvidenceCollectionForWorldMarkers()
        {
            string? previous = Environment.GetEnvironmentVariable(InteractionShowcaseIds.AutoBlinkTimelineEnvKey);
            Environment.SetEnvironmentVariable(InteractionShowcaseIds.AutoBlinkTimelineEnvKey, "1");
            try
            {
                string repoRoot = FindRepoRoot();
                AssertLauncherBinding(repoRoot);

                var backend = new TestInputBackend();
                using var engine = CreateEngine(repoRoot, backend);
                engine.LoadMap(InteractionShowcaseIds.HubMapId);
                Tick(engine, 8);

                Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
                Entity arcweaver = FindEntityByName(engine.World, InteractionShowcaseIds.ArcweaverName);
                Entity vanguard = FindEntityByName(engine.World, InteractionShowcaseIds.VanguardName);
                Entity commander = FindEntityByName(engine.World, InteractionShowcaseIds.CommanderName);
                Entity[] actors = { arcweaver, vanguard, commander };
                Assert.That(actors.All(static actor => actor != Entity.Null), Is.True);

                Entity[] allTogetherRows = AssertBlinkEvidenceCollection(
                    engine,
                    localPlayer,
                    expectedCount: actors.Length,
                    expectedProfileId: AllTogetherDispatchId);
                Assert.That(allTogetherRows, Is.EquivalentTo(actors));

                TickUntil(
                    engine,
                    () => TryGetBlinkEvidenceCount(engine, localPlayer, out int count) && count == 1,
                    maxFrames: 96,
                    describeFailure: () => BuildBlinkEvidenceDiagnostics(engine, localPlayer));
                Entity[] oneByOneRows = AssertBlinkEvidenceCollection(
                    engine,
                    localPlayer,
                    expectedCount: 1,
                    expectedProfileId: OneByOneDispatchId);
                Assert.That(oneByOneRows[0], Is.EqualTo(arcweaver));

                TickUntil(
                    engine,
                    () => TryGetBlinkEvidenceCount(engine, localPlayer, out int count) && count == Math.Min(3, actors.Length),
                    maxFrames: 120,
                    describeFailure: () => BuildBlinkEvidenceDiagnostics(engine, localPlayer));
                Entity[] nearestRows = AssertBlinkEvidenceCollection(
                    engine,
                    localPlayer,
                    expectedCount: Math.Min(3, actors.Length),
                    expectedProfileId: NearestTopNDispatchId);
                Assert.That(nearestRows.All(row => actors.Contains(row)), Is.True);

                AssertInteractionBlinkPerformerRules(repoRoot);
            }
            finally
            {
                Environment.SetEnvironmentVariable(InteractionShowcaseIds.AutoBlinkTimelineEnvKey, previous);
            }
        }

        [Test]
        public void Show6Workflow_ControlSchemeHotSwitchEnablesWasdAxisMoveThroughOrderBuffer()
        {
            string repoRoot = FindRepoRoot();
            AssertLauncherBinding(repoRoot);
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
            Directory.CreateDirectory(artifactDir);

            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend);
            engine.LoadMap(InteractionShowcaseIds.HubMapId);
            Tick(engine, 8);

            Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.Has<WorldPositionCm>(localPlayer), Is.True);

            var schemes = engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException("ControlSchemeRuntime service is missing.");
            int defaultSchemeId = schemes.SchemeIdRegistry.GetId(DefaultSchemeId);
            int wasdSchemeId = schemes.SchemeIdRegistry.GetId(WasdSchemeId);
            Assert.That(schemes.ActiveSchemeId, Is.EqualTo(defaultSchemeId));
            Assert.That(schemes.TryGetActiveAxisMove(out _), Is.False,
                "scheme.default intentionally keeps axis movement disabled by missing axisMove declaration.");

            Assert.That(schemes.TrySwitch(wasdSchemeId), Is.True);
            Assert.That(schemes.ActiveSchemeId, Is.EqualTo(wasdSchemeId));
            Assert.That(schemes.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding), Is.True);
            Assert.That(binding.ActionId, Is.EqualTo("Move"));

            Vector2 start = engine.World.Get<WorldPositionCm>(localPlayer).Value.ToVector2();
            backend.SetButton("<Keyboard>/d", true);
            TickUntil(
                engine,
                () => TryReadAxisMoveOrder(engine, localPlayer, out _),
                maxFrames: 48,
                describeFailure: () => BuildOrderDiagnostics(engine, new[] { localPlayer }));
            backend.SetButton("<Keyboard>/d", false);
            Tick(engine, 2);

            Assert.That(TryReadAxisMoveOrder(engine, localPlayer, out Order order), Is.True);
            int moveToId = engine.GetService(CoreServiceKeys.OrderTypeRegistry)!.GetId("moveTo");
            Assert.That(order.OrderTypeId, Is.EqualTo(moveToId));
            Assert.That(order.PlayerId, Is.EqualTo(1));
            Assert.That(order.Actor, Is.EqualTo(localPlayer));
            Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(order.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.Single));
            Assert.That(order.Args.Spatial.WorldCm.X, Is.EqualTo(start.X + binding.StepDistanceCm).Within(0.001f));
            Assert.That(order.Args.Spatial.WorldCm.Y, Is.EqualTo(start.Y).Within(0.001f));
            Assert.That(order.Args.Spatial.WorldCm.Z, Is.EqualTo(0f).Within(0.001f));

            WriteWasdAcceptanceArtifact(
                artifactDir,
                localPlayer,
                defaultSchemeId,
                wasdSchemeId,
                binding,
                order,
                start);
        }

        private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
                Path.Combine(repoRoot, "assets"));
            InstallInput(engine, backend);
            AcceptanceUiHostInstaller.Install(engine);
            engine.Start();
            return engine;
        }

        private static void AssertStaticInteractionShowcaseCamera(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry service is missing.");
            VirtualCameraDefinition tactical = registry.Get("Camera.Profile.Tactical");
            Assert.That(tactical.DisplayName, Is.EqualTo("Interaction Showcase Static Camera"));
            Assert.That(tactical.PanMode, Is.EqualTo(CameraPanMode.None));
            Assert.That(tactical.EnableGrabDrag, Is.False);
            Assert.That(tactical.EnableZoom, Is.False);
            Assert.That(tactical.AllowUserInput, Is.False);
        }

        private static void InstallInput(GameEngine engine, TestInputBackend backend)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void RightClickCommandWorld(GameEngine engine, TestInputBackend backend, Vector2 worldCm)
        {
            AuthoritativeGroundPointerOverride groundOverride = engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)
                ?? throw new InvalidOperationException("AuthoritativeGroundPointerOverride service is missing.");
            groundOverride.Set("Command", worldCm);
            backend.SetButton("<Mouse>/RightButton", true);
            Tick(engine, 4);
            backend.SetButton("<Mouse>/RightButton", false);
            Tick(engine, 4);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames, Func<string> describeFailure)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames. {describeFailure()}");
        }

        private static bool TryReadSharedMoveOrders(GameEngine engine, Entity[] actors, out Order[] orders)
        {
            orders = Array.Empty<Order>();
            var captured = new List<Order>(actors.Length);
            for (int i = 0; i < actors.Length; i++)
            {
                Entity actor = actors[i];
                if (!engine.World.IsAlive(actor) || !engine.World.TryGet(actor, out OrderBuffer buffer) || !buffer.HasActive)
                {
                    return false;
                }

                Order order = buffer.ActiveOrder.Order;
                if (order.OrderId <= 0 ||
                    order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                    order.Args.Spatial.Mode != OrderCollectionMode.Single)
                {
                    return false;
                }

                captured.Add(order);
            }

            int sharedOrderId = captured[0].OrderId;
            for (int i = 1; i < captured.Count; i++)
            {
                if (captured[i].OrderId != sharedOrderId)
                {
                    return false;
                }
            }

            orders = captured.ToArray();
            return true;
        }

        private static bool TryReadAxisMoveOrder(GameEngine engine, Entity actor, out Order order)
        {
            order = default;
            if (!engine.World.IsAlive(actor) ||
                !engine.World.TryGet(actor, out OrderBuffer buffer) ||
                !buffer.HasActive)
            {
                return false;
            }

            Order candidate = buffer.ActiveOrder.Order;
            if (candidate.OrderId <= 0 ||
                candidate.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                candidate.Args.Spatial.Mode != OrderCollectionMode.Single)
            {
                return false;
            }

            order = candidate;
            return true;
        }

        private static Entity[] CopyCollection(EntityCollectionStore store, EntityCollectionHandle handle)
        {
            Assert.That(store.TryGetView(handle, out EntityCollectionView view), Is.True);
            var members = new Entity[view.Count];
            int copied = store.CopyEntities(handle, 0, members);
            Assert.That(copied, Is.EqualTo(view.Count));
            return members;
        }

        private static Entity[] AssertBlinkEvidenceCollection(
            GameEngine engine,
            Entity owner,
            int expectedCount,
            string expectedProfileId)
        {
            Assert.That(owner, Is.Not.EqualTo(Entity.Null), "blink evidence collection requires the local player owner.");
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");

            Assert.That(
                collections.TryGetView(owner, InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey, out EntityCollectionView view),
                Is.True,
                $"visible blink UAT must publish '{InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey}'.");
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.CollectionSnapshot));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.Display));
            Assert.That(view.Count, Is.EqualTo(expectedCount), view.Summary);
            Assert.That(view.Summary, Does.Contain(expectedProfileId));

            var members = new Entity[view.Count];
            int copied = collections.CopyEntities(owner, InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey, members);
            Assert.That(copied, Is.EqualTo(view.Count));
            return members;
        }

        private static bool TryGetBlinkEvidenceCount(GameEngine engine, Entity owner, out int count)
        {
            count = 0;
            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections ||
                !collections.TryGetView(owner, InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey, out EntityCollectionView view))
            {
                return false;
            }

            count = view.Count;
            return true;
        }

        private static string BuildBlinkEvidenceDiagnostics(GameEngine engine, Entity owner)
        {
            int frame = engine.GlobalContext.TryGetValue(InteractionShowcaseIds.VisibleUatFrameKey, out object? frameObj) &&
                        frameObj is int visibleFrame
                ? visibleFrame
                : 0;

            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections ||
                !collections.TryGetView(owner, InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey, out EntityCollectionView view))
            {
                return $"frame={frame}; blink evidence collection missing.";
            }

            return $"frame={frame}; count={view.Count}; summary={view.Summary}";
        }

        private static DispatchVariantEvidence[] AssertDispatchVariants(
            CastDispatchProfileRegistry dispatch,
            Entity[] actors,
            World world,
            Vector2 targetWorldCm)
        {
            var ctx = new CastDispatchContext(
                world,
                new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y),
                groupKey: 581_650L);
            var selected = new Entity[actors.Length];

            int allId = RequireDispatchProfile(dispatch, AllTogetherDispatchId);
            int allCount = dispatch.SelectDispatchTargets(allId, actors, in ctx, selected, out CastDispatchRouting allRouting);
            Assert.That(allCount, Is.EqualTo(actors.Length));
            Assert.That(allRouting.SharedOrderId, Is.True);
            Assert.That(allRouting.Sequential, Is.False);

            int oneByOneId = RequireDispatchProfile(dispatch, OneByOneDispatchId);
            Array.Clear(selected, 0, selected.Length);
            int oneByOneCount = dispatch.SelectDispatchTargets(oneByOneId, actors, in ctx, selected, out CastDispatchRouting oneByOneRouting);
            Assert.That(oneByOneCount, Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(actors[0]));
            Assert.That(oneByOneRouting.SharedOrderId, Is.False);
            Assert.That(oneByOneRouting.Sequential, Is.True);

            int nearestTopNId = RequireDispatchProfile(dispatch, NearestTopNDispatchId);
            Array.Clear(selected, 0, selected.Length);
            int nearestCount = dispatch.SelectDispatchTargets(nearestTopNId, actors, in ctx, selected, out CastDispatchRouting nearestRouting);
            Assert.That(nearestCount, Is.EqualTo(Math.Min(3, actors.Length)));
            Assert.That(nearestRouting.SharedOrderId, Is.True);
            Assert.That(nearestRouting.Sequential, Is.False);

            return new[]
            {
                new DispatchVariantEvidence(AllTogetherDispatchId, allId, allCount, allRouting.SharedOrderId, allRouting.Sequential),
                new DispatchVariantEvidence(OneByOneDispatchId, oneByOneId, oneByOneCount, oneByOneRouting.SharedOrderId, oneByOneRouting.Sequential),
                new DispatchVariantEvidence(NearestTopNDispatchId, nearestTopNId, nearestCount, nearestRouting.SharedOrderId, nearestRouting.Sequential)
            };
        }

        private static int RequireDispatchProfile(CastDispatchProfileRegistry dispatch, string profileId)
        {
            int id = dispatch.ProfileIdRegistry.GetId(profileId);
            Assert.That(id, Is.GreaterThan(0), $"dispatch profile '{profileId}' should have a registry id.");
            Assert.That(dispatch.IsInstalled(id), Is.True, $"dispatch profile '{profileId}' should be installed.");
            return id;
        }

        private static void PublishHoveredEntity(EntityCollectionStore collections, Entity owner, Entity hovered)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.HoveredEntity,
                EntityCollectionSourceKind.UiHover,
                EntityCollectionRoleKind.Display,
                owner,
                hovered,
                "RFC-0065 hover target",
                "hover entity must not become the ground command target.");
            ReadOnlySpan<Entity> rows = stackalloc Entity[] { hovered };
            collections.Replace(owner, descriptor, rows);
        }

        private static void AssertInteractionBlinkPerformerRules(string repoRoot)
        {
            string performerPath = Path.Combine(repoRoot, LauncherTargetPath, "assets", "Presentation", "performers.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(performerPath, Encoding.UTF8));
            JsonElement root = document.RootElement;

            AssertGroundOverlayRingDefinition(root, InteractionShowcaseIds.BlinkDispatchEvidenceMarkerDefId);
            AssertPerformerCollectionRule(
                root,
                "EntityCollectionMemberAdded",
                InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey,
                "CreatePerformer",
                InteractionShowcaseIds.BlinkDispatchEvidenceMarkerDefId);
            AssertPerformerCollectionRule(
                root,
                "EntityCollectionMemberRemoved",
                InteractionShowcaseIds.BlinkDispatchEvidenceCollectionKey,
                "DestroyScopedPerformer",
                InteractionShowcaseIds.BlinkDispatchEvidenceMarkerDefId);
        }

        private static void AssertGroundOverlayRingDefinition(JsonElement root, string definitionId)
        {
            foreach (JsonElement entry in root.EnumerateArray())
            {
                if (!entry.TryGetProperty("id", out JsonElement id) ||
                    !string.Equals(id.GetString(), definitionId, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(entry.TryGetProperty("behaviors", out JsonElement behaviors), Is.True);
                foreach (JsonElement behavior in behaviors.EnumerateArray())
                {
                    if (!behavior.TryGetProperty("assetBinding", out JsonElement binding))
                    {
                        continue;
                    }

                    if (binding.TryGetProperty("assetKind", out JsonElement assetKind) &&
                        binding.TryGetProperty("assetId", out JsonElement assetId) &&
                        string.Equals(assetKind.GetString(), "GroundOverlay", StringComparison.Ordinal) &&
                        string.Equals(assetId.GetString(), "Ring", StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                Assert.Fail($"Performer '{definitionId}' must bind a GroundOverlay Ring.");
            }

            Assert.Fail($"Performer '{definitionId}' is missing.");
        }

        private static void AssertPerformerCollectionRule(
            JsonElement root,
            string eventKind,
            string collectionKey,
            string commandKind,
            string definitionId)
        {
            foreach (JsonElement entry in root.EnumerateArray())
            {
                if (!entry.TryGetProperty("rules", out JsonElement rules))
                {
                    continue;
                }

                foreach (JsonElement rule in rules.EnumerateArray())
                {
                    if (!rule.TryGetProperty("event", out JsonElement evt) ||
                        !rule.TryGetProperty("command", out JsonElement command) ||
                        !evt.TryGetProperty("kind", out JsonElement kind) ||
                        !evt.TryGetProperty("key", out JsonElement key) ||
                        !command.TryGetProperty("kind", out JsonElement actualCommandKind) ||
                        !command.TryGetProperty("definitionId", out JsonElement actualDefinitionId))
                    {
                        continue;
                    }

                    if (string.Equals(kind.GetString(), eventKind, StringComparison.Ordinal) &&
                        string.Equals(key.GetString(), collectionKey, StringComparison.Ordinal) &&
                        string.Equals(actualCommandKind.GetString(), commandKind, StringComparison.Ordinal) &&
                        string.Equals(actualDefinitionId.GetString(), definitionId, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            Assert.Fail(
                $"Performer rule missing: {eventKind} {collectionKey} -> {commandKind} {definitionId}.");
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

        private static string BuildOrderDiagnostics(GameEngine engine, Entity[] actors)
        {
            var builder = new StringBuilder();
            builder.Append("orders=");
            for (int i = 0; i < actors.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                Entity actor = actors[i];
                builder.Append(actor);
                if (!engine.World.IsAlive(actor))
                {
                    builder.Append(":dead");
                    continue;
                }

                if (!engine.World.TryGet(actor, out OrderBuffer buffer))
                {
                    builder.Append(":no-buffer");
                    continue;
                }

                if (!buffer.HasActive)
                {
                    builder.Append(":no-active queued=");
                    builder.Append(buffer.QueuedCount.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                Order order = buffer.ActiveOrder.Order;
                builder.Append(":active id=");
                builder.Append(order.OrderId.ToString(CultureInfo.InvariantCulture));
                builder.Append(" type=");
                builder.Append(order.OrderTypeId.ToString(CultureInfo.InvariantCulture));
                builder.Append(" target=");
                builder.Append(order.Args.Spatial.WorldCm);
            }

            if (engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastOrder", out object? lastOrder))
            {
                builder.Append(" lastOrder=");
                builder.Append(lastOrder);
            }

            if (engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastGroundWorldCm", out object? lastGround))
            {
                builder.Append(" lastGround=");
                builder.Append(lastGround);
            }

            if (engine.TryGetService(CoreServiceKeys.ActiveInputOrderMapping, out InputOrderMappingSystem mapping))
            {
                builder.Append(" mappingCommandAction=");
                builder.Append(mapping.CommandActionId);
                builder.Append(" aiming=");
                builder.Append(mapping.IsAiming.ToString(CultureInfo.InvariantCulture));
                if (mapping.GetMapping("Command") is InputOrderMapping commandMapping)
                {
                    builder.Append(" commandMapping=");
                    builder.Append(commandMapping.Trigger);
                    builder.Append("/");
                    builder.Append(commandMapping.OrderTypeKey);
                    builder.Append("/");
                    builder.Append(commandMapping.TargetType);
                }
            }

            if (engine.TryGetService(CoreServiceKeys.OrderQueue, out OrderQueue orderQueue))
            {
                builder.Append(" orderQueue=");
                builder.Append(orderQueue.Count.ToString(CultureInfo.InvariantCulture));
            }

            AppendCommandRouteDiagnostics(engine, actors, builder);

            return builder.ToString();
        }

        private static void AppendCommandRouteDiagnostics(GameEngine engine, Entity[] fallbackActors, StringBuilder builder)
        {
            if (engine.GetService(CoreServiceKeys.InteractionContextStack) is not InteractionContextStack stack ||
                engine.GetService(CoreServiceKeys.ControlSchemeRuntime) is not ControlSchemeRuntime schemes ||
                engine.GetService(CoreServiceKeys.CommandIntentProfileRegistry) is not CommandIntentProfileRegistry intents ||
                engine.GetService(CoreServiceKeys.CastDispatchProfileRegistry) is not CastDispatchProfileRegistry dispatch ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                builder.Append(" commandRoute=<missing-service>");
                return;
            }

            builder.Append(" commandRoute=");
            if (!stack.TryPeek(out InteractionContextFrame frame))
            {
                builder.Append("no-frame");
                return;
            }

            int intentId = CommandIntentArbiter.ResolveActiveCommandIntent(stack, schemes);
            builder.Append("intent=");
            builder.Append(stack.CommandIntentProfileIdRegistry.GetName(intentId));
            builder.Append("(");
            builder.Append(intentId.ToString(CultureInfo.InvariantCulture));
            builder.Append(")");
            builder.Append(" dispatch=");
            builder.Append(schemes.ActiveDefaultCastDispatchProfileId.ToString(CultureInfo.InvariantCulture));

            Entity owner = Entity.Null;
            if (frame.ContextEntity != Entity.Null && engine.World.IsAlive(frame.ContextEntity))
            {
                owner = frame.ContextEntity;
            }
            else if (engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer))
            {
                owner = localPlayer;
            }

            builder.Append(" owner=");
            builder.Append(owner);
            builder.Append(" frameContext=");
            builder.Append(frame.ContextEntity);

            if (owner == Entity.Null || intentId == 0)
            {
                return;
            }

            if (!collections.TryGet(owner, frame.ActiveCollectionKeyId, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view))
            {
                builder.Append(" collection=missing");
                return;
            }

            var routeActors = new Entity[view.Count];
            int actorCount = collections.CopyEntities(handle, 0, routeActors);
            if (actorCount <= 0)
            {
                routeActors = fallbackActors;
                actorCount = fallbackActors.Length;
            }

            var routes = new CommandIntentRoute[actorCount];
            int routedCount = intents.RouteGroup(
                intentId,
                routeActors.AsSpan(0, actorCount),
                owner,
                new CommandIntentTargetFacts(Entity.Null, HasEntity: false),
                routes);
            int hasRouteCount = 0;
            int firstRouteOrderTypeId = 0;
            for (int i = 0; i < actorCount; i++)
            {
                if (routes[i].HasRoute)
                {
                    hasRouteCount++;
                    if (firstRouteOrderTypeId == 0)
                    {
                        firstRouteOrderTypeId = routes[i].OrderTypeId;
                    }
                }
            }

            builder.Append(" actors=");
            builder.Append(actorCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" routed=");
            builder.Append(routedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" hasRoute=");
            builder.Append(hasRouteCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" routeType=");
            builder.Append(firstRouteOrderTypeId.ToString(CultureInfo.InvariantCulture));

            if (hasRouteCount <= 0 || schemes.ActiveDefaultCastDispatchProfileId == 0)
            {
                return;
            }

            var routedActors = new Entity[hasRouteCount];
            int write = 0;
            for (int i = 0; i < actorCount; i++)
            {
                if (routes[i].HasRoute)
                {
                    routedActors[write++] = routeActors[i];
                }
            }

            var selected = new Entity[hasRouteCount];
            int dispatchCount = dispatch.SelectDispatchTargets(
                schemes.ActiveDefaultCastDispatchProfileId,
                routedActors,
                new CastDispatchContext(engine.World, Vector3.Zero, frame.OwnerToken),
                selected,
                out CastDispatchRouting routing);
            builder.Append(" selected=");
            builder.Append(dispatchCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" shared=");
            builder.Append(routing.SharedOrderId.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteAcceptanceArtifacts(
            string artifactDir,
            Entity localPlayer,
            Entity[] actors,
            Entity hovered,
            Entity[] commandSource,
            Order[] orders,
            DispatchVariantEvidence[] dispatchVariants,
            Vector2 targetWorldCm,
            int intentProfileId,
            int dispatchProfileId,
            int schemeId)
        {
            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(localPlayer, actors, hovered, commandSource, orders, dispatchVariants, targetWorldCm, intentProfileId, dispatchProfileId, schemeId),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "trace.jsonl"),
                BuildTraceJsonl(localPlayer, actors, hovered, commandSource, orders, dispatchVariants, targetWorldCm, intentProfileId, dispatchProfileId, schemeId),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static void WriteWasdAcceptanceArtifact(
            string artifactDir,
            Entity localPlayer,
            int defaultSchemeId,
            int wasdSchemeId,
            ControlSchemeAxisMoveBinding binding,
            Order order,
            Vector2 start)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: rfc0065-show6-control-scheme-wasd");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- build: GasTests / Show6Workflow_ControlSchemeHotSwitchEnablesWasdAxisMoveThroughOrderBuffer");
            sb.AppendLine("- seed: interaction_showcase_hub deterministic headless run");
            sb.AppendLine($"- execution timestamp UTC: {DateTimeOffset.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Scenario Card");
            sb.AppendLine("- Player goal: hot-switch from mouse/default command scheme to a WASD movement scheme and hold D.");
            sb.AppendLine("- Runtime path: `ControlSchemeRuntime.TrySwitch` -> `PlayerInputHandler` -> `InputRuntimeSystem` -> `AuthoritativeInputSnapshotSystem` -> `AxisMoveOrderSystem` -> `OrderQueue` -> `OrderBufferSystem`.");
            sb.AppendLine("- Primary success condition: the local showcase actor receives a moveTo order offset by the scheme-owned axisMove step distance.");
            sb.AppendLine("- Evidence boundary: this is headless production-path evidence; it does not claim a captured visible UAT recording.");
            sb.AppendLine();
            sb.AppendLine("## Runtime Values");
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| local player actor | {localPlayer} |");
            sb.AppendLine($"| scheme.default registry id | {defaultSchemeId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| scheme.wasd_move registry id | {wasdSchemeId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| axis action | {binding.ActionId} |");
            sb.AppendLine($"| step distance cm | {binding.StepDistanceCm.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| order id | {order.OrderId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| start world cm | ({start.X.ToString(CultureInfo.InvariantCulture)}, {start.Y.ToString(CultureInfo.InvariantCulture)}) |");
            sb.AppendLine($"| target world cm | ({order.Args.Spatial.WorldCm.X.ToString(CultureInfo.InvariantCulture)}, {order.Args.Spatial.WorldCm.Y.ToString(CultureInfo.InvariantCulture)}) |");
            File.WriteAllText(Path.Combine(artifactDir, "wasd-hot-switch-report.md"), sb.ToString(), Encoding.UTF8);
        }

        private static string BuildBattleReport(
            Entity localPlayer,
            Entity[] actors,
            Entity hovered,
            Entity[] commandSource,
            Order[] orders,
            DispatchVariantEvidence[] dispatchVariants,
            Vector2 targetWorldCm,
            int intentProfileId,
            int dispatchProfileId,
            int schemeId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: rfc0065-showcase-workflow");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- build: GasTests / Show5Show6Workflow_RightClickCommandRoutesThroughIntentDispatchAndOrderBuffer");
            sb.AppendLine("- seed: interaction_showcase_hub deterministic headless run");
            sb.AppendLine("- clock: engine fixed step sampled through 1/60s test ticks");
            sb.AppendLine($"- execution timestamp UTC: {DateTimeOffset.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Scenario Card");
            sb.AppendLine("- Player goal: right-click the ground with three command-source actors selected.");
            sb.AppendLine("- Gameplay domain: RFC-0065 SHOW-5 / SHOW-6 production pointer command workflow.");
            sb.AppendLine("- Runtime path: `PlayerInputHandler` -> `InputRuntimeSystem` -> `AuthoritativeInputSnapshotSystem` -> `InteractionShowcaseLocalOrderSourceSystem` -> `InputOrderMappingSystem` -> `CommandIntentArbiter` -> `CommandIntentProfileRegistry.RouteGroup` -> `CastDispatchProfileRegistry.SelectDispatchTargets` -> `OrderQueue` -> `OrderBufferSystem`.");
            sb.AppendLine($"- Launcher binding: `{LauncherBindingName}` (`{ManualGuiLaunchCommand}`).");
            sb.AppendLine("- Primary success condition: Arcweaver, Vanguard, and Commander all receive the same shared moveTo order id at the target point, even when the hover collection contains an entity.");
            sb.AppendLine("- Failure branch condition: no active scheme intent, no command-source collection, hidden legacy fallback, non-shared order ids, or missing OrderBuffer promotion.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine($"- T+000: verify launcher binding `{LauncherBindingName}` -> `{LauncherTargetPath}` and load `interaction_showcase_hub` with CoreInputMod and InteractionShowcaseMod.");
            sb.AppendLine($"- T+004: production startup has active `{DefaultSchemeId}` and resolves `{DefaultIntentId}`.");
            sb.AppendLine("- T+008: publish local `(owner, collection.command.source)` with Arcweaver, Vanguard, and Commander.");
            sb.AppendLine($"- T+012: right-click ground target ({targetWorldCm.X.ToString(CultureInfo.InvariantCulture)}, {targetWorldCm.Y.ToString(CultureInfo.InvariantCulture)}) through production input.");
            sb.AppendLine($"- T+016: `dispatch.all_together` fans out {orders.Length.ToString(CultureInfo.InvariantCulture)} moveTo orders with shared order id {orders[0].OrderId.ToString(CultureInfo.InvariantCulture)}.");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- result: success");
            sb.AppendLine("- headless evidence: production right-click command intake used scheme default intent, command-source collection, cast dispatch fan-out, shared order id assignment, and OrderBuffer promotion.");
            sb.AppendLine("- visible evidence boundary: this run is headless GasTests evidence; it does not claim a captured raylib/CEF video.");
            sb.AppendLine();
            sb.AppendLine("## Runtime Values");
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| local player | {localPlayer} |");
            sb.AppendLine($"| scheme.default registry id | {schemeId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| intent.command.default registry id | {intentProfileId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| dispatch.all_together registry id | {dispatchProfileId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| command source rows | {string.Join(", ", commandSource.Select(static e => e.ToString()))} |");
            sb.AppendLine($"| hover entity ignored by ground command | {hovered} |");
            sb.AppendLine($"| shared order id | {orders[0].OrderId.ToString(CultureInfo.InvariantCulture)} |");
            sb.AppendLine($"| target world cm | ({targetWorldCm.X.ToString(CultureInfo.InvariantCulture)}, {targetWorldCm.Y.ToString(CultureInfo.InvariantCulture)}) |");
            sb.AppendLine();
            sb.AppendLine("## Dispatch Variants");
            sb.AppendLine("| Profile | Registry id | Selected count | Shared order id | Sequential |");
            sb.AppendLine("|---|---:|---:|---|---|");
            for (int i = 0; i < dispatchVariants.Length; i++)
            {
                DispatchVariantEvidence variant = dispatchVariants[i];
                sb.AppendLine($"| {variant.ProfileId} | {variant.RegistryId.ToString(CultureInfo.InvariantCulture)} | {variant.SelectedCount.ToString(CultureInfo.InvariantCulture)} | {variant.SharedOrderId} | {variant.Sequential} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Orders");
            sb.AppendLine("| Actor | Order id | Type id | Player | Target X | Target Z |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            for (int i = 0; i < orders.Length; i++)
            {
                Order order = orders[i];
                sb.AppendLine(
                    $"| {order.Actor} | {order.OrderId.ToString(CultureInfo.InvariantCulture)} | {order.OrderTypeId.ToString(CultureInfo.InvariantCulture)} | {order.PlayerId.ToString(CultureInfo.InvariantCulture)} | {order.Args.Spatial.WorldCm.X.ToString(CultureInfo.InvariantCulture)} | {order.Args.Spatial.WorldCm.Z.ToString(CultureInfo.InvariantCulture)} |");
            }

            return sb.ToString();
        }

        private static string BuildTraceJsonl(
            Entity localPlayer,
            Entity[] actors,
            Entity hovered,
            Entity[] commandSource,
            Order[] orders,
            DispatchVariantEvidence[] dispatchVariants,
            Vector2 targetWorldCm,
            int intentProfileId,
            int dispatchProfileId,
            int schemeId)
        {
            object[] traces =
            {
                new
                {
                    at = "show5-show6.preflight",
                    phase = "engine",
                    status = "pass",
                    map = InteractionShowcaseIds.HubMapId,
                    localPlayer = localPlayer.Id,
                    actors = actors.Select(static e => e.Id).ToArray()
                },
                new
                {
                    at = "show5-show6.scheme",
                    phase = "control-scheme",
                    status = "pass",
                    scheme = DefaultSchemeId,
                    schemeId,
                    intent = DefaultIntentId,
                    intentProfileId
                },
                new
                {
                    at = "show5-show6.collection",
                    phase = "command-source",
                    status = "pass",
                    key = EntityCollectionKeys.CommandSource,
                    owner = localPlayer.Id,
                    rows = commandSource.Select(static e => e.Id).ToArray()
                },
                new
                {
                    at = "show5-show6.input",
                    phase = "production-input",
                    status = "pass",
                    action = "Command",
                    device = "<Mouse>/RightButton",
                    hoveredEntityIgnored = hovered.Id,
                    targetWorldCm = new { x = targetWorldCm.X, z = targetWorldCm.Y }
                },
                new
                {
                    at = "show5-show6.dispatch",
                    phase = "intent-dispatch-order",
                    status = "pass",
                    dispatch = AllTogetherDispatchId,
                    dispatchProfileId,
                    dispatchVariants,
                    sharedOrderId = orders[0].OrderId,
                    orders = orders.Select(order => new
                    {
                        actor = order.Actor.Id,
                        order.OrderId,
                        order.OrderTypeId,
                        order.PlayerId,
                        target = new
                        {
                            x = order.Args.Spatial.WorldCm.X,
                            z = order.Args.Spatial.WorldCm.Z
                        }
                    }).ToArray()
                }
            };

            return string.Join(
                Environment.NewLine,
                traces.Select(trace => JsonSerializer.Serialize(trace, TraceJsonOptions))) + Environment.NewLine;
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[\"Load interaction_showcase_hub\"] --> B[\"Startup active scheme.default\"]",
                "    B --> C[\"Default frame resolves intent.command.default\"]",
                "    C --> D[\"Publish collection.command.source for 3 actors\"]",
                "    D --> E[\"Right mouse Command captured by PlayerInputHandler\"]",
                "    E --> F[\"InputRuntimeSystem writes authoritative snapshot + ground override\"]",
                "    F --> G[\"InteractionShowcaseLocalOrderSourceSystem updates production mapping\"]",
                "    G --> H[\"CommandIntentArbiter.ResolveActiveCommandIntent\"]",
                "    H --> I[\"CommandIntentProfileRegistry.RouteGroup -> moveTo\"]",
                "    I --> J[\"CastDispatchProfileRegistry.SelectDispatchTargets dispatch.all_together\"]",
                "    J --> K[\"OrderQueue assigns shared order id\"]",
                "    K --> L[\"OrderBufferSystem promotes active moveTo on all actors\"]",
                "    L --> M[\"Write battle-report, trace.jsonl, path.mmd\"]",
                "    H -->|no active intent| X[\"Fail: no fallback to legacy mapping\"]",
                "    J -->|dispatch mismatch| Y[\"Fail: no shared fan-out\"]"
            }) + Environment.NewLine;
        }

        private static void AssertLauncherBinding(string repoRoot)
        {
            string launcherConfigPath = Path.Combine(repoRoot, "launcher.config.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfigPath, Encoding.UTF8));
            foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
            {
                if (!binding.TryGetProperty("name", out JsonElement name) ||
                    !string.Equals(name.GetString(), LauncherBindingName, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonElement target = binding.GetProperty("target");
                Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
                Assert.That(target.GetProperty("value").GetString(), Is.EqualTo(LauncherTargetPath));
                Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("InteractionShowcaseMod.csproj"));
                return;
            }

            Assert.Fail($"launcher.config.json does not contain the {LauncherBindingName} binding.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetButton(string path, bool down)
            {
                if (down)
                {
                    _buttons.Add(path);
                }
                else
                {
                    _buttons.Remove(path);
                }
            }
        }

        private readonly record struct DispatchVariantEvidence(
            string ProfileId,
            int RegistryId,
            int SelectedCount,
            bool SharedOrderId,
            bool Sequential);
    }
}
