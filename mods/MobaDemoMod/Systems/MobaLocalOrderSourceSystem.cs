using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
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

namespace MobaDemoMod.Systems
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
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly IModContext _ctx;
        private readonly int _castAbilityOrderTypeId;
        private readonly int _stopOrderTypeId;
        
        // Configuration-driven input-order mapping
        private InputOrderMappingSystem? _inputOrderMapping;
        private bool _initialized = false;

        public MobaLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _ctx = ctx;
            
            if (_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                _castAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
                _stopOrderTypeId = config.Constants.OrderTypeIds["stop"];
            }
            else
            {
                throw new System.InvalidOperationException(
                    "MobaLocalOrderSourceSystem requires GameConfig in globals with order type ids (castAbility, stop). " +
                    "Ensure game.json constants.orderTypeIds is properly configured.");
            }
        }

        public void Initialize() { }
        
        private void InitializeInputOrderMapping()
        {
            if (_initialized) return;
            _initialized = true;
            
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
                return;
            
            // Load input-order mappings from mod assets via VFS
            var config = LoadInputOrderMappings();
            _inputOrderMapping = new InputOrderMappingSystem(input, config);
            _globals[CoreInputMod.Systems.LocalOrderSourceHelper.ActiveMappingKey] = _inputOrderMapping;
            _globals[CoreInputMod.Systems.SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
            
            // Order type key resolver
            _inputOrderMapping.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => _castAbilityOrderTypeId,
                "stop" => _stopOrderTypeId,
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
            _inputOrderMapping.SetSelectedEntitiesProvider((ref OrderEntitySelection entities) =>
            {
                entities = default;
                if (!TryGetSelected(out var entity)) return false;
                entities.Add(entity);
                return true;
            });

            _inputOrderMapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                return TryGetHovered(out entity);
            });
            
            // Order submit handler
            // Visual feedback (markers, cooldown text) is handled by Core PerformerRuleSystem
            // via GAS -> PresentationEvent bridge; no mod-level marker logic needed.
            _inputOrderMapping.SetOrderSubmitHandler((in Order order) =>
            {
                _orders.TryEnqueue(order);
            });

            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj) && bindingsObj is InteractionActionBindings bindings)
            {
                _inputOrderMapping.ConfirmActionId = bindings.ConfirmActionId;
                _inputOrderMapping.CancelActionId = bindings.CancelActionId;
                _inputOrderMapping.CommandActionId = bindings.CommandActionId;
            }

            // Aiming state -> Performer direct API (for AimCast mode)
            // Uses PresentationCommandBuffer to create/destroy a performer scope.
            if (_globals.TryGetValue(CoreServiceKeys.PresentationCommandBuffer.Name, out var cmdObj) && cmdObj is PresentationCommandBuffer commands)
            {
                var mc = (MobaConfig)_globals[InstallMobaDemoOnGameStartTrigger.MobaConfigKey];
                var perfReg = _globals.TryGetValue(CoreServiceKeys.PerformerDefinitionRegistry.Name, out var prObj) && prObj is PerformerDefinitionRegistry pr ? pr : null;
                int rangeCircleDefId = perfReg?.GetId(mc.Presentation.RangeCircleIndicatorDefKey) ?? 0;

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

                // No update handler needed; performer position resolves from Owner entity each frame.
            }
        }

        public void Update(in float dt)
        {
            InitializeInputOrderMapping();

            if (_inputOrderMapping != null &&
                _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                inputObj is IInputActionReader input)
            {
                CheckModeSwitchKeys(input, _inputOrderMapping);

                if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var actorObj) &&
                    actorObj is Entity localPlayer &&
                    _world.IsAlive(localPlayer))
                {
                    var actor = GetControlledActor();
                    _inputOrderMapping.SetLocalPlayer(actor, 1);
                    _inputOrderMapping.Update(dt);
                }
            }

            RenderModeHud();
        }

        private Entity GetControlledActor()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var actorObj) || actorObj is not Entity localPlayer)
                return default;
            if (!_world.IsAlive(localPlayer)) return default;

            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var obj) && obj is Entity selected && _world.IsAlive(selected))
            {
                if (_world.TryGet(selected, out Ludots.Core.Gameplay.Components.PlayerOwner owner) && owner.PlayerId == 1)
                    return selected;
            }
            return localPlayer;
        }

        private bool TryGetSelected(out Entity target)
        {
            target = default;
            if (!_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var obj) || obj is not Entity e) return false;
            if (!_world.IsAlive(e)) return false;
            target = e;
            return true;
        }

        private bool TryGetHovered(out Entity target)
        {
            target = default;
            if (!_globals.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var obj) || obj is not Entity e) return false;
            if (!_world.IsAlive(e)) return false;
            target = e;
            return true;
        }

        private bool TryGetCommandWorldPoint(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) || rayObj is not IScreenRayProvider rayProvider) return false;
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input) return false;

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

        private static void CheckModeSwitchKeys(IInputActionReader input, InputOrderMappingSystem mapping)
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
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay) return;

            overlay.AddRect(
                x: 8,
                y: 8,
                width: 620,
                height: 74,
                fill: new Vector4(0f, 0f, 0f, 0.45f),
                border: new Vector4(1f, 1f, 1f, 0.16f));
            overlay.AddText(16, 16, $"Mode: {ToModeLabel(_inputOrderMapping.InteractionMode)}", 20, new Vector4(1f, 1f, 0.6f, 1f));
            overlay.AddText(16, 42, "F1 WoW(TargetFirst) | F2 LoL(SmartCast) | F3 SC2(AimCast) | F4 Indicator", 16, new Vector4(0.78f, 0.92f, 1f, 1f));
        }

        private static string ToModeLabel(InteractionModeType mode)
        {
            return mode switch
            {
                InteractionModeType.TargetFirst => "WoW / target first",
                InteractionModeType.SmartCast => "LoL / smart cast",
                InteractionModeType.AimCast => "SC2 / aim then confirm",
                InteractionModeType.SmartCastWithIndicator => "LoL Indicator / hold to show, release to cast",
                _ => mode.ToString()
            };
        }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}

