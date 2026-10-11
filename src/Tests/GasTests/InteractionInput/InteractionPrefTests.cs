using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Persistence;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// InteractionPref entity component: the player-owned order routing preferences
    /// (player-level default intent + dispatch profile, per-ability-template overrides), the
    /// resolution chain reading the possessed representative instead of the active control
    /// scheme, the map-binding seed contract, and the world-save round trip.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionPrefTests
    {
        private const string IntentId = "intent.command.pref.test";
        private const string AltIntentId = "intent.command.pref.alt";
        private const string DispatchId = "dispatch.pref.all";
        private const string AltDispatchId = "dispatch.pref.one";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ContextGroupIdRegistry.Clear();
        }

        // ── Component contract: player default / ability override / mixed ──

        [Test]
        public void Resolve_PlayerDefaultOnly_AppliesToEveryAbilityScope()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);

            Assert.That(pref.ResolveCommandIntent(0), Is.EqualTo(11), "whole-command scope reads the player default");
            Assert.That(pref.ResolveCommandIntent(501), Is.EqualTo(11), "an ability without an override inherits the player default");
            Assert.That(pref.ResolveCastDispatchProfile(0), Is.EqualTo(21));
            Assert.That(pref.ResolveCastDispatchProfile(501), Is.EqualTo(21));
        }

        [Test]
        public void Resolve_AbilityOverride_ReplacesBothFields()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            pref.SetAbilityOverride(502, commandIntentId: 12, castDispatchProfileId: 22, castModeId: 0);

            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(12));
            Assert.That(pref.ResolveCastDispatchProfile(502), Is.EqualTo(22));
            Assert.That(pref.ResolveCommandIntent(503), Is.EqualTo(11), "other abilities keep the player default");
            Assert.That(pref.ResolveCastDispatchProfile(503), Is.EqualTo(21));
        }

        [Test]
        public void Resolve_PartialOverride_InheritsTheUnoverriddenField()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 22, castModeId: 0);
            pref.SetAbilityOverride(505, commandIntentId: 12, castDispatchProfileId: 0, castModeId: 0);

            Assert.That(pref.ResolveCommandIntent(504), Is.EqualTo(11), "intent not overridden inherits the player default");
            Assert.That(pref.ResolveCastDispatchProfile(504), Is.EqualTo(22));
            Assert.That(pref.ResolveCommandIntent(505), Is.EqualTo(12));
            Assert.That(pref.ResolveCastDispatchProfile(505), Is.EqualTo(21), "dispatch not overridden inherits the player default");
        }

        [Test]
        public void Mutators_FailFastOnIllegalWrites()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);

            Assert.That(() => pref.SetPlayerDefault(0, 21), Throws.InvalidOperationException, "the player default is complete: no half defaults");
            Assert.That(() => pref.SetAbilityOverride(0, 12, 0, 0), Throws.InvalidOperationException, "overrides need a positive ability template id");
            Assert.That(() => pref.SetAbilityOverride(506, 0, 0, 0), Throws.InvalidOperationException, "an all-zero override is a silent no-op and is rejected");
            Assert.That(() => pref.SetDefaultCastMode(0), Throws.InvalidOperationException, "a player cast mode of 0 is the unseeded state, not a write");

            for (int i = 1; i <= InteractionPref.MaxAbilityOverrides; i++)
            {
                pref.SetAbilityOverride(i, commandIntentId: 0, castDispatchProfileId: 30 + i, castModeId: 0);
            }

            Assert.That(
                () => pref.SetAbilityOverride(999, 12, 22, 0),
                Throws.InvalidOperationException.With.Message.Contains(nameof(InteractionPref.MaxAbilityOverrides)));

            pref.SetAbilityOverride(1, commandIntentId: 99, castDispatchProfileId: 0, castModeId: 0);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(99), "rewriting an existing override replaces it in place");

            Assert.That(pref.ClearAbilityOverride(1), Is.True);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(11), "a cleared override falls back to the player default");
            Assert.That(pref.ClearAbilityOverride(1), Is.False, "clearing twice reports the missing entry");
            Assert.That(pref.TryGetAbilityOverride(2, out int intent, out int dispatch, out int castMode), Is.True);
            Assert.That((intent, dispatch, castMode), Is.EqualTo((0, 32, 0)), "surviving entries stay readable after a clear");
        }

        [Test]
        public void Resolve_CastMode_PlayerDefaultAndPerAbilityPin()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            Assert.That(pref.ResolveCastMode(0), Is.EqualTo(0), "an unseeded player cast mode stays 0");
            Assert.That(pref.ResolveCastMode(501), Is.EqualTo(0));

            pref.SetDefaultCastMode(7);
            pref.SetAbilityOverride(502, commandIntentId: 0, castDispatchProfileId: 0, castModeId: 9);
            pref.SetAbilityOverride(503, commandIntentId: 12, castDispatchProfileId: 0, castModeId: 0);

            Assert.That(pref.ResolveCastMode(0), Is.EqualTo(7), "whole-command scope reads the player cast mode");
            Assert.That(pref.ResolveCastMode(502), Is.EqualTo(9), "an ability pin wins");
            Assert.That(pref.ResolveCastMode(503), Is.EqualTo(7), "a zero cast-mode pin inherits the player default");
            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(11), "a cast-mode-only pin leaves intent on the player default");
            Assert.That(pref.ResolveCastDispatchProfile(502), Is.EqualTo(21));
        }

        // ── Seed config contract ──

        [Test]
        public void SeedConfig_MissingOrBlankFields_FailFast()
        {
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(new InteractionPrefsConfig { Defaults = null }, "test"),
                Throws.InvalidOperationException.With.Message.Contains("defaults"));

            Assert.That(
                () => InteractionPrefConfigLoader.Validate(new InteractionPrefsConfig
                {
                    Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = " ", CastDispatchProfileId = DispatchId },
                }, "test"),
                Throws.InvalidOperationException.With.Message.Contains("commandIntentId"));
        }

        [Test]
        public void SeedResolution_RequiresInstalledProfiles_AndResolvesInTheKernelIdSpace()
        {
            using var world = World.Create();
            CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
            intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
            var dispatch = NewDispatchRegistry();

            var castModes = NewCastModes();
            var abilities = new AbilityDefinitionRegistry();
            InteractionPrefSeed seed = InteractionPrefConfigLoader.ResolveSeed(
                new InteractionPrefsConfig
                {
                    Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = IntentId, CastDispatchProfileId = DispatchId },
                },
                intents.Intents,
                dispatch,
                castModes,
                abilities);
            Assert.That(seed.CommandIntentId, Is.EqualTo(intents.Intents.ProfileIdRegistry.GetId(IntentId)));
            Assert.That(seed.CastDispatchProfileId, Is.EqualTo(dispatch.ProfileIdRegistry.GetId(DispatchId)));
            Assert.That(seed.DefaultCastModeId, Is.EqualTo(0), "omitted defaults.castModeId stays unseeded");
            Assert.That(seed.AbilityCastModes, Is.Empty);

            Assert.That(
                () => InteractionPrefConfigLoader.ResolveSeed(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = "intent.command.not_installed", CastDispatchProfileId = DispatchId },
                    },
                    intents.Intents,
                    dispatch,
                    castModes,
                    abilities),
                Throws.InvalidOperationException.With.Message.Contains("intent.command.not_installed"));

            Assert.That(
                () => InteractionPrefConfigLoader.ResolveSeed(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = IntentId, CastDispatchProfileId = "dispatch.not_installed" },
                    },
                    intents.Intents,
                    dispatch,
                    castModes,
                    abilities),
                Throws.InvalidOperationException.With.Message.Contains("dispatch.not_installed"));
        }

        [Test]
        public void SeedResolution_OptionalCastMode_ResolvesKnownIdsAndFailsUnknownOnes()
        {
            using var world = World.Create();
            CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
            intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
            var dispatch = NewDispatchRegistry();
            const string mode = "castMode.pref.smart";
            const string abilityMode = "castMode.pref.aim";
            const string abilityName = "ability.interaction_pref.cast_pin";
            var castModes = NewCastModes(mode, abilityMode);
            int abilityId = AbilityIdRegistry.Register(abilityName);
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(abilityId, default);

            InteractionPrefSeed seed = InteractionPrefConfigLoader.ResolveSeed(
                Prefs(IntentId, DispatchId, castModeId: mode, abilityId: abilityName, abilityCastModeId: abilityMode),
                intents.Intents,
                dispatch,
                castModes,
                abilities,
                source: "Input/interaction_prefs.json");
            Assert.That(seed.DefaultCastModeId, Is.EqualTo(castModes.GetId(mode)));
            Assert.That(seed.AbilityCastModes, Has.Length.EqualTo(1));
            Assert.That(seed.AbilityCastModes[0].AbilityTemplateId, Is.EqualTo(abilityId));
            Assert.That(seed.AbilityCastModes[0].CastModeId, Is.EqualTo(castModes.GetId(abilityMode)));

            InteractionPref planted = InteractionPref.FromSeed(in seed);
            Assert.That(planted.ResolveCastMode(0), Is.EqualTo(seed.DefaultCastModeId));
            Assert.That(planted.ResolveCastMode(abilityId), Is.EqualTo(seed.AbilityCastModes[0].CastModeId));
            Assert.That(planted.ResolveCommandIntent(abilityId), Is.EqualTo(seed.CommandIntentId), "a cast-mode pin does not invent an intent");

            InvalidOperationException unknownMode = Assert.Throws<InvalidOperationException>(() =>
                InteractionPrefConfigLoader.ResolveSeed(
                    Prefs(IntentId, DispatchId, castModeId: "castMode.missing"),
                    intents.Intents,
                    dispatch,
                    castModes,
                    abilities,
                    source: "Input/interaction_prefs.json"));
            Assert.That(unknownMode!.Message, Does.Contain("Input/interaction_prefs.json"));
            Assert.That(unknownMode.Message, Does.Contain("defaults.castModeId"));
            Assert.That(unknownMode.Message, Does.Contain("castMode.missing"));

            InvalidOperationException unknownAbility = Assert.Throws<InvalidOperationException>(() =>
                InteractionPrefConfigLoader.ResolveSeed(
                    Prefs(IntentId, DispatchId, abilityId: "ability.not_installed", abilityCastModeId: mode),
                    intents.Intents,
                    dispatch,
                    castModes,
                    abilities,
                    source: "Input/interaction_prefs.json"));
            Assert.That(unknownAbility!.Message, Does.Contain("Input/interaction_prefs.json"));
            Assert.That(unknownAbility.Message, Does.Contain("defaults.abilityCastModes[0].abilityId"));
            Assert.That(unknownAbility.Message, Does.Contain("ability.not_installed"));

            InvalidOperationException unknownPairMode = Assert.Throws<InvalidOperationException>(() =>
                InteractionPrefConfigLoader.ResolveSeed(
                    Prefs(IntentId, DispatchId, abilityId: abilityName, abilityCastModeId: "castMode.missing"),
                    intents.Intents,
                    dispatch,
                    castModes,
                    abilities,
                    source: "Input/interaction_prefs.json"));
            Assert.That(unknownPairMode!.Message, Does.Contain("defaults.abilityCastModes[0].castModeId"));
            Assert.That(unknownPairMode.Message, Does.Contain("castMode.missing"));
        }

        [Test]
        public void SeedConfig_BlankOrDuplicateCastModeFields_FailFast()
        {
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(Prefs(IntentId, DispatchId, castModeId: " "), "test"),
                Throws.InvalidOperationException.With.Message.Contains("defaults.castModeId"));

            Assert.That(
                () => InteractionPrefConfigLoader.Validate(
                    Prefs(IntentId, DispatchId, abilityId: " ability.x", abilityCastModeId: "castMode.x"),
                    "test"),
                Throws.InvalidOperationException.With.Message.Contains("abilityId"));

            var duplicated = Prefs(IntentId, DispatchId);
            duplicated.Defaults!.AbilityCastModes = new List<InteractionPrefAbilityCastModeDefinition>
            {
                new() { AbilityId = "ability.x", CastModeId = "castMode.a" },
                new() { AbilityId = "ability.x", CastModeId = "castMode.b" },
            };
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(duplicated, "Input/interaction_prefs.json"),
                Throws.InvalidOperationException.With.Message.Contains("defaults.abilityCastModes[1].abilityId"));
        }

        [Test]
        public void RootEngineCastModeCatalog_IsEmpty_AndPrefsDeclareNeitherCastField()
        {
            string root = FindRepoRoot();
            string modesPath = Path.Combine(root, "assets", "Input", "cast_modes.json");
            JsonNode? modes = JsonNode.Parse(File.ReadAllText(modesPath));
            Assert.That(modes, Is.InstanceOf<JsonArray>());
            Assert.That(modes!.AsArray(), Is.Empty, "the engine declares no cast modes");

            string prefs = File.ReadAllText(Path.Combine(root, "assets", "Input", "interaction_prefs.json"));
            Assert.That(prefs, Does.Not.Contain("castModeId"));
            Assert.That(prefs, Does.Not.Contain("abilityCastModes"));

            string catalog = File.ReadAllText(Path.Combine(root, "assets", "config_catalog.json"));
            Assert.That(catalog, Does.Contain("Input/cast_modes.json"));
        }

        [Test]
        public void CastModeEntries_RejectExtraFieldsBlankIdsAndDuplicates()
        {
            var registry = NewCastModes();
            Assert.That(CastModeConfigLoader.RegisterEntry(registry, new JsonObject { ["id"] = "castMode.one" }, "Input/cast_modes.json", 0), Is.EqualTo(1));

            Assert.That(
                () => CastModeConfigLoader.RegisterEntry(
                    registry,
                    new JsonObject { ["id"] = "castMode.two", ["kind"] = "smart" },
                    "Input/cast_modes.json",
                    1),
                Throws.InvalidOperationException.With.Message.Contains("id-only").And.Message.Contains("kind"));

            Assert.That(
                () => CastModeConfigLoader.RegisterEntry(registry, new JsonObject { ["id"] = "  " }, "Input/cast_modes.json", 2),
                Throws.InvalidOperationException.With.Message.Contains("id"));

            Assert.That(
                () => CastModeConfigLoader.RegisterEntry(registry, new JsonObject { ["id"] = "castMode.one" }, "Input/cast_modes.json", 3),
                Throws.InvalidOperationException.With.Message.Contains("duplicates"));
        }

        // ── Resolution chain: possessed rep, not the scheme ──

        [Test]
        public void CommandIntentResolution_DefaultFrame_ReadsSeededPlayerPref()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);

            Assert.That(
                harness.Intents.ProfileIdRegistry.GetName(
                    CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref)),
                Is.EqualTo(IntentId));
        }

        [Test]
        public void CommandIntentResolution_SwitchingScheme_NeverChangesRoutingPreferences()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);
            harness.InstallSchemes();

            int intentBefore = CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref);
            int dispatchBefore = harness.Pref.ResolveCastDispatchProfile(abilityTemplateId: 0);

            Assert.That(harness.Schemes!.TrySwitch(harness.SchemeId("scheme.pref.alternate")), Is.True);
            Assert.That(harness.Schemes.ActiveSchemeId, Is.EqualTo(harness.SchemeId("scheme.pref.alternate")), "the switch really happened");

            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, harness.Rep, in harness.Pref),
                Is.EqualTo(intentBefore),
                "the player's routing preference lives on the representative and survives scheme switches");
            Assert.That(harness.Pref.ResolveCastDispatchProfile(abilityTemplateId: 0), Is.EqualTo(dispatchBefore));
        }

        // ── World-save round trip ──

        [Test]
        public void WorldSave_RoundTripsPlayerDefaultAndAbilityOverrides()
        {
            byte[] payload;
            var serializer = new LudotsBinaryWorldSerializer();
            using (var world = World.Create())
            {
                world.Create(NewPref(intent: 4242, dispatch: 8484), NewOverrides());
                payload = serializer.Serialize(world);
            }

            using var restored = serializer.Deserialize(payload);
            Entity restoredEntity = Entity.Null;
            restored.Query(in QueryDescription.Null, entity =>
            {
                if (restored.Has<InteractionPref>(entity))
                {
                    restoredEntity = entity;
                }
            });
            Assert.That(restoredEntity, Is.Not.EqualTo(Entity.Null));

            InteractionPref pref = restored.Get<InteractionPref>(restoredEntity);
            Assert.That(pref.DefaultCommandIntentId, Is.EqualTo(4242), "the raw registry id must round-trip untouched");
            Assert.That(pref.DefaultCastDispatchProfileId, Is.EqualTo(8484));
            Assert.That(pref.DefaultCastModeId, Is.EqualTo(77), "the player cast mode rides the same unmanaged formatter");
            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(4243), "the ability override survives byte-for-byte");
            Assert.That(pref.ResolveCastDispatchProfile(504), Is.EqualTo(8485), "a partial override keeps inheriting the unoverridden field");
            Assert.That(pref.ResolveCastMode(506), Is.EqualTo(79), "a cast-mode-only pin survives the save");
            Assert.That(pref.ResolveCastMode(504), Is.EqualTo(77), "an override that leaves cast mode 0 inherits the player default after restore");
            Assert.That(pref.TryGetAbilityOverride(505, out int intent, out int dispatch, out int castMode), Is.True);
            Assert.That((intent, dispatch, castMode), Is.EqualTo((4244, 0, 78)));
        }

        // ── Harness ──

        private static InteractionPref NewPref(int intent, int dispatch)
        {
            InteractionPref pref = default;
            pref.SetPlayerDefault(intent, dispatch);
            return pref;
        }

        private static InteractionPref NewOverrides()
        {
            InteractionPref pref = default;
            pref.SetPlayerDefault(4242, 8484);
            pref.SetDefaultCastMode(77);
            pref.SetAbilityOverride(502, commandIntentId: 4243, castDispatchProfileId: 8485, castModeId: 0);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 8485, castModeId: 0);
            pref.SetAbilityOverride(505, commandIntentId: 4244, castDispatchProfileId: 0, castModeId: 78);
            pref.SetAbilityOverride(506, commandIntentId: 0, castDispatchProfileId: 0, castModeId: 79);
            return pref;
        }

        private static StringIntRegistry NewCastModes(params string[] ids)
        {
            var registry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            for (int i = 0; i < ids.Length; i++)
            {
                registry.Register(ids[i]);
            }

            return registry;
        }

        private static InteractionPrefsConfig Prefs(
            string intentId,
            string dispatchId,
            string? castModeId = null,
            string? abilityId = null,
            string? abilityCastModeId = null)
        {
            var defaults = new InteractionPrefDefaultsDefinition
            {
                CommandIntentId = intentId,
                CastDispatchProfileId = dispatchId,
                CastModeId = castModeId,
            };
            if (abilityId != null || abilityCastModeId != null)
            {
                defaults.AbilityCastModes = new List<InteractionPrefAbilityCastModeDefinition>
                {
                    new() { AbilityId = abilityId, CastModeId = abilityCastModeId },
                };
            }

            return new InteractionPrefsConfig { Defaults = defaults };
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private static CommandIntentProfileDefinition NewIntentDefinition(string id)
        {
            return new CommandIntentProfileDefinition
            {
                Id = id,
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                },
            };
        }

        private static CastDispatchProfileRegistry NewDispatchRegistry()
        {
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(
                new CastDispatchProfileDefinition
                {
                    Id = DispatchId,
                    Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                },
                new CastDispatchProfileDefinition
                {
                    Id = AltDispatchId,
                    Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                    Scorer = new CastDispatchScorerDefinition
                    {
                        Kind = "utility",
                        Considerations = new List<string> { "distanceToTarget:invert" },
                    },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                }));
            return dispatch;
        }

        private sealed class ChainHarness
        {
            public const int MoveToOrderTypeId = 2;

            public CommandIntentProfileRegistry Intents = null!;
            public CastDispatchProfileRegistry Dispatch = null!;
            public ControlSchemeRuntime? Schemes;
            public InteractionPref Pref;
            public Entity Rep;
            private StringIntRegistry? _schemeIds;

            public static ChainHarness Create(World world)
            {
                var harness = new ChainHarness();
                Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
                harness.Rep = rep;

                CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(AltIntentId)));
                harness.Intents = intents.Intents;
                harness.Dispatch = NewDispatchRegistry();

                InteractionPref pref = default;
                pref.SetPlayerDefault(
                    intents.Intents.ProfileIdRegistry.Register(IntentId),
                    harness.Dispatch.ProfileIdRegistry.GetId(DispatchId));
                world.Add(rep, pref);
                harness.Pref = pref;
                return harness;
            }

            public void InstallSchemes()
            {
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = ChainHarness.MoveToOrderTypeId });
                _schemeIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                Schemes = new ControlSchemeRuntime(_schemeIds, orderTypes);
                Schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        new() { Id = "scheme.pref.primary", InputContexts = new List<string>() },
                        new() { Id = "scheme.pref.alternate", InputContexts = new List<string>() },
                    },
                });
            }

            public int SchemeId(string name) => _schemeIds!.GetId(name);
        }
    }
}
