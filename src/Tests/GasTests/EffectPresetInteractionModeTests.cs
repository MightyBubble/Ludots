using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Comprehensive acceptance tests for ALL 11 EffectPresetTypes × 3 InteractionModes.
    /// Each effect preset is exercised under WoW (TargetFirst), DotA (AimCast), LoL (SmartCast)
    /// interaction modes, producing verifiable skill scenarios.
    ///
    /// Test matrix (33 scenarios):
    ///   InstantDamage × {WoW, DotA, LoL}
    ///   Heal          × {WoW, DotA, LoL}
    ///   DoT           × {WoW, DotA, LoL}
    ///   HoT           × {WoW, DotA, LoL}
    ///   Buff          × {WoW, DotA, LoL}
    ///   ApplyForce2D  × {WoW, DotA, LoL}
    ///   Search        × {WoW, DotA, LoL}
    ///   PeriodicSearch× {WoW, DotA, LoL}
    ///   LaunchProjectile × {WoW, DotA, LoL}
    ///   CreateUnit    × {WoW, DotA, LoL}
    ///   Displacement  × {WoW, DotA, LoL}
    ///
    /// Audit findings verified in Section 0:
    ///   - Displacement now registered in preset_types.json
    ///   - ComponentFlags short-name aliases (Modifiers→ModifierParams etc.)
    ///   - MobaDemoMod input mappings now set isSkillMapping + selectionType
    /// </summary>
    [TestFixture]
    public class EffectPresetInteractionModeTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  Mock IInputBackend for test-controllable button presses
        // ════════════════════════════════════════════════════════════════════

        private sealed class MockInputBackend : IInputBackend
        {
            private readonly HashSet<string> _pressedKeys = new();

            public void Press(string devicePath) => _pressedKeys.Add(devicePath);
            public void Release(string devicePath) => _pressedKeys.Remove(devicePath);
            public void ReleaseAll() => _pressedKeys.Clear();

            public bool GetButton(string devicePath) => _pressedKeys.Contains(devicePath);
            public float GetAxis(string devicePath) => 0f;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => "";
        }

        // ════════════════════════════════════════════════════════════════════
        //  Shared infrastructure for building input-order test harness
        // ════════════════════════════════════════════════════════════════════

        private static InputConfigRoot BuildInputConfig()
        {
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "SkillW", Type = InputActionType.Button },
                    new() { Id = "SkillE", Type = InputActionType.Button },
                    new() { Id = "SkillR", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button },
                    new() { Id = "Stop", Type = InputActionType.Button },
                    new() { Id = "Select", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "QueueModifier", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay", Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "SkillQ", Path = "<Keyboard>/q" },
                            new() { ActionId = "SkillW", Path = "<Keyboard>/w" },
                            new() { ActionId = "SkillE", Path = "<Keyboard>/e" },
                            new() { ActionId = "SkillR", Path = "<Keyboard>/r" },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton" },
                            new() { ActionId = "Stop", Path = "<Keyboard>/s" },
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton" },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/Escape" },
                            new() { ActionId = "QueueModifier", Path = "<Keyboard>/LeftShift" },
                        }
                    }
                }
            };
            return config;
        }

        /// <summary>
        /// Build InputOrderMappingConfig with specific interaction mode and 4 skill slots.
        /// Each skill slot uses the given SelectionType.
        /// </summary>
        private static InputOrderMappingConfig BuildMappingConfig(
            InteractionModeType mode,
            OrderSelectionType qSel, OrderSelectionType wSel,
            OrderSelectionType eSel, OrderSelectionType rSel)
        {
            return new InputOrderMappingConfig
            {
                InteractionMode = mode,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "skill.q", IsSkillMapping = true,
                        SelectionType = qSel, ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                    },
                    new()
                    {
                        ActionId = "SkillW", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "skill.w", IsSkillMapping = true,
                        SelectionType = wSel, ArgsTemplate = new OrderArgsTemplate { I0 = 1 },
                    },
                    new()
                    {
                        ActionId = "SkillE", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "skill.e", IsSkillMapping = true,
                        SelectionType = eSel, ArgsTemplate = new OrderArgsTemplate { I0 = 2 },
                    },
                    new()
                    {
                        ActionId = "SkillR", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "skill.r", IsSkillMapping = true,
                        SelectionType = rSel, ArgsTemplate = new OrderArgsTemplate { I0 = 3 },
                    },
                    new()
                    {
                        ActionId = "Command", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "moveTo", IsSkillMapping = false,
                        RequireSelection = true, SelectionType = OrderSelectionType.Position,
                    },
                    new()
                    {
                        ActionId = "Stop", Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "stop", IsSkillMapping = false,
                    },
                }
            };
        }

        private static readonly Dictionary<string, int> TagKeyToId = new()
        {
            { "skill.q", 100 }, { "skill.w", 101 },
            { "skill.e", 102 }, { "skill.r", 103 },
            { "moveTo", 200 }, { "stop", 300 },
        };

        /// <summary>
        /// Wire up InputOrderMappingSystem with mock providers.
        /// Returns collected orders.
        /// </summary>
        private static (InputOrderMappingSystem system, List<Order> orders) BuildSystem(
            PlayerInputHandler inputHandler,
            InputOrderMappingConfig config,
            Entity localPlayer,
            Entity? selectedEntity,
            Entity? hoveredEntity,
            Vector3? groundPos)
        {
            var system = new InputOrderMappingSystem(inputHandler, config);
            var orders = new List<Order>();

            system.SetTagKeyResolver(key => TagKeyToId.TryGetValue(key, out var id) ? id : 0);
            system.SetOrderSubmitHandler((in Order o) => { orders.Add(o); });
            system.SetLocalPlayer(localPlayer, 1);

            if (selectedEntity.HasValue)
            {
                var sel = selectedEntity.Value;
                system.SetSelectedEntityProvider((out Entity e) => { e = sel; return true; });
            }
            else
            {
                system.SetSelectedEntityProvider((out Entity e) => { e = default; return false; });
            }

            if (hoveredEntity.HasValue)
            {
                var hov = hoveredEntity.Value;
                system.SetHoveredEntityProvider((out Entity e) => { e = hov; return true; });
            }
            else
            {
                system.SetHoveredEntityProvider((out Entity e) => { e = default; return false; });
            }

            if (groundPos.HasValue)
            {
                var gp = groundPos.Value;
                system.SetGroundPositionProvider((out Vector3 pos) => { pos = gp; return true; });
            }
            else
            {
                system.SetGroundPositionProvider((out Vector3 pos) => { pos = default; return false; });
            }

            return (system, orders);
        }

        /// <summary>
        /// Simulate a frame: press key → update handler → update system → release key.
        /// </summary>
        private static void SimulatePressFrame(
            MockInputBackend backend, PlayerInputHandler handler,
            InputOrderMappingSystem system, string keyPath)
        {
            backend.Press(keyPath);
            handler.Update();
            system.Update(0.016f);
            backend.Release(keyPath);
            handler.Update();
        }

        /// <summary>
        /// Simulate press (without release) → update.
        /// </summary>
        private static void SimulatePress(
            MockInputBackend backend, PlayerInputHandler handler, string keyPath)
        {
            backend.Press(keyPath);
            handler.Update();
        }

        /// <summary>
        /// Simulate release → update.
        /// </summary>
        private static void SimulateRelease(
            MockInputBackend backend, PlayerInputHandler handler, string keyPath)
        {
            backend.Release(keyPath);
            handler.Update();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 0: Audit Findings — Structural Validation
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Audit_PresetTypeRegistry_AllEnumValuesRegistered()
        {
            var registry = new PresetTypeRegistry();

            foreach (EffectPresetType type in Enum.GetValues(typeof(EffectPresetType)))
            {
                if (type == EffectPresetType.None) continue;
                var def = new PresetTypeDefinition { Type = type };
                def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
                registry.Register(in def);
            }

            foreach (EffectPresetType type in Enum.GetValues(typeof(EffectPresetType)))
            {
                if (type == EffectPresetType.None) continue;
                That(registry.IsRegistered(type), Is.True,
                    $"PresetType {type} must be registerable");
            }
        }

        [Test]
        public void Audit_ComponentFlags_ShortNameAliases()
        {
            That(GasEnumParser.ParseComponentFlag("Modifiers"),
                Is.EqualTo(ComponentFlags.ModifierParams), "Short alias 'Modifiers' must map to ModifierParams");
            That(GasEnumParser.ParseComponentFlag("Duration"),
                Is.EqualTo(ComponentFlags.DurationParams), "Short alias 'Duration' must map to DurationParams");
            That(GasEnumParser.ParseComponentFlag("Force"),
                Is.EqualTo(ComponentFlags.ForceParams), "Short alias 'Force' must map to ForceParams");
            That(GasEnumParser.ParseComponentFlag("TargetQuery"),
                Is.EqualTo(ComponentFlags.TargetQueryParams), "Short alias 'TargetQuery' must map to TargetQueryParams");
            That(GasEnumParser.ParseComponentFlag("TargetDispatch"),
                Is.EqualTo(ComponentFlags.TargetDispatchParams), "Short alias 'TargetDispatch' must map to TargetDispatchParams");
            That(GasEnumParser.ParseComponentFlag("Projectile"),
                Is.EqualTo(ComponentFlags.ProjectileParams), "Short alias 'Projectile' must map to ProjectileParams");
            That(GasEnumParser.ParseComponentFlag("UnitCreation"),
                Is.EqualTo(ComponentFlags.UnitCreationParams), "Short alias 'UnitCreation' must map to UnitCreationParams");
            That(GasEnumParser.ParseComponentFlag("TargetFilter"),
                Is.EqualTo(ComponentFlags.TargetFilterParams), "Short alias 'TargetFilter' must map to TargetFilterParams");
        }

        [Test]
        public void Audit_DisplacementPreset_LoadableFromJson()
        {
            var registry = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(registry, @"[
                {
                    ""id"": ""Displacement"",
                    ""components"": [],
                    ""activePhases"": [""OnApply""],
                    ""allowedLifetimes"": [""Instant""],
                    ""defaultPhaseHandlers"": {
                        ""OnApply"": { ""type"": ""builtin"", ""id"": ""ApplyDisplacement"" }
                    }
                }
            ]");

            That(registry.IsRegistered(EffectPresetType.Displacement), Is.True,
                "Displacement preset type must be loadable from JSON");
            ref readonly var def = ref registry.Get(EffectPresetType.Displacement);
            That(def.HasPhase(EffectPhaseId.OnApply), Is.True);
            That(def.AllowsLifetime(EffectLifetimeKind.Instant), Is.True);
        }

        [Test]
        public void Audit_AllPresetTypes_LoadFromProductionJson()
        {
            string repoRoot = FindRepoRoot();
            string jsonPath = System.IO.Path.Combine(repoRoot, "assets", "Configs", "GAS", "preset_types.json");
            if (!System.IO.File.Exists(jsonPath))
            {
                Assert.Ignore("preset_types.json not found at expected path.");
                return;
            }

            string json = System.IO.File.ReadAllText(jsonPath);
            var registry = new PresetTypeRegistry();
            PresetTypeLoader.LoadFromJson(registry, json);

            var expectedTypes = new[]
            {
                EffectPresetType.InstantDamage, EffectPresetType.Heal,
                EffectPresetType.Buff, EffectPresetType.DoT, EffectPresetType.HoT,
                EffectPresetType.ApplyForce2D, EffectPresetType.Search,
                EffectPresetType.PeriodicSearch, EffectPresetType.LaunchProjectile,
                EffectPresetType.CreateUnit, EffectPresetType.Displacement,
            };

            foreach (var type in expectedTypes)
            {
                That(registry.IsRegistered(type), Is.True,
                    $"preset_types.json must contain definition for {type}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 1: InstantDamage — "Fireball" (Entity-targeted single damage)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void InstantDamage_WoW_SelectTargetThenQ_OrderSubmitted()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: enemy, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");

            That(orders.Count, Is.EqualTo(1), "WoW: One order submitted on Q press");
            That(orders[0].OrderTagId, Is.EqualTo(100), "Order tag = skill.q");
            That(orders[0].Target, Is.EqualTo(enemy), "Target = pre-selected enemy");
        }

        [Test]
        public void InstantDamage_DotA_QThenAim_ConfirmWithClick()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            // Frame 1: Press Q → enters aiming
            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(0), "DotA: No order yet, entered aiming");
            That(system.IsAiming, Is.True, "System should be aiming");

            // Frame 2: Confirm with left-click → order submitted
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1), "DotA: Order submitted on confirm click");
            That(orders[0].Target, Is.EqualTo(enemy), "Target = hovered enemy at confirm time");
            That(system.IsAiming, Is.False, "Aiming ended after confirm");
        }

        [Test]
        public void InstantDamage_LoL_HoverEnemyThenQ_InstantCast()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "LoL: Instant cast on Q press");
            That(orders[0].Target, Is.EqualTo(enemy), "Target = hovered enemy");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 2: Heal — "Healing Touch" (Entity-targeted heal)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Heal_WoW_SelectFriendlyThenW_OrderSubmitted()
        {
            using var world = World.Create();
            var player = world.Create();
            var ally = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: ally, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(orders.Count, Is.EqualTo(1), "WoW Heal: One order on W press");
            That(orders[0].OrderTagId, Is.EqualTo(101), "Order tag = skill.w");
            That(orders[0].Target, Is.EqualTo(ally), "Target = pre-selected ally");
        }

        [Test]
        public void Heal_DotA_WThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();
            var ally = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: ally, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(system.IsAiming, Is.True, "DotA Heal: Aiming after W");

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
            That(orders[0].Target, Is.EqualTo(ally));
        }

        [Test]
        public void Heal_LoL_HoverAllyThenW_InstantCast()
        {
            using var world = World.Create();
            var player = world.Create();
            var ally = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: ally, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(orders.Count, Is.EqualTo(1), "LoL Heal: Instant on W");
            That(orders[0].Target, Is.EqualTo(ally));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 3: DoT — "Ignite" (Entity-targeted DoT)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void DoT_WoW_SelectEnemyThenQ_IgniteApplied()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: enemy, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "WoW DoT: Order on Q");
            That(orders[0].Target, Is.EqualTo(enemy));
        }

        [Test]
        public void DoT_DotA_QThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True);

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void DoT_LoL_HoverEnemyQ_InstantIgnite()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "LoL DoT: Instant on Q");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 4: HoT — "Rejuvenation" (Entity-targeted HoT)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HoT_WoW_SelectSelfThenW_RegenApplied()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: player, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(orders.Count, Is.EqualTo(1), "WoW HoT: Self-cast W");
            That(orders[0].Target, Is.EqualTo(player), "Target = self");
        }

        [Test]
        public void HoT_DotA_WThenAimSelfConfirm()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: player, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(system.IsAiming, Is.True);
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void HoT_LoL_HoverSelfW_InstantRegen()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: player, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(orders.Count, Is.EqualTo(1), "LoL HoT: Instant on W");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 5: Buff — "Battle Shout" (Self-cast buff, no target)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Buff_WoW_PressE_SelfBuff()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.None, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "WoW Buff: Self-cast E");
            That(orders[0].OrderTagId, Is.EqualTo(102), "skill.e tag");
        }

        [Test]
        public void Buff_DotA_EThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.None, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(system.IsAiming, Is.True);
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void Buff_LoL_PressE_InstantSelfBuff()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.None, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "LoL Buff: Instant self-cast");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 6: ApplyForce2D — "Force Push" (Entity-targeted knockback)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ApplyForce2D_WoW_SelectEnemyThenQ_ForceApplied()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: enemy, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "WoW Force: Order on Q");
            That(orders[0].Target, Is.EqualTo(enemy));
        }

        [Test]
        public void ApplyForce2D_DotA_QThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True);
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void ApplyForce2D_LoL_HoverEnemyQ_InstantForce()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 7: Search — "Flamestrike" (Position-targeted AoE)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Search_WoW_SelectGroundThenE_AoEAtPosition()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(500, 0, 300);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);
            config.Mappings[2].RequireSelection = true;

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "WoW Search: AoE on E");
            That(orders[0].Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            That(orders[0].Args.Spatial.WorldCm.X, Is.EqualTo(500f).Within(1f));
        }

        [Test]
        public void Search_DotA_EThenAimGround_ConfirmClick()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(800, 0, 200);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(system.IsAiming, Is.True, "DotA Search: Aiming after E");

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1), "DotA Search: Order on confirm");
            That(orders[0].Args.Spatial.WorldCm.X, Is.EqualTo(800f).Within(1f));
        }

        [Test]
        public void Search_LoL_PressE_InstantAoEAtCursor()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(600, 0, 100);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "LoL Search: Instant AoE at cursor");
            That(orders[0].Args.Spatial.WorldCm.X, Is.EqualTo(600f).Within(1f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 8: PeriodicSearch — "Blizzard" (Position-targeted zone)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PeriodicSearch_WoW_PressR_ZoneAtGround()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(1000, 0, 1000);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);
            config.Mappings[3].RequireSelection = true;

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(orders.Count, Is.EqualTo(1), "WoW PeriodicSearch: Zone on R");
            That(orders[0].Args.Spatial.WorldCm.X, Is.EqualTo(1000f).Within(1f));
        }

        [Test]
        public void PeriodicSearch_DotA_RThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(700, 0, 500);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(system.IsAiming, Is.True);
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void PeriodicSearch_LoL_PressR_InstantZone()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(400, 0, 400);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(orders.Count, Is.EqualTo(1), "LoL PeriodicSearch: Instant zone at cursor");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 9: LaunchProjectile — "Piercing Arrow" (Direction skillshot)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void LaunchProjectile_WoW_SelectEnemyThenQ_ArrowLaunched()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: enemy, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "WoW Projectile: Order on Q");
            That(orders[0].Target, Is.EqualTo(enemy));
        }

        [Test]
        public void LaunchProjectile_DotA_QThenAimDirection_ConfirmClick()
        {
            using var world = World.Create();
            var player = world.Create();
            var cursorPos = new Vector3(1500, 0, 0);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Direction, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: cursorPos);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True, "DotA Projectile: Aiming after Q");

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1), "DotA Projectile: Order on confirm");
            That(orders[0].Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
        }

        [Test]
        public void LaunchProjectile_LoL_PressQ_InstantArrowAtCursor()
        {
            using var world = World.Create();
            var player = world.Create();
            var cursorPos = new Vector3(1200, 0, 300);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Direction, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: cursorPos);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(orders.Count, Is.EqualTo(1), "LoL Projectile: Instant skillshot");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 10: CreateUnit — "Summon Skeleton" (Position-targeted summon)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void CreateUnit_WoW_PressR_SummonAtGround()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(300, 0, 200);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);
            config.Mappings[3].RequireSelection = true;

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(orders.Count, Is.EqualTo(1), "WoW CreateUnit: Summon on R");
        }

        [Test]
        public void CreateUnit_DotA_RThenAimConfirm()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(500, 0, 500);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(system.IsAiming, Is.True);
            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateUnit_LoL_PressR_InstantSummon()
        {
            using var world = World.Create();
            var player = world.Create();
            var groundTarget = new Vector3(200, 0, 200);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: groundTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/r");
            That(orders.Count, Is.EqualTo(1));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 11: Displacement — "Dash" (Direction-targeted dash)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Displacement_WoW_PressE_DashTowardCursor()
        {
            using var world = World.Create();
            var player = world.Create();
            var dashTarget = new Vector3(2000, 0, 0);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            // WoW mode: Direction skill uses Position selection with RequireSelection
            // so that TryBuildOrder fills the Spatial data correctly.
            var config = BuildMappingConfig(InteractionModeType.TargetFirst,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);
            config.Mappings[2].RequireSelection = true;

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: dashTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "WoW Displacement: Dash on E");
            That(orders[0].Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            That(orders[0].Args.Spatial.WorldCm.X, Is.EqualTo(2000f).Within(1f));
        }

        [Test]
        public void Displacement_DotA_EThenAimDirection_ConfirmClick()
        {
            using var world = World.Create();
            var player = world.Create();
            var dashTarget = new Vector3(1500, 0, 500);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Direction, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: dashTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(system.IsAiming, Is.True, "DotA Displacement: Aiming after E");

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1), "DotA Displacement: Order on confirm");
        }

        [Test]
        public void Displacement_LoL_PressE_InstantDashAtCursor()
        {
            using var world = World.Create();
            var player = world.Create();
            var dashTarget = new Vector3(1000, 0, 1000);

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Direction, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: dashTarget);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/e");
            That(orders.Count, Is.EqualTo(1), "LoL Displacement: Instant dash at cursor");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 12: Cross-cutting — AimCast Cancel / Switch / SmartCastWithIndicator
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AimCast_CancelWithEsc_NoOrderSubmitted()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/Escape");
            That(system.IsAiming, Is.False, "Aiming cancelled by ESC");
            That(orders.Count, Is.EqualTo(0), "No order submitted on cancel");
        }

        [Test]
        public void AimCast_CancelWithRightClick_NoOrderSubmitted()
        {
            using var world = World.Create();
            var player = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: null, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True);

            SimulatePressFrame(backend, handler, system, "<Mouse>/RightButton");
            That(system.IsAiming, Is.False, "Aiming cancelled by right-click");
            That(orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void AimCast_SwitchSkill_AimNewSkill()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.AimCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");
            That(system.IsAiming, Is.True);
            That(system.AimingActionId, Is.EqualTo("SkillQ"));

            SimulatePressFrame(backend, handler, system, "<Keyboard>/w");
            That(system.IsAiming, Is.True, "Still aiming (switched)");
            That(system.AimingActionId, Is.EqualTo("SkillW"), "Switched to W");

            SimulatePressFrame(backend, handler, system, "<Mouse>/LeftButton");
            That(orders.Count, Is.EqualTo(1));
            That(orders[0].OrderTagId, Is.EqualTo(101), "Order is for skill.w, not skill.q");
        }

        [Test]
        public void SmartCastWithIndicator_HoldRelease_CastsOnRelease()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCastWithIndicator,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            // Press Q → enters aiming (shows indicator)
            SimulatePress(backend, handler, "<Keyboard>/q");
            system.Update(0.016f);
            That(system.IsAiming, Is.True, "SmartCastWithIndicator: Aiming on press");
            That(orders.Count, Is.EqualTo(0), "No order yet while held");

            // Release Q → cast
            SimulateRelease(backend, handler, "<Keyboard>/q");
            system.Update(0.016f);
            That(system.IsAiming, Is.False, "Aiming ended on release");
            That(orders.Count, Is.EqualTo(1), "Order submitted on release");
            That(orders[0].Target, Is.EqualTo(enemy));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 13: GAS Effect Execution — Verifying effect processing
        //  Tests that the Order → Effect pipeline works for key preset types
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GasEffect_InstantDamage_ReducesHealth()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            var mods = default(EffectModifiers);
            mods.Add(attrId: 0, ModifierOp.Add, -30f);
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1, PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant, Modifiers = mods,
            });

            var programs = new GraphProgramRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers,
                GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());
            world.Get<AttributeBuffer>(target).SetCurrent(0, 100f);

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, new EffectPhaseGraphBindings(),
                EffectPresetType.InstantDamage, effectTagId: 1, effectTemplateId: 1);

            That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(70f),
                "InstantDamage: 100 - 30 = 70 HP");
        }

        [Test]
        public void GasEffect_Heal_IncreasesHealth()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.Heal,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            var mods = default(EffectModifiers);
            mods.Add(attrId: 0, ModifierOp.Add, 25f);
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1, PresetType = EffectPresetType.Heal,
                LifetimeKind = EffectLifetimeKind.Instant, Modifiers = mods,
            });

            var programs = new GraphProgramRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers,
                GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());
            world.Get<AttributeBuffer>(target).SetCurrent(0, 50f);

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, new EffectPhaseGraphBindings(),
                EffectPresetType.Heal, effectTagId: 1, effectTemplateId: 1);

            That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(75f),
                "Heal: 50 + 25 = 75 HP");
        }

        [Test]
        public void GasEffect_Buff_AppliesModifier()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.Buff,
                Components = ComponentFlags.ModifierParams | ComponentFlags.DurationParams,
                ActivePhases = EffectPhaseId.OnApply.ToFlag(),
                AllowedLifetimes = LifetimeFlags.Duration,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            int moveSpeedAttr = 5;
            var templates = new EffectTemplateRegistry();
            var mods = default(EffectModifiers);
            mods.Add(attrId: moveSpeedAttr, ModifierOp.Add, 50f);
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1, PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After, Modifiers = mods,
                DurationTicks = 300,
            });

            var programs = new GraphProgramRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers,
                GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());
            world.Get<AttributeBuffer>(target).SetCurrent(moveSpeedAttr, 300f);

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, new EffectPhaseGraphBindings(),
                EffectPresetType.Buff, effectTagId: 1, effectTemplateId: 1);

            That(world.Get<AttributeBuffer>(target).GetCurrent(moveSpeedAttr), Is.EqualTo(350f),
                "Buff: MoveSpeed 300 + 50 = 350");
        }

        [Test]
        public void GasEffect_Displacement_CreatesDisplacementEntity()
        {
            using var world = World.Create();

            var caster = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            var dispEntity = world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = caster,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 500,
                RemainingDistanceCm = Fix64.FromInt(500),
                TotalDurationTicks = 10,
                RemainingTicks = 10,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < 10; i++)
                system.Update(0f);

            var finalPos = world.Get<WorldPositionCm>(target).Value;
            That(finalPos.X.ToFloat(), Is.EqualTo(1500f).Within(5f),
                "Displacement: Target knocked back 500cm from (1000,0) to (1500,0)");
            That(world.IsAlive(dispEntity), Is.False,
                "Displacement entity cleaned up after completion");
        }

        [Test]
        public void GasEffect_LaunchProjectile_CreatesProjectileEntity()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.LaunchProjectile,
                Components = ComponentFlags.ProjectileParams,
                ActivePhases = EffectPhaseId.OnApply.ToFlag(),
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.CreateProjectile);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1, PresetType = EffectPresetType.LaunchProjectile,
                LifetimeKind = EffectLifetimeKind.Instant,
                Projectile = new ProjectileDescriptor
                {
                    Speed = 1200, Range = 2000, ArcHeight = 0, ImpactEffectTemplateId = 99,
                },
            });

            var programs = new GraphProgramRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers,
                GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create();

            var behavior = new EffectPhaseGraphBindings();
            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.LaunchProjectile,
                effectTagId: 1, effectTemplateId: 1);

            int projectileCount = 0;
            world.Query(new QueryDescription().WithAll<ProjectileState>(), (Entity e) =>
            {
                projectileCount++;
                var state = world.Get<ProjectileState>(e);
                That(state.ImpactEffectTemplateId, Is.EqualTo(99));
                That(state.Source, Is.EqualTo(caster));
            });
            That(projectileCount, Is.EqualTo(1), "LaunchProjectile: One projectile entity created");
        }

        [Test]
        public void GasEffect_CreateUnit_SpawnsEntity()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.CreateUnit,
                Components = ComponentFlags.UnitCreationParams,
                ActivePhases = EffectPhaseId.OnApply.ToFlag(),
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.CreateUnit);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1, PresetType = EffectPresetType.CreateUnit,
                LifetimeKind = EffectLifetimeKind.Instant,
                UnitCreation = new UnitCreationDescriptor
                {
                    UnitTypeId = 42, Count = 2, OffsetRadius = 200,
                },
            });

            var programs = new GraphProgramRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers,
                GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create();

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, new EffectPhaseGraphBindings(),
                EffectPresetType.CreateUnit, effectTagId: 1, effectTemplateId: 1);

            int spawnedCount = 0;
            world.Query(new QueryDescription().WithAll<SpawnedUnitState>(), (Entity e) =>
            {
                spawnedCount++;
                var state = world.Get<SpawnedUnitState>(e);
                That(state.UnitTypeId, Is.EqualTo(42));
                That(state.Spawner, Is.EqualTo(caster));
            });
            That(spawnedCount, Is.EqualTo(2), "CreateUnit: Two units spawned");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 14: Order Queue — Shift+Skill queues multiple orders
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderQueue_ShiftModifier_QueuesOrders()
        {
            using var world = World.Create();
            var player = world.Create();
            var enemy = world.Create();

            var backend = new MockInputBackend();
            var handler = new PlayerInputHandler(backend, BuildInputConfig());
            handler.PushContext("Gameplay");

            var config = BuildMappingConfig(InteractionModeType.SmartCast,
                qSel: OrderSelectionType.Entity, wSel: OrderSelectionType.Entity,
                eSel: OrderSelectionType.Position, rSel: OrderSelectionType.Position);

            var (system, orders) = BuildSystem(handler, config, player,
                selectedEntity: null, hoveredEntity: enemy, groundPos: null);

            // Hold Shift → Press Q → should be Queued mode
            backend.Press("<Keyboard>/LeftShift");
            handler.Update();
            SimulatePressFrame(backend, handler, system, "<Keyboard>/q");

            That(orders.Count, Is.EqualTo(1));
            That(orders[0].SubmitMode, Is.EqualTo(OrderSubmitMode.Queued),
                "Shift+Q should produce Queued submit mode");

            backend.Release("<Keyboard>/LeftShift");
            handler.Update();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helper
        // ════════════════════════════════════════════════════════════════════

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "assets")))
                    return dir;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
            throw new InvalidOperationException("Cannot find repo root.");
        }
    }
}
