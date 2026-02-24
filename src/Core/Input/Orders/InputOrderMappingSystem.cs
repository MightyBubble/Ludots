using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Delegate for resolving order tag key to order tag ID.
    /// </summary>
    public delegate int OrderTagKeyResolver(string orderTagKey);
    
    /// <summary>
    /// Delegate for getting the ground position for movement commands.
    /// </summary>
    public delegate bool GroundPositionProvider(out Vector3 worldCm);
    
    /// <summary>
    /// Delegate for getting the selected entity.
    /// </summary>
    public delegate bool SelectedEntityProvider(out Entity entity);

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
    /// The system itself has no knowledge of indicators — it only signals state changes.
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
    /// Callback fired each frame during vector aiming so the consumer can show
    /// the origin→cursor line indicator. The system has no knowledge of indicators.
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
    ///   TargetFirst (WoW): trigger → immediate submit using selected entity
    ///   SmartCast (LoL):   trigger → immediate submit using cursor/hovered entity
    ///   AimCast (DotA):    trigger → enter aiming → confirm click → submit
    ///
    /// Non-skill mappings (IsSkillMapping=false) always use TargetFirst behavior.
    /// </summary>
    public sealed class InputOrderMappingSystem
    {
        private readonly PlayerInputHandler _inputHandler;
        private readonly InputOrderMappingConfig _config;
        private readonly Dictionary<string, InputOrderMapping> _mappingsByActionId;
        private readonly Dictionary<string, InputOrderMapping> _userOverrides;
        
        // Callbacks
        private OrderTagKeyResolver? _tagKeyResolver;
        private GroundPositionProvider? _groundPositionProvider;
        private SelectedEntityProvider? _selectedEntityProvider;
        private HoveredEntityProvider? _hoveredEntityProvider;
        private OrderSubmitHandler? _orderSubmitHandler;
        private ModifierKeyProvider? _queueModifierProvider;
        private AimingStateChangedHandler? _aimingStateChangedHandler;
        private AimingUpdateHandler? _aimingUpdateHandler;
        private VectorAimUpdateHandler? _vectorAimUpdateHandler;
        private AutoTargetProvider? _autoTargetProvider;
        
        // Context
        private Entity _localPlayer;
        private int _playerId = 1;

        // ── Aiming state (AimCast mode) ──
        private bool _isAiming;
        private string _aimingActionId = string.Empty;
        private InputOrderMapping? _aimingMapping;
        
        // ── Held Start/End tracking ──
        private readonly HashSet<string> _activeHeldStartEndActions = new();
        
        // ── SmartCastWithIndicator state ──
        private bool _smartCastWithIndicatorActive;
        
        // ── Vector aim state (two-point targeting) ──
        private VectorAimPhase _vectorAimPhase;
        private Vector3 _vectorAimOrigin;
        private bool _isVectorAiming;

        /// <summary>Whether the system is currently in aiming state (AimCast).</summary>
        public bool IsAiming => _isAiming;

        /// <summary>The ActionId of the mapping being aimed (valid only when IsAiming).</summary>
        public string AimingActionId => _aimingActionId;

        /// <summary>The confirm action ID used to fire the aimed ability. Default: "Select" (left-click).</summary>
        public string ConfirmActionId { get; set; } = "Select";

        /// <summary>The cancel action IDs. Default: "Cancel" (ESC). Right-click ("Command") also cancels.</summary>
        public string CancelActionId { get; set; } = "Cancel";
        
        public InputOrderMappingSystem(PlayerInputHandler inputHandler, InputOrderMappingConfig config)
        {
            _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
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
        
        // ── Callback setters (unchanged API + new ones) ──

        public void SetTagKeyResolver(OrderTagKeyResolver resolver) => _tagKeyResolver = resolver;
        public void SetGroundPositionProvider(GroundPositionProvider provider) => _groundPositionProvider = provider;
        public void SetSelectedEntityProvider(SelectedEntityProvider provider) => _selectedEntityProvider = provider;
        public void SetHoveredEntityProvider(HoveredEntityProvider provider) => _hoveredEntityProvider = provider;
        public void SetOrderSubmitHandler(OrderSubmitHandler handler) => _orderSubmitHandler = handler;
        public void SetQueueModifierProvider(ModifierKeyProvider provider) => _queueModifierProvider = provider;
        public void SetAimingStateChangedHandler(AimingStateChangedHandler handler) => _aimingStateChangedHandler = handler;
        public void SetAimingUpdateHandler(AimingUpdateHandler handler) => _aimingUpdateHandler = handler;
        public void SetVectorAimUpdateHandler(VectorAimUpdateHandler handler) => _vectorAimUpdateHandler = handler;
        public void SetAutoTargetProvider(AutoTargetProvider provider) => _autoTargetProvider = provider;
        
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
            if (_tagKeyResolver == null) return;

            var mode = _config.InteractionMode;

            // ── 0. Process Held StartEnd releases (must run even during aiming) ──
            ProcessHeldStartEndReleases();

            // ── 1. Handle active aiming state (AimCast only) ──
            if (_isAiming)
            {
                HandleAimingState();
                return; // While aiming, don't process other mappings
            }
            
            // ── 2. Process all mappings ──
            foreach (var (actionId, mapping) in _mappingsByActionId)
            {
                var effectiveMapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                    ? overrideMapping
                    : mapping;

                // Held+StartEnd is handled separately via press/release detection
                if (effectiveMapping.Trigger == InputTriggerType.Held && effectiveMapping.HeldPolicy == HeldPolicy.StartEnd)
                {
                    if (_inputHandler.PressedThisFrame(actionId) && !_activeHeldStartEndActions.Contains(actionId))
                    {
                        // Emit .Start order
                        if (TryBuildOrderWithTagSuffix(effectiveMapping, ".Start", out var startOrder))
                        {
                            _orderSubmitHandler(in startOrder);
                        }
                        _activeHeldStartEndActions.Add(actionId);
                    }
                    continue; // Release is handled in ProcessHeldStartEndReleases
                }
                
                if (!CheckTrigger(actionId, effectiveMapping.Trigger)) continue;

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
                    _orderSubmitHandler(in order);
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
            foreach (var actionId in _activeHeldStartEndActions)
            {
                if (_inputHandler.ReleasedThisFrame(actionId))
                {
                    var effectiveMapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                        ? overrideMapping
                        : _mappingsByActionId.TryGetValue(actionId, out var m) ? m : null;
                    
                    if (effectiveMapping != null && TryBuildOrderWithTagSuffix(effectiveMapping, ".End", out var endOrder))
                    {
                        _orderSubmitHandler!(in endOrder);
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

        private bool CheckTrigger(string actionId, InputTriggerType trigger)
        {
            return trigger switch
            {
                InputTriggerType.PressedThisFrame => _inputHandler.PressedThisFrame(actionId),
                InputTriggerType.ReleasedThisFrame => _inputHandler.ReleasedThisFrame(actionId),
                InputTriggerType.Held => _inputHandler.IsDown(actionId),
                InputTriggerType.DoubleTap => false, // Obsolete — belongs to selection system
                _ => false
            };
        }

        // ── Interaction mode handling ──

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
                    // Press → enter aiming (show indicator);
                    // Release is handled in the aiming state.
                    EnterAimingState(actionId, mapping);
                    _smartCastWithIndicatorActive = true;
                    break;

                default: // TargetFirst — should not reach here due to guard above
                    if (TryBuildOrder(mapping, out var order))
                    {
                        _orderSubmitHandler!(in order);
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
                _orderSubmitHandler!(in order);
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

        /// <summary>
        /// Called every frame while aiming. Handles confirm/cancel and signals update.
        /// Routes to vector aiming state machine when applicable.
        /// </summary>
        private void HandleAimingState()
        {
            if (_aimingMapping == null) { ExitAimingState(); return; }

            // ── Vector aiming (two-point targeting) ──
            if (_isVectorAiming)
            {
                HandleVectorAimingState();
                return;
            }

            // SmartCastWithIndicator: release of the skill key = confirm cast
            if (_smartCastWithIndicatorActive)
            {
                if (_inputHandler.ReleasedThisFrame(_aimingActionId))
                {
                    if (TryBuildOrderSmartCast(_aimingMapping, out var order))
                    {
                        _orderSubmitHandler!(in order);
                    }
                    ExitAimingState();
                    return;
                }
                
                // Cancel: right-click or ESC
                if (_inputHandler.PressedThisFrame(CancelActionId) || _inputHandler.PressedThisFrame("Command"))
                {
                    ExitAimingState();
                    return;
                }
                
                // Signal aiming update (for indicator refresh)
                _aimingUpdateHandler?.Invoke(_aimingMapping);
                return;
            }

            // AimCast: Confirm by left-click
            if (_inputHandler.PressedThisFrame(ConfirmActionId))
            {
                // Build order using current cursor/selection
                if (TryBuildOrderSmartCast(_aimingMapping, out var order))
                {
                    _orderSubmitHandler!(in order);
                }
                ExitAimingState();
                return;
            }

            // Cancel: right-click or ESC
            if (_inputHandler.PressedThisFrame(CancelActionId) || _inputHandler.PressedThisFrame("Command"))
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
                if (!_inputHandler.PressedThisFrame(actionId)) continue;

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
            if (_inputHandler.PressedThisFrame(CancelActionId) || _inputHandler.PressedThisFrame("Command"))
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
                    if (_inputHandler.PressedThisFrame(ConfirmActionId) && hasCursor)
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
                    
                    // Confirm direction with left-click → build and submit vector order
                    if (_inputHandler.PressedThisFrame(ConfirmActionId) && hasCursor)
                    {
                        if (TryBuildVectorOrder(_aimingMapping!, _vectorAimOrigin, cursorPos, out var order))
                        {
                            _orderSubmitHandler!(in order);
                        }
                        ExitAimingState();
                    }
                    break;
            }
        }

        // ── Order building ──

        /// <summary>
        /// Build an order with a tag key suffix (e.g. ".Start", ".End" for Held StartEnd mode).
        /// </summary>
        private bool TryBuildOrderWithTagSuffix(InputOrderMapping mapping, string tagSuffix, out Order order)
        {
            order = default;
            int orderTagId = _tagKeyResolver!(mapping.OrderTagKey + tagSuffix);
            if (orderTagId <= 0)
            {
                // Fallback: try base tag key (for .End when mod only registered base key)
                orderTagId = _tagKeyResolver(mapping.OrderTagKey);
            }
            if (orderTagId <= 0) return false;

            Entity actor = _localPlayer;
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
                }
            }
            else if (mapping.SelectionType == OrderSelectionType.Entity)
            {
                if (_selectedEntityProvider != null && _selectedEntityProvider(out var target))
                {
                    order.Target = target;
                }
            }

            order.OrderTagId = orderTagId;
            order.PlayerId = _playerId;
            order.Actor = actor;
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

            int orderTagId = _tagKeyResolver!(mapping.OrderTagKey);
            if (orderTagId <= 0) return false;

            Entity actor = _localPlayer;
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);

            // SmartCast targeting priority:
            //   1. Hovered entity (entity under cursor)
            //   2. Auto-target (nearest in range, if configured)
            //   3. Selected entity (fallback)
            switch (mapping.SelectionType)
            {
                case OrderSelectionType.Entity:
                    if (_hoveredEntityProvider != null && _hoveredEntityProvider(out var hovered))
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
                    else if (_selectedEntityProvider != null && _selectedEntityProvider(out var selected))
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
                    }
                    else if (mapping.RequireSelection)
                    {
                        return false;
                    }
                    break;

                case OrderSelectionType.None:
                    // Self-cast or no-target — nothing to fill
                    break;
            }

            order.OrderTagId = orderTagId;
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
            
            int orderTagId = _tagKeyResolver!(mapping.OrderTagKey);
            if (orderTagId <= 0) return false;
            
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            
            // Store both points in List mode: point[0] = origin, point[1] = endpoint
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.List;
            args.Spatial.WorldCm = origin; // Primary point (also stored as WorldCm for backward compat)
            args.Spatial.AddPointWorldCm((int)origin.X, (int)origin.Y, (int)origin.Z);
            args.Spatial.AddPointWorldCm((int)endpoint.X, (int)endpoint.Y, (int)endpoint.Z);
            
            order.OrderTagId = orderTagId;
            order.PlayerId = _playerId;
            order.Actor = _localPlayer;
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
            
            int orderTagId = _tagKeyResolver!(mapping.OrderTagKey);
            if (orderTagId <= 0) return false;
            
            Entity actor = _localPlayer;
            if (_selectedEntityProvider != null && _selectedEntityProvider(out var selected))
            {
                // Use selected entity as actor if available (for RTS-style control)
            }
            
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            
            if (mapping.RequireSelection)
            {
                switch (mapping.SelectionType)
                {
                    case OrderSelectionType.Position:
                        if (_groundPositionProvider == null || !_groundPositionProvider(out var groundPos))
                        {
                            return false;
                        }
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;
                        break;
                        
                    case OrderSelectionType.Entity:
                        if (_selectedEntityProvider == null || !_selectedEntityProvider(out var target))
                        {
                            return false;
                        }
                        order.Target = target;
                        break;
                        
                    case OrderSelectionType.Entities:
                        return false;
                }
            }
            else if (mapping.SelectionType == OrderSelectionType.Entity)
            {
                if (_selectedEntityProvider != null && _selectedEntityProvider(out var target))
                {
                    order.Target = target;
                }
            }
            
            order.OrderTagId = orderTagId;
            order.PlayerId = _playerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }
        
        private OrderSubmitMode DetermineSubmitMode(ModifierSubmitBehavior behavior)
        {
            bool queueModifierHeld = _queueModifierProvider?.Invoke() ?? _inputHandler.IsDown("QueueModifier");
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

        // ── Public API (Remap, Save, Load — unchanged) ──
        
        public void Remap(string actionId, string orderTagKey, OrderArgsTemplate? argsTemplate = null)
        {
            if (!_mappingsByActionId.TryGetValue(actionId, out var original))
            {
                throw new ArgumentException($"No mapping found for action: {actionId}");
            }
            
            var newMapping = new InputOrderMapping
            {
                ActionId = actionId,
                Trigger = original.Trigger,
                OrderTagKey = orderTagKey,
                ArgsTemplate = argsTemplate ?? original.ArgsTemplate,
                RequireSelection = original.RequireSelection,
                SelectionType = original.SelectionType,
                IsSkillMapping = original.IsSkillMapping
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
        
        public IEnumerable<string> GetMappedActionIds() => _mappingsByActionId.Keys;
        
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
