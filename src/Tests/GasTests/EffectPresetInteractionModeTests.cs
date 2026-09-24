using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EffectPresetInteractionModeTests
    {
        [Test]
        public void MobaEffects_CoversSharedCombatPresetTypes()
        {
            string repoRoot = FindRepoRoot();
            string effectsPath = Path.Combine(repoRoot, "mods", "MobaDemoMod", "assets", "GAS", "effects.json");
            Assert.That(File.Exists(effectsPath), Is.True, "MobaDemoMod effects.json is missing.");

            using var stream = File.OpenRead(effectsPath);
            using var doc = JsonDocument.Parse(stream);

            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("presetType", out var presetNode))
                {
                    var preset = presetNode.GetString();
                    if (!string.IsNullOrWhiteSpace(preset))
                    {
                        found.Add(preset);
                    }
                }
            }

            string[] expected =
            {
                "InstantDamage", "Heal", "DoT", "HoT", "Buff",
                "ApplyForce2D", "Search", "PeriodicSearch",
                "LaunchProjectile", "CreateUnit", "Displacement"
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(found.Contains(expected[i]), Is.True, $"Missing presetType '{expected[i]}' in MobaDemoMod effects.");
            }
        }

        [Test]
        public void InputOrderMapping_ThreeInteractionModes_GenerateExpectedOrders()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        IsSkillMapping = true,
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity
                    }
                }
            };

            var mapping = new InputOrderMappingSystem(input, cfg);
            using var world = World.Create();
            var actor = world.Create();
            var target = world.Create();
            mapping.SetLocalPlayer(actor, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedEntityProvider((string _, out Entity e) => { e = target; return true; });
            mapping.SetHoveredEntityProvider((out Entity e) => { e = target; return true; });

            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            mapping.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            // WoW / TargetFirst: press skill -> immediate order
            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Target, Is.EqualTo(target));
            input.Update();
            mapping.Update(0f);

            // LoL / SmartCast: press skill -> immediate order
            mapping.SetInteractionMode(InteractionModeType.SmartCast);
            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);
            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[1].Target, Is.EqualTo(target));
            input.Update();
            mapping.Update(0f);

            // SC2 / AimCast: press skill -> enter aiming (no immediate order), then Select confirms
            mapping.SetInteractionMode(InteractionModeType.AimCast);
            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.True);
            Assert.That(orders.Count, Is.EqualTo(2));

            input.InjectButtonPress("Select");
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.False);
            Assert.That(orders.Count, Is.EqualTo(3));
            Assert.That(orders[2].Target, Is.EqualTo(target));

            // PressReleaseAimCast: press -> pending, release -> enter aiming, Select confirms
            mapping.SetInteractionMode(InteractionModeType.PressReleaseAimCast);
            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.False);
            Assert.That(orders.Count, Is.EqualTo(3));

            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.True);
            Assert.That(orders.Count, Is.EqualTo(3));

            input.InjectButtonPress("Select");
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.False);
            Assert.That(orders.Count, Is.EqualTo(4));
            Assert.That(orders[3].Target, Is.EqualTo(target));
        }

        [Test]
        public void PressReleaseAimCast_CommandCancel_DoesNotFallThroughToMoveOrder()
        {
            var backend = new TestInputBackend();
            var input = new PlayerInputHandler(backend, CreateInputConfig());
            input.PushContext("Test");

            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.PressReleaseAimCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        IsSkillMapping = true,
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Position
                    },
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        IsSkillMapping = false,
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Position
                    }
                }
            };

            using var world = World.Create();
            var actor = world.Create();
            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetLocalPlayer(actor, 1);
            mapping.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => 100,
                "moveTo" => 101,
                _ => 0
            });
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = new Vector3(320f, 0f, 480f);
                return true;
            });
            mapping.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            backend.Buttons["<Keyboard>/q"] = true;
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.False);
            Assert.That(orders, Is.Empty);

            backend.Buttons["<Keyboard>/q"] = false;
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.True);
            Assert.That(orders, Is.Empty);

            backend.Buttons["<Mouse>/RightButton"] = true;
            input.Update();
            mapping.Update(0f);
            Assert.That(mapping.IsAiming, Is.False, "Command cancel should exit aiming immediately.");
            Assert.That(orders, Is.Empty, "Consuming command as aim cancel must not leak a move order.");

            input.Update();
            mapping.Update(0f);
            Assert.That(orders, Is.Empty, "Holding the cancel button after aim cancel must stay suppressed until release.");

            backend.Buttons["<Mouse>/RightButton"] = false;
            input.Update();
            mapping.Update(0f);

            backend.Buttons["<Mouse>/RightButton"] = true;
            input.Update();
            mapping.Update(0f);
            Assert.That(orders.Count, Is.EqualTo(1), "A fresh command press after release should still submit move orders normally.");
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(101));
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesPresentationMetadata()
        {
            var obj = JsonNode.Parse(
                """
                {
                  "presentation": {
                    "displayName": "Mystic Shot",
                    "iconGlyph": "Q",
                    "accentColor": "#2AA7FF",
                    "hintText": "Quick skill shot",
                    "modeIconGlyphs": {
                      "PressReleaseAimCast": "QA"
                    },
                    "modeHints": {
                      "PressReleaseAimCast": "Release then confirm"
                    }
                  }
                }
                """) as JsonObject;

            Assert.That(obj, Is.Not.Null);

            var ability = AbilityExecLoader.CompileAbility(obj!, "ez.q", "test://abilities.json");
            Assert.That(ability.HasPresentation, Is.True);
            Assert.That(ability.Presentation, Is.Not.Null);
            Assert.That(ability.Presentation!.ResolveDisplayName("fallback"), Is.EqualTo("Mystic Shot"));
            Assert.That(ability.Presentation.ResolveIconGlyph(nameof(InteractionModeType.SmartCast), "?"), Is.EqualTo("Q"));
            Assert.That(ability.Presentation.ResolveIconGlyph(nameof(InteractionModeType.PressReleaseAimCast), "?"), Is.EqualTo("QA"));
            Assert.That(ability.Presentation.ResolveHintText(nameof(InteractionModeType.PressReleaseAimCast), "fallback"), Is.EqualTo("Release then confirm"));
        }

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "Select", Name = "Select", Type = InputActionType.Button },
                    new() { Id = "Command", Name = "Command", Type = InputActionType.Button },
                    new() { Id = "Cancel", Name = "Cancel", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Test",
                        Name = "Test",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "SkillQ", Path = "<Keyboard>/q", Processors = new() },
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                        }
                    }
                }
            };
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

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new(StringComparer.Ordinal);

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}

