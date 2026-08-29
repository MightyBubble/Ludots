using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
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
    /// RFC-0065 INT-5/INT-7 (Section 5.11, DEC-14/DEC-15): control scheme catalog + hot switch
    /// (IMC push/pop, allowed set, preference persistence, headless null handler) and the frame
    /// command intent arbiter's three branches. Scheme/intent/context names are test data, never
    /// Core concepts.
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
        public void Install_ActivatesFirstAllowedSchemeAndPushesItsInputContexts()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: true);
            harness.InstallTwoSchemes();

            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True, "the initial scheme's IMC context must be active after install.");

            uint revisionBefore = harness.Runtime.Revision;
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(harness.Runtime.Revision, Is.GreaterThan(revisionBefore));

            harness.Backend.Buttons["<Keyboard>/e"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.False, "scheme A's IMC context must be popped on switch.");
            Assert.That(harness.Handler.IsDown("CmdB"), Is.True, "scheme B's IMC context must be pushed on switch.");
        }

        [Test]
        public void Install_InputContextNotDeclaredInInputConfig_FailsFastWhenInputConfigIsAvailable()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false, withInputConfig: true);

            ControlSchemeDefinition typoContext = Harness.Scheme(SchemeA, ContextA, "imc.test.typo");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(
                new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition> { typoContext },
                }));

            Assert.That(error!.Message, Does.Contain(SchemeA));
            Assert.That(error.Message, Does.Contain("imc.test.typo"));
            Assert.That(error.Message, Does.Contain("unknown input context"));
        }

        [Test]
        public void Install_InputContextsWithoutInputConfig_SkipReferenceValidation()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false, withInputConfig: false);

            Assert.DoesNotThrow(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeA, ContextA) },
            }));
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));
        }

        [Test]
        public void TrySwitch_DisallowedOrUninstalledScheme_RefusedAndStateUnchanged()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: true);
            harness.InstallTwoSchemes(allowedSchemes: new List<string> { SchemeA });

            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));

            // Mod allowed-set refusal keeps the active scheme and its contexts.
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.False);
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeA)));
            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True);

            Assert.That(harness.Runtime.TrySwitch(schemeId: 0), Is.False, "id 0 is never installed.");
            Assert.That(harness.Runtime.TrySwitch(schemeId: 999), Is.False, "unknown ids are refused.");
        }

        [Test]
        public void TrySwitch_NullHandler_PersistsPreference()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();

            // Headless: no input handler exists; only preference bookkeeping runs.
            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(harness.Preferences.ActiveSchemeId, Is.EqualTo(SchemeB),
                "the active scheme choice must persist into the CTX-8 preference store.");
        }

        [Test]
        public void TrySwitchRuntimeOnly_SwitchesRuntimeWithoutPersistingPreference()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.InstallTwoSchemes();
            Assert.That(harness.Preferences.ActiveSchemeId, Is.EqualTo(SchemeA),
                "install persists the initial scheme as the player preference.");
            uint preferenceRevision = harness.Preferences.Revision;

            Assert.That(harness.Runtime.TrySwitchRuntimeOnly(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeB)));
            Assert.That(harness.Preferences.ActiveSchemeId, Is.EqualTo(SchemeA),
                "runtime-only activation must leave the persisted preference untouched.");
            Assert.That(harness.Preferences.Revision, Is.EqualTo(preferenceRevision));

            Assert.That(harness.Runtime.TrySwitchRuntimeOnly(schemeId: 999), Is.False,
                "runtime-only activation keeps the installed/allowed-set refusal semantics.");
            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeB)));
        }

        [Test]
        public void Install_ActivatesPersistedSchemeWhenItIsAllowed()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.Preferences.SetActiveScheme(SchemeB);

            harness.InstallTwoSchemes();

            Assert.That(harness.Runtime.ActiveSchemeId, Is.EqualTo(harness.SchemeId(SchemeB)));
        }

        [Test]
        public void Install_PersistedUnknownScheme_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            harness.Preferences.SetActiveScheme("scheme.test.removed");

            Assert.Throws<InvalidOperationException>(() => harness.InstallTwoSchemes());
        }

        [Test]
        public void Install_DuplicateScheme_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);

            harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeA, ContextA) },
            });
            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeA, ContextA) },
            }));

            Assert.Throws<InvalidOperationException>(
                () => ControlSchemeConfigLoader.Validate(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition> { Harness.Scheme(SchemeB) },
                    AllowedSchemes = new List<string> { "scheme.test.undeclared" },
                }, "test"),
                "allowedSchemes must reference declared schemes.");
        }

        [Test]
        public void TryGetActiveAxisMove_TracksActiveSchemeDeclaration()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false, withInputConfig: true);
            ControlSchemeDefinition withAxis = Harness.Scheme(SchemeA);
            withAxis.AxisMove = new ControlSchemeAxisMove
            {
                ActionId = "Move",
                OrderTypeKey = "moveTo",
                ThrottleTicks = 6,
                StepDistanceCm = 400,
            };
            harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { withAxis, Harness.Scheme(SchemeB) },
            });

            Assert.That(harness.Runtime.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding), Is.True);
            Assert.That(binding.ActionId, Is.EqualTo("Move"));
            Assert.That(binding.OrderTypeId, Is.EqualTo(2), "orderTypeKey resolves to its registry id at install.");
            Assert.That(binding.ThrottleTicks, Is.EqualTo(6));
            Assert.That(binding.StepDistanceCm, Is.EqualTo(400));

            Assert.That(harness.Runtime.TrySwitch(harness.SchemeId(SchemeB)), Is.True);
            Assert.That(harness.Runtime.TryGetActiveAxisMove(out _), Is.False);
        }

        [Test]
        public void Install_AxisMoveUnknownOrNonAxis2DAction_FailsFastWhenInputConfigIsAvailable()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false, withInputConfig: true);

            ControlSchemeDefinition unknownAction = Harness.Scheme(SchemeA);
            unknownAction.AxisMove = new ControlSchemeAxisMove
            {
                ActionId = "TypoMove",
                OrderTypeKey = "moveTo",
                ThrottleTicks = 6,
                StepDistanceCm = 400,
            };

            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { unknownAction },
            }));

            ControlSchemeDefinition buttonAction = Harness.Scheme(SchemeB);
            buttonAction.AxisMove = new ControlSchemeAxisMove
            {
                ActionId = "CmdA",
                OrderTypeKey = "moveTo",
                ThrottleTicks = 6,
                StepDistanceCm = 400,
            };

            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { buttonAction },
            }));
        }

        [Test]
        public void Install_AxisMoveUnknownOrderTypeKey_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            ControlSchemeDefinition scheme = Harness.Scheme(SchemeA);
            scheme.AxisMove = new ControlSchemeAxisMove
            {
                ActionId = "Move",
                OrderTypeKey = "orders.test.unknown",
                ThrottleTicks = 6,
                StepDistanceCm = 400,
            };

            Assert.Throws<InvalidOperationException>(() => harness.Runtime.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition> { scheme },
            }));
        }

        [TestCase("", "moveTo", 6, 400, TestName = "Validate_AxisMoveMissingActionId_FailsFast")]
        [TestCase("Move", "", 6, 400, TestName = "Validate_AxisMoveMissingOrderTypeKey_FailsFast")]
        [TestCase("Move", "moveTo", 0, 400, TestName = "Validate_AxisMoveZeroThrottleTicks_FailsFast")]
        [TestCase("Move", "moveTo", 6, 0, TestName = "Validate_AxisMoveZeroStepDistance_FailsFast")]
        public void Validate_AxisMoveDeclarationWithMissingOrIllegalField_FailsFast(
            string actionId, string orderTypeKey, int throttleTicks, int stepDistanceCm)
        {
            ControlSchemeDefinition scheme = Harness.Scheme(SchemeA);
            scheme.AxisMove = new ControlSchemeAxisMove
            {
                ActionId = actionId,
                OrderTypeKey = orderTypeKey,
                ThrottleTicks = throttleTicks,
                StepDistanceCm = stepDistanceCm,
            };

            Assert.Throws<InvalidOperationException>(
                () => ControlSchemeConfigLoader.Validate(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition> { scheme },
                }, "test"),
                "a declared axisMove requires all four fields to be present and legal.");
        }

        [Test]
        public void Arbiter_ActiveContextExplicitIntent_WinsOverPlayerPref()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            Entity rep = world.Create();
            world.Add(rep, new ActiveInteractionContext
            {
                ContextEntity = world.Create(),
                CommandIntentProfileId = harness.Stack.CommandIntentProfileIdRegistry.Register(AltIntent),
            });

            CommandPref pref = NewPref(harness, DefaultIntent);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in pref),
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)),
                "the active context's explicit intent must win over the player default.");
        }

        [Test]
        public void Arbiter_SteadyStateNoActiveContext_UsesPlayerPrefDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            Entity rep = world.Create();

            CommandPref pref = NewPref(harness, DefaultIntent);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in pref),
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(DefaultIntent)),
                "steady state (no mounted interaction context) consumes the possessed rep's player default intent (DEC-14).");

            CommandPref other = NewPref(harness, AltIntent);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in other),
                Is.EqualTo(harness.Stack.CommandIntentProfileIdRegistry.GetId(AltIntent)),
                "switching the player's preference changes the steady state's intent.");
        }

        [Test]
        public void Arbiter_ActiveContextWithoutIntent_ReturnsZero_NoBubbling()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, withHandler: false);
            Entity rep = world.Create();
            world.Add(rep, new ActiveInteractionContext
            {
                ContextEntity = world.Create(),
                CommandIntentProfileId = 0,
            });

            CommandPref pref = NewPref(harness, DefaultIntent);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in pref),
                Is.EqualTo(0),
                "an active context without an explicit intent does not route and never bubbles to the player default (DEC-14).");
        }

        [Test]
        public void DefaultSchemesConfigFile_DeserializesValidatesAndCarriesNoDefaults()
        {
            string configPath = Path.Combine(FindRepoRoot(), "assets", "Input", "control_schemes.json");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");

            var config = JsonSerializer.Deserialize<ControlSchemesConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            ControlSchemeConfigLoader.Validate(config, "assets");
            ControlSchemeDefinition defaultScheme = config!.Schemes.Single(scheme => scheme.Id == "scheme.default");
            Assert.That(defaultScheme.AxisMove, Is.Null, "default shipped scheme keeps axis movement disabled by topology.");

            string prefsPath = Path.Combine(FindRepoRoot(), "assets", "Input", "command_prefs.json");
            Assert.That(File.Exists(prefsPath), Is.True, $"Missing {prefsPath}");
            var prefs = JsonSerializer.Deserialize<CommandPrefsConfig>(
                File.ReadAllText(prefsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            CommandPrefConfigLoader.Validate(prefs!, "assets");
            Assert.That(prefs!.Defaults.CastDispatchProfileId, Is.EqualTo("dispatch.all_together"));

            string catalogPath = Path.Combine(FindRepoRoot(), "assets", "config_catalog.json");
            string catalog = File.ReadAllText(catalogPath);
            Assert.That(catalog, Does.Not.Contain("Input/axis_move.json"));
            Assert.That(catalog, Does.Contain("Input/command_prefs.json"),
                "the command preference seed config is a catalog-declared required config.");
        }

        [Test]
        public void Load_LegacySchemeDefaultsNode_FailsFastWithMigrationHint()
        {
            var legacy = System.Text.Json.Nodes.JsonNode.Parse("""
                {
                  "schemes": [
                    { "id": "scheme.test.a", "inputContexts": [], "defaults": { "commandIntentId": "intent.command.default", "castDispatchProfileId": "dispatch.all_together" } }
                  ]
                }
                """)!.AsObject();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ControlSchemeConfigLoader.RejectLegacySchemeDefaults(legacy, "Input/control_schemes.json"));
            Assert.That(error!.Message, Does.Contain("scheme.test.a"));
            Assert.That(error.Message, Does.Contain("'defaults'"));
            Assert.That(error.Message, Does.Contain("command_prefs.json"));

            var current = System.Text.Json.Nodes.JsonNode.Parse("""
                {
                  "schemes": [
                    { "id": "scheme.test.a", "inputContexts": [] }
                  ]
                }
                """)!.AsObject();
            Assert.DoesNotThrow(() => ControlSchemeConfigLoader.RejectLegacySchemeDefaults(current, "test"));
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

        private static CommandPref NewPref(Harness harness, string intentName)
        {
            return NewPref(harness.Stack.CommandIntentProfileIdRegistry.Register(intentName));
        }

        private static CommandPref NewPref(int intentId)
        {
            CommandPref pref = default;
            pref.SetPlayerDefault(intentId, castDispatchProfileId: 777);
            return pref;
        }

        private sealed class Harness
        {
            public InteractionContextStack Stack = null!;
            public ControlSchemeRuntime Runtime = null!;
            public ClientCastPreferenceStore Preferences = null!;
            public PlayerInputHandler Handler;
            public TestInputBackend Backend;
            private StringIntRegistry _schemeIds = null!;

            public static Harness Create(World world, bool withHandler, bool withInputConfig = false)
            {
                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var stack = new InteractionContextStack(collectionKeys);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    "collection.test.command_source",
                    "view.test.default"));

                // Minimal CTX-8 store: this fixture only exercises active-scheme persistence.
                var castCommitIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var castCommitActionIds = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
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

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });
                harness.Runtime = new ControlSchemeRuntime(
                    harness._schemeIds,
                    orderTypes,
                    withHandler ? () => harness.Handler : null,
                    preferences,
                    inputConfig: withInputConfig ? BuildInputConfig(includeMoveAxis: true) : null);
                return harness;
            }

            public int SchemeId(string name) => _schemeIds.GetId(name);

            public void InstallTwoSchemes(List<string> allowedSchemes = null)
            {
                Runtime.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        Scheme(SchemeA, ContextA),
                        Scheme(SchemeB, ContextB),
                    },
                    AllowedSchemes = allowedSchemes,
                });
            }

            public static ControlSchemeDefinition Scheme(string id, params string[] inputContexts)
            {
                return new ControlSchemeDefinition
                {
                    Id = id,
                    InputContexts = new List<string>(inputContexts),
                };
            }

            private static InputConfigRoot BuildInputConfig(bool includeMoveAxis = false)
            {
                var config = new InputConfigRoot
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

                if (includeMoveAxis)
                {
                    config.Actions.Add(new InputActionDef { Id = "Move", Type = InputActionType.Axis2D });
                }

                return config;
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
