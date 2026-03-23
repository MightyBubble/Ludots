using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
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
    /// Delegate for checking if a modifier key is held.
    /// </summary>
    public delegate bool ModifierKeyProvider();

    /// <summary>
    /// Callback fired when the system enters or exits aiming state (AimCast mode).
    /// Consumers use this to show/hide indicators via IndicatorRequestBuffer.
    /// The system itself has no knowledge of indicators; it only signals state changes.
    /// </summary>
    /// <param name="isAiming">True when entering aiming, false when exiting.</param>
    /// <param name="mapping">The mapping being aimed.</param>
    public delegate void AimingStateChangedHandler(bool isAiming, InputOrderMapping mapping);

    /// <summary>
    /// Callback fired each frame while aiming (AimCast mode) so the consumer can
    /// update indicator position/shape. The system has no knowledge of indicators.
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
    /// Callback fired each frame during vector aiming so the consumer can show
    /// the origin-to-cursor line indicator. The system has no knowledge of indicators.
    /// </summary>
    /// <param name="mapping">The mapping being vector-aimed.</param>
    /// <param name="origin">The locked-in origin point (world cm).</param>
    /// <param name="cursor">Current cursor ground position (world cm).</param>
    /// <param name="phase">Current phase of vector aiming.</param>
    public delegate void VectorAimUpdateHandler(InputOrderMapping mapping, Vector3 origin, Vector3 cursor, VectorAimPhase phase);

    /// <summary>
    /// Phase of a two-point vector aiming interaction (e.g. Viktor E, Rumble R).
    /// </summary>
    public enum VectorAimPhase : byte
    {
        /// <summary>Choosing the origin point. Indicator shows cast range circle.</summary>
        Origin = 0,
        /// <summary>Origin is locked; dragging to set direction/endpoint. Indicator shows line.</summary>
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

        private readonly IInputActionReader _input;
        private readonly InputOrderMappingConfig _config;
        private readonly Dictionary<string, InputOrderMapping> _mappingsByActionId;
        private readonly Dictionary<string, InputOrderMapping> _userOverrides;
        private readonly Dictionary<string, float> _lastPressedAtSecondsByActionId = new();
        
        // Callbacks
        private OrderTypeKeyResolver? _orderTypeKeyResolver;
        private GroundPositionProvider? _groundPositionProvider;
        private ActorProvider? _actorProvider;
        private SelectedEntityProvider? _selectedEntityProvider;
        private SelectedContainerProvider? _selectedContainerProvider;
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
        
        // Context
        private Entity _localPlayer;
        private int _playerId = 1;
        private float _elapsedSeconds;
        private readonly List<Entity> _selectedActorsScratch = new(16);

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
        private VectorAimPhase _vectorAimPhase;
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

        /// <summary>The current vector aim phase. Valid only when <see cref="IsVectorAiming"/> is true.</summary>
        public VectorAimPhase VectorAimPhase => _vectorAimPhase;

        /// <summary>The locked origin for vector aiming. Valid only during direction phase.</summary>
        public Vector3 VectorAimOrigin => _vectorAimOrigin;

        /// <summary>The confirm action ID used to fire the aimed ability. Default: "Select" (left-click).</summary>
        public string ConfirmActionId { get; set; } = "Select";

        /// <summary>The cancel action ID. Default: "Cancel" (ESC).</summary>
        public string CancelActionId { get; set; } = "Cancel";

        /// <summary>The secondary cancel / command action ID. Default: "Command" (right-click).</summary>
        public string CommandActionId { get; set; } = "Command";
        
        public InputOrderMappingSystem(IInputActionReader input, InputOrderMappingConfig config)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _mappingsByActionId = new Dictionary<string, InputOrderMapping>();
            _userOverrides = new Dictionary<string, InputOrderMapping>();
            
            // Index mappings by action ID
            foreach (var mapping in config.Mappings)
            {
                if (!string.IsNullOrEmpty(mapping.ActionId))
                {
                    _mappingsByActionId[mapping.ActionId] = mapping;
                }
            }
        }
        
        // Callback setters (unchanged API + new ones)

        public void SetOrderTypeKeyResolver(OrderTypeKeyResolver resolver) => _orderTypeKeyResolver = resolver;
        public void SetGroundPositionProvider(GroundPositionProvider provider) => _groundPositionProvider = provider;
        public void SetActorProvider(ActorProvider provider) => _actorProvider = provider;
        public void SetSelectedEntityProvider(SelectedEntityProvider provider) => _selectedEntityProvider = provider;
        public void SetSelectedContainerProvider(SelectedContainerProvider provider) => _selectedContainerProvider = provider;
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
        public void SetSkillMappingOverrideProvider(SkillMappingOverrideProvider provider) => _skillMappingOverrideProvider = provider;
        
        public void SetLocalPlayer(Entity entity, int playerId)
        {
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
            foreach (var (actionId, mapping) in _mappingsByActionId)
            {
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
                if (TryBuildOrder(effectiveMapping, out var order))
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
            foreach (var kvp in _activeHeldStartEndActions)
            {
                string actionId = kvp.Key;
                if (_input.ReleasedThisFrame(actionId))
                {
                    HeldStartEndState state = kvp.Value;
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
                    // Press -> enter aiming (show indicator)
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
                _vectorAimPhase = VectorAimPhase.Origin;
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
            _vectorAimPhase = VectorAimPhase.Origin;
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

            if (_input.PressedThisFrame(CancelActionId) || _input.PressedThisFrame(CommandActionId))
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

            // Vector aiming (two-point targeting)
            if (_isVectorAiming)
            {
                HandleVectorAimingState();
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
                if (_input.PressedThisFrame(CancelActionId) || _input.PressedThisFrame(CommandActionId))
                {
                    ExitAimingState();
                    return;
                }
                
                // Signal aiming update (for indicator refresh)
                _aimingUpdateHandler?.Invoke(_aimingMapping);
                return;
            }

            // AimCast: Confirm by left-click
            if (_input.PressedThisFrame(ConfirmActionId))
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
            if (_input.PressedThisFrame(CancelActionId) || _input.PressedThisFrame(CommandActionId))
            {
                ExitAimingState();
                return;
            }

            // Pressing a different skill key while aiming switches to that skill
            foreach (var (actionId, mapping) in _mappingsByActionId)
            {
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

            // Signal aiming update (for indicator refresh)
            _aimingUpdateHandler?.Invoke(_aimingMapping);
        }

        /// <summary>
        /// Two-phase vector aiming state machine.
        /// Phase Origin: click to lock origin point.
        /// Phase Direction: click to lock endpoint, then build and submit order.
        /// </summary>
        private void HandleVectorAimingState()
        {
            // Cancel: right-click or ESC at any phase
            if (_input.PressedThisFrame(CancelActionId) || _input.PressedThisFrame(CommandActionId))
            {
                ExitAimingState();
                return;
            }

            // Get current cursor position
            Vector3 cursorPos = default;
            bool hasCursor = _groundPositionProvider != null && _groundPositionProvider(out cursorPos);

            switch (_vectorAimPhase)
            {
                case VectorAimPhase.Origin:
                    // Signal update: show origin indicator (range circle at cursor)
                    if (hasCursor)
                    {
                        _vectorAimUpdateHandler?.Invoke(_aimingMapping!, cursorPos, cursorPos, VectorAimPhase.Origin);
                    }
                    
                    // Confirm origin with left-click
                    if (_input.PressedThisFrame(ConfirmActionId) && hasCursor)
                    {
                        _vectorAimOrigin = cursorPos;
                        _vectorAimPhase = VectorAimPhase.Direction;
                    }
                    break;

                case VectorAimPhase.Direction:
                    // Signal update: show line from origin to cursor
                    if (hasCursor)
                    {
                        _vectorAimUpdateHandler?.Invoke(_aimingMapping!, _vectorAimOrigin, cursorPos, VectorAimPhase.Direction);
                    }
                    
                    // Confirm direction with left-click -> build and submit vector order
                    if (_input.PressedThisFrame(ConfirmActionId) && hasCursor)
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
                    _vectorAimUpdateHandler?.Invoke(mapping, cursorPos, cursorPos, VectorAimPhase.Origin);
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
            int orderTypeId = _orderTypeKeyResolver!(mapping.OrderTypeKey + orderTypeSuffix);
            if (orderTypeId <= 0) return false;
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

            int orderTypeId = _orderTypeKeyResolver!(mapping.OrderTypeKey);
            if (orderTypeId <= 0)
            {
                return false;
            }

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

            int orderTypeId = _orderTypeKeyResolver!(mapping.OrderTypeKey);
            if (orderTypeId <= 0) return false;

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
            
            int orderTypeId = _orderTypeKeyResolver!(mapping.OrderTypeKey);
            if (orderTypeId <= 0) return false;
            
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
            order = default;
            
            int orderTypeId = _orderTypeKeyResolver!(mapping.OrderTypeKey);
            if (orderTypeId <= 0) return false;
            
            Entity actor = ResolvePrimaryActor(mapping);
            
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            
            if (mapping.RequireSelection)
            {
                switch (mapping.SelectionType)
                {
                    case OrderSelectionType.Position:
                    case OrderSelectionType.Direction:
                        if (_groundPositionProvider == null || !_groundPositionProvider(out var groundPos))
                        {
                            return false;
                        }
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;
                        if (mapping.SelectionType == OrderSelectionType.Direction &&
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

        private bool TryCaptureSelectedContainer(string selectionSetKey, ref OrderSelectionReference selection)
        {
            selection = default;
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
                ApplyGroupMoveFormation(mapping, _selectedActorsScratch.Count, i, ref cloned);
                _orderSubmitHandler!(in cloned);
            }
        }

        private void ApplyGroupMoveFormation(InputOrderMapping mapping, int totalCount, int index, ref Order order)
        {
            if (totalCount <= 1 ||
                mapping.IsSkillMapping ||
                mapping.SelectionType != OrderSelectionType.Position ||
                !string.Equals(mapping.OrderTypeKey, "moveTo", StringComparison.OrdinalIgnoreCase) ||
                _config.GroupMoveFormation.Mode != GroupMoveFormationMode.Grid ||
                order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
                order.Args.Spatial.Mode != OrderCollectionMode.Single)
            {
                return;
            }

            int spacingCm = Math.Max(1, _config.GroupMoveFormation.SpacingCm);
            order.Args.Spatial.WorldCm = MoveFormationPlanner.ComputeOffsetTarget(order.Args.Spatial.WorldCm, index, totalCount, spacingCm);
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

        /// <summary>
        /// Returns the runtime-effective mapping for an action after user remaps and
        /// actor-specific skill overrides have been applied.
        /// </summary>
        public InputOrderMapping? GetEffectiveMapping(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            if (_mappingsByActionId.TryGetValue(actionId, out var mapping))
            {
                return ResolveEffectiveMapping(actionId, mapping, out _);
            }

            if (_userOverrides.TryGetValue(actionId, out var overrideMapping))
            {
                return overrideMapping;
            }

            return null;
        }

        public IEnumerable<string> GetMappedActionIds() => _mappingsByActionId.Keys;

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
                Mappings = new List<InputOrderMapping>(_userOverrides.Values)
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
                    _userOverrides[mapping.ActionId] = mapping;
                }
            }
        }

        /// <summary>
        /// Programmatically cancel the current aiming state (if any).
        /// </summary>
        public void CancelAiming()
        {
            ExitAimingState();
        }

    }
}


