using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Delegate for resolving an order type key to an order type id.
    /// </summary>
    public delegate int OrderTypeKeyResolver(string orderTypeKey);
    
    /// <summary>
    /// Delegate for getting the ground position for movement commands.
    /// </summary>
    public delegate bool GroundPositionProvider(out Vector3 worldCm);
    
    /// <summary>
    /// Delegate for resolving the acting entity for an order.
    /// </summary>
    public delegate bool ActorProvider(out Entity entity);

    /// <summary>
    /// Delegate for getting the selected entity from a named selection set.
    /// </summary>
    public delegate bool SelectedEntityProvider(string selectionSetKey, out Entity entity);

    /// <summary>
    /// Delegate for getting the current selected container snapshot from a named selection set.
    /// </summary>
    public delegate bool SelectedContainerProvider(string selectionSetKey, out Entity container);

    /// <summary>
    /// Delegate for getting the current EntityView command-source collection handle from a named selection set.
    /// </summary>
    public delegate bool SelectedCollectionProvider(string selectionSetKey, out Entity owner, out EntityCollectionHandle handle);

    /// <summary>
    /// Delegate for copying the current selected entities into a reusable list.
    /// </summary>
    public delegate bool SelectedEntityListProvider(string selectionSetKey, List<Entity> entities);

    /// <summary>
    /// Delegate for getting the entity currently under the cursor (for SmartCast).
    /// </summary>
    public delegate bool HoveredEntityProvider(out Entity entity);
    
    /// <summary>
    /// Delegate for submitting an order.
    /// </summary>
    public delegate void OrderSubmitHandler(in Order order);

    /// <summary>
    /// Delegate for resolving a per-actor routing candidate from actorOrderRouting candidates.
    /// </summary>
    public delegate bool ActorOrderRoutingResolver(
        Entity actor,
        ActorOrderRoutingSettings routing,
        out ActorOrderRoutingCandidate matchedCandidate);


    /// <summary>
    /// Delegate for checking if a modifier key is held.
    /// </summary>
    public delegate bool ModifierKeyProvider();

    /// <summary>
    /// Callback fired when the system enters or exits aiming state (AimCast mode).
    /// Consumers use this to show/hide aim presentation.
    /// The system itself has no knowledge of presentation; it only signals state changes.
    /// </summary>
    /// <param name="isAiming">True when entering aiming, false when exiting.</param>
    /// <param name="mapping">The mapping being aimed.</param>
    public delegate void AimingStateChangedHandler(bool isAiming, InputOrderMapping mapping);

    /// <summary>
    /// Callback fired each frame while aiming (AimCast mode) so the consumer can
    /// update aim presentation state. The system has no knowledge of presentation.
    /// </summary>
    /// <param name="mapping">The mapping currently being aimed.</param>
    public delegate void AimingUpdateHandler(InputOrderMapping mapping);

    /// <summary>
    /// Delegate for automatic target acquisition via spatial query.
    /// Returns the nearest valid entity within the specified range and policy.
    /// The implementation should use ISpatialQueryService.
    /// </summary>
    /// <param name="actor">The caster entity.</param>
    /// <param name="policy">The auto-target policy.</param>
    /// <param name="rangeCm">Search range in world centimeters.</param>
    /// <param name="target">The found target entity.</param>
    /// <returns>True if a valid target was found.</returns>
    public delegate bool AutoTargetProvider(Entity actor, AutoTargetPolicy policy, int rangeCm, out Entity target);

    /// <summary>
    /// Delegate for resolving an entity near the current cursor ground point.
    /// The implementation should use logical spatial queries instead of screen hover.
    /// </summary>
    public delegate bool CursorTargetProvider(Entity actor, AutoTargetPolicy policy, int rangeCm, Vector3 cursorWorldCm, out Entity target);

    /// <summary>
    /// Delegate for resolving a context-scored mapping into a concrete cast slot and target.
    /// </summary>
    public delegate bool ContextScoredResolutionProvider(
        Entity actor,
        InputOrderMapping mapping,
        Entity hoveredEntity,
        out ContextScoredOrderResolution resolution);

    /// <summary>
    /// Delegate for applying ability-level overrides to a skill mapping after the acting
    /// entity and effective slot have been resolved.
    /// </summary>
    public delegate bool SkillMappingOverrideProvider(Entity actor, InputOrderMapping mapping, out InputOrderMapping overrideMapping);

    /// <summary>
    /// Callback fired each frame during vector aiming so the consumer can publish
    /// origin-to-cursor aim preview state.
    /// </summary>
    /// <param name="mapping">The mapping being vector-aimed.</param>
    /// <param name="origin">The locked-in origin point (world cm).</param>
    /// <param name="cursor">Current cursor ground position (world cm).</param>
    /// <param name="slot">Current vector aim input slot.</param>
    public delegate void VectorAimUpdateHandler(InputOrderMapping mapping, Vector3 origin, Vector3 cursor, VectorAimInputSlot slot);

    /// <summary>
    /// Input slot of a two-point vector aiming interaction.
    /// </summary>
    public enum VectorAimInputSlot : byte
    {
        /// <summary>Choosing the origin point.</summary>
        Origin = 0,
        /// <summary>Origin is locked; dragging to set direction/endpoint.</summary>
        Direction = 1,
    }
    
    /// <summary>
    /// System that converts InputAction triggers to Orders based on configuration.
    ///
    /// Supports three interaction modes (config-level, not per-ability):
    ///   TargetFirst (WoW): trigger -> immediate submit using selected entity
    ///   SmartCast (LoL):   trigger -> immediate submit using cursor/hovered entity
    ///   AimCast (DotA):    trigger -> enter aiming -> confirm click -> submit
    ///
    /// Non-skill mappings (IsSkillMapping=false) always use TargetFirst behavior.
    /// </summary>
    public sealed class InputOrderMappingSystem
    {
        private readonly struct HeldStartEndState
        {
            public HeldStartEndState(Entity actor, InputOrderMapping mapping)
            {
                Actor = actor;
                Mapping = mapping;
            }

            public Entity Actor { get; }
            public InputOrderMapping Mapping { get; }
        }

        private readonly record struct MappingEntry(
            string ActionId,
            InputOrderMapping Mapping,
            int Priority,
            int ActionIdOrdinal);

        private readonly IInputActionReader _input;
        private readonly InputOrderMappingConfig _config;
        private readonly Dictionary<string, InputOrderMapping> _mappingsByActionId;
        private readonly Dictionary<string, InputOrderMapping> _userOverrides;
        private readonly MappingEntry[] _orderedMappings;
        private readonly Dictionary<string, float> _lastPressedAtSecondsByActionId = new();
        private string _confirmActionId = string.Empty;
        private string _cancelActionId = string.Empty;
        private string _commandActionId = string.Empty;
        
        // Callbacks
        private OrderTypeKeyResolver? _orderTypeKeyResolver;
        private GroundPositionProvider? _groundPositionProvider;
        private ActorProvider? _actorProvider;
        private SelectedEntityProvider? _selectedEntityProvider;
        private SelectedContainerProvider? _selectedContainerProvider;
        private SelectedCollectionProvider? _selectedCollectionProvider;
        private SelectedEntityListProvider? _selectedEntityListProvider;
        private HoveredEntityProvider? _hoveredEntityProvider;
        private OrderSubmitHandler? _orderSubmitHandler;
        private ModifierKeyProvider? _queueModifierProvider;
        private AimingStateChangedHandler? _aimingStateChangedHandler;
        private AimingUpdateHandler? _aimingUpdateHandler;
        private VectorAimUpdateHandler? _vectorAimUpdateHandler;
        private AutoTargetProvider? _autoTargetProvider;
        private CursorTargetProvider? _cursorTargetProvider;
        private ContextScoredResolutionProvider? _contextScoredProvider;
        private SkillMappingOverrideProvider? _skillMappingOverrideProvider;
        private ActorOrderRoutingResolver? _actorOrderRoutingResolver;
        
        // Context
        private Entity _localPlayer;
        private int _playerId;
        private float _elapsedSeconds;
        private readonly List<Entity> _selectedActorsScratch = new(16);

        private readonly struct RoutedOrderSubmission
        {
            public RoutedOrderSubmission(in Order order, string orderTypeKey)
            {
                Order = order;
                OrderTypeKey = orderTypeKey;
            }

            public Order Order { get; }
            public string OrderTypeKey { get; }
        }

        private readonly List<RoutedOrderSubmission> _routedOrdersScratch = new(16);

        // Aiming state (AimCast mode)
        private bool _isAiming;
        private string _aimingActionId = string.Empty;
        private InputOrderMapping? _aimingMapping;
        
        // Held Start/End tracking
        private readonly Dictionary<string, HeldStartEndState> _activeHeldStartEndActions = new();
        
        // SmartCastWithIndicator state
        private bool _smartCastWithIndicatorActive;

        // PressReleaseAimCast state
        private bool _pressReleaseAimPending;
        private string _pressReleaseAimActionId = string.Empty;
        private InputOrderMapping? _pressReleaseAimMapping;
        
        // Vector aim state (two-point targeting)
        private VectorAimInputSlot _vectorAimSlot;
        private Vector3 _vectorAimOrigin;
        private bool _isVectorAiming;

        /// <summary>
        /// Change global interaction mode at runtime.
        /// The change takes effect immediately and will cancel current aiming state.
        /// </summary>
        public void SetInteractionMode(InteractionModeType mode)
        {
            if (_config.InteractionMode == mode) return;
            if (_isAiming) ExitAimingState();
            ClearPressReleaseAimPending();
            _config.InteractionMode = mode;
        }

        /// <summary>The current global interaction mode.</summary>
        public InteractionModeType InteractionMode => _config.InteractionMode;

        /// <summary>Whether the system is currently in aiming state (AimCast).</summary>
        public bool IsAiming => _isAiming;

        /// <summary>Whether the current aiming interaction is a two-phase vector aim.</summary>
        public bool IsVectorAiming => _isVectorAiming;

        /// <summary>The ActionId of the mapping being aimed (valid only when IsAiming).</summary>
        public string AimingActionId => _aimingActionId;

        /// <summary>The currently active aiming mapping, including user overrides.</summary>
        public InputOrderMapping? CurrentAimingMapping => _aimingMapping;

        /// <summary>The current vector aim input slot. Valid only when <see cref="IsVectorAiming"/> is true.</summary>
        public VectorAimInputSlot VectorAimSlot => _vectorAimSlot;

        /// <summary>The locked origin for vector aiming. Valid only during direction phase.</summary>
        public Vector3 VectorAimOrigin => _vectorAimOrigin;

        /// <summary>The confirm action ID used to fire the aimed ability.</summary>
        public string ConfirmActionId
        {
            get => _confirmActionId;
            set => _confirmActionId = RequireConfiguredActionId(value, nameof(ConfirmActionId));
        }

        /// <summary>The cancel action ID.</summary>
        public string CancelActionId
        {
            get => _cancelActionId;
            set => _cancelActionId = RequireConfiguredActionId(value, nameof(CancelActionId));
        }

        /// <summary>The secondary cancel / command action ID.</summary>
        public string CommandActionId
        {
            get => _commandActionId;
            set => _commandActionId = RequireConfiguredActionId(value, nameof(CommandActionId));
        }
        
        public InputOrderMappingSystem(IInputActionReader input, InputOrderMappingConfig config)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            InputOrderMappingLoader.Validate(_config, "InputOrderMappingSystem config");
            
            _mappingsByActionId = new Dictionary<string, InputOrderMapping>();
            _userOverrides = new Dictionary<string, InputOrderMapping>();

            foreach (var mapping in config.Mappings)
            {
                _mappingsByActionId.Add(mapping.ActionId, mapping);
            }

            var actionIds = new string[config.Mappings.Count];
            for (int i = 0; i < config.Mappings.Count; i++)
            {
                actionIds[i] = config.Mappings[i].ActionId;
            }

            Array.Sort(actionIds, StringComparer.Ordinal);
            var actionIdOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < actionIds.Length; i++)
            {
                actionIdOrdinals.Add(actionIds[i], i);
            }

            _orderedMappings = new MappingEntry[config.Mappings.Count];
            for (int i = 0; i < config.Mappings.Count; i++)
            {
                var mapping = config.Mappings[i];
                _orderedMappings[i] = new MappingEntry(
                    mapping.ActionId,
                    mapping,
                    ResolveMappingPriority(mapping),
                    actionIdOrdinals[mapping.ActionId]);
            }

            Array.Sort(_orderedMappings, CompareMappingEntries);
        }
        
        // Callback setters (unchanged API + new ones)

        public void SetOrderTypeKeyResolver(OrderTypeKeyResolver resolver)
        {
            _orderTypeKeyResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            ValidateAllOrderTypeKeys();
        }
        public void SetGroundPositionProvider(GroundPositionProvider provider) => _groundPositionProvider = provider;
        public void SetActorProvider(ActorProvider provider) => _actorProvider = provider;
        public void SetSelectedEntityProvider(SelectedEntityProvider provider) => _selectedEntityProvider = provider;
        public void SetSelectedContainerProvider(SelectedContainerProvider provider) => _selectedContainerProvider = provider;
        public void SetSelectedCollectionProvider(SelectedCollectionProvider provider) => _selectedCollectionProvider = provider;
        public void SetSelectedEntityListProvider(SelectedEntityListProvider provider) => _selectedEntityListProvider = provider;
        public void SetHoveredEntityProvider(HoveredEntityProvider provider) => _hoveredEntityProvider = provider;
        public void SetOrderSubmitHandler(OrderSubmitHandler handler) => _orderSubmitHandler = handler;
        public void SetQueueModifierProvider(ModifierKeyProvider provider) => _queueModifierProvider = provider;
        public void SetAimingStateChangedHandler(AimingStateChangedHandler handler) => _aimingStateChangedHandler = handler;
        public void SetAimingUpdateHandler(AimingUpdateHandler handler) => _aimingUpdateHandler = handler;
        public void SetVectorAimUpdateHandler(VectorAimUpdateHandler handler) => _vectorAimUpdateHandler = handler;
        public void SetAutoTargetProvider(AutoTargetProvider provider) => _autoTargetProvider = provider;
        public void SetCursorTargetProvider(CursorTargetProvider provider) => _cursorTargetProvider = provider;
        public void SetContextScoredProvider(ContextScoredResolutionProvider provider) => _contextScoredProvider = provider;
        public void SetActorOrderRoutingResolver(ActorOrderRoutingResolver resolver) =>
            _actorOrderRoutingResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        public void SetSkillMappingOverrideProvider(SkillMappingOverrideProvider provider) => _skillMappingOverrideProvider = provider;

        public void SetInteractionActionBindings(InteractionActionBindings bindings)
        {
            if (bindings == null)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ORDER_ACTION_BINDING_REQUIRED: {nameof(InputOrderMappingSystem)} requires {nameof(InteractionActionBindings)}.");
            }

            ConfirmActionId = bindings.ConfirmActionId;
            CancelActionId = bindings.CancelActionId;
            CommandActionId = bindings.CommandActionId;
        }
        
        public void SetLocalPlayer(Entity entity, int playerId)
        {
            if (entity == Entity.Null)
            {
                throw new ArgumentException("InputOrderMappingSystem requires a non-null local player entity.", nameof(entity));
            }

            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId), "InputOrderMappingSystem requires a positive player id.");
            }

            _localPlayer = entity;
            _playerId = playerId;
        }
        
        /// <summary>
        /// Process input and generate orders.
        /// </summary>
        public void Update(float dt)
        {
            if (_orderSubmitHandler == null) return;
            if (_orderTypeKeyResolver == null) return;
            if (dt > 0f)
            {
                _elapsedSeconds += dt;
            }

            var mode = _config.InteractionMode;

            // 0. Process Held StartEnd releases (must run even during aiming)
            ProcessHeldStartEndReleases();

            // 1. Handle active aiming state (AimCast only)
            if (_isAiming)
            {
                HandleAimingState();
                return; // While aiming, don't process other mappings
            }

            ProcessPressReleaseAimPending();
            
            // 2. Process all mappings
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                InputOrderMapping mapping = entry.Mapping;
                var effectiveMapping = ResolveEffectiveMapping(actionId, mapping, out var resolvedActor);

                // Held+StartEnd is handled separately via press/release detection
                if (effectiveMapping.Trigger == InputTriggerType.Held && effectiveMapping.HeldPolicy == HeldPolicy.StartEnd)
                {
                    if (_input.PressedThisFrame(actionId) && !_activeHeldStartEndActions.ContainsKey(actionId))
                    {
                        Entity heldActor = resolvedActor != default ? resolvedActor : ResolvePrimaryActor(effectiveMapping);
                        // Emit .Start order
                        if (TryBuildOrderWithOrderTypeSuffix(effectiveMapping, heldActor, ".Start", out var startOrder))
                        {
                            SubmitOrder(effectiveMapping, in startOrder);
                        }
                        if (_input.ReleasedThisFrame(actionId) && !_input.IsDown(actionId))
                        {
                            if (TryBuildOrderWithOrderTypeSuffix(effectiveMapping, heldActor, ".End", out var endOrder))
                            {
                                SubmitOrder(effectiveMapping, in endOrder);
                            }
                        }
                        else
                        {
                            _activeHeldStartEndActions[actionId] = new HeldStartEndState(heldActor, effectiveMapping);
                        }
                    }
                    continue; // Release is handled in ProcessHeldStartEndReleases
                }
                
                if (!CheckTrigger(actionId, effectiveMapping)) continue;

                // Skill mappings are affected by InteractionMode; non-skill mappings always go through immediately.
                // Per-ability CastModeOverride takes precedence over the global InteractionMode.
                if (effectiveMapping.IsSkillMapping)
                {
                    var effectiveMode = effectiveMapping.CastModeOverride ?? mode;
                    if (effectiveMode != InteractionModeType.TargetFirst)
                    {
                        HandleSkillMappingWithMode(actionId, effectiveMapping, effectiveMode);
                        continue;
                    }
                }
                
                // TargetFirst or non-skill: immediate build and submit
                if (effectiveMapping.ActorOrderRouting != null && effectiveMapping.ActorOrderRouting.Candidates.Count > 0)
                {
                    SubmitRoutedOrders(effectiveMapping);
                }
                else if (TryBuildOrder(effectiveMapping, out var order))
                {
                    SubmitOrder(effectiveMapping, in order);
                }
            }
        }
        
        /// <summary>
        /// Check for releases of Held+StartEnd actions and emit .End orders.
        /// Runs before aiming check so that releases are never missed.
        /// </summary>
        private void ProcessHeldStartEndReleases()
        {
            if (_activeHeldStartEndActions.Count == 0) return;
            
            // Collect releases to avoid modifying set during iteration
            List<string>? toRemove = null;
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                if (!_activeHeldStartEndActions.TryGetValue(actionId, out var state))
                {
                    continue;
                }

                if (_input.ReleasedThisFrame(actionId))
                {
                    if (TryBuildOrderWithOrderTypeSuffix(state.Mapping, state.Actor, ".End", out var endOrder))
                    {
                        SubmitOrder(state.Mapping, in endOrder);
                    }
                    toRemove ??= new List<string>();
                    toRemove.Add(actionId);
                }
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove) _activeHeldStartEndActions.Remove(id);
            }
        }

        private bool CheckTrigger(string actionId, InputOrderMapping mapping)
        {
            return mapping.Trigger switch
            {
                InputTriggerType.PressedThisFrame => _input.PressedThisFrame(actionId),
                InputTriggerType.ReleasedThisFrame => _input.ReleasedThisFrame(actionId),
                InputTriggerType.Held => _input.IsDown(actionId),
                InputTriggerType.DoubleTap => CheckDoubleTap(actionId, mapping.DoubleTapWindowSeconds),
                _ => false
            };
        }

        private bool CheckDoubleTap(string actionId, float windowSeconds)
        {
            if (!_input.PressedThisFrame(actionId))
            {
                return false;
            }

            float effectiveWindow = windowSeconds > 0f ? windowSeconds : 0.30f;
            bool triggered = _lastPressedAtSecondsByActionId.TryGetValue(actionId, out float lastPressedAt) &&
                             _elapsedSeconds - lastPressedAt <= effectiveWindow;
            _lastPressedAtSecondsByActionId[actionId] = _elapsedSeconds;
            return triggered;
        }

        // Interaction mode handling

        private void HandleSkillMappingWithMode(string actionId, InputOrderMapping mapping, InteractionModeType mode)
        {
            // Vector selection always requires two-click interaction (origin + endpoint),
            // so all modes fall through to AimCast for vector-targeted abilities.
            if (mapping.SelectionType == OrderSelectionType.Vector)
            {
                EnterAimingState(actionId, mapping);
                return;
            }
            
            switch (mode)
            {
                case InteractionModeType.SmartCast:
                    HandleSmartCast(mapping);
                    break;

                case InteractionModeType.AimCast:
                    EnterAimingState(actionId, mapping);
                    break;

                case InteractionModeType.SmartCastWithIndicator:
                    // Press -> enter aiming and publish aim preview.
                    // Release is handled in the aiming state.
                    EnterAimingState(actionId, mapping);
                    _smartCastWithIndicatorActive = true;
                    break;

                case InteractionModeType.PressReleaseAimCast:
                    QueuePressReleaseAim(actionId, mapping);
                    break;

                case InteractionModeType.ContextScored:
                    HandleContextScored(mapping);
                    break;

                default: // TargetFirst should not reach here due to guard above
                    if (TryBuildOrder(mapping, out var order))
                    {
                        SubmitOrder(mapping, in order);
                    }
                    break;
            }
        }

        /// <summary>
        /// SmartCast: immediately build and submit, but prefer hovered entity / cursor
        /// over selected entity for targeting.
        /// </summary>
        private void HandleSmartCast(InputOrderMapping mapping)
        {
            if (TryBuildOrderSmartCast(mapping, out var order))
            {
                SubmitOrder(mapping, in order);
            }
        }

        /// <summary>
        /// AimCast: enter aiming state. The confirm action will later trigger the order.
        /// Automatically enters vector aiming mode for Vector selection type.
        /// </summary>
        private void EnterAimingState(string actionId, InputOrderMapping mapping)
        {
            // If already aiming a different skill, cancel old first
            if (_isAiming && _aimingActionId != actionId)
            {
                ExitAimingState();
            }

            _isAiming = true;
            _aimingActionId = actionId;
            _aimingMapping = mapping;
            
            // Auto-detect vector aiming mode
            if (mapping.SelectionType == OrderSelectionType.Vector)
            {
                _isVectorAiming = true;
                _vectorAimSlot = VectorAimInputSlot.Origin;
                _vectorAimOrigin = default;
            }
            
            _aimingStateChangedHandler?.Invoke(true, mapping);
            EmitAimingPreviewOnEnter(mapping);
        }

        private void ExitAimingState()
        {
            if (!_isAiming) return;
            var mapping = _aimingMapping!;
            _isAiming = false;
            _aimingActionId = string.Empty;
            _aimingMapping = null;
            _smartCastWithIndicatorActive = false;
            _isVectorAiming = false;
            _vectorAimSlot = VectorAimInputSlot.Origin;
            _vectorAimOrigin = default;
            _aimingStateChangedHandler?.Invoke(false, mapping);
        }

        private void QueuePressReleaseAim(string actionId, InputOrderMapping mapping)
        {
            _pressReleaseAimPending = true;
            _pressReleaseAimActionId = actionId ?? string.Empty;
            _pressReleaseAimMapping = mapping;
        }

        private void ClearPressReleaseAimPending()
        {
            _pressReleaseAimPending = false;
            _pressReleaseAimActionId = string.Empty;
            _pressReleaseAimMapping = null;
        }

        private void ProcessPressReleaseAimPending()
        {
            if (!_pressReleaseAimPending || _pressReleaseAimMapping == null)
            {
                return;
            }

            string cancelActionId = RequireCancelActionId();
            string commandActionId = RequireCommandActionId();

            if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
            {
                ClearPressReleaseAimPending();
                return;
            }

            if (string.IsNullOrWhiteSpace(_pressReleaseAimActionId))
            {
                ClearPressReleaseAimPending();
                return;
            }

            if (!_input.ReleasedThisFrame(_pressReleaseAimActionId))
            {
                return;
            }

            string actionId = _pressReleaseAimActionId;
            InputOrderMapping mapping = _pressReleaseAimMapping;
            ClearPressReleaseAimPending();
            EnterAimingState(actionId, mapping);
        }

        /// <summary>
        /// Called every frame while aiming. Handles confirm/cancel and signals update.
        /// Routes to vector aiming state machine when applicable.
        /// </summary>
        private void HandleAimingState()
        {
            if (_aimingMapping == null) { ExitAimingState(); return; }

            string confirmActionId = RequireConfirmActionId();
            string cancelActionId = RequireCancelActionId();
            string commandActionId = RequireCommandActionId();

            // Vector aiming (two-point targeting)
            if (_isVectorAiming)
            {
                HandleVectorAimingState(confirmActionId, cancelActionId, commandActionId);
                return;
            }

            // SmartCastWithIndicator: release of the skill key = confirm cast
            if (_smartCastWithIndicatorActive)
            {
                if (_input.ReleasedThisFrame(_aimingActionId))
                {
                    if (TryBuildOrderSmartCast(_aimingMapping, out var order))
                    {
                        SubmitOrder(_aimingMapping, in order);
                    }
                    ExitAimingState();
                    return;
                }
                
                // Cancel: right-click or ESC
                if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
                {
                    ExitAimingState();
                    return;
                }
                
                // Signal aiming update for presentation refresh.
                _aimingUpdateHandler?.Invoke(_aimingMapping);
                return;
            }

            // AimCast: Confirm by left-click
            if (_input.PressedThisFrame(confirmActionId))
            {
                // Build order using current cursor/selection
                if (TryBuildOrderSmartCast(_aimingMapping, out var order))
                {
                    SubmitOrder(_aimingMapping, in order);
                }
                ExitAimingState();
                return;
            }

            // Cancel: right-click or ESC
            if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
            {
                ExitAimingState();
                return;
            }

            // Pressing a different skill key while aiming switches to that skill
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                InputOrderMapping mapping = entry.Mapping;
                if (actionId == _aimingActionId) continue;
                var effectiveMapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                    ? overrideMapping
                    : mapping;
                if (!effectiveMapping.IsSkillMapping) continue;
                if (!_input.PressedThisFrame(actionId)) continue;

                // Switch aim to the new skill
                EnterAimingState(actionId, effectiveMapping);
                return;
            }

            // Signal aiming update for presentation refresh.
            _aimingUpdateHandler?.Invoke(_aimingMapping);
        }

        /// <summary>
        /// Two-phase vector aiming state machine.
        /// Phase Origin: click to lock origin point.
        /// Phase Direction: click to lock endpoint, then build and submit order.
        /// </summary>
        private void HandleVectorAimingState(string confirmActionId, string cancelActionId, string commandActionId)
        {
            // Cancel: right-click or ESC at any phase
            if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
            {
                ExitAimingState();
                return;
            }

            // Get current cursor position
            Vector3 cursorPos = default;
            bool hasCursor = _groundPositionProvider != null && _groundPositionProvider(out cursorPos);

            switch (_vectorAimSlot)
            {
                case VectorAimInputSlot.Origin:
                    // Signal update for origin-slot preview.
                    if (hasCursor)
                    {
                        _vectorAimUpdateHandler?.Invoke(_aimingMapping!, cursorPos, cursorPos, VectorAimInputSlot.Origin);
                    }
                    
                    // Confirm origin with left-click
                    if (_input.PressedThisFrame(confirmActionId) && hasCursor)
                    {
                        _vectorAimOrigin = cursorPos;
                        _vectorAimSlot = VectorAimInputSlot.Direction;
                    }
                    break;

                case VectorAimInputSlot.Direction:
                    // Signal update: show line from origin to cursor
                    if (hasCursor)
                    {
                        _vectorAimUpdateHandler?.Invoke(_aimingMapping!, _vectorAimOrigin, cursorPos, VectorAimInputSlot.Direction);
                    }
                    
                    // Confirm direction with left-click -> build and submit vector order
                    if (_input.PressedThisFrame(confirmActionId) && hasCursor)
                    {
                        if (TryBuildVectorOrder(_aimingMapping!, _vectorAimOrigin, cursorPos, out var order))
                        {
                            SubmitOrder(_aimingMapping!, in order);
                        }
                        ExitAimingState();
                    }
                    break;
            }
        }

        private void EmitAimingPreviewOnEnter(InputOrderMapping mapping)
        {
            if (_isVectorAiming)
            {
                Vector3 cursorPos = default;
                if (_groundPositionProvider != null && _groundPositionProvider(out cursorPos))
                {
                    _vectorAimUpdateHandler?.Invoke(mapping, cursorPos, cursorPos, VectorAimInputSlot.Origin);
                }

                return;
            }

            _aimingUpdateHandler?.Invoke(mapping);
        }

        // Order building

        /// <summary>
        /// Build an order with a order type key suffix (e.g. ".Start", ".End" for Held StartEnd mode).
        /// </summary>
        private bool TryBuildOrderWithOrderTypeSuffix(InputOrderMapping mapping, string orderTypeSuffix, out Order order)
        {
            return TryBuildOrderWithOrderTypeSuffix(mapping, ResolvePrimaryActor(mapping), orderTypeSuffix, out order);
        }

        /// <summary>
        /// Build an order with a order type key suffix (e.g. ".Start", ".End" for Held StartEnd mode)
        /// using a pinned actor captured when the held interaction began.
        /// </summary>
        private bool TryBuildOrderWithOrderTypeSuffix(InputOrderMapping mapping, Entity actor, string orderTypeSuffix, out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer()) return false;
            int orderTypeId = RequireOrderTypeId(mapping, orderTypeSuffix);
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);

            // Fill selection data same as TryBuildOrder
            if (mapping.SelectionType == OrderSelectionType.Position || mapping.SelectionType == OrderSelectionType.Direction)
            {
                if (_groundPositionProvider != null && _groundPositionProvider(out var pos))
                {
                    args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    args.Spatial.Mode = OrderCollectionMode.Single;
                    args.Spatial.WorldCm = pos;

                    if (mapping.SelectionType == OrderSelectionType.Position &&
                        mapping.AutoTargetPolicy != AutoTargetPolicy.None)
                    {
                        if (TryResolveHoveredEntity(out var hovered))
                        {
                            order.Target = hovered;
                        }
                        else if (TryResolveCursorTarget(actor, mapping, pos, out var cursorTarget))
                        {
                            order.Target = cursorTarget;
                        }
                        else if (mapping.AutoTargetRangeCm > 0 &&
                                 _autoTargetProvider != null &&
                                 _autoTargetProvider(actor, mapping.AutoTargetPolicy, mapping.AutoTargetRangeCm, out var autoTarget))
                        {
                            order.Target = autoTarget;
                        }
                    }
                    else if (mapping.SelectionType == OrderSelectionType.Direction &&
                             TryResolveDirectionalTarget(actor, mapping, pos, out var directionTarget))
                    {
                        order.Target = directionTarget;
                    }
                }
            }
            else if (mapping.SelectionType == OrderSelectionType.Entity)
            {
                if (_selectedEntityProvider != null && _selectedEntityProvider(mapping.SelectionSetKey, out var target))
                {
                    order.Target = target;
                }
            }
            else if (mapping.SelectionType == OrderSelectionType.Entities)
            {
                TryCaptureSelectedContainer(mapping.SelectionSetKey, ref args.Selection);
            }

            order.OrderTypeId = orderTypeId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        private int RequireOrderTypeId(InputOrderMapping mapping, string orderTypeSuffix = "")
        {
            return RequireOrderTypeId(mapping.ActionId, mapping.OrderTypeKey + orderTypeSuffix);
        }

        private int RequireOrderTypeId(string actionId, string orderTypeKey)
        {
            if (string.IsNullOrWhiteSpace(orderTypeKey))
            {
                throw new InvalidOperationException(
                    $"Input mapping '{actionId}' must define non-empty orderTypeKey.");
            }

            int orderTypeId = _orderTypeKeyResolver!(orderTypeKey);
            if (orderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{actionId}' orderTypeKey '{orderTypeKey}' is not registered.");
            }

            return orderTypeId;
        }

        private InputOrderMapping ResolveEffectiveMapping(string actionId, InputOrderMapping mapping, out Entity resolvedActor)
        {
            var effectiveMapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                ? overrideMapping
                : mapping;
            resolvedActor = default;

            if (!effectiveMapping.IsSkillMapping || _skillMappingOverrideProvider == null)
            {
                return effectiveMapping;
            }

            resolvedActor = ResolvePrimaryActor(effectiveMapping);
            if (resolvedActor == default)
            {
                return effectiveMapping;
            }

            if (_skillMappingOverrideProvider(resolvedActor, effectiveMapping, out var overrideFromAbility))
            {
                return overrideFromAbility;
            }

            return effectiveMapping;
        }

        private void HandleContextScored(InputOrderMapping mapping)
        {
            if (_contextScoredProvider == null)
            {
                return;
            }

            Entity hoveredEntity = default;
            _hoveredEntityProvider?.Invoke(out hoveredEntity);
            if (TryBuildContextScoredOrder(mapping, hoveredEntity, out var order))
            {
                SubmitOrder(mapping, in order);
            }
        }

        private bool TryBuildContextScoredOrder(InputOrderMapping mapping, Entity hoveredEntity, out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer()) return false;

            int orderTypeId = RequireOrderTypeId(mapping);

            Entity actor = ResolvePrimaryActor(mapping);
            if (!_contextScoredProvider!(actor, mapping, hoveredEntity, out var resolution))
            {
                return false;
            }

            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            args.I0 = resolution.SlotIndex;

            order.OrderTypeId = orderTypeId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Target = resolution.Target;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        /// <summary>
        /// Build order for SmartCast: prefer hovered entity, then cursor ground position,
        /// then fall back to selected entity.
        /// </summary>
        private bool TryBuildOrderSmartCast(InputOrderMapping mapping, out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer()) return false;

            int orderTypeId = RequireOrderTypeId(mapping);

            Entity actor = ResolvePrimaryActor(mapping);
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);

            // SmartCast targeting priority:
            //   1. Hovered entity (entity under cursor)
            //   2. Auto-target (nearest in range, if configured)
            //   3. Selected entity (fallback)
            switch (mapping.SelectionType)
            {
                case OrderSelectionType.Entity:
                    if (TryResolveHoveredEntity(out var hovered))
                    {
                        order.Target = hovered;
                    }
                    else if (mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                             mapping.AutoTargetRangeCm > 0 &&
                             _autoTargetProvider != null &&
                             _autoTargetProvider(actor, mapping.AutoTargetPolicy, mapping.AutoTargetRangeCm, out var autoTarget))
                    {
                        order.Target = autoTarget;
                    }
                    else if (_selectedEntityProvider != null && _selectedEntityProvider(mapping.SelectionSetKey, out var selected))
                    {
                        order.Target = selected;
                    }
                    break;

                case OrderSelectionType.Position:
                    if (_groundPositionProvider != null && _groundPositionProvider(out var groundPos))
                    {
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;

                        if (mapping.AutoTargetPolicy != AutoTargetPolicy.None)
                        {
                            if (TryResolveHoveredEntity(out var hoveredTarget))
                            {
                                order.Target = hoveredTarget;
                            }
                            else if (TryResolveCursorTarget(actor, mapping, groundPos, out var cursorTarget))
                            {
                                order.Target = cursorTarget;
                            }
                            else if (mapping.AutoTargetRangeCm > 0 &&
                                     _autoTargetProvider != null &&
                                     _autoTargetProvider(actor, mapping.AutoTargetPolicy, mapping.AutoTargetRangeCm, out var autoTarget))
                            {
                                order.Target = autoTarget;
                            }
                        }
                    }
                    else if (mapping.RequireSelection)
                    {
                        return false;
                    }
                    break;

                case OrderSelectionType.Direction:
                    // Direction: store normalized direction from actor to cursor
                    if (_groundPositionProvider != null && _groundPositionProvider(out var dirPos))
                    {
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = dirPos;

                        if (TryResolveDirectionalTarget(actor, mapping, dirPos, out var directionTarget))
                        {
                            order.Target = directionTarget;
                        }
                    }
                    else if (mapping.RequireSelection)
                    {
                        return false;
                    }
                    break;

                case OrderSelectionType.Entities:
                    if (_selectedContainerProvider != null)
                    {
                        TryCaptureSelectedContainer(mapping.SelectionSetKey, ref args.Selection);
                    }
                    else if (mapping.RequireSelection)
                    {
                        return false;
                    }
                    break;

                case OrderSelectionType.None:
                    // Self-cast or no-target; nothing to fill
                    break;
            }

            order.OrderTypeId = orderTypeId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        /// <summary>
        /// Build a vector order with two spatial points (origin + endpoint).
        /// Used by vector-targeted abilities (e.g. Rumble R, Viktor E).
        /// </summary>
        private bool TryBuildVectorOrder(InputOrderMapping mapping, Vector3 origin, Vector3 endpoint, out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer()) return false;
            
            int orderTypeId = RequireOrderTypeId(mapping);
            
            Entity actor = ResolvePrimaryActor(mapping);
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            
            // Store both points in List mode: point[0] = origin, point[1] = endpoint
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.List;
            args.Spatial.WorldCm = origin; // Primary point
            args.Spatial.AddPointWorldCm((int)origin.X, (int)origin.Y, (int)origin.Z);
            args.Spatial.AddPointWorldCm((int)endpoint.X, (int)endpoint.Y, (int)endpoint.Z);
            
            order.OrderTypeId = orderTypeId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        /// <summary>
        /// Original order building logic (TargetFirst / non-skill mappings).
        /// </summary>
        private bool TryBuildOrder(InputOrderMapping mapping, out Order order)
        {
            Entity actor = ResolvePrimaryActor(mapping);
            return TryBuildOrderForActor(mapping, actor, mapping.OrderTypeKey, selectionTypeOverride: null, out order);
        }

        private bool TryBuildOrderForActor(
            InputOrderMapping mapping,
            Entity actor,
            string orderTypeKey,
            OrderSelectionType? selectionTypeOverride,
            out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer() || actor == default)
            {
                return false;
            }

            int orderTypeId = RequireOrderTypeId(mapping.ActionId, orderTypeKey);
            
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            OrderSelectionType selectionType = selectionTypeOverride ?? mapping.SelectionType;
            
            if (mapping.RequireSelection)
            {
                switch (selectionType)
                {
                    case OrderSelectionType.HoveredEntityOrPosition:
                        if (TryResolveHoveredEntity(out var hoveredTarget))
                        {
                            order.Target = hoveredTarget;
                        }
                        else if (_groundPositionProvider != null && _groundPositionProvider(out var hoveredOrGroundPos))
                        {
                            args.Spatial.Kind = OrderSpatialKind.WorldCm;
                            args.Spatial.Mode = OrderCollectionMode.Single;
                            args.Spatial.WorldCm = hoveredOrGroundPos;
                        }
                        else
                        {
                            return false;
                        }
                        break;

                    case OrderSelectionType.Position:
                    case OrderSelectionType.Direction:
                        if (_groundPositionProvider == null || !_groundPositionProvider(out var groundPos))
                        {
                            return false;
                        }
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;
                        if (selectionType == OrderSelectionType.Direction &&
                            TryResolveDirectionalTarget(actor, mapping, groundPos, out var directionTarget))
                        {
                            order.Target = directionTarget;
                        }
                        break;
                        
                    case OrderSelectionType.Entity:
                        if (_selectedEntityProvider == null || !_selectedEntityProvider(mapping.SelectionSetKey, out var target))
                        {
                            return false;
                        }
                        order.Target = target;
                        break;
                        
                    case OrderSelectionType.Entities:
                        if (!TryCaptureSelectedContainer(mapping.SelectionSetKey, ref args.Selection))
                        {
                            return false;
                        }
                        break;
                }
            }
            else if (selectionType == OrderSelectionType.Entity)
            {
                if (_selectedEntityProvider != null && _selectedEntityProvider(mapping.SelectionSetKey, out var target))
                {
                    order.Target = target;
                }
            }
            else if (selectionType == OrderSelectionType.Entities)
            {
                TryCaptureSelectedContainer(mapping.SelectionSetKey, ref args.Selection);
            }
            
            order.OrderTypeId = orderTypeId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        private void SubmitRoutedOrders(InputOrderMapping mapping)
        {
            if (_actorOrderRoutingResolver == null)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' defines actorOrderRouting but no resolver is configured.");
            }

            if (mapping.IsSkillMapping)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' actorOrderRouting is only valid when isSkillMapping is false.");
            }

            if (mapping.SelectionType == OrderSelectionType.Entities)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' actorOrderRouting does not support Entities selection type.");
            }

            if (!TryCaptureSelectedActors(mapping.SelectionSetKey, _selectedActorsScratch))
            {
                return;
            }

            _routedOrdersScratch.Clear();
            for (int i = 0; i < _selectedActorsScratch.Count; i++)
            {
                Entity actor = _selectedActorsScratch[i];
                if (actor == default)
                {
                    continue;
                }

                if (!_actorOrderRoutingResolver(actor, mapping.ActorOrderRouting!, out ActorOrderRoutingCandidate matchedCandidate))
                {
                    continue;
                }

                if (!TryBuildOrderForActor(
                        mapping,
                        actor,
                        matchedCandidate.OrderTypeKey,
                        matchedCandidate.SelectionType,
                        out var order))
                {
                    continue;
                }

                _routedOrdersScratch.Add(new RoutedOrderSubmission(in order, matchedCandidate.OrderTypeKey));
            }

            if (_routedOrdersScratch.Count == 0)
            {
                return;
            }

            int formationEligibleCount = 0;
            for (int i = 0; i < _routedOrdersScratch.Count; i++)
            {
                if (IsGroupMoveFormationOrderType(_routedOrdersScratch[i].OrderTypeKey))
                {
                    formationEligibleCount++;
                }
            }

            int formationIndex = 0;
            for (int i = 0; i < _routedOrdersScratch.Count; i++)
            {
                Order order = _routedOrdersScratch[i].Order;
                string orderTypeKey = _routedOrdersScratch[i].OrderTypeKey;
                if (formationEligibleCount > 1 &&
                    !mapping.IsSkillMapping &&
                    mapping.SelectionType == OrderSelectionType.Position &&
                    _config.GroupMoveFormation.Mode != GroupMoveFormationMode.None &&
                    IsGroupMoveFormationOrderType(orderTypeKey))
                {
                    ApplyGroupMoveFormation(mapping, orderTypeKey, formationEligibleCount, formationIndex, ref order);
                    formationIndex++;
                }

                _orderSubmitHandler!(in order);
            }
        }

        private Entity ResolvePrimaryActor(InputOrderMapping mapping)
        {
            if (_actorProvider != null && _actorProvider(out var actor) && actor != default)
            {
                return actor;
            }

            if (mapping.SelectionType != OrderSelectionType.Entities)
            {
                if (TryCaptureSelectedActors(mapping.SelectionSetKey, _selectedActorsScratch))
                {
                    return _selectedActorsScratch[0];
                }
            }

            if (_selectedEntityProvider != null && _selectedEntityProvider(mapping.SelectionSetKey, out var selected))
            {
                return selected;
            }

            return _localPlayer;
        }

        private bool HasExplicitLocalPlayer()
        {
            return _playerId > 0 && _localPlayer != Entity.Null;
        }

        private bool TryCaptureSelectedContainer(string selectionSetKey, ref OrderSelectionReference selection)
        {
            selection = default;
            if (_selectedCollectionProvider != null &&
                _selectedCollectionProvider(selectionSetKey, out Entity owner, out EntityCollectionHandle handle) &&
                handle.IsValid)
            {
                selection.CollectionOwner = owner;
                selection.CollectionHandle = handle;
                return true;
            }

            return _selectedContainerProvider != null &&
                   _selectedContainerProvider(selectionSetKey, out selection.Container) &&
                   selection.HasContainer;
        }

        private bool TryCaptureSelectedActors(string selectionSetKey, List<Entity> entities)
        {
            entities.Clear();
            return _selectedEntityListProvider != null &&
                   _selectedEntityListProvider(selectionSetKey, entities) &&
                   entities.Count > 0;
        }

        private void SubmitOrder(InputOrderMapping mapping, in Order order)
        {
            if (mapping.SelectionType == OrderSelectionType.Entities)
            {
                _orderSubmitHandler!(in order);
                return;
            }

            if (!TryCaptureSelectedActors(mapping.SelectionSetKey, _selectedActorsScratch) || _selectedActorsScratch.Count <= 1)
            {
                _orderSubmitHandler!(in order);
                return;
            }

            for (int i = 0; i < _selectedActorsScratch.Count; i++)
            {
                Entity actor = _selectedActorsScratch[i];
                if (actor == default)
                {
                    continue;
                }

                var cloned = order;
                cloned.Actor = actor;
                ApplyGroupMoveFormation(mapping, mapping.OrderTypeKey, _selectedActorsScratch.Count, i, ref cloned);
                _orderSubmitHandler!(in cloned);
            }
        }

        private void ApplyGroupMoveFormation(InputOrderMapping mapping, string orderTypeKey, int totalCount, int index, ref Order order)
        {
            if (totalCount <= 1 ||
                mapping.IsSkillMapping ||
                mapping.SelectionType != OrderSelectionType.Position ||
                !IsGroupMoveFormationOrderType(orderTypeKey) ||
                _config.GroupMoveFormation.Mode != GroupMoveFormationMode.Grid ||
                order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                order.Args.Spatial.Mode != OrderCollectionMode.Single)
            {
                return;
            }

            int spacingCm = Math.Max(1, _config.GroupMoveFormation.SpacingCm);
            order.Args.Spatial.WorldCm = MoveFormationPlanner.ComputeOffsetTarget(order.Args.Spatial.WorldCm, index, totalCount, spacingCm);
        }

        private bool IsGroupMoveFormationOrderType(string orderTypeKey)
        {
            if (_config.GroupMoveFormation.Mode == GroupMoveFormationMode.None ||
                string.IsNullOrWhiteSpace(orderTypeKey))
            {
                return false;
            }

            List<string> keys = _config.GroupMoveFormation.OrderTypeKeys;
            if (keys == null || keys.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], orderTypeKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveHoveredEntity(out Entity entity)
        {
            entity = default;
            return _hoveredEntityProvider != null &&
                   _hoveredEntityProvider(out entity) &&
                   entity != Entity.Null;
        }

        private bool TryResolveCursorTarget(Entity actor, InputOrderMapping mapping, Vector3 cursorWorldCm, out Entity target)
        {
            target = default;
            return mapping.CursorTargetPolicy != AutoTargetPolicy.None &&
                   mapping.CursorTargetRangeCm > 0 &&
                   _cursorTargetProvider != null &&
                   _cursorTargetProvider(actor, mapping.CursorTargetPolicy, mapping.CursorTargetRangeCm, cursorWorldCm, out target) &&
                   target != Entity.Null;
        }

        private bool TryResolveDirectionalTarget(Entity actor, InputOrderMapping mapping, Vector3 cursorWorldCm, out Entity target)
        {
            if (TryResolveHoveredEntity(out target))
            {
                return true;
            }

            return TryResolveCursorTarget(actor, mapping, cursorWorldCm, out target);
        }
        
        private OrderSubmitMode DetermineSubmitMode(ModifierSubmitBehavior behavior)
        {
            bool queueModifierHeld = _queueModifierProvider?.Invoke() ?? _input.IsDown("QueueModifier");
            return behavior switch
            {
                ModifierSubmitBehavior.IgnoreModifier => OrderSubmitMode.Immediate,
                ModifierSubmitBehavior.QueueOnModifier => queueModifierHeld ? OrderSubmitMode.Queued : OrderSubmitMode.Immediate,
                ModifierSubmitBehavior.AlwaysImmediate => OrderSubmitMode.Immediate,
                ModifierSubmitBehavior.AlwaysQueued => OrderSubmitMode.Queued,
                _ => OrderSubmitMode.Immediate
            };
        }
        
        private static void ApplyArgsTemplate(ref OrderArgs args, OrderArgsTemplate template)
        {
            if (template.I0.HasValue) args.I0 = template.I0.Value;
            if (template.I1.HasValue) args.I1 = template.I1.Value;
            if (template.I2.HasValue) args.I2 = template.I2.Value;
            if (template.I3.HasValue) args.I3 = template.I3.Value;
            if (template.F0.HasValue) args.F0 = template.F0.Value;
            if (template.F1.HasValue) args.F1 = template.F1.Value;
            if (template.F2.HasValue) args.F2 = template.F2.Value;
            if (template.F3.HasValue) args.F3 = template.F3.Value;
        }

        // Public API (Remap, Save, Load - unchanged)
        
        public void Remap(string actionId, string orderTypeKey, OrderArgsTemplate? argsTemplate = null)
        {
            if (!_mappingsByActionId.TryGetValue(actionId, out var original))
            {
                throw new ArgumentException($"No mapping found for action: {actionId}");
            }
            
            var newMapping = new InputOrderMapping
            {
                ActionId = actionId,
                Trigger = original.Trigger,
                OrderTypeKey = orderTypeKey,
                ArgsTemplate = argsTemplate ?? original.ArgsTemplate,
                RequireSelection = original.RequireSelection,
                SelectionSetKey = original.SelectionSetKey,
                SelectionType = original.SelectionType,
                IsSkillMapping = original.IsSkillMapping,
                CursorTargetPolicy = original.CursorTargetPolicy,
                CursorTargetRangeCm = original.CursorTargetRangeCm
            };

            InputOrderMappingLoader.Validate(
                new InputOrderMappingConfig { Mappings = new List<InputOrderMapping> { newMapping } },
                $"input mapping override '{actionId}'");
            ValidateOrderTypeKeys(newMapping);
            _userOverrides[actionId] = newMapping;
        }
        
        public void ResetToDefault(string actionId) => _userOverrides.Remove(actionId);
        public void ResetAllToDefault() => _userOverrides.Clear();
        
        public InputOrderMapping? GetMapping(string actionId)
        {
            if (_userOverrides.TryGetValue(actionId, out var overrideMapping)) return overrideMapping;
            if (_mappingsByActionId.TryGetValue(actionId, out var mapping)) return mapping;
            return null;
        }

        public IEnumerable<string> GetMappedActionIds()
        {
            for (int i = 0; i < _orderedMappings.Length; i++)
            {
                yield return _orderedMappings[i].ActionId;
            }
        }

        public int CopyPrimarySkillActionIds(Span<string> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            Span<int> priorities = stackalloc int[destination.Length];
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = string.Empty;
                priorities[i] = int.MaxValue;
            }

            int resolved = 0;
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                InputOrderMapping mapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                    ? overrideMapping
                    : entry.Mapping;
                if (!mapping.IsSkillMapping ||
                    !mapping.ArgsTemplate.I0.HasValue)
                {
                    continue;
                }

                int slotIndex = mapping.ArgsTemplate.I0.Value;
                if ((uint)slotIndex >= (uint)destination.Length)
                {
                    continue;
                }

                int priority = ResolveSkillActionPriority(actionId, mapping);
                string current = destination[slotIndex];
                if (priority < priorities[slotIndex] ||
                    (priority == priorities[slotIndex] && string.CompareOrdinal(actionId, current) < 0))
                {
                    if (string.IsNullOrEmpty(current))
                    {
                        resolved++;
                    }

                    destination[slotIndex] = actionId;
                    priorities[slotIndex] = priority;
                }
            }

            return resolved;
        }

        /// <summary>
        /// Activates a mapped action programmatically.
        /// UI callers may prefer aiming over hold/release semantics when no key lifecycle exists.
        /// </summary>
        public bool TryActivateMappedAction(string actionId, bool preferUiAiming = false)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                _orderSubmitHandler == null ||
                _orderTypeKeyResolver == null)
            {
                return false;
            }

            if (!_mappingsByActionId.TryGetValue(actionId, out var mapping))
            {
                return false;
            }

            var effectiveMapping = ResolveEffectiveMapping(actionId, mapping, out var resolvedActor);

            if (effectiveMapping.Trigger == InputTriggerType.Held && effectiveMapping.HeldPolicy == HeldPolicy.StartEnd)
            {
                Entity heldActor = resolvedActor != default ? resolvedActor : ResolvePrimaryActor(effectiveMapping);
                if (!TryBuildOrderWithOrderTypeSuffix(effectiveMapping, heldActor, ".Start", out var startOrder))
                {
                    return false;
                }

                SubmitOrder(effectiveMapping, in startOrder);
                return true;
            }

            if (effectiveMapping.IsSkillMapping)
            {
                var effectiveMode = effectiveMapping.CastModeOverride ?? _config.InteractionMode;
                if (preferUiAiming &&
                    (effectiveMode == InteractionModeType.SmartCastWithIndicator ||
                     effectiveMode == InteractionModeType.PressReleaseAimCast))
                {
                    effectiveMode = InteractionModeType.AimCast;
                }

                if (effectiveMode != InteractionModeType.TargetFirst)
                {
                    HandleSkillMappingWithMode(actionId, effectiveMapping, effectiveMode);
                    return true;
                }
            }

            if (!TryBuildOrder(effectiveMapping, out var order))
            {
                return false;
            }

            SubmitOrder(effectiveMapping, in order);
            return true;
        }

        private static int ResolveSkillActionPriority(string actionId, InputOrderMapping mapping)
        {
            if (mapping.ArgsTemplate.I0 is not int priority || priority < 0)
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ORDER_SKILL_PRIORITY_REQUIRED: skill mapping '{actionId}' must define argsTemplate.i0 as its data-driven priority.");
            }

            return priority;
        }

        public void SaveUserPreferences(string? path = null)
        {
            var effectivePath = path ?? _config.UserOverrides.PersistPath;
            if (string.IsNullOrEmpty(effectivePath)) return;
            if (effectivePath.StartsWith("user://"))
            {
                effectivePath = effectivePath.Replace("user://", 
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/Ludots/");
            }
            var overrideConfig = new InputOrderMappingConfig
            {
                Mappings = CopyOrderedUserOverrides()
            };
            InputOrderMappingLoader.SaveToFile(effectivePath, overrideConfig);
        }
        
        public void LoadUserPreferences(string? path = null)
        {
            var effectivePath = path ?? _config.UserOverrides.PersistPath;
            if (string.IsNullOrEmpty(effectivePath)) return;
            if (effectivePath.StartsWith("user://"))
            {
                effectivePath = effectivePath.Replace("user://", 
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/Ludots/");
            }
            var overrideConfig = InputOrderMappingLoader.LoadFromFile(effectivePath);
            _userOverrides.Clear();
            foreach (var mapping in overrideConfig.Mappings)
            {
                if (!string.IsNullOrEmpty(mapping.ActionId))
                {
                    if (!_mappingsByActionId.ContainsKey(mapping.ActionId))
                    {
                        throw new InvalidOperationException(
                            $"Input mapping override references unknown actionId '{mapping.ActionId}'.");
                    }

                    ValidateOrderTypeKeys(mapping);
                    _userOverrides[mapping.ActionId] = mapping;
                }
            }
        }

        private void ValidateAllOrderTypeKeys()
        {
            foreach (var entry in _orderedMappings)
            {
                ValidateOrderTypeKeys(entry.Mapping);
            }

            foreach (var mapping in _userOverrides.Values)
            {
                ValidateOrderTypeKeys(mapping);
            }
        }

        private void ValidateOrderTypeKeys(InputOrderMapping mapping)
        {
            if (_orderTypeKeyResolver == null)
            {
                return;
            }

            if (mapping.ActorOrderRouting is { Candidates.Count: > 0 })
            {
                for (int i = 0; i < mapping.ActorOrderRouting.Candidates.Count; i++)
                {
                    ActorOrderRoutingCandidate candidate = mapping.ActorOrderRouting.Candidates[i];
                    RequireOrderTypeId(mapping.ActionId, candidate.OrderTypeKey);
                }
            }
            else
            {
                RequireOrderTypeId(mapping);
            }

            if (!string.IsNullOrWhiteSpace(mapping.OrderTypeKey) &&
                mapping.Trigger == InputTriggerType.Held &&
                mapping.HeldPolicy == HeldPolicy.StartEnd)
            {
                RequireOrderTypeId(mapping, ".Start");
                RequireOrderTypeId(mapping, ".End");
            }
        }

        /// <summary>
        /// Programmatically cancel the current aiming state (if any).
        /// </summary>
        public void CancelAiming()
        {
            ExitAimingState();
        }

        private List<InputOrderMapping> CopyOrderedUserOverrides()
        {
            var mappings = new List<InputOrderMapping>(_userOverrides.Count);
            for (int i = 0; i < _orderedMappings.Length; i++)
            {
                if (_userOverrides.TryGetValue(_orderedMappings[i].ActionId, out var mapping))
                {
                    mappings.Add(mapping);
                }
            }

            return mappings;
        }

        private static int ResolveMappingPriority(InputOrderMapping mapping)
        {
            return mapping.IsSkillMapping
                ? ResolveSkillActionPriority(mapping.ActionId, mapping)
                : int.MaxValue;
        }

        private static int CompareMappingEntries(MappingEntry left, MappingEntry right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0)
            {
                return priority;
            }

            return left.ActionIdOrdinal.CompareTo(right.ActionIdOrdinal);
        }

        private static string RequireConfiguredActionId(string actionId, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !string.Equals(actionId, actionId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_INPUT_ORDER_ACTION_BINDING_REQUIRED: {nameof(InputOrderMappingSystem)} requires {propertyName} from {nameof(InteractionActionBindings)}.");
            }

            return actionId;
        }

        private string RequireConfirmActionId() => RequireConfiguredActionId(_confirmActionId, nameof(ConfirmActionId));

        private string RequireCancelActionId() => RequireConfiguredActionId(_cancelActionId, nameof(CancelActionId));

        private string RequireCommandActionId() => RequireConfiguredActionId(_commandActionId, nameof(CommandActionId));

    }
}
