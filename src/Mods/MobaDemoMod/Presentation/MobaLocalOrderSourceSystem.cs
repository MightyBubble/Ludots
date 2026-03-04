using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
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
    /// </summary>
    public sealed class MobaLocalOrderSourceSystem : ISystem<float>
    {
        private static int _debugModeHudLogsRemaining = 4;
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly IModContext _ctx;
        private readonly int _castAbilityTagId;
        private readonly int _stopTagId;
        private readonly int _hoverScopeId = 99002;
        
        // Configuration-driven input-order mapping
        private InputOrderMappingSystem? _inputOrderMapping;
        private bool _initialized = false;
        private Entity _hoveredIndicatorOwner;

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

            _inputOrderMapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                return TryGetHovered(out entity);
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
            InitializeInputOrderMapping();

            if (_inputOrderMapping != null &&
                _globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                CheckModeSwitchKeys(input, _inputOrderMapping);

                if (_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var actorObj) &&
                    actorObj is Entity localPlayer &&
                    _world.IsAlive(localPlayer))
                {
                    var actor = GetControlledActor();
                    _inputOrderMapping.SetLocalPlayer(actor, 1);
                    _inputOrderMapping.Update(dt);
                }
            }

            RenderModeHud();
            UpdateHoverIndicator();
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

        private bool TryGetHovered(out Entity target)
        {
            target = default;
            if (!_globals.TryGetValue(ContextKeys.HoveredEntity, out var obj) || obj is not Entity e) return false;
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
            return InputOrderMappingLoader.LoadFromStream(stream);
        }

        private static void CheckModeSwitchKeys(PlayerInputHandler input, InputOrderMappingSystem mapping)
        {
            if (input.PressedThisFrame("ModeWoW"))
            {
                mapping.SetInteractionMode(InteractionModeType.TargetFirst);
            }
            else if (input.PressedThisFrame("ModeLoL"))
            {
                mapping.SetInteractionMode(InteractionModeType.SmartCast);
            }
            else if (input.PressedThisFrame("ModeSC2"))
            {
                mapping.SetInteractionMode(InteractionModeType.AimCast);
            }
            else if (input.PressedThisFrame("ModeIndicator"))
            {
                mapping.SetInteractionMode(InteractionModeType.SmartCastWithIndicator);
            }
        }

        private void RenderModeHud()
        {
            if (_inputOrderMapping == null) return;
            if (!_globals.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay) return;

            overlay.AddRect(
                x: 8,
                y: 8,
                width: 620,
                height: 98,
                fill: new Vector4(0f, 0f, 0f, 0.45f),
                border: new Vector4(1f, 1f, 1f, 0.16f));
            overlay.AddText(16, 16, $"Mode: {ToModeLabel(_inputOrderMapping.InteractionMode)}", 20, new Vector4(1f, 1f, 0.6f, 1f));
            overlay.AddText(16, 42, "F1 WoW(TargetFirst) | F2 LoL(SmartCast) | F3 SC2(AimCast) | F4 Indicator", 16, new Vector4(0.78f, 0.92f, 1f, 1f));
            overlay.AddText(16, 66, "F6 Showcase Toggle | F7 Showcase Reset | F9 Fireball | F10 Circle | F11 Summon", 16, new Vector4(0.85f, 1f, 0.85f, 1f));
            if (_debugModeHudLogsRemaining > 0)
            {
                _debugModeHudLogsRemaining--;
                // #region agent log
                File.AppendAllText("/opt/cursor/logs/debug.log", JsonSerializer.Serialize(new
                {
                    hypothesisId = "H3",
                    location = "MobaLocalOrderSourceSystem:RenderModeHud",
                    message = "Mode HUD overlay drawn",
                    data = new { rectX = 8, rectY = 8, rectWidth = 620, rectHeight = 98, titleY = 16, line2Y = 42, line3Y = 66 },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + "\n");
                // #endregion
            }
        }

        private static string ToModeLabel(InteractionModeType mode)
        {
            return mode switch
            {
                InteractionModeType.TargetFirst => "WoW / TargetFirst",
                InteractionModeType.SmartCast => "LoL / SmartCast",
                InteractionModeType.AimCast => "SC2 / AimCast",
                InteractionModeType.SmartCastWithIndicator => "LoL Indicator / Hold-Release",
                _ => mode.ToString()
            };
        }

        private void UpdateHoverIndicator()
        {
            if (!_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) || cmdObj is not PresentationCommandBuffer commands)
                return;
            if (!_globals.TryGetValue(InstallMobaDemoOnGameStartTrigger.MobaConfigKey, out var cfgObj) || cfgObj is not MobaConfig cfg)
                return;

            int hoverDefId = cfg.Presentation.HoverIndicatorDefId;
            bool hasHovered = TryGetHovered(out var hovered);
            if (hasHovered && _world.IsAlive(hovered) && hovered == _hoveredIndicatorOwner)
                return;

            if (_hoveredIndicatorOwner != default)
            {
                commands.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.DestroyPerformerScope,
                    IdA = _hoverScopeId
                });
                _hoveredIndicatorOwner = default;
            }

            if (hasHovered && _world.IsAlive(hovered))
            {
                commands.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.CreatePerformer,
                    IdA = hoverDefId,
                    IdB = _hoverScopeId,
                    Source = hovered
                });
                _hoveredIndicatorOwner = hovered;
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
