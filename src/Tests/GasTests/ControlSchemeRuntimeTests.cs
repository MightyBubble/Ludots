using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 INT-5/INT-7 (§5.11, DEC-14/DEC-15): control scheme catalog + hot switch (IMC
    /// push/pop, allowed set, preference persistence, headless null handler) and the frame command
    /// intent arbiter's three branches. Scheme/intent/context names are test data, never Core
    /// concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ControlSchemeRuntimeTests
    {
        private const string DefaultIntent = "intent.test.default";
        private const string AltIntent = "intent.test.alt";
        private const string SchemeA = "scheme.test.a";
        private const string SchemeB = "scheme.test.b";
        private const string ContextA = "imc.test.a";
        private const string ContextB = "imc.test.b";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ContextGroupIdRegistry.Clear();
        }

        [Test]
        public void TrySwitch_PushesAndPopsSchemeInputContexts()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: true);
            harness.InstallTwoSchemes();

            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeA)), Is.True);
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True, "scheme A's IMC context must be active after switch.");

            uint revisionBefore = harness.Runtime.Revision;
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(harness.Runtime.Revision, Is.GreaterThan(revisionBefore));

            harness.Backend.Buttons["<Keyboard>/e"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.False, "scheme A's IMC context must be popped on switch.");
            Assert.That(harness.Handler.IsDown("CmdB"), Is.True, "scheme B's IMC context must be pushed on switch.");
            Assert.That(
                harness.Runtime.ActiveDefaultCommandIntentId,
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)));
        }

        [Test]
        public void TrySwitch_DisallowedOrUninstalledScheme_RefusedAndStateUnchanged()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: true);
            harness.InstallTwoSchemes(allowedSchemes: new List<string> { SchemeA });

            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeA)), Is.True);

            // Mod allowed-set refusal keeps the active scheme, its contexts, and its intent default.
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.False);
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));
            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True);
            Assert.That(
                harness.Runtime.ActiveDefaultCommandIntentId,
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(DefaultIntent)));

            Assert.That(harness.Runtime.TrySwitch(schemeId: 0), Is.False, "id 0 is never installed.");
            Assert.That(harness.Runtime.TrySwitch(schemeId: 999), Is.False, "unknown ids are refused.");
        }

        [Test]
        public void TrySwitch_NullHandler_TracksIntentDefaultAndPersistsPreference()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();

            // Headless: no input handler exists; only intent default + preference bookkeeping run.
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(
                harness.Runtime.ActiveDefaultCommandIntentId,
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)));
            Assert.That(harness.Preferences.ActiveSchemeId, Is.EqualTo(SchemeB),
                "the active scheme choice must persist into the CTX-8 preference store.");
        }

        [Test]
        public void Install_UnknownCommandIntentOrDuplicateScheme_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);

            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition>
                {
                    Harness.Scheme("scheme.test.bad", "intent.test.not_installed"),
                },
            }));

            harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeA, DefaultIntent) },
            });
            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeA, DefaultIntent) },
            }));

            Assert.Throws<InvalidOperationException>(
                () => ControlSchemeConfigLoader.Validate(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeB, AltIntent) },
                    AllowedSchemes = new List<string> { "scheme.test.undeclared" },
                }, "test"),
                "allowedSchemes must reference declared schemes.");
        }

        [Test]
        public void Arbiter_TopFrameExplicitIntent_WinsOverSchemeDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeA)), Is.True);

            harness.Stack.Push(InteractionContextFrameDescriptor.Create(
                "context.test.targeting",
                "collection.test.targeting",
                "view.test.targeting",
                commandIntentProfileId: AltIntent));

            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(harness.Stack, harness.Runtime),
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)),
                "the pushed frame's explicit intent must win over the scheme default.");
        }

        [Test]
        public void Arbiter_DefaultFrame_UsesActiveSchemeDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();

            // No scheme active yet: the default frame resolves to 0 (no fallback intent).
            Assert.That(CommandIntentArbiter.ResolveActiveCommandIntent(harness.Stack, harness.Runtime), Is.EqualTo(0));
            Assert.That(CommandIntentArbiter.ResolveActiveCommandIntent(harness.Stack, scheme: null), Is.EqualTo(0));

            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(harness.Stack, harness.Runtime),
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)),
                "the default frame consumes the active scheme's default intent (DEC-14).");
        }

        [Test]
        public void Arbiter_NonDefaultFrameWithoutIntent_ReturnsZero_NoBubbling()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeA)), Is.True);

            harness.Stack.Push(InteractionContextFrameDescriptor.Create(
                "context.test.modal",
                "collection.test.targeting",
                "view.test.targeting"));

            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(harness.Stack, harness.Runtime),
                Is.EqualTo(0),
                "a non-default frame without an explicit intent does not route and never bubbles (DEC-14).");
        }

        [Test]
        public void Arbiter_EmptyStack_ReturnsZero()
        {
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var emptyStack = new InteractionContextStack(collectionKeys);
            Assert.That(CommandIntentArbiter.ResolveActiveCommandIntent(emptyStack, scheme: null), Is.EqualTo(0));
        }

        [Test]
        public void DefaultSchemesConfigFile_DeserializesAndValidates()
        {
            string configPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "Input", "control_schemes.json");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");

            var config = JsonSerializer.Deserialize<ControlSchemesConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            ControlSchemeConfigLoader.Validate(config, "assets");
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

        private sealed class Harness
        {
            public InteractionContextStack Stack = null!;
            public ControlSchemeRuntime Runtime = null!;
            public ClientCastPreferenceStore Preferences = null!;
            public PlayerInputHandler Handler;
            public TestInputBackend Backend;
            private StringIntRegistry _schemeIds = null!;

            public static Harness Create(World world, bool withHandler)
            {
                CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
                InstallGroundIntent(intents, DefaultIntent);
                InstallGroundIntent(intents, AltIntent);

                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var stack = new InteractionContextStack(collectionKeys);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    "collection.test.command_source",
                    "view.test.default"));

                // Minimal CTX-8 store: this fixture only exercises active-scheme persistence.
                var castCommitIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var castCommitActionIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var templateKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var formSetKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var preferences = new ClientCastPreferenceStore(
                    new CastCommitProfileRegistry(
                        castCommitIds,
                        castCommitActionIds,
                        new InteractionContextProfileRegistry(stack.ContextIdRegistry)),
                    templateKeys.Register,
                    templateKeys.GetName,
                    formSetKeys.Register,
                    formSetKeys.GetName);

                var harness = new Harness
                {
                    Stack = stack,
                    Preferences = preferences,
                    _schemeIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                };

                if (withHandler)
                {
                    harness.Backend = new TestInputBackend();
                    harness.Handler = new PlayerInputHandler(harness.Backend, BuildInputConfig());
                }

                harness.Runtime = new ControlSchemeRuntime(
                    harness._schemeIds,
                    stack,
                    intents.Intents,
                    withHandler ? () => harness.Handler : null,
                    preferences);
                return harness;
            }

            public int SchemeId(string name) => _schemeIds.GetId(name);

            public void InstallTwoSchemes(List<string> allowedSchemes = null)
            {
                Runtime.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        Scheme(SchemeA, DefaultIntent, ContextA),
                        Scheme(SchemeB, AltIntent, ContextB),
                    },
                    AllowedSchemes = allowedSchemes,
                });
            }

            public static ControlSchemeDefinition Scheme(string id, string commandIntentId, params string[] inputContexts)
            {
                return new ControlSchemeDefinition
                {
                    Id = id,
                    InputContexts = new List<string>(inputContexts),
                    Defaults = new ControlSchemeDefaults { CommandIntentId = commandIntentId },
                };
            }

            private static void InstallGroundIntent(CommandIntentProfileTests.Harness intents, string profileId)
            {
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
                {
                    Id = profileId,
                    GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                    Rules = new List<CommandIntentRuleDefinition>
                    {
                        CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                    },
                }));
            }

            private static InputConfigRoot BuildInputConfig()
            {
                return new InputConfigRoot
                {
                    Actions = new List<InputActionDef>
                    {
                        new() { Id = "CmdA", Type = InputActionType.Button },
                        new() { Id = "CmdB", Type = InputActionType.Button },
                    },
                    Contexts = new List<InputContextDef>
                    {
                        new()
                        {
                            Id = ContextA,
                            Priority = 1,
                            Bindings = new List<InputBindingDef>
                            {
                                new() { ActionId = "CmdA", Path = "<Keyboard>/q", Processors = new() },
                            },
                        },
                        new()
                        {
                            Id = ContextB,
                            Priority = 1,
                            Bindings = new List<InputBindingDef>
                            {
                                new() { ActionId = "CmdB", Path = "<Keyboard>/e", Processors = new() },
                            },
                        },
                    },
                };
            }
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new(StringComparer.Ordinal);

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out bool down) && down;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
