using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using MobaDemoMod.Triggers;

namespace MobaDemoMod.Systems
{
    /// <summary>
    /// System that converts local player input to Orders using InputOrderMappingSystem.
    /// Uses configuration-driven mapping for input checks.
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
        private readonly int _moveToOrderTypeId;
        private readonly int _stopOrderTypeId;
        private readonly EntityCollectionStore _entityCollections;
        private readonly PlayerEntityLookup _playerEntities;
        private readonly ControlDomainQuery _controlDomains;
        private readonly Entity[] _collectionScratch;
        
        // Configuration-driven input-order mapping
        private InputOrderMappingSystem? _inputOrderMapping;
        private bool _initialized = false;
        private readonly int _commandIntentScratchCapacity;

        public MobaLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _ctx = ctx;
            
            if (_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                _castAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
                _moveToOrderTypeId = config.Constants.OrderTypeIds["moveTo"];
                _stopOrderTypeId = config.Constants.OrderTypeIds["stop"];
                GasRuntimeCapacityConfig capacity = config.GasRuntimeCapacity
                    ?? throw new InvalidOperationException(
                        "MobaLocalOrderSourceSystem requires GameConfig.gasRuntimeCapacity.");
                if (capacity.CommandIntentScratchCapacity <= 0)
                {
                    throw new InvalidOperationException(
                        "GameConfig.gasRuntimeCapacity.commandIntentScratchCapacity must be positive.");
                }

                _commandIntentScratchCapacity = capacity.CommandIntentScratchCapacity;
                _collectionScratch = new Entity[_commandIntentScratchCapacity];
            }
            else
            {
                throw new System.InvalidOperationException(
                    "MobaLocalOrderSourceSystem requires GameConfig in globals with order type ids (castAbility, moveTo, stop). " +
                    "Ensure game.json constants.orderTypeIds is properly configured.");
            }

            _entityCollections = _globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                collectionsObj is EntityCollectionStore collections
                    ? collections
                    : throw new InvalidOperationException(
                        $"{nameof(MobaLocalOrderSourceSystem)} requires {CoreServiceKeys.EntityCollectionStore.Name} to be registered.");
            _playerEntities = _globals.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object? playersObj) &&
                playersObj is PlayerEntityLookup players
                    ? players
                    : throw new InvalidOperationException(
                        $"{nameof(MobaLocalOrderSourceSystem)} requires {CoreServiceKeys.PlayerEntityLookup.Name} to be registered.");
            _controlDomains = _globals.TryGetValue(CoreServiceKeys.ControlDomainQuery.Name, out object? domainsObj) &&
                domainsObj is ControlDomainQuery controlDomains
                    ? controlDomains
                    : throw new InvalidOperationException(
                        $"{nameof(MobaLocalOrderSourceSystem)} requires {CoreServiceKeys.ControlDomainQuery.Name} to be registered.");
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
            _inputOrderMapping = new InputOrderMappingSystem(input, config, _commandIntentScratchCapacity);
            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = _inputOrderMapping;
            _globals[CoreInputMod.Systems.SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
            var bindings = InteractionActionBindingsResolver.Require(_globals, nameof(MobaLocalOrderSourceSystem));
            _inputOrderMapping.ConfirmActionId = bindings.ConfirmActionId;
            _inputOrderMapping.CancelActionId = bindings.CancelActionId;
            _inputOrderMapping.CommandActionId = bindings.CommandActionId;
            
            // Order type key resolver
            _inputOrderMapping.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => _castAbilityOrderTypeId,
                "moveTo" => _moveToOrderTypeId,
                "stop" => _stopOrderTypeId,
                _ => throw new InvalidOperationException(
                    $"[{_ctx.ModId}] input_order_mappings.json references unknown orderTypeKey '{key}'.")
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
            
            // Entity collection providers
            _inputOrderMapping.SetActorProvider((out Entity entity) =>
            {
                entity = TryGetSolePossessedPlayerId(out int playerId)
                    ? GetControlledActor(playerId)
                    : default;
                return _world.IsAlive(entity);
            });
            _inputOrderMapping.SetActivationActorValidator((actor, playerId) =>
                InputOrderActorAuthorization.IsAuthorized(
                    _world,
                    _playerEntities,
                    _controlDomains,
                    actor,
                    playerId));
            _inputOrderMapping.SetOrderIdentityAssigner((ref Order order) => _orders.EnsureOrderId(ref order));
            _inputOrderMapping.SetCollectionPrimaryEntityProvider((string collectionKey, out Entity entity) =>
            {
                return TryGetCollectionPrimary(collectionKey, out entity);
            });
            _inputOrderMapping.SetCollectionEntityListProvider((string collectionKey, List<Entity> entities, int capacity, out OrderSubmitResult rejection) =>
            {
                return TryCopyCollectionEntities(collectionKey, entities, capacity, out rejection);
            });

            _inputOrderMapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                return TryGetHovered(out entity);
            });
            
            // Order submit handler
            // Visual feedback (markers, cooldown text) is handled by Core PresenterRuleSystem
            // via GAS -> PresentationEvent bridge; no mod-level marker logic needed.
            _inputOrderMapping.SetOrderSubmitHandler((in Order order) =>
            {
                return _orders.Submit(in order);
            });

            // Aiming state -> Presenter direct API (for AimCast mode)
            // Uses PresenterCommandBuffer to create/destroy a presenter scope.
            if (_globals.TryGetValue(CoreServiceKeys.PresenterCommandBuffer.Name, out var cmdObj) && cmdObj is PresenterCommandBuffer commands)
            {
                var mc = (MobaConfig)_globals[InstallMobaDemoOnGameStartTrigger.MobaConfigKey];
                var perfReg = _globals.TryGetValue(CoreServiceKeys.PresenterDefinitionRegistry.Name, out var prObj) && prObj is PresenterDefinitionRegistry pr ? pr : null;
                int rangeCircleDefId = perfReg?.GetId(mc.Presentation.RangeCircleIndicatorDefKey) ?? 0;

                _inputOrderMapping.SetAimingStateChangedHandler((isAiming, mapping) =>
                {
                    int scopeId = mapping.ActionId.GetHashCode();
                    if (isAiming)
                    {
                        commands.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rangeCircleDefId,
                            ScopeTag = scopeId,
                            Source = TryGetSolePossessedPlayerId(out int playerId)
                                ? GetControlledActor(playerId)
                                : default
                        });
                    }
                    else
                    {
                        // Destroy the entire aiming scope
                        commands.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.DestroyPresenterScope,
                            ScopeTag = scopeId
                        });
                    }
                });

                // No update handler needed; presenter position resolves from Owner entity each frame.
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

                if (ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity localPlayer) &&
                    _world.IsAlive(localPlayer))
                {
                    if (!TryGetSolePossessedPlayerId(out int playerId))
                    {
                        return;
                    }

                    var actor = GetControlledActor(playerId);
                    if (!_world.IsAlive(actor))
                    {
                        return;
                    }

                    _inputOrderMapping.SetSolePossessedActor(actor, playerId);
                    _inputOrderMapping.Update(dt);
                }
            }

            RenderModeHud();
        }

        private bool TryGetSolePossessedPlayerId(out int playerId)
        {
            playerId = 0;
            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_globals);
            if (!seats.TryGetSoleSeat(out ClientLocalSeat seat) || !seat.HasPossession)
            {
                return false;
            }

            playerId = seat.PossessedPlayerId;
            return true;
        }

        private Entity GetControlledActor(int playerId)
        {
            if (playerId <= 0)
            {
                return default;
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out var localPlayer))
                return default;
            if (!_world.IsAlive(localPlayer)) return default;

            if (TryGetCollectionPrimary(EntityCollectionKeys.CommandSource, out var commandSourcePrimary))
            {
                if (_world.TryGet(commandSourcePrimary, out Ludots.Core.Gameplay.Components.PlayerOwner owner) && owner.PlayerId == playerId)
                    return commandSourcePrimary;
            }
            return localPlayer;
        }

        private bool TryGetCollectionPrimary(string collectionKey, out Entity target)
        {
            target = default;
            if (!TryResolveLocalCollection(collectionKey, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            Entity primary = view.PrimaryEntity != Entity.Null
                ? view.PrimaryEntity
                : _entityCollections.TryGetEntityAt(handle, 0, out Entity first)
                    ? first
                    : Entity.Null;
            if (primary == Entity.Null || !_world.IsAlive(primary))
            {
                return false;
            }

            target = primary;
            return true;
        }

        private bool TryCopyCollectionEntities(
            string collectionKey,
            List<Entity> entities,
            int capacity,
            out OrderSubmitResult rejection)
        {
            entities.Clear();
            rejection = OrderSubmitResult.RejectedInvalidActor;
            if (!TryResolveLocalCollection(collectionKey, out EntityCollectionHandle handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            if (view.Count > capacity)
            {
                rejection = OrderSubmitResult.RejectedAdmissionCapacity;
                return false;
            }

            EnsureCollectionScratch(view.Count);
            int count = _entityCollections.CopyEntities(handle, 0, _collectionScratch);
            for (int i = 0; i < count; i++)
            {
                Entity entity = _collectionScratch[i];
                if (_world.IsAlive(entity))
                {
                    if (entities.Count >= capacity)
                    {
                        entities.Clear();
                        rejection = OrderSubmitResult.RejectedAdmissionCapacity;
                        return false;
                    }

                    entities.Add(entity);
                }
            }

            if (entities.Count <= 0)
            {
                rejection = OrderSubmitResult.RejectedInvalidActor;
                return false;
            }

            rejection = OrderSubmitResult.Activated;
            return entities.Count > 0;
        }

        private bool TryResolveLocalCollection(
            string collectionKey,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            handle = EntityCollectionHandle.Invalid;
            view = default;
            return !string.IsNullOrWhiteSpace(collectionKey) &&
                   TryGetLocalCollectionOwner(out Entity owner) &&
                   _entityCollections.TryGet(owner, collectionKey, out handle) &&
                   _entityCollections.TryGetView(handle, out view);
        }

        private bool TryGetLocalCollectionOwner(out Entity owner)
        {
            owner = default;
            return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private void EnsureCollectionScratch(int required)
        {
            if (_collectionScratch.Length >= required)
            {
                return;
            }

            throw new InvalidOperationException(
                $"MOBA.INPUT.ERR.CollectionScratchCapacityExceeded: required={required}, capacity={_collectionScratch.Length}.");
        }

        private bool TryGetHovered(out Entity target)
        {
            target = default;
            return TryGetLocalCollectionOwner(out Entity owner) &&
                   EntityCollectionContextRuntime.TryGetHovered(_world, _entityCollections, owner, out target);
        }

        private bool TryGetCommandWorldPoint(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input) return false;
            return AuthoritativeGroundPointerHelper.TryRead(input, out worldCm);
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
