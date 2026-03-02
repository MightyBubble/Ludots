using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MobaDemoMod.Triggers;
using MobaDemoMod.Utils;

namespace MobaDemoMod.Presentation
{
    /// <summary>
    /// System that converts local player input to Orders using InputOrderMappingSystem.
    /// Uses configuration-driven mapping instead of hardcoded input checks.
    ///
    /// The interaction mode (TargetFirst/SmartCast/AimCast) is handled entirely
    /// inside InputOrderMappingSystem based on the config's InteractionMode setting.
    /// This system only wires up the callbacks and bridges aiming state to indicators.
    ///
    /// F1/F2/F3 keys switch the interaction mode at runtime:
    ///   F1 = WoW (TargetFirst), F2 = LoL (SmartCast), F3 = DotA (AimCast)
    /// Current mode is rendered to ScreenOverlayBuffer (generic core HUD buffer).
    /// </summary>
    public sealed class MobaLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly IModContext _ctx;
        private readonly int _castAbilityTagId;
        private readonly int _stopTagId;
        
        private InputOrderMappingSystem? _inputOrderMapping;
        private bool _initialized = false;
        private InteractionModeType _currentMode = InteractionModeType.SmartCast;
        private bool _modeChangePending = false;

        public MobaLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _ctx = ctx;
            
            if (_globals.TryGetValue(ContextKeys.GameConfig, out var configObj) && configObj is GameConfig config)
            {
                _castAbilityTagId = config.Constants.OrderTags["castAbility"];
                _stopTagId = config.Constants.OrderTags["stop"];
            }
            else
            {
                throw new System.InvalidOperationException(
                    "MobaLocalOrderSourceSystem requires GameConfig in globals with orderTags (castAbility, stop). " +
                    "Ensure game.json constants.orderTags is properly configured.");
            }
        }

        public void Initialize() { }
        
        private void InitializeInputOrderMapping()
        {
            if (_initialized) return;
            _initialized = true;
            
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input)
                return;
            
            // Load input-order mappings from mod assets via VFS
            var config = LoadInputOrderMappings();
            _inputOrderMapping = new InputOrderMappingSystem(input, config);
            
            // Tag key resolver
            _inputOrderMapping.SetTagKeyResolver(key => key switch
            {
                "castAbility" => _castAbilityTagId,
                "stop" => _stopTagId,
                _ => 0
            });
            
            // Ground position provider
            _inputOrderMapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = default;
                if (TryGetCommandWorldPoint(out var pos))
                {
                    worldCm = new Vector3(pos.X, 0f, pos.Y);
                    return true;
                }
                return false;
            });
            
            // Selected entity provider
            _inputOrderMapping.SetSelectedEntityProvider((out Entity entity) =>
            {
                return TryGetSelected(out entity);
            });

            // Hovered entity provider (for SmartCast: entity under cursor)
            // TODO: Wire up actual hover-detection from EntityClickSelectSystem
            _inputOrderMapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = default;
                return false; // Not yet implemented — falls back to selected
            });
            
            // Order submit handler
            // Visual feedback (markers, cooldown text) is handled by Core PerformerRuleSystem
            // via GAS → PresentationEvent bridge — no mod-level marker logic needed.
            _inputOrderMapping.SetOrderSubmitHandler((in Order order) =>
            {
                _orders.TryEnqueue(order);
            });

            // Aiming state → Performer direct API (for AimCast mode)
            // Uses PresentationCommandBuffer to create/destroy a performer scope.
            if (_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer commands)
            {
                var mc = (MobaConfig)_globals[InstallMobaDemoOnGameStartTrigger.MobaConfigKey];
                int rangeCircleDefId = mc.Presentation.RangeCircleIndicatorDefId;

                _inputOrderMapping.SetAimingStateChangedHandler((isAiming, mapping) =>
                {
                    int scopeId = mapping.ActionId.GetHashCode();
                    if (isAiming)
                    {
                        commands.TryAdd(new PresentationCommand
                        {
                            Kind = PresentationCommandKind.CreatePerformer,
                            IdA = rangeCircleDefId,
                            IdB = scopeId,
                            Source = GetControlledActor()
                        });
                    }
                    else
                    {
                        // Destroy the entire aiming scope
                        commands.TryAdd(new PresentationCommand
                        {
                            Kind = PresentationCommandKind.DestroyPerformerScope,
                            IdA = scopeId
                        });
                    }
                });

                // No update handler needed — Performer position resolves from Owner entity each frame.
            }
        }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input)
            {
                UpdateHudState();
                return;
            }

            // F1/F2/F3/F4 or 1/2/3/4: switch interaction mode
            CheckModeSwitchKeys(input);
            RunAutoDemo(dt);

            if (_modeChangePending)
            {
                _modeChangePending = false;
                _initialized = false;
                _inputOrderMapping?.CancelAiming();
                _inputOrderMapping = null;
            }

            InitializeInputOrderMapping();

            if (_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var actorObj) && actorObj is Entity localPlayer && _world.IsAlive(localPlayer))
            {
                var actor = GetControlledActor();
                if (_inputOrderMapping != null)
                {
                    _inputOrderMapping.SetLocalPlayer(actor, 1);
                    _inputOrderMapping.Update(dt);
                }
            }

            UpdateHudState();
        }

        private void UpdateHudState()
        {
            if (!_globals.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var bufObj) || bufObj is not ScreenOverlayBuffer buf)
                return;

            string modeName = _currentMode switch
            {
                InteractionModeType.TargetFirst => "WoW (TargetFirst)",
                InteractionModeType.SmartCast => "LoL (SmartCast)",
                InteractionModeType.AimCast => "DotA (AimCast)",
                InteractionModeType.SmartCastWithIndicator => "LoL+ (Indicator)",
                _ => _currentMode.ToString()
            };

            int x = 840;
            int y = 40;

            buf.AddRect(x - 6, y - 6, 356, 120,
                new Vector4(0, 0, 0, 0.7f), new Vector4(0, 0.78f, 1, 0.78f));

            buf.AddText(x, y, $"Mode: {modeName}", 20, new Vector4(0, 1, 0.78f, 1));
            y += 24;
            buf.AddText(x, y, "[1/F1] WoW  [2/F2] LoL  [3/F3] DotA  [4/F4] LoL+", 14, new Vector4(0.7f, 0.7f, 0.7f, 1));
            y += 18;
            buf.AddText(x, y, "[Q] Fireball  [W] Heal  [E] ConeAoE  [R] Blizzard", 14, new Vector4(0.78f, 0.78f, 0.4f, 1));
            y += 20;

            bool isAiming = _inputOrderMapping?.IsAiming ?? false;
            string aimAction = _inputOrderMapping?.AimingActionId ?? "";

            if (isAiming && !string.IsNullOrEmpty(aimAction))
            {
                buf.AddText(x, y, $">>> AIMING: {aimAction} (click to cast) <<<", 16, new Vector4(1, 0.4f, 0.2f, 1));
            }
            else
            {
                buf.AddText(x, y, "Click entity to select, then use skills", 14, new Vector4(0.55f, 0.55f, 0.55f, 1));
            }
            y += 20;
            buf.AddText(x, y, "[LClick] Select  [RClick] Move  [S] Stop", 14, new Vector4(0.55f, 0.55f, 0.55f, 1));

            if (_autoDemoEnabled)
            {
                y += 18;
                buf.AddText(x, y, $"[AutoDemo] Step={_autoDemoStep} Timer={_autoDemoTimer:F1}s", 12, new Vector4(1, 0.5f, 0, 1));
            }
        }

        // Auto-demo: cycles through modes and skills for recording purposes.
        // Activated by env var MOBA_AUTO_DEMO=1. Disables itself after one full cycle.
        private float _autoDemoTimer;
        private int _autoDemoStep;
        private bool _autoDemoEnabled = System.Environment.GetEnvironmentVariable("MOBA_AUTO_DEMO") == "1";
        private static readonly (InteractionModeType mode, string label)[] AutoDemoSequence =
        {
            (InteractionModeType.SmartCast,             "LoL SmartCast"),
            (InteractionModeType.TargetFirst,           "WoW TargetFirst"),
            (InteractionModeType.AimCast,               "DotA AimCast"),
            (InteractionModeType.SmartCastWithIndicator, "LoL+ Indicator"),
            (InteractionModeType.SmartCast,             "Back to LoL"),
        };

        private void RunAutoDemo(float dt)
        {
            if (!_autoDemoEnabled) return;
            _autoDemoTimer += dt;
            if (_autoDemoTimer < 2f) return; // 2s per step
            _autoDemoTimer = 0f;

            if (_autoDemoStep < AutoDemoSequence.Length)
            {
                _currentMode = AutoDemoSequence[_autoDemoStep].mode;
                _modeChangePending = true;
                _autoDemoStep++;
            }
            else
            {
                _autoDemoEnabled = false;
            }
        }

        private void CheckModeSwitchKeys(PlayerInputHandler input)
        {
            if (input.PressedThisFrame("ModeWoW") || input.PressedThisFrame("Hotkey1"))
            {
                _currentMode = InteractionModeType.TargetFirst;
                _modeChangePending = true;
            }
            else if (input.PressedThisFrame("ModeLoL") || input.PressedThisFrame("Hotkey2"))
            {
                _currentMode = InteractionModeType.SmartCast;
                _modeChangePending = true;
            }
            else if (input.PressedThisFrame("ModeDotA") || input.PressedThisFrame("Hotkey3"))
            {
                _currentMode = InteractionModeType.AimCast;
                _modeChangePending = true;
            }
            else if (input.PressedThisFrame("ModeLoLPlus") || input.PressedThisFrame("Hotkey4"))
            {
                _currentMode = InteractionModeType.SmartCastWithIndicator;
                _modeChangePending = true;
            }
        }

        private Entity GetControlledActor()
        {
            if (!_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var actorObj) || actorObj is not Entity localPlayer)
                return default;
            if (!_world.IsAlive(localPlayer)) return default;

            if (_globals.TryGetValue(ContextKeys.SelectedEntity, out var obj) && obj is Entity selected && _world.IsAlive(selected))
            {
                if (_world.TryGet(selected, out Ludots.Core.Gameplay.Components.PlayerOwner owner) && owner.PlayerId == 1)
                    return selected;
            }
            return localPlayer;
        }

        private bool TryGetSelected(out Entity target)
        {
            target = default;
            if (!_globals.TryGetValue(ContextKeys.SelectedEntity, out var obj) || obj is not Entity e) return false;
            if (!_world.IsAlive(e)) return false;
            target = e;
            return true;
        }

        private bool TryGetCommandWorldPoint(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(ContextKeys.ScreenRayProvider, out var rayObj) || rayObj is not IScreenRayProvider rayProvider) return false;
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input) return false;

            Vector2 mouse = input.ReadAction<Vector2>("PointerPos");
            var ray = rayProvider.GetRay(mouse);
            return GroundRaycast.TryGetGroundWorldCm(in ray, out worldCm);
        }

        private InputOrderMappingConfig LoadInputOrderMappings()
        {
            string uri = $"{_ctx.ModId}:assets/Input/input_order_mappings.json";
            using var stream = _ctx.VFS.GetStream(uri);
            var config = InputOrderMappingLoader.LoadFromStream(stream);
            config.InteractionMode = _currentMode;
            return config;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
