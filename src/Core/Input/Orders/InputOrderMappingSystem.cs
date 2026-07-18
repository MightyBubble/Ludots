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

    public delegate bool ActivationActorValidator(Entity actor, int playerId);

    /// <summary>
    /// Delegate for resolving the owner of the active actor collection.
    /// </summary>
    public delegate bool ActiveActorCollectionOwnerProvider(out Entity owner);

    /// <summary>
    /// Delegate for resolving command-click target facts frozen for the current input mapping trigger.
    /// </summary>
    public delegate bool CommandIntentTargetFactsProvider(InputOrderMapping mapping, out CommandIntentTargetFacts facts);

    /// <summary>
    /// Delegate for assigning an order id before submission when a dispatch profile requires shared fan-out.
    /// </summary>
    public delegate void OrderIdentityAssigner(ref Order order);

    /// <summary>
    /// Delegate for getting the primary entity from a caller-supplied entity collection.
    /// </summary>
    public delegate bool CollectionPrimaryEntityProvider(string collectionKey, out Entity entity);

    /// <summary>
    /// Delegate for copying caller-supplied entity collection members into a reusable list.
    /// </summary>
    public delegate bool CollectionEntityListProvider(string collectionKey, List<Entity> entities);

    /// <summary>
    /// Delegate for getting the entity currently under the cursor (for SmartCast).
    /// </summary>
    public delegate bool HoveredEntityProvider(out Entity entity);
    
    /// <summary>
    /// Delegate for submitting an order.
    /// </summary>
    public delegate OrderSubmitResult OrderSubmitHandler(in Order order);

    public readonly struct InputOrderActivationContext
    {
        public InputOrderActivationContext(Entity actor, int playerId)
        {
            Actor = actor;
            PlayerId = playerId;
        }

        public Entity Actor { get; }
        public int PlayerId { get; }
    }

    public enum InputOrderActivationState : byte
    {
        EnteredAiming = 0,
        Submitted = 1,
        Rejected = 2
    }

    public readonly struct InputOrderActivationResult
    {
        private InputOrderActivationResult(
            InputOrderActivationState state,
            Entity actor,
            int orderId,
            OrderSubmitResult rejection)
        {
            State = state;
            Actor = actor;
            OrderId = orderId;
            Rejection = rejection;
        }

        public InputOrderActivationState State { get; }
        public Entity Actor { get; }
        public int OrderId { get; }
        public OrderSubmitResult Rejection { get; }

        public static InputOrderActivationResult EnteredAiming(Entity actor) => new(InputOrderActivationState.EnteredAiming, actor, 0, default);
        public static InputOrderActivationResult Submitted(Entity actor, int orderId) => new(InputOrderActivationState.Submitted, actor, orderId, default);
        public static InputOrderActivationResult Rejected(Entity actor, OrderSubmitResult reason) => new(InputOrderActivationState.Rejected, actor, 0, reason);
        public static InputOrderActivationResult Rejected(Entity actor, int orderId, OrderSubmitResult reason) => new(InputOrderActivationState.Rejected, actor, orderId, reason);
    }

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
    ///   TargetFirst (WoW): trigger -> immediate submit using the configured entity target
    ///   SmartCast (LoL):   trigger -> immediate submit using the mapping's declared target source
    ///   AimCast (DotA):    trigger -> enter aiming -> confirm action -> submit
    ///
    /// Non-skill mappings (IsSkillMapping=false) always use TargetFirst behavior.
    /// </summary>
    public sealed class InputOrderMappingSystem
    {
        private const int InitialScratchCapacity = 16;

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
        private CollectionPrimaryEntityProvider? _collectionPrimaryEntityProvider;
        private CollectionEntityListProvider? _collectionEntityListProvider;
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

        // Pointer command intent routing. Production wiring injects these services; non-command
        // mappings continue through the direct order path.
        private World? _commandIntentWorld;
        private InteractionContextStack? _interactionContextStack;
        private ControlSchemeRuntime? _controlSchemeRuntime;
        private CommandIntentProfileRegistry? _commandIntentProfiles;
        private CastDispatchProfileRegistry? _castDispatchProfiles;
        private EntityCollectionStore? _entityCollections;
        private ActiveActorCollectionOwnerProvider? _activeActorCollectionOwnerProvider;
        private CommandIntentTargetFactsProvider? _commandIntentTargetFactsProvider;
        private OrderIdentityAssigner? _orderIdentityAssigner;
        
        // Context
        private Entity _localPlayer;
        private int _playerId;
        private ActivationActorValidator? _activationActorValidator;
        private Entity _explicitActivationActor;
        private int _explicitActivationPlayerId;
        private bool _hasExplicitActivationContext;
        private int _lastSubmittedOrderId;
        private float _elapsedSeconds;
        private readonly List<Entity> _collectionActorsScratch = new(InitialScratchCapacity);

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

        private readonly List<RoutedOrderSubmission> _routedOrdersScratch = new(InitialScratchCapacity);
        private Entity[] _commandIntentActorsScratch = new Entity[InitialScratchCapacity];
        private Entity[] _commandIntentRoutedActorsScratch = new Entity[InitialScratchCapacity];
        private CommandIntentRoute[] _commandIntentRoutesScratch = new CommandIntentRoute[InitialScratchCapacity];
        private CommandIntentRoute[] _commandIntentRoutedRoutesScratch = new CommandIntentRoute[InitialScratchCapacity];
        private Entity[] _commandIntentDispatchActorsScratch = new Entity[InitialScratchCapacity];

        // Aiming state (AimCast mode)
        private bool _isAiming;
        private string _aimingActionId = string.Empty;
        private InputOrderMapping? _aimingMapping;
        private InputOrderActivationContext _aimingContext;
        public InputOrderActivationResult LastActivationResult { get; private set; }
        
        // Held Start/End tracking
        private readonly Dictionary<string, HeldStartEndState> _activeHeldStartEndActions = new();
        
        // SmartCastWithIndicator state
        private bool _smartCastWithIndicatorActive;

        // PressReleaseAimCast state
        private bool _pressReleaseAimPending;
        private string _pressReleaseAimActionId = string.Empty;
        private InputOrderMapping? _pressReleaseAimMapping;
        private InputOrderActivationContext _pressReleaseAimContext;
        
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
        public int PlayerId => _playerId;

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
        public void SetActivationActorValidator(ActivationActorValidator validator) =>
            _activationActorValidator = validator ?? throw new ArgumentNullException(nameof(validator));
        public void SetCollectionPrimaryEntityProvider(CollectionPrimaryEntityProvider provider) => _collectionPrimaryEntityProvider = provider;
        public void SetCollectionEntityListProvider(CollectionEntityListProvider provider) => _collectionEntityListProvider = provider;
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

        public void SetCommandIntentRouting(
            World world,
            InteractionContextStack stack,
            ControlSchemeRuntime controlSchemeRuntime,
            CommandIntentProfileRegistry commandIntentProfiles,
            CastDispatchProfileRegistry castDispatchProfiles,
            EntityCollectionStore entityCollections,
            ActiveActorCollectionOwnerProvider? activeActorCollectionOwnerProvider = null)
        {
            _commandIntentWorld = world ?? throw new ArgumentNullException(nameof(world));
            _interactionContextStack = stack ?? throw new ArgumentNullException(nameof(stack));
            _controlSchemeRuntime = controlSchemeRuntime ?? throw new ArgumentNullException(nameof(controlSchemeRuntime));
            _commandIntentProfiles = commandIntentProfiles ?? throw new ArgumentNullException(nameof(commandIntentProfiles));
            _castDispatchProfiles = castDispatchProfiles ?? throw new ArgumentNullException(nameof(castDispatchProfiles));
            _entityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
            _activeActorCollectionOwnerProvider = activeActorCollectionOwnerProvider;
        }

        public void SetOrderIdentityAssigner(OrderIdentityAssigner assigner) =>
            _orderIdentityAssigner = assigner ?? throw new ArgumentNullException(nameof(assigner));

        public void SetCommandIntentTargetFactsProvider(CommandIntentTargetFactsProvider provider) =>
            _commandIntentTargetFactsProvider = provider ?? throw new ArgumentNullException(nameof(provider));

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

                if (IsCommandAction(actionId))
                {
                    SubmitCommandIntentOrder(effectiveMapping);
                    continue;
                }

                // Skill mappings are affected by InteractionMode; non-skill mappings always go through immediately.
                // Per-ability CastModeOverride takes precedence over the global InteractionMode.
                if (effectiveMapping.IsSkillMapping)
                {
                    var effectiveMode = effectiveMapping.CastModeOverride ?? mode;
                    if (effectiveMode != InteractionModeType.TargetFirst)
                    {
                        HandleSkillMappingWithMode(actionId, effectiveMapping, effectiveMode, resolvedActor);
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

        private void HandleSkillMappingWithMode(
            string actionId,
            InputOrderMapping mapping,
            InteractionModeType mode,
            Entity resolvedActor)
        {
            Entity activationActor = _hasExplicitActivationContext
                ? _explicitActivationActor
                : resolvedActor != default
                    ? resolvedActor
                    : ResolvePrimaryActor(mapping);
            var activationContext = new InputOrderActivationContext(
                activationActor,
                CurrentActivationPlayerId);

            // Vector target input always requires two-click interaction (origin + endpoint),
            // so all modes fall through to AimCast for vector-targeted abilities.
            if (mapping.TargetType == OrderTargetType.Vector)
            {
                EnterAimingState(actionId, mapping, in activationContext);
                return;
            }
            
            switch (mode)
            {
                case InteractionModeType.SmartCast:
                    HandleSmartCast(mapping);
                    break;

                case InteractionModeType.AimCast:
                    EnterAimingState(actionId, mapping, in activationContext);
                    break;

                case InteractionModeType.SmartCastWithIndicator:
                    // Press -> enter aiming and publish aim preview.
                    // Release is handled in the aiming state.
                    EnterAimingState(actionId, mapping, in activationContext);
                    _smartCastWithIndicatorActive = true;
                    break;

                case InteractionModeType.PressReleaseAimCast:
                    QueuePressReleaseAim(actionId, mapping, in activationContext);
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
        /// SmartCast: immediately build and submit through the mapping's declared target source.
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
        /// Automatically enters vector aiming mode for Vector target type.
        /// </summary>
        private void EnterAimingState(
            string actionId,
            InputOrderMapping mapping,
            in InputOrderActivationContext context)
        {
            // If already aiming a different skill, cancel old first
            if (_isAiming && _aimingActionId != actionId)
            {
                ExitAimingState();
            }

            _isAiming = true;
            _aimingActionId = actionId;
            _aimingMapping = mapping;
            _aimingContext = context;
            
            // Auto-detect vector aiming mode
            if (mapping.TargetType == OrderTargetType.Vector)
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
            _aimingContext = default;
            _smartCastWithIndicatorActive = false;
            _isVectorAiming = false;
            _vectorAimSlot = VectorAimInputSlot.Origin;
            _vectorAimOrigin = default;
            _aimingStateChangedHandler?.Invoke(false, mapping);
        }

        private void QueuePressReleaseAim(
            string actionId,
            InputOrderMapping mapping,
            in InputOrderActivationContext context)
        {
            _pressReleaseAimPending = true;
            _pressReleaseAimActionId = actionId ?? string.Empty;
            _pressReleaseAimMapping = mapping;
            _pressReleaseAimContext = context;
        }

        private void ClearPressReleaseAimPending()
        {
            _pressReleaseAimPending = false;
            _pressReleaseAimActionId = string.Empty;
            _pressReleaseAimMapping = null;
            _pressReleaseAimContext = default;
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
            InputOrderActivationContext context = _pressReleaseAimContext;
            ClearPressReleaseAimPending();
            EnterAimingState(actionId, mapping, in context);
        }

        /// <summary>
        /// Called every frame while aiming. Handles confirm/cancel and signals update.
        /// Routes to vector aiming state machine when applicable.
        /// </summary>
        private void HandleAimingState()
        {
            if (_aimingMapping == null) { ExitAimingState(); return; }
            if (_aimingContext.Actor != Entity.Null &&
                _activationActorValidator != null &&
                !_activationActorValidator(_aimingContext.Actor, _aimingContext.PlayerId))
            {
                RecordRejectedActivation(
                    _aimingContext.Actor,
                    OrderSubmitResult.RejectedInvalidActor);
                ExitAimingState();
                return;
            }

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
                
                // Cancel through the configured cancel or command action.
                if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
                {
                    ExitAimingState();
                    return;
                }
                
                // Signal aiming update for presentation refresh.
                _aimingUpdateHandler?.Invoke(_aimingMapping);
                return;
            }

            // AimCast: confirm through the configured confirm action.
            if (_input.PressedThisFrame(confirmActionId))
            {
                // Build order using current cursor input.
                if (TryBuildOrderSmartCast(_aimingMapping, out var order))
                {
                    SubmitOrder(_aimingMapping, in order);
                }
                ExitAimingState();
                return;
            }

            // Cancel through the configured cancel or command action.
            if (_input.PressedThisFrame(cancelActionId) || _input.PressedThisFrame(commandActionId))
            {
                ExitAimingState();
                return;
            }

            // Pressing a different skill key while aiming cancels the old aim and obeys
            // the new skill's effective cast mode, including per-ability overrides.
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                InputOrderMapping mapping = entry.Mapping;
                if (actionId == _aimingActionId) continue;
                var effectiveMapping = ResolveEffectiveMapping(actionId, mapping, out Entity resolvedActor);
                if (!effectiveMapping.IsSkillMapping) continue;
                if (!_input.PressedThisFrame(actionId)) continue;

                ExitAimingState();
                var effectiveMode = effectiveMapping.CastModeOverride ?? _config.InteractionMode;
                if (effectiveMode != InteractionModeType.TargetFirst)
                {
                    HandleSkillMappingWithMode(actionId, effectiveMapping, effectiveMode, resolvedActor);
                    return;
                }

                if (TryBuildOrder(effectiveMapping, out var order))
                {
                    SubmitOrder(effectiveMapping, in order);
                }

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
            // Cancel through the configured cancel or command action at any phase.
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
                    
                    // Confirm origin through the configured confirm action.
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
                    
                    // Confirm direction through the configured confirm action, then build and submit vector order.
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
            RequireValidConfiguredTargetResolver(mapping, mapping.TargetType);

            // Fill target data same as TryBuildOrder.
            if (mapping.TargetType == OrderTargetType.Position || mapping.TargetType == OrderTargetType.Direction)
            {
                if (_groundPositionProvider != null && _groundPositionProvider(out var pos))
                {
                    args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    args.Spatial.Mode = OrderCollectionMode.Single;
                    args.Spatial.WorldCm = pos;

                    if (mapping.TargetType == OrderTargetType.Position)
                    {
                        if (TryResolveCursorTarget(actor, mapping, pos, out var cursorTarget))
                        {
                            order.Target = cursorTarget;
                        }
                        else if (TryResolveAutoTarget(actor, mapping, out var positionAutoTarget))
                        {
                            order.Target = positionAutoTarget;
                        }
                    }
                    else if (mapping.TargetType == OrderTargetType.Direction)
                    {
                        if (TryResolveCursorTarget(actor, mapping, pos, out var directionTarget))
                        {
                            order.Target = directionTarget;
                        }
                    }
                }
            }
            else if (mapping.TargetType == OrderTargetType.Entity)
            {
                if (_collectionPrimaryEntityProvider != null && _collectionPrimaryEntityProvider(mapping.TargetCollectionKey, out var target))
                {
                    order.Target = target;
                }
            }
            else if (mapping.TargetType == OrderTargetType.Entities)
            {
                TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch);
            }

            order.OrderTypeId = orderTypeId;
            order.PlayerId = CurrentActivationPlayerId;
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
            order.PlayerId = CurrentActivationPlayerId;
            order.Actor = actor;
            order.Target = resolution.Target;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        /// <summary>
        /// Build order for skill/cast SmartCast paths. Target sources are explicit: entity casts
        /// use either an auto-target policy or the hover collection; position/direction casts use
        /// cursor/auto-target policies only when configured. Command intent actions bypass this method.
        /// </summary>
        private bool TryBuildOrderSmartCast(InputOrderMapping mapping, out Order order)
        {
            order = default;
            if (!HasExplicitLocalPlayer()) return false;

            int orderTypeId = RequireOrderTypeId(mapping);

            Entity actor = ResolvePrimaryActor(mapping);
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            RequireValidConfiguredTargetResolver(mapping, mapping.TargetType);

            switch (mapping.TargetType)
            {
                case OrderTargetType.Entity:
                    if (mapping.AutoTargetPolicy != AutoTargetPolicy.None)
                    {
                        if (TryResolveAutoTarget(actor, mapping, out var autoTarget))
                        {
                            order.Target = autoTarget;
                        }
                        else if (mapping.RequireTarget)
                        {
                            return false;
                        }
                    }
                    else if (TryResolveHoveredEntity(out var hovered))
                    {
                        order.Target = hovered;
                    }
                    else if (mapping.RequireTarget)
                    {
                        return false;
                    }
                    break;

                case OrderTargetType.Position:
                    if (_groundPositionProvider != null && _groundPositionProvider(out var groundPos))
                    {
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;

                        if (TryResolveCursorTarget(actor, mapping, groundPos, out var cursorTarget))
                        {
                            order.Target = cursorTarget;
                        }
                        else if (TryResolveAutoTarget(actor, mapping, out var positionAutoTarget))
                        {
                            order.Target = positionAutoTarget;
                        }
                    }
                    else if (mapping.RequireTarget)
                    {
                        return false;
                    }
                    break;

                case OrderTargetType.Direction:
                    // Direction: store normalized direction from actor to cursor
                    if (_groundPositionProvider != null && _groundPositionProvider(out var dirPos))
                    {
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = dirPos;

                        if (TryResolveCursorTarget(actor, mapping, dirPos, out var directionTarget))
                        {
                            order.Target = directionTarget;
                        }
                    }
                    else if (mapping.RequireTarget)
                    {
                        return false;
                    }
                    break;

                case OrderTargetType.Entities:
                    if (!TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch) &&
                        mapping.RequireTarget)
                    {
                        return false;
                    }
                    break;

                case OrderTargetType.None:
                    // Self-cast or no-target; nothing to fill
                    break;
            }

            order.OrderTypeId = orderTypeId;
            order.PlayerId = CurrentActivationPlayerId;
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
            RequireValidConfiguredTargetResolver(mapping, mapping.TargetType);
            
            // Store both points in List mode: point[0] = origin, point[1] = endpoint
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.List;
            args.Spatial.WorldCm = origin; // Primary point
            args.Spatial.AddInlinePointWorldCm((int)origin.X, (int)origin.Y, (int)origin.Z);
            args.Spatial.AddInlinePointWorldCm((int)endpoint.X, (int)endpoint.Y, (int)endpoint.Z);
            
            order.OrderTypeId = orderTypeId;
            order.PlayerId = CurrentActivationPlayerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        /// <summary>
        /// Build immediate TargetFirst and non-skill orders.
        /// </summary>
        private bool TryBuildOrder(InputOrderMapping mapping, out Order order)
        {
            Entity actor = ResolvePrimaryActor(mapping);
            return TryBuildOrderForActor(mapping, actor, mapping.OrderTypeKey, TargetTypeOverride: null, out order);
        }

        private bool TryBuildOrderForActor(
            InputOrderMapping mapping,
            Entity actor,
            string orderTypeKey,
            OrderTargetType? TargetTypeOverride,
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
            OrderTargetType TargetType = TargetTypeOverride ?? mapping.TargetType;
            RequireValidConfiguredTargetResolver(mapping, TargetType);
            
            if (mapping.RequireTarget)
            {
                switch (TargetType)
                {
                    case OrderTargetType.HoveredEntityOrPosition:
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

                    case OrderTargetType.Position:
                    case OrderTargetType.Direction:
                        if (_groundPositionProvider == null || !_groundPositionProvider(out var groundPos))
                        {
                            return false;
                        }
                        args.Spatial.Kind = OrderSpatialKind.WorldCm;
                        args.Spatial.Mode = OrderCollectionMode.Single;
                        args.Spatial.WorldCm = groundPos;
                        if (TargetType == OrderTargetType.Direction)
                        {
                            if (TryResolveCursorTarget(actor, mapping, groundPos, out var directionTarget))
                            {
                                order.Target = directionTarget;
                            }
                        }
                        break;
                        
                    case OrderTargetType.Entity:
                        if (_collectionPrimaryEntityProvider == null || !_collectionPrimaryEntityProvider(mapping.TargetCollectionKey, out var target))
                        {
                            return false;
                        }
                        order.Target = target;
                        break;
                        
                    case OrderTargetType.Entities:
                        if (!TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch))
                        {
                            return false;
                        }
                        break;
                }
            }
            else if (TargetType == OrderTargetType.Entity)
            {
                if (_collectionPrimaryEntityProvider != null && _collectionPrimaryEntityProvider(mapping.TargetCollectionKey, out var target))
                {
                    order.Target = target;
                }
            }
            else if (TargetType == OrderTargetType.Entities)
            {
                TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch);
            }
            
            order.OrderTypeId = orderTypeId;
            order.PlayerId = CurrentActivationPlayerId;
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

            if (mapping.TargetType == OrderTargetType.Entities)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' actorOrderRouting does not support Entities target type.");
            }

            if (!TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch))
            {
                return;
            }

            _routedOrdersScratch.Clear();
            for (int i = 0; i < _collectionActorsScratch.Count; i++)
            {
                Entity actor = _collectionActorsScratch[i];
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
                        matchedCandidate.TargetType,
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

            for (int i = 0; i < _routedOrdersScratch.Count; i++)
            {
                Order order = _routedOrdersScratch[i].Order;
                if (!TryAuthorizeActor(order.Actor, order.PlayerId))
                {
                    return;
                }
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
                    mapping.TargetType == OrderTargetType.Position &&
                    _config.GroupMoveFormation.Mode != GroupMoveFormationMode.None &&
                    IsGroupMoveFormationOrderType(orderTypeKey))
                {
                    ApplyGroupMoveFormation(mapping, orderTypeKey, formationEligibleCount, formationIndex, ref order);
                    formationIndex++;
                }

                SubmitAuthorizedToHandler(in order);
            }
        }

        private bool SubmitCommandIntentOrder(InputOrderMapping mapping)
        {
            if (_commandIntentWorld == null ||
                _interactionContextStack == null ||
                _controlSchemeRuntime == null ||
                _commandIntentProfiles == null ||
                _castDispatchProfiles == null ||
                _entityCollections == null ||
                _commandIntentTargetFactsProvider == null)
            {
                throw new InvalidOperationException(
                    "Command intent routing is partially configured; Command actions must not fall back to legacy input-order mappings.");
            }

            int activeStackIntentId = CommandIntentArbiter.ResolveActiveCommandIntent(
                _interactionContextStack,
                _controlSchemeRuntime);
            if (activeStackIntentId == 0)
            {
                return false;
            }

            if (!_interactionContextStack.TryPeek(out InteractionContextFrame frame))
            {
                throw new InvalidOperationException(
                    "Command intent routing requires a non-empty interaction context stack.");
            }

            string intentName = _interactionContextStack.CommandIntentProfileIdRegistry.GetName(activeStackIntentId);
            if (!_commandIntentProfiles.ProfileIdRegistry.TryGetId(intentName, out int commandIntentProfileId) ||
                !_commandIntentProfiles.IsInstalled(commandIntentProfileId))
            {
                throw new InvalidOperationException(
                    $"Active command intent '{intentName}' is not installed in the command intent registry.");
            }

            if (!HasExplicitLocalPlayer())
            {
                return false;
            }

            if (_groundPositionProvider == null || !_groundPositionProvider(out Vector3 groundWorldCm))
            {
                return false;
            }

            Entity actorCollectionOwner = ResolveActiveActorCollectionOwner();
            if (actorCollectionOwner == Entity.Null)
            {
                return false;
            }

            int actorCount;
            if (_hasExplicitActivationContext)
            {
                _commandIntentActorsScratch[0] = _explicitActivationActor;
                actorCount = 1;
            }
            else
            {
                if (!_entityCollections.TryGet(actorCollectionOwner, frame.ActiveCollectionKeyId, out EntityCollectionHandle handle))
                {
                    return false;
                }

                EnsureCommandIntentScratch(handle);
                actorCount = _entityCollections.CopyEntities(handle, 0, _commandIntentActorsScratch);
            }
            if (actorCount <= 0)
            {
                return false;
            }

            Span<Entity> actors = _commandIntentActorsScratch.AsSpan(0, actorCount);
            Span<CommandIntentRoute> routes = _commandIntentRoutesScratch.AsSpan(0, actorCount);
            CommandIntentTargetFacts targetFacts = ResolveCommandIntentTargetFacts(mapping);
            _commandIntentProfiles.RouteGroup(
                commandIntentProfileId,
                actors,
                actorCollectionOwner,
                in targetFacts,
                routes);

            EnsureEntityScratch(ref _commandIntentRoutedActorsScratch, actorCount);
            EnsureRouteScratch(ref _commandIntentRoutedRoutesScratch, actorCount);
            int routedCount = CompactRoutedActors(
                actors,
                routes,
                _commandIntentRoutedActorsScratch,
                _commandIntentRoutedRoutesScratch);
            if (routedCount <= 0)
            {
                return false;
            }

            Span<Entity> routedActors = _commandIntentRoutedActorsScratch.AsSpan(0, routedCount);
            Span<CommandIntentRoute> routedRoutes = _commandIntentRoutedRoutesScratch.AsSpan(0, routedCount);
            int dispatchProfileId = _controlSchemeRuntime.ActiveDefaultCastDispatchProfileId;
            if (dispatchProfileId == 0)
            {
                throw new InvalidOperationException(
                    "Command intent routing requires the active control scheme to declare defaults.castDispatchProfileId.");
            }

            int dispatchCount = _castDispatchProfiles.SelectDispatchTargets(
                dispatchProfileId,
                routedActors,
                new CastDispatchContext(_commandIntentWorld, groundWorldCm, frame.OwnerToken),
                _commandIntentDispatchActorsScratch.AsSpan(0, routedCount),
                out CastDispatchRouting routing);

            if (dispatchCount <= 0)
            {
                return false;
            }

            if (routing.Sequential && dispatchCount > 1)
            {
                throw new InvalidOperationException(
                    "Command intent dispatch profile returned multiple actors for a sequential router; sequential dispatch must select exactly one actor per trigger.");
            }

            int activationPlayerId = CurrentActivationPlayerId;
            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                if (!TryAuthorizeActor(_commandIntentDispatchActorsScratch[dispatchIndex], activationPlayerId))
                {
                    return false;
                }
            }

            int sharedOrderId = 0;
            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                Entity dispatchActor = _commandIntentDispatchActorsScratch[dispatchIndex];
                int actorIndex = IndexOfEntity(routedActors, dispatchActor);
                if (actorIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Cast dispatch returned actor '{dispatchActor}' that was not present in the routed actor group.");
                }

                CommandIntentRoute route = routedRoutes[actorIndex];

                var args = new OrderArgs();
                ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
                args.Spatial.Kind = OrderSpatialKind.WorldCm;
                args.Spatial.Mode = OrderCollectionMode.Single;
                args.Spatial.WorldCm = groundWorldCm;

                var order = new Order
                {
                    OrderTypeId = route.OrderTypeId,
                    PlayerId = activationPlayerId,
                    Actor = dispatchActor,
                    Args = args,
                    SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior)
                };

                if (routing.SharedOrderId)
                {
                    if (sharedOrderId == 0)
                    {
                        if (_orderIdentityAssigner == null)
                        {
                            throw new InvalidOperationException(
                                "Command intent dispatch profile requires shared order ids, but no order identity assigner is configured.");
                        }

                        _orderIdentityAssigner(ref order);
                        sharedOrderId = order.OrderId;
                    }

                    order.OrderId = sharedOrderId;
                }

                SubmitAuthorizedToHandler(in order);
            }
            return true;
        }

        private Entity ResolveActiveActorCollectionOwner()
        {
            if (_activeActorCollectionOwnerProvider != null)
            {
                if (_activeActorCollectionOwnerProvider(out Entity owner) &&
                    owner != Entity.Null)
                {
                    return owner;
                }

                return Entity.Null;
            }

            if (_localPlayer != Entity.Null)
            {
                return _localPlayer;
            }

            return Entity.Null;
        }

        private CommandIntentTargetFacts ResolveCommandIntentTargetFacts(InputOrderMapping mapping)
        {
            if (_commandIntentTargetFactsProvider == null)
            {
                throw new InvalidOperationException(
                    "Command intent routing requires a command target facts provider.");
            }

            if (!_commandIntentTargetFactsProvider(mapping, out CommandIntentTargetFacts facts) || !facts.HasEntity)
            {
                return new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
            }

            if (facts.Target == Entity.Null)
            {
                throw new InvalidOperationException(
                    "Command intent target facts provider returned HasEntity=true with Entity.Null.");
            }

            return facts;
        }

        private bool IsCommandAction(string actionId)
        {
            return !string.IsNullOrWhiteSpace(_commandActionId) &&
                   string.Equals(actionId, _commandActionId, StringComparison.Ordinal);
        }

        private void EnsureCommandIntentScratch(EntityCollectionHandle handle)
        {
            if (_entityCollections == null)
            {
                return;
            }

            if (!_entityCollections.TryGetView(handle, out EntityCollectionView view))
            {
                throw new InvalidOperationException("Command intent routing received an invalid active collection handle.");
            }

            EnsureEntityScratch(ref _commandIntentActorsScratch, view.Count);
            EnsureRouteScratch(ref _commandIntentRoutesScratch, view.Count);
            EnsureEntityScratch(ref _commandIntentDispatchActorsScratch, view.Count);
        }

        private static int IndexOfEntity(ReadOnlySpan<Entity> entities, Entity value)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompactRoutedActors(
            ReadOnlySpan<Entity> actors,
            ReadOnlySpan<CommandIntentRoute> routes,
            Entity[] routedActors,
            CommandIntentRoute[] routedRoutes)
        {
            int routedCount = 0;
            int count = Math.Min(actors.Length, routes.Length);
            for (int i = 0; i < count; i++)
            {
                CommandIntentRoute route = routes[i];
                if (!route.HasRoute)
                {
                    continue;
                }

                routedActors[routedCount] = actors[i];
                routedRoutes[routedCount] = route;
                routedCount++;
            }

            return routedCount;
        }

        private static void EnsureEntityScratch(ref Entity[] scratch, int required)
        {
            if (scratch.Length >= required)
            {
                return;
            }

            int next = scratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref scratch, next);
        }

        private static void EnsureRouteScratch(ref CommandIntentRoute[] scratch, int required)
        {
            if (scratch.Length >= required)
            {
                return;
            }

            int next = scratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref scratch, next);
        }

        private Entity ResolvePrimaryActor(InputOrderMapping mapping)
        {
            if (_hasExplicitActivationContext)
            {
                return _explicitActivationActor;
            }

            if (_isAiming && _aimingContext.Actor != Entity.Null)
            {
                return _aimingContext.Actor;
            }

            if (_actorProvider != null && _actorProvider(out var actor) && actor != default)
            {
                return actor;
            }

            if (_collectionPrimaryEntityProvider != null &&
                !string.IsNullOrWhiteSpace(mapping.ActorCollectionKey) &&
                _collectionPrimaryEntityProvider(mapping.ActorCollectionKey, out var primary) &&
                primary != Entity.Null)
            {
                return primary;
            }

            if (!string.IsNullOrWhiteSpace(mapping.ActorCollectionKey) &&
                TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch))
            {
                return _collectionActorsScratch[0];
            }

            return _localPlayer;
        }

        private int CurrentActivationPlayerId => _hasExplicitActivationContext
            ? _explicitActivationPlayerId
            : _isAiming && _aimingContext.PlayerId > 0
                ? _aimingContext.PlayerId
                : _playerId;

        private bool HasExplicitLocalPlayer()
        {
            return _playerId > 0 && _localPlayer != Entity.Null;
        }

        private bool TryCaptureCollectionEntities(string collectionKey, List<Entity> entities)
        {
            entities.Clear();
            return _collectionEntityListProvider != null &&
                   _collectionEntityListProvider(collectionKey, entities) &&
                   entities.Count > 0;
        }

        private OrderSubmitResult SubmitOrder(InputOrderMapping mapping, in Order order)
        {
            if (mapping.TargetType == OrderTargetType.Entities)
            {
                return SubmitToHandler(in order);
            }

            bool actorPinned = _hasExplicitActivationContext ||
                               (_isAiming && _aimingContext.Actor != Entity.Null);
            if (actorPinned ||
                string.IsNullOrWhiteSpace(mapping.ActorCollectionKey) ||
                !TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch) ||
                _collectionActorsScratch.Count <= 1)
            {
                return SubmitToHandler(in order);
            }

            for (int i = 0; i < _collectionActorsScratch.Count; i++)
            {
                Entity actor = _collectionActorsScratch[i];
                if (actor != default && !TryAuthorizeActor(actor, order.PlayerId))
                {
                    return OrderSubmitResult.RejectedInvalidActor;
                }
            }

            OrderSubmitResult aggregate = OrderSubmitResult.Queued;
            for (int i = 0; i < _collectionActorsScratch.Count; i++)
            {
                Entity actor = _collectionActorsScratch[i];
                if (actor == default)
                {
                    continue;
                }

                var cloned = order;
                cloned.Actor = actor;
                ApplyGroupMoveFormation(mapping, mapping.OrderTypeKey, _collectionActorsScratch.Count, i, ref cloned);
                OrderSubmitResult result = SubmitAuthorizedToHandler(in cloned);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    aggregate = result;
                }
            }

            return aggregate;
        }

        private OrderSubmitResult SubmitToHandler(in Order order)
        {
            if (!TryAuthorizeActor(order.Actor, order.PlayerId))
            {
                return OrderSubmitResult.RejectedInvalidActor;
            }

            return SubmitAuthorizedToHandler(in order);
        }

        private OrderSubmitResult SubmitAuthorizedToHandler(in Order order)
        {
            var submitted = order;
            if (submitted.OrderId <= 0)
            {
                _orderIdentityAssigner?.Invoke(ref submitted);
            }

            OrderSubmitResult result = _orderSubmitHandler!(in submitted);
            _lastSubmittedOrderId = submitted.OrderId;
            LastActivationResult = OrderSubmitResultSemantics.IsAccepted(result)
                ? InputOrderActivationResult.Submitted(submitted.Actor, submitted.OrderId)
                : InputOrderActivationResult.Rejected(submitted.Actor, submitted.OrderId, result);
            return result;
        }

        private bool TryAuthorizeActor(Entity actor, int playerId)
        {
            if (_activationActorValidator == null || _activationActorValidator(actor, playerId))
            {
                return true;
            }

            RecordRejectedActivation(actor, OrderSubmitResult.RejectedInvalidActor);
            return false;
        }

        private void ApplyGroupMoveFormation(InputOrderMapping mapping, string orderTypeKey, int totalCount, int index, ref Order order)
        {
            if (totalCount <= 1 ||
                mapping.IsSkillMapping ||
                mapping.TargetType != OrderTargetType.Position ||
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

        private bool TryResolveAutoTarget(Entity actor, InputOrderMapping mapping, out Entity target)
        {
            target = default;
            return mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                   mapping.AutoTargetRangeCm > 0 &&
                   _autoTargetProvider != null &&
                   _autoTargetProvider(actor, mapping.AutoTargetPolicy, mapping.AutoTargetRangeCm, out target) &&
                   target != Entity.Null;
        }

        private static void RequireValidConfiguredTargetResolver(InputOrderMapping mapping, OrderTargetType targetType)
        {
            if (mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                mapping.CursorTargetPolicy != AutoTargetPolicy.None)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' declares both autoTargetPolicy and cursorTargetPolicy; configured target source must be explicit.");
            }

            if (mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                targetType != OrderTargetType.Entity &&
                targetType != OrderTargetType.Position)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' autoTargetPolicy requires targetType Entity or Position.");
            }

            if (mapping.CursorTargetPolicy != AutoTargetPolicy.None &&
                targetType != OrderTargetType.Position &&
                targetType != OrderTargetType.Direction)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' cursorTargetPolicy requires targetType Position or Direction.");
            }
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
            
            var newMapping = original.Clone();
            newMapping.ActionId = actionId;
            newMapping.OrderTypeKey = orderTypeKey;
            newMapping.ArgsTemplate = argsTemplate?.Clone() ?? original.ArgsTemplate.Clone();

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

        private InputOrderActivationResult ActivateMappedActionCore(
            string actionId,
            bool preferUiAiming)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                _orderSubmitHandler == null ||
                _orderTypeKeyResolver == null)
            {
                return RecordRejectedActivation(
                    _explicitActivationActor,
                    OrderSubmitResult.RejectedInvalidOrderType);
            }

            if (!_mappingsByActionId.TryGetValue(actionId, out var mapping))
            {
                return RecordRejectedActivation(
                    _explicitActivationActor,
                    OrderSubmitResult.RejectedInvalidOrderType);
            }

            var effectiveMapping = ResolveEffectiveMapping(actionId, mapping, out var resolvedActor);

            if (effectiveMapping.Trigger == InputTriggerType.Held && effectiveMapping.HeldPolicy == HeldPolicy.StartEnd)
            {
                Entity heldActor = resolvedActor != default ? resolvedActor : ResolvePrimaryActor(effectiveMapping);
                if (!TryBuildOrderWithOrderTypeSuffix(effectiveMapping, heldActor, ".Start", out var startOrder))
                {
                    return RecordRejectedActivation(
                        _explicitActivationActor,
                        OrderSubmitResult.RejectedValidation);
                }

                OrderSubmitResult submitResult = SubmitOrder(effectiveMapping, in startOrder);
                return BuildActivationResult(startOrder.Actor, submitResult);
            }

            if (IsCommandAction(actionId))
            {
                SubmitCommandIntentOrder(effectiveMapping);
                return LastActivationResult;
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
                    HandleSkillMappingWithMode(actionId, effectiveMapping, effectiveMode, resolvedActor);
                    if (_isAiming && string.Equals(_aimingActionId, actionId, StringComparison.Ordinal))
                    {
                        LastActivationResult = InputOrderActivationResult.EnteredAiming(_aimingContext.Actor);
                        return LastActivationResult;
                    }

                    return LastActivationResult.State == InputOrderActivationState.Submitted ||
                           LastActivationResult.State == InputOrderActivationState.Rejected
                        ? LastActivationResult
                        : RecordRejectedActivation(
                            _explicitActivationActor,
                            OrderSubmitResult.RejectedByRule);
                }
            }

            if (!TryBuildOrder(effectiveMapping, out var order))
            {
                return RecordRejectedActivation(
                    _explicitActivationActor,
                    OrderSubmitResult.RejectedValidation);
            }

            OrderSubmitResult result = SubmitOrder(effectiveMapping, in order);
            return BuildActivationResult(order.Actor, result);
        }

        public InputOrderActivationResult ActivateMappedAction(
            string actionId,
            in InputOrderActivationContext context,
            bool preferUiAiming = false)
        {
            if (context.Actor == Entity.Null || context.PlayerId <= 0)
            {
                return RecordRejectedActivation(
                    context.Actor,
                    OrderSubmitResult.RejectedInvalidActor);
            }

            if (_activationActorValidator == null)
            {
                throw new InvalidOperationException(
                    "Programmatic mapped-action activation requires an explicit actor validator.");
            }

            if (!_activationActorValidator(context.Actor, context.PlayerId))
            {
                return RecordRejectedActivation(
                    context.Actor,
                    OrderSubmitResult.RejectedInvalidActor);
            }

            if (_orderIdentityAssigner == null)
            {
                throw new InvalidOperationException(
                    "Programmatic mapped-action activation requires an order identity assigner.");
            }

            Entity previousActor = _explicitActivationActor;
            int previousPlayerId = _explicitActivationPlayerId;
            bool previousHasContext = _hasExplicitActivationContext;
            _explicitActivationActor = context.Actor;
            _explicitActivationPlayerId = context.PlayerId;
            _hasExplicitActivationContext = true;
            RecordRejectedActivation(
                context.Actor,
                OrderSubmitResult.RejectedByRule);
            try
            {
                return ActivateMappedActionCore(actionId, preferUiAiming);
            }
            finally
            {
                _explicitActivationActor = previousActor;
                _explicitActivationPlayerId = previousPlayerId;
                _hasExplicitActivationContext = previousHasContext;
            }
        }

        private InputOrderActivationResult BuildActivationResult(
            Entity actor,
            OrderSubmitResult result)
        {
            LastActivationResult = OrderSubmitResultSemantics.IsAccepted(result)
                ? InputOrderActivationResult.Submitted(actor, _lastSubmittedOrderId)
                : InputOrderActivationResult.Rejected(actor, _lastSubmittedOrderId, result);
            return LastActivationResult;
        }

        private InputOrderActivationResult RecordRejectedActivation(
            Entity actor,
            OrderSubmitResult result,
            int orderId = 0)
        {
            _lastSubmittedOrderId = orderId;
            LastActivationResult = InputOrderActivationResult.Rejected(actor, orderId, result);
            return LastActivationResult;
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
