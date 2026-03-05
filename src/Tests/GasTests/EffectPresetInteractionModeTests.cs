using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
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
        public void MobaEffects_CoversAllElevenPresetTypes()
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
                        OrderTagKey = "castAbility",
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
            mapping.SetTagKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedEntityProvider((out Entity e) => { e = target; return true; });
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
        }

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "Select", Name = "Select", Type = InputActionType.Button },
                    new() { Id = "Cancel", Name = "Cancel", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new() { Id = "Test", Name = "Test", Priority = 1 }
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
