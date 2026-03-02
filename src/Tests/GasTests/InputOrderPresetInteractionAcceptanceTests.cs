using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class InputOrderPresetInteractionAcceptanceTests
    {
        private sealed class StubInputBackend : IInputBackend
        {
            public readonly Dictionary<string, bool> Buttons = new();

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out bool pressed) && pressed;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        public static IEnumerable<TestCaseData> AllPresetModeCases()
        {
            var presets = new[]
            {
                EffectPresetType.InstantDamage,
                EffectPresetType.DoT,
                EffectPresetType.Heal,
                EffectPresetType.HoT,
                EffectPresetType.Buff,
                EffectPresetType.ApplyForce2D,
                EffectPresetType.Search,
                EffectPresetType.PeriodicSearch,
                EffectPresetType.LaunchProjectile,
                EffectPresetType.CreateUnit,
                EffectPresetType.Displacement
            };

            var modes = new[]
            {
                InteractionModeType.TargetFirst,            // WOW
                InteractionModeType.AimCast,                // DOTA
                InteractionModeType.SmartCast,              // LOL quick cast
                InteractionModeType.SmartCastWithIndicator  // LOL indicator cast
            };

            for (int p = 0; p < presets.Length; p++)
            {
                for (int m = 0; m < modes.Length; m++)
                {
                    var preset = presets[p];
                    var mode = modes[m];
                    int slot = p * 10 + m;
                    var selection = ResolveSelectionType(preset);
                    yield return new TestCaseData(preset, mode, selection, slot)
                        .SetName($"Preset_{preset}_{mode}_SubmitsOrder");
                }
            }
        }

        [TestCaseSource(nameof(AllPresetModeCases))]
        public void PresetSkill_InAllInteractionModes_SubmitsExpectedOrder(
            EffectPresetType presetType,
            InteractionModeType mode,
            OrderSelectionType selectionType,
            int slot)
        {
            var (backend, input) = BuildInputHandler();

            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = slot },
                        IsSkillMapping = true,
                        CastModeOverride = mode,
                        SelectionType = selectionType,
                        RequireSelection = selectionType != OrderSelectionType.None
                    }
                }
            };

            var system = new InputOrderMappingSystem(input, config);
            system.SetTagKeyResolver(tagKey => tagKey == "castAbility" ? 100 : 0);
            system.SetGroundPositionProvider((out Vector3 pos) =>
            {
                pos = new Vector3(1200f, 0f, 3400f);
                return true;
            });

            using var world = World.Create();
            Entity actor = world.Create();
            Entity selectedTarget = world.Create();
            Entity hoveredTarget = world.Create();
            system.SetSelectedEntityProvider((out Entity e) =>
            {
                e = selectedTarget;
                return true;
            });
            system.SetHoveredEntityProvider((out Entity e) =>
            {
                e = hoveredTarget;
                return true;
            });

            Order? submitted = null;
            system.SetOrderSubmitHandler((in Order order) =>
            {
                submitted = order;
            });
            system.SetLocalPlayer(actor, 1);

            switch (mode)
            {
                case InteractionModeType.TargetFirst:
                case InteractionModeType.SmartCast:
                    backend.Buttons["<Keyboard>/q"] = true;
                    TickFrame(input, system);
                    break;

                case InteractionModeType.AimCast:
                    backend.Buttons["<Keyboard>/q"] = true;
                    TickFrame(input, system);
                    That(submitted.HasValue, Is.False, "AimCast should not submit before confirm click");
                    That(system.IsAiming, Is.True, "AimCast should enter aiming state");

                    backend.Buttons["<Keyboard>/q"] = false;
                    backend.Buttons["<Mouse>/LeftButton"] = true;
                    TickFrame(input, system);
                    break;

                case InteractionModeType.SmartCastWithIndicator:
                    backend.Buttons["<Keyboard>/q"] = true;
                    TickFrame(input, system);
                    That(submitted.HasValue, Is.False, "Indicator cast should not submit on key press");
                    That(system.IsAiming, Is.True, "Indicator cast should enter aiming state");

                    backend.Buttons["<Keyboard>/q"] = false;
                    TickFrame(input, system);
                    break;
            }

            That(submitted.HasValue, Is.True, $"Preset={presetType}, Mode={mode} should submit exactly one order");
            Order orderValue = submitted!.Value;
            That(orderValue.OrderTagId, Is.EqualTo(100));
            That(orderValue.Actor, Is.EqualTo(actor));
            That(orderValue.PlayerId, Is.EqualTo(1));
            That(orderValue.Args.I0, Is.EqualTo(slot));

            if (selectionType == OrderSelectionType.Entity)
            {
                Entity expectedTarget = mode == InteractionModeType.TargetFirst ? selectedTarget : hoveredTarget;
                That(orderValue.Target, Is.EqualTo(expectedTarget), "Entity selection should follow mode-specific target source");
            }
            else if (selectionType == OrderSelectionType.Position || selectionType == OrderSelectionType.Direction)
            {
                That(orderValue.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
                That(orderValue.Args.Spatial.WorldCm.X, Is.EqualTo(1200f));
                That(orderValue.Args.Spatial.WorldCm.Z, Is.EqualTo(3400f));
            }
            else
            {
                That(orderValue.Target, Is.EqualTo(default(Entity)));
            }
        }

        private static void TickFrame(PlayerInputHandler input, InputOrderMappingSystem system)
        {
            input.Update();
            system.Update(1f / 60f);
        }

        private static (StubInputBackend Backend, PlayerInputHandler Handler) BuildInputHandler()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Skill", Type = InputActionType.Button },
                    new() { Id = "Select", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button }
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 100,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Skill", Path = "<Keyboard>/q" },
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton" },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton" }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private static OrderSelectionType ResolveSelectionType(EffectPresetType presetType)
        {
            return presetType switch
            {
                EffectPresetType.Heal => OrderSelectionType.None,
                EffectPresetType.Buff => OrderSelectionType.None,
                EffectPresetType.ApplyForce2D => OrderSelectionType.Position,
                EffectPresetType.Search => OrderSelectionType.Position,
                EffectPresetType.PeriodicSearch => OrderSelectionType.Position,
                EffectPresetType.LaunchProjectile => OrderSelectionType.Position,
                EffectPresetType.CreateUnit => OrderSelectionType.Position,
                EffectPresetType.Displacement => OrderSelectionType.Position,
                _ => OrderSelectionType.Entity
            };
        }
    }
}
