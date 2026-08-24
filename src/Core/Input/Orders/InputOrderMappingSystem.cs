using Ludots.Platform.Abstractions;
using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;

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
    /// Delegate for resolving an actor's authoritative world-centimeter position.
    /// </summary>
    public delegate bool ActorWorldPositionProvider(Entity actor, out WorldCmInt2 worldCm);

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
    /// Delegate for copying caller-supplied entity collection members into a bounded reusable list.
    /// </summary>
    public delegate bool CollectionEntityListProvider(
        string collectionKey,
        List<Entity> entities,
        int capacity,
        out OrderSubmitResult rejection);

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
        None = 0,
        EnteredAiming = 1,
        Submitted = 2,
        Rejected = 3
    }

    public readonly struct InputOrderActivationResult
    {
        private InputOrderActivationResult(
            InputOrderActivationState state,
            Entity actor,
            int orderId,
            Entity target,
            OrderSubmitResult rejection)
        {
            State = state;
            Actor = actor;
            OrderId = orderId;
            Target = target;
            Rejection = rejection;
        }

        public InputOrderActivationState State { get; }
        public Entity Actor { get; }
        public int OrderId { get; }
        /// <summary>
        /// Entity target shared by the submitted order batch. <see cref="Entity.Null"/> means the
        /// activation did not submit an entity target or the batch contained different targets.
        /// Aiming and rejected results always report <see cref="Entity.Null"/>.
        /// </summary>
        public Entity Target { get; }
        public OrderSubmitResult Rejection { get; }

        public static InputOrderActivationResult EnteredAiming(Entity actor) =>
            new(InputOrderActivationState.EnteredAiming, actor, 0, Entity.Null, default);
        public static InputOrderActivationResult Submitted(Entity actor, int orderId, Entity target)
        {
            if (target == default)
            {
                throw new InvalidOperationException(
                    "Submitted input activation target must use Entity.Null for no entity target, not default(Entity).");
            }

            return new InputOrderActivationResult(InputOrderActivationState.Submitted, actor, orderId, target, default);
        }
        public static InputOrderActivationResult Rejected(Entity actor, OrderSubmitResult reason) =>
            new(InputOrderActivationState.Rejected, actor, 0, Entity.Null, reason);
        public static InputOrderActivationResult Rejected(Entity actor, int orderId, OrderSubmitResult reason) =>
            new(InputOrderActivationState.Rejected, actor, orderId, Entity.Null, reason);
    }

    /// <summary>
    /// Delegate for atomically submitting a caller-owned order batch.
    /// </summary>
    public delegate OrderSubmitResult OrderBatchSubmitHandler(Span<Order> orders);

    public delegate OrderSubmitResult OrderClusterBatchSubmitHandler(Span<Order> orders);

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
        public const int DefaultCommandIntentScratchCapacity = 4096;

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

        private readonly struct HeldStartEndOrderTypeKeys
        {
            public HeldStartEndOrderTypeKeys(string start, string end)
            {
                Start = start;
                End = end;
            }

            public string Start { get; }
            public string End { get; }
        }

        private readonly IInputActionReader _input;
        private readonly InputOrderMappingConfig _config;
        private int[] _groupMoveTargetLayoutOrderTypeIds = Array.Empty<int>();
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
        private OrderBatchSubmitHandler? _orderBatchSubmitHandler;
        private OrderClusterBatchSubmitHandler? _orderClusterBatchSubmitHandler;
        private ModifierKeyProvider? _queueModifierProvider;
        private AimingStateChangedHandler? _aimingStateChangedHandler;
        private AimingUpdateHandler? _aimingUpdateHandler;
        private VectorAimUpdateHandler? _vectorAimUpdateHandler;
        private AutoTargetProvider? _autoTargetProvider;
        private CursorTargetProvider? _cursorTargetProvider;
        private ContextScoredResolutionProvider? _contextScoredProvider;
        private SkillMappingOverrideProvider? _skillMappingOverrideProvider;
        private ActorOrderRoutingResolver? _actorOrderRoutingResolver;
        private ActorWorldPositionProvider? _actorWorldPositionProvider;

        // Pointer command intent routing. Production wiring injects these services; non-command
        // mappings continue through the direct order path.
        private World? _commandIntentWorld;
        private InteractionContextStack? _interactionContextStack;
        private ControlSchemeRuntime? _controlSchemeRuntime;
        private CommandIntentProfileRegistry? _commandIntentProfiles;
        private CastDispatchProfileRegistry? _castDispatchProfiles;
        private ICommandActorExpander? _commandActorExpander;
        private EntityCollectionStore? _entityCollections;
        private ActiveActorCollectionOwnerProvider? _activeActorCollectionOwnerProvider;
        private CommandIntentTargetFactsProvider? _commandIntentTargetFactsProvider;
        private OrderIdentityAssigner? _orderIdentityAssigner;
        
        // Context
        private Entity _solePossessedRep;
        private int _playerId;
        private ActivationActorValidator? _activationActorValidator;
        private Entity _explicitActivationActor;
        private int _explicitActivationPlayerId;
        private bool _hasExplicitActivationContext;
        private int _lastSubmittedOrderId;
        private float _elapsedSeconds;
        private readonly int _commandIntentScratchCapacity;
        private readonly List<Entity> _collectionActorsScratch;

        private readonly struct RoutedOrderSubmission
        {
            public RoutedOrderSubmission(in Order order)
            {
                Order = order;
            }

            public Order Order { get; }
        }

        private readonly List<RoutedOrderSubmission> _routedOrdersScratch;
        private Entity[] _commandIntentActorsScratch;
        private Entity[] _commandIntentExpandedActorsScratch;
        private Entity[] _commandIntentExpansionSourcesScratch;
        private CommandIntentRoute[] _commandIntentExpandedRoutesScratch;
        private Entity[] _commandIntentRoutedActorsScratch;
        private CommandIntentRoute[] _commandIntentRoutesScratch;
        private CommandIntentRoute[] _commandIntentRoutedRoutesScratch;
        private Entity[] _commandIntentDispatchActorsScratch;
        private Order[] _commandIntentOrdersScratch;
        private readonly int[] _groupMoveTargetParticipantByOrderScratch;
        private readonly Entity[] _groupMoveTargetParticipantsScratch;
        private readonly WorldCmInt2[] _groupMoveTargetPositionsScratch;
        private readonly int[] _groupMoveTargetSlotByParticipantScratch;
        private readonly int[] _groupMoveTargetActorIndicesScratch;
        private readonly int[] _groupMoveTargetSlotIndicesScratch;
        private readonly Int128[] _groupMoveTargetActorForwardScratch;
        private readonly Int128[] _groupMoveTargetActorLateralScratch;
        private readonly Int128[] _groupMoveTargetSlotForwardScratch;
        private readonly Int128[] _groupMoveTargetSlotLateralScratch;

        // Aiming state (AimCast mode)
        private bool _isAiming;
        private string _aimingActionId = string.Empty;
        private InputOrderMapping? _aimingMapping;
        private InputOrderActivationContext _aimingContext;
        public InputOrderActivationResult LastActivationResult { get; private set; }
        
        // Held Start/End tracking
        private readonly Dictionary<string, HeldStartEndOrderTypeKeys> _heldStartEndOrderTypeKeys = new(StringComparer.Ordinal);
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
        
        public InputOrderMappingSystem(
            IInputActionReader input,
            InputOrderMappingConfig config,
            int commandIntentScratchCapacity = DefaultCommandIntentScratchCapacity)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            InputOrderMappingLoader.Validate(_config, "InputOrderMappingSystem config");
            if (commandIntentScratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commandIntentScratchCapacity),
                    commandIntentScratchCapacity,
                    "Command intent scratch capacity must be positive.");
            }

            _commandIntentScratchCapacity = commandIntentScratchCapacity;
            _collectionActorsScratch = new List<Entity>(commandIntentScratchCapacity);
            _routedOrdersScratch = new List<RoutedOrderSubmission>(commandIntentScratchCapacity);
            _commandIntentActorsScratch = new Entity[commandIntentScratchCapacity];
            _commandIntentExpandedActorsScratch = new Entity[commandIntentScratchCapacity];
            _commandIntentExpansionSourcesScratch = new Entity[commandIntentScratchCapacity];
            _commandIntentExpandedRoutesScratch = new CommandIntentRoute[commandIntentScratchCapacity];
            _commandIntentRoutedActorsScratch = new Entity[commandIntentScratchCapacity];
            _commandIntentRoutesScratch = new CommandIntentRoute[commandIntentScratchCapacity];
            _commandIntentRoutedRoutesScratch = new CommandIntentRoute[commandIntentScratchCapacity];
            _commandIntentDispatchActorsScratch = new Entity[commandIntentScratchCapacity];
            _commandIntentOrdersScratch = new Order[commandIntentScratchCapacity];
            _groupMoveTargetParticipantByOrderScratch = new int[commandIntentScratchCapacity];
            _groupMoveTargetParticipantsScratch = new Entity[commandIntentScratchCapacity];
            _groupMoveTargetPositionsScratch = new WorldCmInt2[commandIntentScratchCapacity];
            _groupMoveTargetSlotByParticipantScratch = new int[commandIntentScratchCapacity];
            _groupMoveTargetActorIndicesScratch = new int[commandIntentScratchCapacity];
            _groupMoveTargetSlotIndicesScratch = new int[commandIntentScratchCapacity];
            _groupMoveTargetActorForwardScratch = new Int128[commandIntentScratchCapacity];
            _groupMoveTargetActorLateralScratch = new Int128[commandIntentScratchCapacity];
            _groupMoveTargetSlotForwardScratch = new Int128[commandIntentScratchCapacity];
            _groupMoveTargetSlotLateralScratch = new Int128[commandIntentScratchCapacity];
            
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
            CompileGroupMoveTargetLayoutOrderTypeIds();
        }
        public void SetGroundPositionProvider(GroundPositionProvider provider) => _groundPositionProvider = provider;
        public void SetActorProvider(ActorProvider provider) => _actorProvider = provider;
        public void SetActivationActorValidator(ActivationActorValidator validator) =>
            _activationActorValidator = validator ?? throw new ArgumentNullException(nameof(validator));
        public void SetCollectionPrimaryEntityProvider(CollectionPrimaryEntityProvider provider) => _collectionPrimaryEntityProvider = provider;
        public void SetCollectionEntityListProvider(CollectionEntityListProvider provider) => _collectionEntityListProvider = provider;
        public void SetHoveredEntityProvider(HoveredEntityProvider provider) => _hoveredEntityProvider = provider;
        public void SetOrderSubmitHandler(OrderSubmitHandler handler) => _orderSubmitHandler = handler;
        public void SetOrderBatchSubmitHandler(OrderBatchSubmitHandler handler) => _orderBatchSubmitHandler = handler;
        public void SetOrderClusterBatchSubmitHandler(OrderClusterBatchSubmitHandler handler) =>
            _orderClusterBatchSubmitHandler = handler ?? throw new ArgumentNullException(nameof(handler));
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
        public void SetActorWorldPositionProvider(ActorWorldPositionProvider provider) =>
            _actorWorldPositionProvider = provider ?? throw new ArgumentNullException(nameof(provider));

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

        public void SetCommandActorExpander(ICommandActorExpander expander)
        {
            _commandActorExpander = expander ?? throw new ArgumentNullException(nameof(expander));
            if (expander.MaxExpandedActorsPerSource <= 0)
            {
                throw new InvalidOperationException(
                    "Command actor expansion requires MaxExpandedActorsPerSource > 0.");
            }

            if (expander.MaxExpandedActorCount < expander.MaxExpandedActorsPerSource)
            {
                throw new InvalidOperationException(
                    "Command actor expansion requires MaxExpandedActorCount >= MaxExpandedActorsPerSource.");
            }

            _commandIntentExpandedActorsScratch = new Entity[expander.MaxExpandedActorCount];
            _commandIntentExpansionSourcesScratch = new Entity[expander.MaxExpandedActorCount];
            _commandIntentExpandedRoutesScratch = new CommandIntentRoute[expander.MaxExpandedActorCount];
            if (_commandIntentOrdersScratch.Length < expander.MaxExpandedActorCount)
            {
                _commandIntentOrdersScratch = new Order[expander.MaxExpandedActorCount];
            }
        }

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
        
        public void SetSolePossessedActor(Entity entity, int playerId)
        {
            if (entity == Entity.Null)
            {
                throw new ArgumentException("InputOrderMappingSystem requires a non-null sole possessed actor entity.", nameof(entity));
            }

            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId), "InputOrderMappingSystem requires a positive player id.");
            }

            _solePossessedRep = entity;
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
                        HeldStartEndOrderTypeKeys heldKeys = ResolveHeldStartEndOrderTypeKeys(effectiveMapping);
                        // Emit .Start order
                        if (TryBuildOrderWithOrderTypeKey(effectiveMapping, heldActor, heldKeys.Start, out var startOrder))
                        {
                            SubmitOrder(effectiveMapping, in startOrder);
                        }
                        if (_input.ReleasedThisFrame(actionId) && !_input.IsDown(actionId))
                        {
                            if (TryBuildOrderWithOrderTypeKey(effectiveMapping, heldActor, heldKeys.End, out var endOrder))
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
            
            foreach (var entry in _orderedMappings)
            {
                string actionId = entry.ActionId;
                if (!_activeHeldStartEndActions.TryGetValue(actionId, out var state))
                {
                    continue;
                }

                if (_input.ReleasedThisFrame(actionId))
                {
                    if (TryBuildOrderWithOrderTypeKey(
                            state.Mapping,
                            state.Actor,
                            ResolveHeldStartEndOrderTypeKeys(state.Mapping).End,
                            out var endOrder))
                    {
                        SubmitOrder(state.Mapping, in endOrder);
                    }
                    _activeHeldStartEndActions.Remove(actionId);
                }
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
                    HandleSmartCast(mapping, activationActor);
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
        /// One activation pins <paramref name="actor"/> for target resolution and the submitted order.
        /// </summary>
        private void HandleSmartCast(InputOrderMapping mapping, Entity actor)
        {
            if (TryBuildOrderSmartCast(mapping, actor, out var order))
            {
                SubmitOrder(mapping, in order);
                return;
            }

            RecordRejectedActivation(
                actor,
                OrderSubmitResult.RejectedValidation);
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
            if (_isAiming &&
                string.Equals(_aimingActionId, actionId, StringComparison.Ordinal) &&
                (_aimingContext.Actor != context.Actor || _aimingContext.PlayerId != context.PlayerId))
            {
                RecordRejectedActivation(
                    context.Actor,
                    OrderSubmitResult.RejectedByRule);
                return;
            }

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
            LastActivationResult = InputOrderActivationResult.EnteredAiming(context.Actor);
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
                    Entity aimingActor = _aimingContext.Actor != Entity.Null
                        ? _aimingContext.Actor
                        : ResolvePrimaryActor(_aimingMapping);
                    if (TryBuildOrderSmartCast(_aimingMapping, aimingActor, out var order))
                    {
                        SubmitOrder(_aimingMapping, in order);
                    }
                    else
                    {
                        RecordRejectedActivation(aimingActor, OrderSubmitResult.RejectedValidation);
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
                // Build order using current cursor input; pin the aiming actor for the whole confirm.
                Entity aimingActor = _aimingContext.Actor != Entity.Null
                    ? _aimingContext.Actor
                    : ResolvePrimaryActor(_aimingMapping);
                if (TryBuildOrderSmartCast(_aimingMapping, aimingActor, out var order))
                {
                    SubmitOrder(_aimingMapping, in order);
                }
                else
                {
                    RecordRejectedActivation(aimingActor, OrderSubmitResult.RejectedValidation);
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
        /// Build an order with an explicit order type key (the precomputed ".Start"/".End" variants
        /// for Held StartEnd mode, see <see cref="ResolveHeldStartEndOrderTypeKeys"/>) using a
        /// pinned actor captured when the held interaction began.
        /// </summary>
        private bool TryBuildOrderWithOrderTypeKey(InputOrderMapping mapping, Entity actor, string orderTypeKey, out Order order)
        {
            order = new Order();
            if (!HasExplicitSolePossessedActor()) return false;
            int orderTypeId = RequireOrderTypeId(mapping.ActionId, orderTypeKey);            var args = new OrderArgs();
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
                TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch, out _);
            }

            order.OrderTypeId = orderTypeId;
            order.PlayerId = CurrentActivationPlayerId;
            order.Actor = actor;
            order.Args = args;
            order.SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior);
            return true;
        }

        private int RequireOrderTypeId(InputOrderMapping mapping)
        {
            return RequireOrderTypeId(mapping.ActionId, mapping.OrderTypeKey);
        }

        private HeldStartEndOrderTypeKeys ResolveHeldStartEndOrderTypeKeys(InputOrderMapping mapping)
        {
            string orderTypeKey = mapping.OrderTypeKey ?? string.Empty;
            if (!_heldStartEndOrderTypeKeys.TryGetValue(orderTypeKey, out HeldStartEndOrderTypeKeys keys))
            {
                keys = new HeldStartEndOrderTypeKeys(orderTypeKey + ".Start", orderTypeKey + ".End");
                _heldStartEndOrderTypeKeys[orderTypeKey] = keys;
            }

            return keys;
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
            order = new Order();
            if (!HasExplicitSolePossessedActor()) return false;
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
        /// <paramref name="actor"/> is fixed for this activation (target resolution + order.Actor).
        /// </summary>
        private bool TryBuildOrderSmartCast(InputOrderMapping mapping, Entity actor, out Order order)
        {
            order = new Order();
            if (!HasExplicitSolePossessedActor() || actor == default)            {
                return false;
            }

            int orderTypeId = RequireOrderTypeId(mapping);

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
                    if (!TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch, out _) &&
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
            order = new Order();
            if (!HasExplicitSolePossessedActor()) return false;            
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
            order = new Order();
            if (!HasExplicitSolePossessedActor() || actor == default)            {
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
                        if (!TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch, out _))
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
                TryCaptureCollectionEntities(mapping.TargetCollectionKey, _collectionActorsScratch, out _);
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

            if (!TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch, out OrderSubmitResult collectionRejection))
            {
                RejectInputActivation(mapping, collectionRejection);
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

                if (_routedOrdersScratch.Count >= _commandIntentScratchCapacity)
                {
                    RejectInputActivation(mapping, OrderSubmitResult.RejectedAdmissionCapacity);
                    return;
                }

                AddFixed(
                    _routedOrdersScratch,
                    new RoutedOrderSubmission(in order),
                    nameof(_routedOrdersScratch));
            }

            if (_routedOrdersScratch.Count == 0)
            {
                RejectInputActivation(mapping, OrderSubmitResult.RejectedByRule);
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

            if (_routedOrdersScratch.Count > 1 && _orderBatchSubmitHandler == null)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' actorOrderRouting produced {_routedOrdersScratch.Count} orders, but no atomic batch submit handler is configured.");
            }

            EnsureOrderScratch(ref _commandIntentOrdersScratch, _routedOrdersScratch.Count);
            for (int i = 0; i < _routedOrdersScratch.Count; i++)
            {
                _commandIntentOrdersScratch[i] = _routedOrdersScratch[i].Order;
            }

            Span<Order> orders = _commandIntentOrdersScratch.AsSpan(0, _routedOrdersScratch.Count);
            if (!TryApplyGroupMoveTargetLayout(mapping, orders))
            {
                RejectInputActivation(mapping, OrderSubmitResult.RejectedValidation);
                return;
            }

            if (orders.Length == 1)
            {
                SubmitAuthorizedToHandler(in orders[0]);
            }
            else
            {
                SubmitAtomicOrderBatch(
                    mapping,
                    orders,
                    "actorOrderRouting");
            }
        }

        private OrderSubmitResult SubmitCommandIntentOrder(InputOrderMapping mapping)
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
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedByRule);
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

            if (!HasExplicitSolePossessedActor())
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedInvalidActor);
            }

            if (_groundPositionProvider == null || !_groundPositionProvider(out Vector3 groundWorldCm))
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedValidation);
            }

            Entity actorCollectionOwner = ResolveActiveActorCollectionOwner();
            if (actorCollectionOwner == Entity.Null)
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedInvalidActor);
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
                    return RejectCommandIntent(mapping, OrderSubmitResult.RejectedInvalidActor);
                }

                if (!TryEnsureCommandIntentScratch(handle))
                {
                    return RejectCommandIntent(mapping, OrderSubmitResult.RejectedAdmissionCapacity);
                }
                actorCount = _entityCollections.CopyEntities(handle, 0, _commandIntentActorsScratch);
            }
            if (actorCount <= 0)
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedInvalidActor);
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
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedByRule);
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
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedValidation);
            }

            if (routing.Sequential && dispatchCount > 1)
            {
                throw new InvalidOperationException(
                    "Command intent dispatch profile returned multiple actors for a sequential router; sequential dispatch must select exactly one actor per trigger.");
            }

            int activationPlayerId = CurrentActivationPlayerId;
            if (!CanExpandDispatchedActors(dispatchCount))
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedAdmissionCapacity);
            }

            dispatchCount = ExpandDispatchedActors(
                routedActors,
                routedRoutes,
                _commandIntentDispatchActorsScratch.AsSpan(0, dispatchCount));
            if (dispatchCount <= 0)
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedInvalidActor);
            }

            Span<Entity> dispatchActors = _commandIntentExpandedActorsScratch.AsSpan(0, dispatchCount);
            Span<Entity> dispatchSources = _commandIntentExpansionSourcesScratch.AsSpan(0, dispatchCount);
            Span<CommandIntentRoute> dispatchRoutes = _commandIntentExpandedRoutesScratch.AsSpan(0, dispatchCount);
            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                if (!TryAuthorizeActor(dispatchActors[dispatchIndex], activationPlayerId))
                {
                    return OrderSubmitResult.RejectedInvalidActor;
                }
            }

            if (_commandActorExpander != null)
            {
                if (_orderClusterBatchSubmitHandler == null)
                {
                    throw new InvalidOperationException(
                        "Command actor expansion requires an atomic clustered batch submit handler.");
                }

                EnsureOrderScratch(ref _commandIntentOrdersScratch, dispatchCount);
                for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
                {
                    CommandIntentRoute route = dispatchRoutes[dispatchIndex];
                    _commandIntentOrdersScratch[dispatchIndex] = BuildCommandIntentOrder(
                        mapping,
                        dispatchActors[dispatchIndex],
                        dispatchSources[dispatchIndex],
                        in route,
                        in targetFacts,
                        groundWorldCm);
                }

                Span<Order> clusteredOrders = _commandIntentOrdersScratch.AsSpan(0, dispatchCount);
                if (!TryApplyGroupMoveTargetLayout(mapping, clusteredOrders))
                {
                    return RejectCommandIntent(mapping, OrderSubmitResult.RejectedValidation);
                }

                OrderSubmitResult result = SubmitClusteredOrderBatch(
                    mapping,
                    clusteredOrders,
                    "command intent clustered fan-out");
                return result;
            }

            EnsureOrderScratch(ref _commandIntentOrdersScratch, dispatchCount);
            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                Entity dispatchActor = dispatchActors[dispatchIndex];
                CommandIntentRoute route = dispatchRoutes[dispatchIndex];
                _commandIntentOrdersScratch[dispatchIndex] = BuildCommandIntentOrder(
                    mapping,
                    dispatchActor,
                    Entity.Null,
                    in route,
                    in targetFacts,
                    groundWorldCm);
            }

            Span<Order> dispatchOrders = _commandIntentOrdersScratch.AsSpan(0, dispatchCount);
            if (!TryApplyGroupMoveTargetLayout(mapping, dispatchOrders))
            {
                return RejectCommandIntent(mapping, OrderSubmitResult.RejectedValidation);
            }

            if (routing.SharedOrderId && dispatchCount > 1)
            {
                OrderSubmitResult result = SubmitAtomicOrderBatch(
                    mapping,
                    dispatchOrders,
                    "command intent shared fan-out");
                return result;
            }

            int sharedOrderId = 0;
            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                Order order = dispatchOrders[dispatchIndex];

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

                OrderSubmitResult result = SubmitAuthorizedToHandler(in order);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    return result;
                }
            }
            return OrderSubmitResult.Activated;
        }

        private Order BuildCommandIntentOrder(
            InputOrderMapping mapping,
            Entity actor,
            Entity commandSource,
            in CommandIntentRoute route,
            in CommandIntentTargetFacts targetFacts,
            Vector3 groundWorldCm)
        {
            var args = new OrderArgs();
            ApplyArgsTemplate(ref args, mapping.ArgsTemplate);
            Entity target = Entity.Null;

            switch (route.TargetShape)
            {
                case CommandIntentTargetShape.None:
                    break;

                case CommandIntentTargetShape.WorldPositionCm:
                    SetSingleWorldPosition(ref args, groundWorldCm);
                    break;

                case CommandIntentTargetShape.Entity:
                    target = RequireCommandIntentEntityTarget(in route, in targetFacts);
                    break;

                case CommandIntentTargetShape.WorldPositionAndEntity:
                    SetSingleWorldPosition(ref args, groundWorldCm);
                    target = RequireCommandIntentEntityTarget(in route, in targetFacts);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Command intent route for order type {route.OrderTypeId} has unsupported target shape '{route.TargetShape}'.");
            }

            return new Order
            {
                OrderTypeId = route.OrderTypeId,
                PlayerId = CurrentActivationPlayerId,
                Actor = actor,
                CommandSource = commandSource,
                Target = target,
                Args = args,
                SubmitMode = DetermineSubmitMode(mapping.ModifierBehavior),
            };
        }

        private static void SetSingleWorldPosition(ref OrderArgs args, Vector3 worldCm)
        {
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = worldCm;
        }

        private static Entity RequireCommandIntentEntityTarget(
            in CommandIntentRoute route,
            in CommandIntentTargetFacts targetFacts)
        {
            if (!targetFacts.HasEntity || targetFacts.Target == Entity.Null || targetFacts.Target == default)
            {
                throw new InvalidOperationException(
                    $"Command intent route for order type {route.OrderTypeId} requires an entity target, but the frozen target facts contain only a ground hit.");
            }

            return targetFacts.Target;
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

            if (_solePossessedRep != Entity.Null)
            {
                return _solePossessedRep;
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

        private bool TryEnsureCommandIntentScratch(EntityCollectionHandle handle)
        {
            if (_entityCollections == null)
            {
                return true;
            }

            if (!_entityCollections.TryGetView(handle, out EntityCollectionView view))
            {
                throw new InvalidOperationException("Command intent routing received an invalid active collection handle.");
            }

            if (view.Count > _commandIntentScratchCapacity)
            {
                return false;
            }

            EnsureEntityScratch(ref _commandIntentActorsScratch, view.Count);
            EnsureRouteScratch(ref _commandIntentRoutesScratch, view.Count);
            EnsureEntityScratch(ref _commandIntentDispatchActorsScratch, view.Count);
            return true;
        }

        private OrderSubmitResult RejectCommandIntent(InputOrderMapping mapping, OrderSubmitResult result) =>
            RejectInputActivation(mapping, result);

        private OrderSubmitResult RejectInputActivation(InputOrderMapping mapping, OrderSubmitResult result)
        {
            Entity actor = _hasExplicitActivationContext
                ? _explicitActivationActor
                : _isAiming && _aimingContext.Actor != Entity.Null
                    ? _aimingContext.Actor
                    : _solePossessedRep;
            RecordRejectedActivation(actor, result);
            return result;
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

        private int ExpandDispatchedActors(
            ReadOnlySpan<Entity> routedActors,
            ReadOnlySpan<CommandIntentRoute> routedRoutes,
            ReadOnlySpan<Entity> dispatchedActors)
        {
            int perSourceCapacity = _commandActorExpander?.MaxExpandedActorsPerSource ?? 1;
            int required = checked(dispatchedActors.Length * perSourceCapacity);
            if (_commandActorExpander != null && required > _commandActorExpander.MaxExpandedActorCount)
            {
                throw new InvalidOperationException(
                    $"Command actor expansion requires capacity {required}, exceeding declared batch capacity {_commandActorExpander.MaxExpandedActorCount}.");
            }

            EnsureEntityScratch(ref _commandIntentExpandedActorsScratch, required);
            EnsureEntityScratch(ref _commandIntentExpansionSourcesScratch, required);
            EnsureRouteScratch(ref _commandIntentExpandedRoutesScratch, required);
            int written = 0;
            for (int sourceIndex = 0; sourceIndex < dispatchedActors.Length; sourceIndex++)
            {
                Entity source = dispatchedActors[sourceIndex];
                int routedIndex = IndexOfEntity(routedActors, source);
                if (routedIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Cast dispatch returned actor '{source}' that was not present in the routed actor group.");
                }

                int expanded;
                if (_commandActorExpander == null)
                {
                    _commandIntentExpandedActorsScratch[written] = source;
                    expanded = 1;
                }
                else
                {
                    expanded = _commandActorExpander.Expand(
                        source,
                        _commandIntentExpandedActorsScratch.AsSpan(written, perSourceCapacity));
                }

                if (expanded < 0 || expanded > perSourceCapacity)
                {
                    throw new InvalidOperationException(
                        $"Command actor expander returned {expanded}, outside its declared per-source capacity {perSourceCapacity}.");
                }

                for (int i = 0; i < expanded; i++)
                {
                    Entity actor = _commandIntentExpandedActorsScratch[written + i];
                    if (actor == Entity.Null)
                    {
                        throw new InvalidOperationException("Command actor expansion produced Entity.Null.");
                    }

                    if (IndexOfEntity(_commandIntentExpandedActorsScratch.AsSpan(0, written + i), actor) >= 0)
                    {
                        throw new InvalidOperationException(
                            $"Command actor expansion produced duplicate actor '{actor}'.");
                    }

                    _commandIntentExpansionSourcesScratch[written + i] = source;
                    _commandIntentExpandedRoutesScratch[written + i] = routedRoutes[routedIndex];
                }

                written += expanded;
            }

            return written;
        }

        private bool CanExpandDispatchedActors(int dispatchCount)
        {
            int perSourceCapacity = _commandActorExpander?.MaxExpandedActorsPerSource ?? 1;
            int required = checked(dispatchCount * perSourceCapacity);
            return required <= _commandIntentScratchCapacity &&
                   (_commandActorExpander == null || required <= _commandActorExpander.MaxExpandedActorCount);
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

            throw new InvalidOperationException(
                $"INPUT.ORDER_MAPPING.ERR.EntityScratchCapacityExceeded: required={required}, capacity={scratch.Length}.");
        }

        private static void EnsureRouteScratch(ref CommandIntentRoute[] scratch, int required)
        {
            if (scratch.Length >= required)
            {
                return;
            }

            throw new InvalidOperationException(
                $"INPUT.ORDER_MAPPING.ERR.RouteScratchCapacityExceeded: required={required}, capacity={scratch.Length}.");
        }

        private static void EnsureOrderScratch(ref Order[] scratch, int required)
        {
            if (scratch.Length >= required)
            {
                return;
            }

            throw new InvalidOperationException(
                $"INPUT.ORDER_MAPPING.ERR.OrderScratchCapacityExceeded: required={required}, capacity={scratch.Length}.");
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
                TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch, out _))
            {
                return _collectionActorsScratch[0];
            }

            return _solePossessedRep;
        }

        private int CurrentActivationPlayerId => _hasExplicitActivationContext
            ? _explicitActivationPlayerId
            : _isAiming && _aimingContext.PlayerId > 0
                ? _aimingContext.PlayerId
                : _playerId;

        private bool HasExplicitSolePossessedActor()
        {
            if (_hasExplicitActivationContext &&
                _explicitActivationActor != Entity.Null &&
                _explicitActivationPlayerId > 0)
            {
                return true;
            }

            if (_isAiming &&
                _aimingContext.Actor != Entity.Null &&
                _aimingContext.PlayerId > 0)
            {
                return true;
            }

            return _playerId > 0 && _solePossessedRep != Entity.Null;
        }

        private bool TryCaptureCollectionEntities(
            string collectionKey,
            List<Entity> entities,
            out OrderSubmitResult rejection)
        {
            entities.Clear();
            rejection = OrderSubmitResult.RejectedInvalidActor;
            if (_collectionEntityListProvider == null)
            {
                return false;
            }

            bool captured = _collectionEntityListProvider(
                collectionKey,
                entities,
                _commandIntentScratchCapacity,
                out rejection);
            if (!captured || entities.Count <= 0)
            {
                entities.Clear();
                if (OrderSubmitResultSemantics.IsAccepted(rejection))
                {
                    rejection = OrderSubmitResult.RejectedInvalidActor;
                }

                return false;
            }

            if (entities.Count > _commandIntentScratchCapacity ||
                entities.Capacity > _commandIntentScratchCapacity)
            {
                entities.Clear();
                rejection = OrderSubmitResult.RejectedAdmissionCapacity;
                return false;
            }

            rejection = OrderSubmitResult.Activated;
            return true;
        }

        private void AddFixed<T>(List<T> list, T item, string name)
        {
            if (list.Count >= _commandIntentScratchCapacity ||
                list.Count >= list.Capacity)
            {
                throw new InvalidOperationException(
                    $"INPUT.ORDER_MAPPING.ERR.FixedListCapacityExceeded: list={name}, required={list.Count + 1}, capacity={Math.Min(_commandIntentScratchCapacity, list.Capacity)}.");
            }

            list.Add(item);
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
                !TryCaptureCollectionEntities(mapping.ActorCollectionKey, _collectionActorsScratch, out _) ||
                _collectionActorsScratch.Count <= 1)
            {
                return SubmitToHandler(in order);
            }

            if (_orderBatchSubmitHandler == null)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' targets actorCollectionKey '{mapping.ActorCollectionKey}' with {_collectionActorsScratch.Count} actors, but no atomic batch submit handler is configured.");
            }

            EnsureOrderScratch(ref _commandIntentOrdersScratch, _collectionActorsScratch.Count);
            int batchCount = 0;
            for (int i = 0; i < _collectionActorsScratch.Count; i++)
            {
                Entity actor = _collectionActorsScratch[i];
                if (actor == default)
                {
                    continue;
                }

                if (!TryAuthorizeActor(actor, order.PlayerId))
                {
                    return OrderSubmitResult.RejectedInvalidActor;
                }

                var cloned = order;
                cloned.Actor = actor;
                _commandIntentOrdersScratch[batchCount++] = cloned;
            }

            OrderSubmitResult aggregate = OrderSubmitResult.Queued;
            if (batchCount > 0)
            {
                Span<Order> orders = _commandIntentOrdersScratch.AsSpan(0, batchCount);
                if (!TryApplyGroupMoveTargetLayout(mapping, orders))
                {
                    return RejectInputActivation(mapping, OrderSubmitResult.RejectedValidation);
                }

                aggregate = SubmitAtomicOrderBatch(
                    mapping,
                    orders,
                    "actorCollectionKey fan-out");
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
                ? InputOrderActivationResult.Submitted(submitted.Actor, submitted.OrderId, submitted.Target)
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

        private OrderSubmitResult SubmitAtomicOrderBatch(InputOrderMapping mapping, Span<Order> orders, string context)
        {
            if (_orderBatchSubmitHandler == null)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' requires atomic batch submission for {context}, but no order batch submit handler is configured.");
            }

            OrderSubmitResult result = _orderBatchSubmitHandler(orders);
            return RecordBatchSubmissionResult(orders, result);
        }

        private OrderSubmitResult SubmitClusteredOrderBatch(InputOrderMapping mapping, Span<Order> orders, string context)
        {
            if (_orderClusterBatchSubmitHandler == null)
            {
                throw new InvalidOperationException(
                    $"Input mapping '{mapping.ActionId}' requires atomic clustered batch submission for {context}, but no order cluster batch submit handler is configured.");
            }

            OrderSubmitResult result = _orderClusterBatchSubmitHandler(orders);
            return RecordBatchSubmissionResult(orders, result);
        }

        private OrderSubmitResult RecordBatchSubmissionResult(ReadOnlySpan<Order> orders, OrderSubmitResult result)
        {
            Entity actor = orders.IsEmpty ? Entity.Null : orders[0].Actor;
            Entity target = ResolveSharedSubmittedTarget(orders);
            _lastSubmittedOrderId = orders.IsEmpty ? 0 : orders[0].OrderId;
            LastActivationResult = OrderSubmitResultSemantics.IsAccepted(result)
                ? InputOrderActivationResult.Submitted(actor, _lastSubmittedOrderId, target)
                : InputOrderActivationResult.Rejected(actor, _lastSubmittedOrderId, result);

            return result;
        }

        private static Entity ResolveSharedSubmittedTarget(ReadOnlySpan<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return Entity.Null;
            }

            Entity target = orders[0].Target;
            for (int i = 1; i < orders.Length; i++)
            {
                if (orders[i].Target != target)
                {
                    return Entity.Null;
                }
            }

            return target;
        }

        private bool TryApplyGroupMoveTargetLayout(InputOrderMapping mapping, Span<Order> orders)
        {
            if (_config.GroupMoveTargetLayout.Mode == GroupMoveTargetLayoutMode.None || orders.Length <= 1)
            {
                return true;
            }

            if (orders.Length > _groupMoveTargetParticipantByOrderScratch.Length)
            {
                throw new InvalidOperationException(
                    $"Group move target layout requires capacity {orders.Length}, exceeding configured scratch capacity {_groupMoveTargetParticipantByOrderScratch.Length}.");
            }

            int participantCount = 0;
            bool hasAnchor = false;
            Vector3 anchorWorldCm = default;
            Entity previousCommandSource = Entity.Null;
            int previousCommandSourceParticipant = -1;
            for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
            {
                _groupMoveTargetParticipantByOrderScratch[orderIndex] = -1;
                ref readonly Order order = ref orders[orderIndex];
                Entity commandSource = order.CommandSource;
                bool continuesCommandSource = commandSource != Entity.Null && commandSource == previousCommandSource;
                if (commandSource != Entity.Null && !continuesCommandSource)
                {
                    previousCommandSource = commandSource;
                    previousCommandSourceParticipant = -1;
                }

                if (!CanApplyGroupMoveTargetLayout(mapping, in order))
                {
                    continue;
                }

                if (!hasAnchor)
                {
                    anchorWorldCm = order.Args.Spatial.WorldCm;
                    hasAnchor = true;
                }
                else if (order.Args.Spatial.WorldCm.X != anchorWorldCm.X ||
                         order.Args.Spatial.WorldCm.Z != anchorWorldCm.Z)
                {
                    return false;
                }

                int participantIndex;
                if (continuesCommandSource && previousCommandSourceParticipant >= 0)
                {
                    participantIndex = previousCommandSourceParticipant;
                }
                else
                {
                    participantIndex = participantCount++;
                    Entity participant = commandSource != Entity.Null ? commandSource : order.Actor;
                    if (participant == Entity.Null || participant == default)
                    {
                        return false;
                    }

                    _groupMoveTargetParticipantsScratch[participantIndex] = participant;
                    if (commandSource != Entity.Null)
                    {
                        previousCommandSourceParticipant = participantIndex;
                    }
                }

                _groupMoveTargetParticipantByOrderScratch[orderIndex] = participantIndex;
            }

            if (participantCount <= 1)
            {
                return true;
            }

            switch (_config.GroupMoveTargetLayout.Assignment)
            {
                case GroupMoveTargetAssignmentMode.ActorOrder:
                    for (int participantIndex = 0; participantIndex < participantCount; participantIndex++)
                    {
                        _groupMoveTargetSlotByParticipantScratch[participantIndex] = participantIndex;
                    }
                    break;

                case GroupMoveTargetAssignmentMode.PreserveRelative:
                    if (_actorWorldPositionProvider == null)
                    {
                        return false;
                    }

                    for (int participantIndex = 0; participantIndex < participantCount; participantIndex++)
                    {
                        if (!_actorWorldPositionProvider(
                                _groupMoveTargetParticipantsScratch[participantIndex],
                                out _groupMoveTargetPositionsScratch[participantIndex]))
                        {
                            return false;
                        }
                    }

                    if (!MoveTargetLayoutPlanner.TryComputePositionPreservingSlots(
                            _groupMoveTargetPositionsScratch.AsSpan(0, participantCount),
                            anchorWorldCm,
                            _config.GroupMoveTargetLayout.SpacingCm,
                            _groupMoveTargetSlotByParticipantScratch.AsSpan(0, participantCount),
                            _groupMoveTargetActorIndicesScratch.AsSpan(0, participantCount),
                            _groupMoveTargetSlotIndicesScratch.AsSpan(0, participantCount),
                            _groupMoveTargetActorForwardScratch.AsSpan(0, participantCount),
                            _groupMoveTargetActorLateralScratch.AsSpan(0, participantCount),
                            _groupMoveTargetSlotForwardScratch.AsSpan(0, participantCount),
                            _groupMoveTargetSlotLateralScratch.AsSpan(0, participantCount)))
                    {
                        return false;
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported group move target assignment '{_config.GroupMoveTargetLayout.Assignment}'.");
            }

            for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
            {
                int participantIndex = _groupMoveTargetParticipantByOrderScratch[orderIndex];
                if (participantIndex < 0)
                {
                    continue;
                }

                ref Order order = ref orders[orderIndex];
                order.Args.Spatial.WorldCm = MoveTargetLayoutPlanner.ComputeOffsetTarget(
                    order.Args.Spatial.WorldCm,
                    _groupMoveTargetSlotByParticipantScratch[participantIndex],
                    participantCount,
                    _config.GroupMoveTargetLayout.SpacingCm);
            }

            return true;
        }

        private bool CanApplyGroupMoveTargetLayout(InputOrderMapping mapping, in Order order)
        {
            return IsGroupMoveTargetLayoutOrderType(order.OrderTypeId) &&
                !mapping.IsSkillMapping &&
                _config.GroupMoveTargetLayout.Mode == GroupMoveTargetLayoutMode.Grid &&
                order.Args.Spatial.Kind == OrderSpatialKind.WorldCm &&
                order.Args.Spatial.Mode == OrderCollectionMode.Single;
        }

        private bool IsGroupMoveTargetLayoutOrderType(int orderTypeId)
        {
            if (_config.GroupMoveTargetLayout.Mode == GroupMoveTargetLayoutMode.None || orderTypeId <= 0)
            {
                return false;
            }

            for (int i = 0; i < _groupMoveTargetLayoutOrderTypeIds.Length; i++)
            {
                if (_groupMoveTargetLayoutOrderTypeIds[i] == orderTypeId)
                {
                    return true;
                }
            }

            return false;
        }

        private void CompileGroupMoveTargetLayoutOrderTypeIds()
        {
            List<string> keys = _config.GroupMoveTargetLayout.OrderTypeKeys;
            if (_config.GroupMoveTargetLayout.Mode == GroupMoveTargetLayoutMode.None ||
                keys == null ||
                keys.Count == 0)
            {
                _groupMoveTargetLayoutOrderTypeIds = Array.Empty<int>();
                return;
            }

            var orderTypeIds = new int[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                orderTypeIds[i] = _orderTypeKeyResolver!(keys[i]);
            }

            _groupMoveTargetLayoutOrderTypeIds = orderTypeIds;
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
                ModifierSubmitBehavior.PersistentQueueOnModifier => queueModifierHeld ? OrderSubmitMode.PersistentQueued : OrderSubmitMode.Immediate,
                ModifierSubmitBehavior.AlwaysImmediate => OrderSubmitMode.Immediate,
                ModifierSubmitBehavior.AlwaysQueued => OrderSubmitMode.Queued,
                _ => throw new InvalidOperationException($"Unsupported modifier submit behavior '{behavior}'.")
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

        public bool WouldEnterUiAiming(string actionId, Entity actor)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !_mappingsByActionId.TryGetValue(actionId, out var mapping))
            {
                return false;
            }

            var effectiveMapping = _userOverrides.TryGetValue(actionId, out var overrideMapping)
                ? overrideMapping
                : mapping;
            if (effectiveMapping.IsSkillMapping &&
                _skillMappingOverrideProvider != null &&
                actor != Entity.Null &&
                _skillMappingOverrideProvider(actor, effectiveMapping, out var overrideFromAbility))
            {
                effectiveMapping = overrideFromAbility;
            }

            if (!effectiveMapping.IsSkillMapping)
            {
                return false;
            }

            var effectiveMode = effectiveMapping.CastModeOverride ?? _config.InteractionMode;
            if (effectiveMode == InteractionModeType.SmartCastWithIndicator ||
                effectiveMode == InteractionModeType.PressReleaseAimCast)
            {
                effectiveMode = InteractionModeType.AimCast;
            }

            if (effectiveMode == InteractionModeType.TargetFirst)
            {
                return false;
            }

            if (effectiveMapping.TargetType == OrderTargetType.Vector)
            {
                return true;
            }

            return effectiveMode == InteractionModeType.AimCast;
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
                Entity heldActor = resolvedActor != Entity.Null ? resolvedActor : ResolvePrimaryActor(effectiveMapping);
                return RecordRejectedActivation(
                    heldActor,
                    OrderSubmitResult.RejectedByRule);
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
                    if (LastActivationResult.State == InputOrderActivationState.Rejected)
                    {
                        return LastActivationResult;
                    }

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
            return BuildActivationResult(order.Actor, order.Target, result);
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

        public InputOrderActivationResult RecordExternalActivationResult(in InputOrderActivationResult result)
        {
            _lastSubmittedOrderId = result.OrderId;
            LastActivationResult = result;
            return LastActivationResult;
        }

        private InputOrderActivationResult BuildActivationResult(
            Entity actor,
            Entity target,
            OrderSubmitResult result)
        {
            LastActivationResult = OrderSubmitResultSemantics.IsAccepted(result)
                ? InputOrderActivationResult.Submitted(actor, _lastSubmittedOrderId, target)
                : InputOrderActivationResult.Rejected(actor, _lastSubmittedOrderId, result);
            return LastActivationResult;
        }

        private InputOrderActivationResult RecordRejectedActivation(
            Entity actor,
            OrderSubmitResult result,
            int orderId = 0)
        {
            Console.WriteLine($"[PROBE] rejected activation: result={result} actor={actor} orderId={orderId} explicit={_hasExplicitActivationContext} aiming={_isAiming} playerId={_playerId} soleRep={_solePossessedRep}\n{Environment.StackTrace}");
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
                HeldStartEndOrderTypeKeys heldKeys = ResolveHeldStartEndOrderTypeKeys(mapping);
                RequireOrderTypeId(mapping.ActionId, heldKeys.Start);
                RequireOrderTypeId(mapping.ActionId, heldKeys.End);
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
