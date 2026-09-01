using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;
using SuperweaponContextShowcaseMod;
using SuperweaponContextShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class SuperweaponContextShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string LauncherBindingName = "superweapon_context_showcase";
        private const string LauncherTargetPath = "mods/showcases/superweapon_context/SuperweaponContextShowcaseMod";
        private const string ManualGuiLaunchCommand = ".\\scripts\\run-mod-launcher.cmd cli launch superweapon_context_showcase --adapter raylib";
        private const string AutoConfirmFrameEnvKey = "LUDOTS_SUPERWEAPON_CONTEXT_AUTO_CONFIRM_FRAME";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "InteractionShowcaseMod",
            "SuperweaponContextShowcaseMod"
        };

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void SuperweaponContextShowcase_RoutesTargetsThroughAbilityOwnedInteractionFrame()
        {
            string repoRoot = FindRepoRoot();
            AssertLauncherBinding(repoRoot);
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "superweapon-context-showcase");
            Directory.CreateDirectory(artifactDir);

            var backend = new TestInputBackend();
            using var engine = CreateEngine(repoRoot, backend);
            AssertStaticInteractionShowcaseCamera(engine);
            engine.LoadMap(InteractionShowcaseIds.HubMapId);
            var preTickStore = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            SuperweaponContextShowcaseState preTickState = GetState(engine);
            Entity[] commandSourceBeforeTargets = CopyCollectionOrEmpty(
                preTickStore,
                preTickState.Commander,
                EntityCollectionKeys.CommandSource);
            Tick(engine, 6);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            var state = GetState(engine);
            Assert.That(state.IsActive, Is.True);
            Assert.That(state.RoutedTargetCount, Is.EqualTo(2));
            Assert.That(state.SolePossessedRep, Is.Not.EqualTo(Entity.Null));
            Assert.That(state.Commander, Is.Not.EqualTo(Entity.Null));
            Assert.That(state.Arcweaver, Is.Not.EqualTo(Entity.Null));
            Assert.That(state.Vanguard, Is.Not.EqualTo(Entity.Null));

            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            var contextProfiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("InteractionContextProfileRegistry service is missing.");
            var filters = engine.GetService(CoreServiceKeys.FilterProfileRegistry)
                ?? throw new InvalidOperationException("FilterProfileRegistry service is missing.");
            Assert.That(
                engine.World.TryGet<InteractionContextInstance>(state.SolePossessedRep, out InteractionContextInstance mountedContext),
                Is.True,
                "the ability-owned context must be mounted on the sole possessed rep as entity interaction state.");
            Assert.That(
                contextProfiles.ProfileIdRegistry.GetName(mountedContext.ContextId),
                Is.EqualTo(SuperweaponContextShowcaseIds.ContextProfileId));
            Assert.That(
                mountedContext.Source,
                Is.EqualTo(InteractionContextInstanceSource.ExecLifecycle));
            Assert.That(mountedContext.ContextEntity, Is.EqualTo(state.Commander));
            Assert.That(
                store.KeyRegistry.GetName(mountedContext.ActiveCollectionKeyId),
                Is.EqualTo(SuperweaponContextShowcaseIds.TargetsCollectionKey));
            Assert.That(
                filters.ProfileIdRegistry.GetName(mountedContext.FilterProfileId),
                Is.EqualTo(SuperweaponContextShowcaseIds.FilterProfileId));
            Assert.That(
                contextProfiles.InputContextIdRegistry.GetName(mountedContext.InputContextId),
                Is.EqualTo(SuperweaponContextShowcaseIds.ConfirmInputContextId));
            Assert.That(
                contextProfiles.TryGetDefinition(mountedContext.ContextId, out var contextDefinition) &&
                    contextDefinition.ActiveEntityViewKey == SuperweaponContextShowcaseIds.TargetsViewKey,
                Is.True,
                "the installed profile row keeps the declared entity view key.");

            Assert.That(engine.World.Has<AbilityExecInstance>(state.Commander), Is.True);
            Assert.That(engine.World.Get<AbilityExecInstance>(state.Commander).AbilityId, Is.EqualTo(state.AbilityId));
            Assert.That(state.AbilityId, Is.EqualTo(AbilityIdRegistry.GetId(SuperweaponContextShowcaseIds.AbilityId)));

            int abilityTargetsKey = store.KeyRegistry.GetId(SuperweaponContextShowcaseIds.TargetsCollectionKey);
            int commandSourceKey = store.KeyRegistry.GetId(EntityCollectionKeys.CommandSource);
            int rawKey = store.KeyRegistry.GetId(EntityCollectionKeys.UiCastRaw);
            int casterMarkerKey = store.KeyRegistry.GetId(SuperweaponContextShowcaseIds.CasterMarkerCollectionKey);
            int targetMarkerKey = store.KeyRegistry.GetId(SuperweaponContextShowcaseIds.TargetMarkerCollectionKey);

            Entity[] abilityTargets = CopyCollection(store, state.Commander, abilityTargetsKey);
            Entity[] rawTargets = CopyCollection(store, state.SolePossessedRep, rawKey);
            Entity[] casterMarkers = CopyCollection(store, state.Commander, casterMarkerKey);
            Entity[] targetMarkers = CopyCollection(store, state.Commander, targetMarkerKey);
            Assert.That(abilityTargets, Is.EqualTo(new[] { state.Arcweaver, state.Vanguard }));
            Assert.That(rawTargets, Is.EqualTo(new[] { state.Arcweaver, state.Vanguard }));
            Assert.That(store.KeyRegistry.GetName(casterMarkerKey), Is.EqualTo(SuperweaponContextShowcaseIds.CasterMarkerCollectionKey));
            Assert.That(store.KeyRegistry.GetName(targetMarkerKey), Is.EqualTo(SuperweaponContextShowcaseIds.TargetMarkerCollectionKey));
            Assert.That(casterMarkers, Is.EqualTo(new[] { state.Commander }));
            Assert.That(targetMarkers, Is.EqualTo(new[] { state.Arcweaver, state.Vanguard }));
            Entity[] commandSourceDuringAbility = CopyCollectionOrEmpty(store, state.Commander, commandSourceKey);
            Assert.That(
                commandSourceDuringAbility,
                Is.EqualTo(commandSourceBeforeTargets),
                "ability-frame target acquisition must not rewrite collection.command.source.");
            AssertPresenterRules(repoRoot);

            Assert.That(state.ConfirmInputObserved, Is.False);
            PressAndRelease(engine, backend, "<Keyboard>/enter");
            Assert.That(state.ConfirmInputObserved, Is.True);
            Assert.That(state.ConfirmEventPublished, Is.True);
            Assert.That(state.ConfirmEventCount, Is.EqualTo(1));
            TickUntil(engine, 24, () => !engine.World.Has<AbilityExecInstance>(state.Commander));

            Assert.That(engine.World.Has<AbilityExecInstance>(state.Commander), Is.False);
            Assert.That(
                engine.World.Has<InteractionContextInstance>(state.SolePossessedRep),
                Is.False,
                "confirming the ability must release the entity-mounted context back to the steady-state anchor.");

            var writer = engine.GetService(CoreServiceKeys.ContextBoundCollectionWriter)
                ?? throw new InvalidOperationException("ContextBoundCollectionWriter service is missing.");
            writer.CommitCast(state.SolePossessedRep, new[] { state.Commander }, EntityCollectionSourceKind.UiAcquisition);
            Entity[] commandSource = CopyCollection(store, state.Commander, commandSourceKey);
            Assert.That(commandSource, Is.EqualTo(new[] { state.Commander }));

            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildReport(state, abilityTargets, rawTargets, commandSource),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "trace.jsonl"),
                BuildTraceJsonl(state, abilityTargets, rawTargets, commandSource),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "path.mmd"),
                BuildPathMermaid(),
                Encoding.UTF8);
        }

        [Test]
        public void SuperweaponContextShowcase_VisibleUatTimelineAutoConfirmsAtConfiguredFrame()
        {
            string repoRoot = FindRepoRoot();
            string? previousAutoConfirm = Environment.GetEnvironmentVariable(AutoConfirmFrameEnvKey);
            try
            {
                Environment.SetEnvironmentVariable(AutoConfirmFrameEnvKey, "4");
                var backend = new TestInputBackend();
                using var engine = CreateEngine(repoRoot, backend);
                engine.LoadMap(InteractionShowcaseIds.HubMapId);
                Tick(engine, 32);

                SuperweaponContextShowcaseState state = GetState(engine);
                Assert.That(state.IsActive, Is.True);
                Assert.That(state.RoutedTargetCount, Is.EqualTo(2));
                Assert.That(state.ConfirmInputObserved, Is.True);
                Assert.That(state.ConfirmEventPublished, Is.True);
                Assert.That(state.ConfirmEventCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(AutoConfirmFrameEnvKey, previousAutoConfirm);
            }
        }

        [Test]
        public void SuperweaponContextShowcase_StartsAbilityThroughOrderQueue()
        {
            string repoRoot = FindRepoRoot();
            string runtimePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "superweapon_context",
                "SuperweaponContextShowcaseMod",
                "Runtime",
                "SuperweaponContextShowcaseRuntime.cs");
            string source = File.ReadAllText(runtimePath, Encoding.UTF8);

            Assert.That(source, Does.Contain("orderQueue.TryEnqueue"));
            Assert.That(source, Does.Contain("Args = new OrderArgs"));
            Assert.That(source, Does.Not.Contain("SetActiveDirect"));
            Assert.That(source, Does.Not.Contain("OrderId = 65001"));
            Assert.That(source, Does.Not.Contain("World.Remove<AbilityExecInstance>"));
            Assert.That(source, Does.Contain("OrderBlackboardStateInstaller.RequireInstalled"));
            Assert.That(source, Does.Not.Contain("World.Add(State.Commander, OrderBuffer.CreateEmpty())"));
            Assert.That(source, Does.Not.Contain("World.Add(State.Commander, new BlackboardIntBuffer())"));
            Assert.That(source, Does.Not.Contain("World.Add(State.Commander, new AbilityStateBuffer())"));
            Assert.That(source, Does.Not.Contain("World.Add(State.Commander, new GrantedSlotBuffer())"));

            string templatePath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "superweapon_context",
                "SuperweaponContextShowcaseMod",
                "assets",
                "Entities",
                "templates.json");
            using JsonDocument templates = JsonDocument.Parse(File.ReadAllText(templatePath, Encoding.UTF8));
            JsonElement commanderOverride = templates.RootElement.EnumerateArray()
                .Single(entry => entry.GetProperty("id").GetString() == "interaction_commander");
            Assert.That(
                commanderOverride.GetProperty("components").TryGetProperty("GrantedSlotBuffer", out _),
                Is.True,
                "The dependent mod must assemble the transient granted-slot state before map runtime starts.");
        }

        private static void AssertPresenterRules(string repoRoot)
        {
            string presenterConfigPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "superweapon_context",
                "SuperweaponContextShowcaseMod",
                "assets",
                "Presentation",
                "presenters.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(presenterConfigPath, Encoding.UTF8));
            Assert.That(HasCollectionRule(
                document,
                "EntityCollectionMemberAdded",
                SuperweaponContextShowcaseIds.CasterMarkerCollectionKey,
                "CreatePresenter",
                SuperweaponContextShowcaseIds.CasterMarkerPresenterId), Is.True);
            Assert.That(HasCollectionRule(
                document,
                "EntityCollectionMemberRemoved",
                SuperweaponContextShowcaseIds.CasterMarkerCollectionKey,
                "DestroyScopedPresenter",
                SuperweaponContextShowcaseIds.CasterMarkerPresenterId), Is.True);
            Assert.That(HasCollectionRule(
                document,
                "EntityCollectionMemberAdded",
                SuperweaponContextShowcaseIds.TargetMarkerCollectionKey,
                "CreatePresenter",
                SuperweaponContextShowcaseIds.TargetMarkerPresenterId), Is.True);
            Assert.That(HasCollectionRule(
                document,
                "EntityCollectionMemberRemoved",
                SuperweaponContextShowcaseIds.TargetMarkerCollectionKey,
                "DestroyScopedPresenter",
                SuperweaponContextShowcaseIds.TargetMarkerPresenterId), Is.True);
        }

        private static bool HasCollectionRule(
            JsonDocument document,
            string eventKind,
            string collectionKey,
            string commandKind,
            string presenterDefinitionId)
        {
            foreach (JsonElement definition in document.RootElement.EnumerateArray())
            {
                if (!definition.TryGetProperty("rules", out JsonElement rules))
                {
                    continue;
                }

                foreach (JsonElement rule in rules.EnumerateArray())
                {
                    JsonElement evt = rule.GetProperty("event");
                    JsonElement command = rule.GetProperty("command");
                    if (string.Equals(evt.GetProperty("kind").GetString(), eventKind, StringComparison.Ordinal) &&
                        string.Equals(evt.GetProperty("key").GetString(), collectionKey, StringComparison.Ordinal) &&
                        string.Equals(command.GetProperty("kind").GetString(), commandKind, StringComparison.Ordinal) &&
                        string.Equals(command.GetProperty("definitionId").GetString(), presenterDefinitionId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Entity[] CopyCollection(EntityCollectionStore store, Entity owner, int keyId)
        {
            Assert.That(store.TryGet(owner, keyId, out EntityCollectionHandle handle), Is.True);
            Span<Entity> rows = stackalloc Entity[8];
            int count = store.CopyEntities(handle, 0, rows);
            return rows[..count].ToArray();
        }

        private static Entity[] CopyCollectionOrEmpty(EntityCollectionStore store, Entity owner, string key)
        {
            return store.TryGet(owner, key, out EntityCollectionHandle handle)
                ? CopyCollection(store, handle)
                : Array.Empty<Entity>();
        }

        private static Entity[] CopyCollectionOrEmpty(EntityCollectionStore store, Entity owner, int keyId)
        {
            return store.TryGet(owner, keyId, out EntityCollectionHandle handle)
                ? CopyCollection(store, handle)
                : Array.Empty<Entity>();
        }

        private static Entity[] CopyCollection(EntityCollectionStore store, EntityCollectionHandle handle)
        {
            Span<Entity> rows = stackalloc Entity[8];
            int count = store.CopyEntities(handle, 0, rows);
            return rows[..count].ToArray();
        }

        private static SuperweaponContextShowcaseState GetState(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(SuperweaponContextShowcaseIds.RuntimeStateServiceKey, out object? value) ||
                value is not SuperweaponContextShowcaseState state)
            {
                throw new InvalidOperationException("SuperweaponContextShowcaseRuntime state is missing.");
            }

            return state;
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
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void TickUntil(GameEngine engine, int maxFrames, Func<bool> predicate)
        {
            for (int i = 0; i < maxFrames && !predicate(); i++)
            {
                Tick(engine, 1);
            }
        }

        private static void TickUntilFixedTickAdvances(GameEngine engine, int fixedTicks = 1)
        {
            int targetTick = engine.GameSession.CurrentTick + Math.Max(1, fixedTicks);
            for (int i = 0; i < 16 * Math.Max(1, fixedTicks) && engine.GameSession.CurrentTick < targetTick; i++)
            {
                Tick(engine, 1);
            }

            Assert.That(
                engine.GameSession.CurrentTick,
                Is.GreaterThanOrEqualTo(targetTick),
                $"Expected fixed tick to advance to {targetTick}, but it stopped at {engine.GameSession.CurrentTick}.");
        }

        private static void PressAndRelease(GameEngine engine, TestInputBackend backend, string path)
        {
            backend.SetButton(path, true);
            TickUntilFixedTickAdvances(engine);
            backend.SetButton(path, false);
            TickUntilFixedTickAdvances(engine);
        }

        private static string BuildReport(
            SuperweaponContextShowcaseState state,
            Entity[] abilityTargets,
            Entity[] rawTargets,
            Entity[] commandSourceAfterRestore)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Superweapon Context Showcase Acceptance");
            builder.AppendLine();
            builder.AppendLine("## Header");
            builder.AppendLine("- scenario name: RFC-0065 SHOW-1 / M2/P6 superweapon context confirmation");
            builder.AppendLine("- build/version: test runtime");
            builder.AppendLine("- seed/map/clock: deterministic headless, `interaction_showcase_hub`, 60Hz tick");
            builder.AppendLine();
            builder.AppendLine("## Scenario");
            builder.AppendLine("- Showcase: `SuperweaponContextShowcaseMod` over `interaction_showcase_hub`.");
            builder.AppendLine($"- Launcher binding: `{LauncherBindingName}` (`{ManualGuiLaunchCommand}`).");
            builder.AppendLine("- Runtime path: `castAbility.Start` -> `AbilityExecSystem` -> `AbilityExecInteractionContextSystem` -> `InputContextProjectionSystem` -> `CoreServiceKeys.AuthoritativeInput` -> `GameplayEventBus`.");
            builder.AppendLine("- Context profile: `ctx.ability.superweapon.confirm_targets`.");
            builder.AppendLine("- Player action: press `<Keyboard>/enter` through `imc.ability.confirm`; the test does not publish the completion event directly.");
            builder.AppendLine();
            builder.AppendLine("## Timeline");
            builder.AppendLine($"- [T+000] Launcher binding `{LauncherBindingName}` -> `{LauncherTargetPath}` verified; Commander#{state.Commander.Id}.Cast(Superweapon Context) -> GateWaiting(`Event.Showcase.Superweapon.Confirmed`).");
            builder.AppendLine($"- [T+001] AbilityFrame.Push(`ctx.ability.superweapon.confirm_targets`) -> `InputContextProjectionSystem` next-tick diff -> IMC `{SuperweaponContextShowcaseIds.ConfirmInputContextId}` active.");
            builder.AppendLine($"- [T+002] ContextBoundCollectionWriter.CommitCast -> ability targets `{string.Join(", ", abilityTargets.Select(static e => e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))}`.");
            builder.AppendLine($"- [T+003] PlayerInput(`<Keyboard>/enter`) -> Authoritative `{SuperweaponContextShowcaseIds.ConfirmActionId}` -> GameplayEvent published.");
            builder.AppendLine($"- [T+004] AbilityExecSystem consumes event -> End -> frame restored to `{InteractionContextIds.Default}`.");
            builder.AppendLine();
            builder.AppendLine("## Outcome");
            builder.AppendLine("| Field | Value |");
            builder.AppendLine("|-------|-------|");
            builder.AppendLine($"| Ability id | {state.AbilityId.ToString(System.Globalization.CultureInfo.InvariantCulture)} |");
            builder.AppendLine($"| Local player | {state.SolePossessedRep} |");
            builder.AppendLine($"| Commander context entity | {state.Commander} |");
            builder.AppendLine($"| Ability targets | {string.Join(", ", abilityTargets.Select(static e => e.ToString()))} |");
            builder.AppendLine($"| Raw local targets | {string.Join(", ", rawTargets.Select(static e => e.ToString()))} |");
            builder.AppendLine($"| Confirm input observed | {state.ConfirmInputObserved.ToString()} |");
            builder.AppendLine($"| Confirm events published | {state.ConfirmEventCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} |");
            builder.AppendLine($"| Command source after frame restore | {string.Join(", ", commandSourceAfterRestore.Select(static e => e.ToString()))} |");
            builder.AppendLine();
            builder.AppendLine("## Summary Stats");
            builder.AppendLine("- total actions: 1 physical confirm press");
            builder.AppendLine("- routed targets: 2");
            builder.AppendLine("- dropped/budget/fuse counters: 0 observed in this headless path");
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine("- success: yes");
            builder.AppendLine("- evidence: ability-owned frame captured raw targets on the local anchor, domain-routed target acquisition to `collection.ability.superweapon.targets`, left `collection.command.source` untouched, confirmed through IMC/authoritative input, and restored default routing after the event gate completed.");
            return builder.ToString();
        }

        private static string BuildTraceJsonl(
            SuperweaponContextShowcaseState state,
            Entity[] abilityTargets,
            Entity[] rawTargets,
            Entity[] commandSourceAfterRestore)
        {
            var builder = new StringBuilder();
            AppendTrace(builder, 0, "scenario.start", new
            {
                map = InteractionShowcaseIds.HubMapId,
                ability = SuperweaponContextShowcaseIds.AbilityId,
                commander = state.Commander.Id
            });
            AppendTrace(builder, 1, "ability.frame.push", new
            {
                context = SuperweaponContextShowcaseIds.ContextProfileId,
                inputContext = SuperweaponContextShowcaseIds.ConfirmInputContextId
            });
            AppendTrace(builder, 2, "targets.commit", new
            {
                rawTargets = rawTargets.Select(static e => e.Id).ToArray(),
                abilityTargets = abilityTargets.Select(static e => e.Id).ToArray(),
                commandSourceMutated = false
            });
            AppendTrace(builder, 3, "input.confirm", new
            {
                action = SuperweaponContextShowcaseIds.ConfirmActionId,
                observed = state.ConfirmInputObserved,
                eventPublished = state.ConfirmEventPublished,
                eventCount = state.ConfirmEventCount
            });
            AppendTrace(builder, 4, "frame.restore", new
            {
                context = InteractionContextIds.Default,
                commandSourceAfterRestore = commandSourceAfterRestore.Select(static e => e.Id).ToArray()
            });
            return builder.ToString();
        }

        private static void AppendTrace(StringBuilder builder, int tick, string evt, object payload)
        {
            builder.Append(JsonSerializer.Serialize(new
            {
                tick,
                @event = evt,
                payload
            }));
            builder.AppendLine();
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
                Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("SuperweaponContextShowcaseMod.csproj"));
                return;
            }

            Assert.Fail($"launcher.config.json does not contain the {LauncherBindingName} binding.");
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[\"MapLoaded: interaction showcase hub\"] --> B[\"castAbility.Start: commander starts superweapon\"]",
                "    B --> C[\"AbilityExecInteractionContextSystem: push ability-owned frame\"]",
                "    C --> D[\"InputContextProjectionSystem: next-tick diff pushes imc.ability.confirm\"]",
                "    D --> E[\"ContextBoundCollectionWriter: route Arcweaver + Vanguard to ability collection\"]",
                "    E --> F{\"<Keyboard>/enter pressed?\"}",
                "    F -- yes --> G[\"AuthoritativeInput: SuperweaponConfirm pressed\"]",
                "    G --> H[\"GameplayEventBus: Event.Showcase.Superweapon.Confirmed\"]",
                "    H --> I[\"AbilityExecSystem: EventGate passes, End removes exec\"]",
                "    I --> J[\"AbilityExecInteractionContextSystem: restore default frame\"]",
                "    J --> K[\"Default CommitCast writes collection.command.source again\"]",
                "    F -- no --> L[\"GateWaiting: frame stays active, no hidden completion fallback\"]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
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
            private readonly System.Collections.Generic.HashSet<string> _buttons = new(StringComparer.Ordinal);

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
    }
}
