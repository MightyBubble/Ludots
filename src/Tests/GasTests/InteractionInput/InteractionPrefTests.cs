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
            pref.SetAbilityOverride(502, commandIntentId: 12, castDispatchProfileId: 22);

            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(12));
            Assert.That(pref.ResolveCastDispatchProfile(502), Is.EqualTo(22));
            Assert.That(pref.ResolveCommandIntent(503), Is.EqualTo(11), "other abilities keep the player default");
            Assert.That(pref.ResolveCastDispatchProfile(503), Is.EqualTo(21));
        }

        [Test]
        public void Resolve_PartialOverride_InheritsTheUnoverriddenField()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 22);
            pref.SetAbilityOverride(505, commandIntentId: 12, castDispatchProfileId: 0);

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
            Assert.That(() => pref.SetAbilityOverride(0, 12, 0), Throws.InvalidOperationException, "overrides need a positive ability template id");
            Assert.That(() => pref.SetAbilityOverride(506, 0, 0), Throws.InvalidOperationException, "an all-zero override is a silent no-op and is rejected");
            for (int i = 1; i <= InteractionPref.MaxAbilityOverrides; i++)
            {
                pref.SetAbilityOverride(i, commandIntentId: 0, castDispatchProfileId: 30 + i);
            }

            Assert.That(
                () => pref.SetAbilityOverride(999, 12, 22),
                Throws.InvalidOperationException.With.Message.Contains(nameof(InteractionPref.MaxAbilityOverrides)));

            pref.SetAbilityOverride(1, commandIntentId: 99, castDispatchProfileId: 0);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(99), "rewriting an existing override replaces it in place");

            Assert.That(pref.ClearAbilityOverride(1), Is.True);
            Assert.That(pref.ResolveCommandIntent(1), Is.EqualTo(11), "a cleared override falls back to the player default");
            Assert.That(pref.ClearAbilityOverride(1), Is.False, "clearing twice reports the missing entry");
            Assert.That(pref.TryGetAbilityOverride(2, out int intent, out int dispatch), Is.True);
            Assert.That((intent, dispatch), Is.EqualTo((0, 32)), "surviving entries stay readable after a clear");
        }

        [Test]
        public void Resolve_Pref_PlayerLevelAndAbilityPin()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            Assert.That(pref.ResolvePref(0, 3), Is.EqualTo(0), "an unseeded key stays 0");
            Assert.That(pref.ResolvePref(501, 3), Is.EqualTo(0));

            pref.SetPlayerPref(3, 7);
            pref.SetAbilityPref(502, 3, 9);
            pref.SetAbilityOverride(503, commandIntentId: 12, castDispatchProfileId: 0);

            Assert.That(pref.ResolvePref(0, 3), Is.EqualTo(7), "whole-command scope reads the player preference");
            Assert.That(pref.ResolvePref(502, 3), Is.EqualTo(9), "an ability pin wins");
            Assert.That(pref.ResolvePref(503, 3), Is.EqualTo(7), "an ability without this key inherits the player value");
            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(11), "a preference pin does not invent an intent");
            Assert.That(pref.ResolveCastDispatchProfile(502), Is.EqualTo(21));
        }

        [Test]
        public void PrefMutators_FailFastWhenIdsAreIllegalOrTheTableIsFull()
        {
            InteractionPref pref = NewPref(intent: 11, dispatch: 21);
            Assert.That(() => pref.SetPlayerPref(0, 1), Throws.InvalidOperationException);
            Assert.That(() => pref.SetAbilityPref(0, 1, 1), Throws.InvalidOperationException);

            for (int i = 1; i <= InteractionPref.MaxPlayerPrefs; i++)
            {
                pref.SetPlayerPref(i, 10 + i);
            }

            Assert.That(
                () => pref.SetPlayerPref(99, 1),
                Throws.InvalidOperationException.With.Message.Contains(nameof(InteractionPref.MaxPlayerPrefs)));
            pref.SetPlayerPref(1, 77);
            Assert.That(pref.ResolvePref(0, 1), Is.EqualTo(77), "replacing a key does not grow the table");

            for (int i = 1; i <= InteractionPref.MaxAbilityPrefs; i++)
            {
                pref.SetAbilityPref(1000 + i, 1, 20 + i);
            }

            Assert.That(
                () => pref.SetAbilityPref(2000, 1, 1),
                Throws.InvalidOperationException.With.Message.Contains(nameof(InteractionPref.MaxAbilityPrefs)));
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
            var abilities = new AbilityDefinitionRegistry();
            InteractionPrefSeed seed = Resolve(BarePrefs(), intents.Intents, dispatch, abilities);
            Assert.That(seed.CommandIntentId, Is.EqualTo(intents.Intents.ProfileIdRegistry.GetId(IntentId)));
            Assert.That(seed.CastDispatchProfileId, Is.EqualTo(dispatch.ProfileIdRegistry.GetId(DispatchId)));
            Assert.That(seed.PlayerPrefs, Is.Empty);
            Assert.That(seed.AbilityPrefs, Is.Empty);

            Assert.That(
                () => Resolve(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = "intent.command.not_installed", CastDispatchProfileId = DispatchId },
                    },
                    intents.Intents,
                    dispatch,
                    abilities),
                Throws.InvalidOperationException.With.Message.Contains("intent.command.not_installed"));

            Assert.That(
                () => Resolve(
                    new InteractionPrefsConfig
                    {
                        Defaults = new InteractionPrefDefaultsDefinition { CommandIntentId = IntentId, CastDispatchProfileId = "dispatch.not_installed" },
                    },
                    intents.Intents,
                    dispatch,
                    abilities),
                Throws.InvalidOperationException.With.Message.Contains("dispatch.not_installed"));
        }

        [Test]
        public void SeedResolution_ContextValuedKey_ResolvesProfilesAndFailsUnknownOnes()
        {
            using var world = World.Create();
            CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
            intents.Intents.Install(CommandIntentProfileTests.Harness.Config(NewIntentDefinition(IntentId)));
            var dispatch = NewDispatchRegistry();
            const string key = "pref.test.gesture";
            const string smart = "interaction.context.pref.smart";
            const string click = "interaction.context.pref.click";
            const string abilityName = "ability.interaction_pref.pin";
            int abilityId = AbilityIdRegistry.Register(abilityName);
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(abilityId, default);
            InteractionContextProfileRegistry profiles = InstalledProfiles(smart, click);
            var catalog = new InteractionPrefCatalog();

            InteractionPrefSeed seed = InteractionPrefConfigLoader.ResolveSeed(
                Prefs(key, smart, abilityName, click, contextValued: true, values: new[] { smart, click }),
                intents.Intents,
                dispatch,
                catalog,
                profiles,
                abilities,
                source: "Input/interaction_prefs.json");
            Assert.That(seed.PlayerPrefs, Has.Length.EqualTo(1));
            Assert.That(seed.PlayerPrefs[0].KeyId, Is.EqualTo(catalog.Keys.GetId(key)));
            Assert.That(seed.PlayerPrefs[0].ValueId, Is.EqualTo(catalog.Values.GetId(smart)));
            Assert.That(seed.AbilityPrefs[0].AbilityTemplateId, Is.EqualTo(abilityId));
            Assert.That(seed.AbilityPrefs[0].ValueId, Is.EqualTo(catalog.Values.GetId(click)));
            Assert.That(catalog.IsContextValued(catalog.Keys.GetId(key)), Is.True);

            InteractionPref planted = InteractionPref.FromSeed(in seed);
            Assert.That(planted.ResolvePref(0, seed.PlayerPrefs[0].KeyId), Is.EqualTo(seed.PlayerPrefs[0].ValueId));
            Assert.That(planted.ResolvePref(abilityId, seed.PlayerPrefs[0].KeyId), Is.EqualTo(seed.AbilityPrefs[0].ValueId));
            Assert.That(planted.ResolveCommandIntent(abilityId), Is.EqualTo(seed.CommandIntentId), "a preference pin does not invent an intent");

            InvalidOperationException unknownProfile = Assert.Throws<InvalidOperationException>(() =>
                InteractionPrefConfigLoader.ResolveSeed(
                    Prefs(key, "interaction.context.missing", abilityName: null, abilityValue: null, contextValued: true, values: new[] { "interaction.context.missing" }),
                    intents.Intents,
                    dispatch,
                    new InteractionPrefCatalog(),
                    profiles,
                    abilities,
                    source: "Input/interaction_prefs.json"));
            Assert.That(unknownProfile!.Message, Does.Contain(key));
            Assert.That(unknownProfile.Message, Does.Contain("interaction.context.missing"));

            InvalidOperationException unknownAbility = Assert.Throws<InvalidOperationException>(() =>
                InteractionPrefConfigLoader.ResolveSeed(
                    Prefs(key, smart, "ability.not_installed", smart, contextValued: true, values: new[] { smart, click }),
                    intents.Intents,
                    dispatch,
                    new InteractionPrefCatalog(),
                    profiles,
                    abilities,
                    source: "Input/interaction_prefs.json"));
            Assert.That(unknownAbility!.Message, Does.Contain("ability.not_installed"));
            Assert.That(unknownAbility.Message, Does.Contain("defaults.abilityPrefs"));
        }

        [Test]
        public void SeedConfig_DuplicateKeysAndUndeclaredValues_FailFast()
        {
            const string key = "pref.test.gesture";
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(
                    Prefs(key, " ", abilityName: null, abilityValue: null, contextValued: false, values: new[] { "symbol.a" }),
                    "test"),
                Throws.InvalidOperationException.With.Message.Contains("defaults.prefs"));

            var duplicated = Prefs(key, "symbol.a", abilityName: null, abilityValue: null, contextValued: false, values: new[] { "symbol.a", "symbol.a" });
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(duplicated, "Input/interaction_prefs.json"),
                Throws.InvalidOperationException.With.Message.Contains("duplicates value"));

            var undeclared = BarePrefs();
            undeclared.Defaults!.Prefs = new List<InteractionPrefPairDefinition>
            {
                new() { Key = "pref.missing", Value = "symbol.a" },
            };
            Assert.That(
                () => InteractionPrefConfigLoader.Validate(undeclared, "Input/interaction_prefs.json"),
                Throws.InvalidOperationException.With.Message.Contains("pref.missing"));
        }

        [Test]
        public void RootEngine_DoesNotShipACastModeCatalog()
        {
            string root = FindRepoRoot();
            Assert.That(File.Exists(Path.Combine(root, "assets", "Input", "cast_modes.json")), Is.False);

            string prefs = File.ReadAllText(Path.Combine(root, "assets", "Input", "interaction_prefs.json"));
            Assert.That(prefs, Does.Not.Contain("castModeId"));
            Assert.That(prefs, Does.Not.Contain("abilityCastModes"));

            string catalog = File.ReadAllText(Path.Combine(root, "assets", "config_catalog.json"));
            Assert.That(catalog, Does.Not.Contain("Input/cast_modes.json"));
        }

        // ── Resolution chain: possessed rep, not the scheme ──

        [Test]
        public void CommandIntentResolution_DefaultFrame_ReadsSeededPlayerPref()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);

            Assert.That(
                harness.Intents.ProfileIdRegistry.GetName(
                    InteractionContextInstance.ResolveCommandIntent(world, harness.Rep, in harness.Pref)),
                Is.EqualTo(IntentId));
        }

        [Test]
        public void CommandIntentResolution_SwitchingScheme_NeverChangesRoutingPreferences()
        {
            using var world = World.Create();
            ChainHarness harness = ChainHarness.Create(world);
            harness.InstallSchemes();

            int intentBefore = InteractionContextInstance.ResolveCommandIntent(world, harness.Rep, in harness.Pref);
            int dispatchBefore = harness.Pref.ResolveCastDispatchProfile(abilityTemplateId: 0);

            Assert.That(harness.Schemes!.TrySwitch(harness.SchemeId("scheme.pref.alternate")), Is.True);
            Assert.That(harness.Schemes.ActiveSchemeId, Is.EqualTo(harness.SchemeId("scheme.pref.alternate")), "the switch really happened");

            Assert.That(
                InteractionContextInstance.ResolveCommandIntent(world, harness.Rep, in harness.Pref),
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
            Assert.That(pref.ResolvePref(0, 3), Is.EqualTo(77), "the player preference rides the same unmanaged formatter");
            Assert.That(pref.ResolveCommandIntent(502), Is.EqualTo(4243), "the ability override survives byte-for-byte");
            Assert.That(pref.ResolveCastDispatchProfile(504), Is.EqualTo(8485), "a partial override keeps inheriting the unoverridden field");
            Assert.That(pref.ResolvePref(506, 3), Is.EqualTo(79), "an ability preference pin survives the save");
            Assert.That(pref.ResolvePref(504, 3), Is.EqualTo(77), "an ability without the key inherits the player value after restore");
            Assert.That(pref.ResolvePref(505, 4), Is.EqualTo(78));
            Assert.That(pref.TryGetAbilityOverride(505, out int intent, out int dispatch), Is.True);
            Assert.That((intent, dispatch), Is.EqualTo((4244, 0)));
            Assert.That(pref.ResolveCommandIntent(506), Is.EqualTo(4242), "a preference pin does not invent an intent");
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
            pref.SetPlayerPref(3, 77);
            pref.SetAbilityOverride(502, commandIntentId: 4243, castDispatchProfileId: 8485);
            pref.SetAbilityOverride(504, commandIntentId: 0, castDispatchProfileId: 8485);
            pref.SetAbilityOverride(505, commandIntentId: 4244, castDispatchProfileId: 0);
            pref.SetAbilityPref(505, 4, 78);
            pref.SetAbilityPref(506, 3, 79);
            return pref;
        }

        private static InteractionPrefsConfig BarePrefs()
        {
            return new InteractionPrefsConfig
            {
                Defaults = new InteractionPrefDefaultsDefinition
                {
                    CommandIntentId = IntentId,
                    CastDispatchProfileId = DispatchId,
                },
            };
        }

        private static InteractionPrefsConfig Prefs(
            string key,
            string value,
            string? abilityName,
            string? abilityValue,
            bool contextValued,
            string[] values)
        {
            var config = BarePrefs();
            config.Keys = new List<InteractionPrefKeyDefinition>
            {
                new()
                {
                    Id = key,
                    ContextValued = contextValued,
                    Values = new List<string>(values),
                },
            };
            config.Defaults!.Prefs = new List<InteractionPrefPairDefinition>
            {
                new() { Key = key, Value = value },
            };
            if (abilityName != null || abilityValue != null)
            {
                config.Defaults.AbilityPrefs = new List<InteractionPrefAbilityPrefDefinition>
                {
                    new() { AbilityId = abilityName, Key = key, Value = abilityValue },
                };
            }

            return config;
        }

        private static InteractionPrefSeed Resolve(
            InteractionPrefsConfig config,
            CommandIntentProfileRegistry intents,
            CastDispatchProfileRegistry dispatch,
            AbilityDefinitionRegistry abilities)
        {
            return InteractionPrefConfigLoader.ResolveSeed(
                config,
                intents,
                dispatch,
                new InteractionPrefCatalog(),
                InstalledProfiles(),
                abilities);
        }

        private static InteractionContextProfileRegistry InstalledProfiles(params string[] ids)
        {
            var profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var profiles = new InteractionContextProfileRegistry(profileIds);
            var definitions = new List<InteractionContextProfileDefinition>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                definitions.Add(new InteractionContextProfileDefinition { Id = ids[i] });
            }

            profiles.Install(
                new InteractionContextProfilesConfig { Profiles = definitions },
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            return profiles;
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
                Schemes = new ControlSchemeRuntime(
                    _schemeIds,
                    orderTypes,
                    new ClientControlSchemePreferenceStore(Path.Combine(Path.GetTempPath(), $"ludots_scheme_{Guid.NewGuid():N}.json")));
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
