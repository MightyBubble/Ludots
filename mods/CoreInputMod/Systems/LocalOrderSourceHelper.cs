using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    public sealed class LocalOrderSourceHelper
    {
        public delegate bool OrderSubmissionFilter(in Order order);
        public delegate void OrderAcceptedHandler(in Order order);

        public const string LastGroundWorldDebugKey = "CoreInputMod.Debug.LastGroundWorldCm";
        public const string LastOrderDebugKey = "CoreInputMod.Debug.LastOrder";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly InputInteractionContextAccessor _context;
        private readonly IReadOnlyDictionary<string, int> _orderTypeIds;
        private readonly OrderTypeRegistry? _orderTypeRegistry;
        private readonly CompositeOrderPlanner? _planner;
        private KnowledgeCommandTargetGate? _commandTargetGate;

        public int CastAbilityOrderTypeId { get; }
        public int MoveToOrderTypeId { get; }
        public int StopOrderTypeId { get; }
        public OrderSubmissionFilter? BeforeOrderSubmit { get; set; }
        public OrderAcceptedHandler? AfterOrderAccepted { get; set; }

        public LocalOrderSourceHelper(World world, Dictionary<string, object> globals, OrderQueue orders)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _context = new InputInteractionContextAccessor(world, globals);
            if (globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                _orderTypeIds = config.Constants.OrderTypeIds;
                CastAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
                MoveToOrderTypeId = config.Constants.OrderTypeIds["moveTo"];
                StopOrderTypeId = config.Constants.OrderTypeIds["stop"];
            }
            else
            {
                _orderTypeIds = new Dictionary<string, int>();
            }

            if (globals.TryGetValue(CoreServiceKeys.AbilityDefinitionRegistry.Name, out var abilitiesObj) &&
                abilitiesObj is AbilityDefinitionRegistry abilities &&
                CastAbilityOrderTypeId > 0 &&
                MoveToOrderTypeId > 0)
            {
                _planner = new CompositeOrderPlanner(world, orders, abilities, CastAbilityOrderTypeId, MoveToOrderTypeId);
            }

            if (globals.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out var orderTypesObj) &&
                orderTypesObj is OrderTypeRegistry orderTypeRegistry)
            {
                _orderTypeRegistry = orderTypeRegistry;
            }
        }

        public InputOrderMappingSystem? TryCreateMapping(IModContext ctx)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
            {
                return null;
            }

            string uri = $"{ctx.ModId}:assets/Input/input_order_mappings.json";
            if (!ctx.VFS.TryResolveFullPath(uri, out var fullPath) || !File.Exists(fullPath))
            {
                ctx.Log($"[{ctx.ModId}] input_order_mappings.json not found, skipping local order mapping.");
                return null;
            }

            using var stream = File.OpenRead(fullPath);
            var config = InputOrderMappingLoader.LoadFromStream(stream);
            var mapping = new InputOrderMappingSystem(input, config);
            PlayerEntityLookup players = RequireService<PlayerEntityLookup>(CoreServiceKeys.PlayerEntityLookup.Name);
            var controlDomains = RequireService<Ludots.Core.Gameplay.Relationships.ControlDomainQuery>(
                CoreServiceKeys.ControlDomainQuery.Name);

            mapping.SetOrderTypeKeyResolver(key =>
            {
                if (_orderTypeRegistry != null && _orderTypeRegistry.TryGetId(key, out int registryOrderTypeId))
                {
                    return registryOrderTypeId;
                }

                if (_orderTypeIds.TryGetValue(key, out int configOrderTypeId) && configOrderTypeId > 0)
                {
                    return configOrderTypeId;
                }

                throw new InvalidOperationException(
                    $"[{ctx.ModId}] input_order_mappings.json references unknown orderTypeKey '{key}'.");
            });
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = default;
                if (!_context.TryGetGroundWorldCm(out var point))
                {
                    _globals[LastGroundWorldDebugKey] = "<none>";
                    return false;
                }

                _globals[LastGroundWorldDebugKey] = $"({point.X},{point.Y})";
                worldCm = new Vector3(point.X, 0f, point.Y);
                return true;
            });
            mapping.SetActorProvider((out Entity entity) =>
            {
                entity = TryGetLocalPlayerId(out int playerId)
                    ? _context.GetControlledActor(playerId)
                    : default;
                return _world.IsAlive(entity);
            });
            mapping.SetActivationActorValidator((actor, playerId) =>
                InputOrderActorAuthorization.IsAuthorized(
                    _world,
                    players,
                    controlDomains,
                    actor,
                    playerId));
            mapping.SetCollectionPrimaryEntityProvider(TryResolveCollectionPrimary);
            mapping.SetCollectionEntityListProvider(TryCopyCollectionEntities);
            RequireCommandTargetGate();
            mapping.SetHoveredEntityProvider(TryResolveHoveredCommandTarget);
            mapping.SetCommandIntentTargetFactsProvider(TryResolveCommandIntentTargetFacts);
            RequireConfigureCommandIntentRouting(mapping);
            var bindings = InteractionActionBindingsResolver.Require(_globals, nameof(LocalOrderSourceHelper));
            mapping.ConfirmActionId = bindings.ConfirmActionId;
            mapping.CancelActionId = bindings.CancelActionId;
            mapping.CommandActionId = bindings.CommandActionId;
            mapping.SetOrderIdentityAssigner((ref Order order) => _orders.EnsureOrderId(ref order));
            mapping.SetOrderSubmitHandler((in Order order) =>
            {
                _globals[LastOrderDebugKey] = DescribeOrder(in order);

                if (BeforeOrderSubmit != null && !BeforeOrderSubmit(in order))
                {
                    return OrderSubmitResult.RejectedByRule;
                }

                if (!_world.IsAlive(order.Actor))
                {
                    return OrderSubmitResult.RejectedInvalidActor;
                }

                OrderSubmitResult result = _planner != null
                    ? _planner.Submit(in order)
                    : _orders.Submit(in order);
                if (OrderSubmitResultSemantics.IsAccepted(result))
                {
                    AfterOrderAccepted?.Invoke(in order);
                }
                return result;
            });
            mapping.SetOrderBatchSubmitHandler((Span<Order> orders) =>
            {
                if (orders.IsEmpty)
                {
                    return OrderSubmitResult.Queued;
                }

                _globals[LastOrderDebugKey] = DescribeOrder(in orders[0]);
                for (int i = 0; i < orders.Length; i++)
                {
                    if (BeforeOrderSubmit != null && !BeforeOrderSubmit(in orders[i]))
                    {
                        return OrderSubmitResult.RejectedByRule;
                    }
                }

                OrderSubmitResult result = _planner != null
                    ? _planner.TrySubmitSharedBatch(orders)
                    : _orders.TryEnqueueSharedBatch(orders);
                if (OrderSubmitResultSemantics.IsAccepted(result))
                {
                    for (int i = 0; i < orders.Length; i++)
                    {
                        AfterOrderAccepted?.Invoke(in orders[i]);
                    }
                }

                return result;
            });
            mapping.SetOrderClusterBatchSubmitHandler((Span<Order> orders) =>
            {
                if (orders.IsEmpty)
                {
                    return OrderSubmitResult.Queued;
                }

                _globals[LastOrderDebugKey] = DescribeOrder(in orders[0]);
                for (int i = 0; i < orders.Length; i++)
                {
                    if (BeforeOrderSubmit != null && !BeforeOrderSubmit(in orders[i]))
                    {
                        return OrderSubmitResult.RejectedByRule;
                    }
                }

                OrderSubmitResult result = _planner != null
                    ? _planner.TrySubmitClusteredBatch(orders)
                    : _orders.TryEnqueueClusteredBatch(orders);
                if (OrderSubmitResultSemantics.IsAccepted(result))
                {
                    for (int i = 0; i < orders.Length; i++)
                    {
                        AfterOrderAccepted?.Invoke(in orders[i]);
                    }
                }

                return result;
            });
            if (TryCreateContextScoredResolver(out var contextResolver))
            {
                mapping.SetContextScoredProvider(contextResolver.TryResolve);
            }
            if (TryCreateAutoTargetResolver(out var autoTargetResolver))
            {
                mapping.SetAutoTargetProvider(autoTargetResolver.TryResolve);
                mapping.SetCursorTargetProvider(autoTargetResolver.TryResolveCursor);
            }
            if (TryCreateSkillMappingOverrideProvider(out var mappingOverrideProvider))
            {
                mapping.SetSkillMappingOverrideProvider(mappingOverrideProvider.TryResolve);
            }

            if (RequiresActorOrderRouting(config))
            {
                if (!_globals.TryGetValue(CoreServiceKeys.TagOps.Name, out var tagOpsObj) ||
                    tagOpsObj is not TagOps tagOps)
                {
                    throw new InvalidOperationException(
                        $"[{ctx.ModId}] input_order_mappings.json defines actorOrderRouting but TagOps is not registered.");
                }

                mapping.SetActorOrderRoutingResolver((Entity actor, ActorOrderRoutingSettings routing, out ActorOrderRoutingCandidate matchedCandidate) =>
                    ActorOrderRoutingMatcher.TryResolveCandidate(_world, tagOps, actor, routing.Candidates, out matchedCandidate));
            }

            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mapping;
            return mapping;
        }

        private void RequireConfigureCommandIntentRouting(InputOrderMappingSystem mapping)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InteractionContextStack.Name, out var stackObj) ||
                stackObj is not InteractionContextStack stack ||
                !_globals.TryGetValue(CoreServiceKeys.ControlSchemeRuntime.Name, out var schemeObj) ||
                schemeObj is not ControlSchemeRuntime schemes ||
                !_globals.TryGetValue(CoreServiceKeys.CommandIntentProfileRegistry.Name, out var intentsObj) ||
                intentsObj is not CommandIntentProfileRegistry intents ||
                !_globals.TryGetValue(CoreServiceKeys.CastDispatchProfileRegistry.Name, out var dispatchObj) ||
                dispatchObj is not CastDispatchProfileRegistry dispatch ||
                !_globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) ||
                collectionsObj is not EntityCollectionStore collections)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)} requires command intent routing services before input-order mappings install.");
            }

            mapping.SetCommandIntentRouting(
                _world,
                stack,
                schemes,
                intents,
                dispatch,
                collections,
                TryGetCommandSourceOwner);
        }

        public bool TryGetLocalPlayerId(out int playerId)
        {
            return _context.TryGetLocalPlayerId(out playerId);
        }

        public bool TrySetLocalPlayer(InputOrderMappingSystem mapping, Entity actor)
        {
            if (mapping == null ||
                actor == Entity.Null ||
                !_world.IsAlive(actor) ||
                !TryGetLocalPlayerId(out int playerId))
            {
                return false;
            }

            mapping.SetLocalPlayer(actor, playerId);
            return true;
        }

        public Entity GetControlledActor()
        {
            return TryGetLocalPlayerId(out int playerId)
                ? _context.GetControlledActor(playerId)
                : default;
        }

        private T RequireService<T>(string key) where T : class
        {
            if (!_globals.TryGetValue(key, out object? serviceObj) || serviceObj is not T service)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)} requires {key} before input-order mappings install.");
            }

            return service;
        }

        private bool TryGetCommandSourceOwner(out Entity owner)
        {
            owner = Entity.Null;
            if (_globals.TryGetValue(CoreServiceKeys.InteractionContextStack.Name, out object? stackObj) &&
                stackObj is InteractionContextStack stack &&
                stack.TryPeek(out InteractionContextFrame frame) &&
                HasEntityValue(frame.ContextEntity))
            {
                if (!_world.IsAlive(frame.ContextEntity))
                {
                    return false;
                }

                owner = frame.ContextEntity;
                return true;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                localObj is Entity local &&
                local != Entity.Null &&
                _world.IsAlive(local))
            {
                owner = local;
                return true;
            }

            return false;
        }

        private bool TryResolveCommandIntentTargetFacts(InputOrderMapping mapping, out CommandIntentTargetFacts facts)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            facts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
            if (!TryGetCommandSourceOwner(out Entity owner) ||
                !PointerInteractionSnapshotReader.TryRead(_globals, out PointerInteractionSnapshot pointer))
            {
                return false;
            }

            PointerActionSnapshot command = pointer.Command;
            Vector2 screenPoint = ResolveCommandPointer(mapping.Trigger, in command);
            Entity target = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                _world,
                _globals,
                owner,
                screenPoint,
                RequireCommandPickRadiusPixels());
            if (!_world.IsAlive(target))
            {
                return false;
            }

            facts = new CommandIntentTargetFacts(target, HasEntity: true);
            return true;
        }

        private float RequireCommandPickRadiusPixels()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CommandSourceAcquisitionConfig.Name, out object? configObj) ||
                configObj is not CommandSourceAcquisitionConfig config)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)} requires {CoreServiceKeys.CommandSourceAcquisitionConfig.Name} before command target facts can be resolved.");
            }

            return config.ClickPickRadiusPixels;
        }

        private static Vector2 ResolveCommandPointer(InputTriggerType trigger, in PointerActionSnapshot command)
        {
            return trigger switch
            {
                InputTriggerType.ReleasedThisFrame => command.ResolveReleasePointerOrCurrent(),
                InputTriggerType.Held => command.ResolveDownPointerOrCurrent(),
                _ => command.ResolvePressPointerOrCurrent()
            };
        }

        private bool TryResolveCollectionPrimary(string collectionKey, out Entity entity)
        {
            entity = Entity.Null;
            return TryGetCommandSourceOwner(out Entity owner) &&
                   _context.TryGetCollectionPrimary(owner, collectionKey, out entity);
        }

        private bool TryCopyCollectionEntities(string collectionKey, List<Entity> entities)
        {
            entities.Clear();
            return TryGetCommandSourceOwner(out Entity owner) &&
                   _context.TryCopyCollectionEntities(owner, collectionKey, entities);
        }

        private static bool HasEntityValue(Entity entity)
        {
            return entity.Id != 0 || entity.WorldId != 0 || entity.Version != 0;
        }

        private bool TryCreateContextScoredResolver(out ContextScoredOrderResolver resolver)
        {
            resolver = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.ContextGroupRegistry.Name, out var groupsObj) ||
                groupsObj is not ContextGroupRegistry contextGroups ||
                !_globals.TryGetValue(CoreServiceKeys.GraphProgramRegistry.Name, out var graphsObj) ||
                graphsObj is not Ludots.Core.GraphRuntime.GraphProgramRegistry graphPrograms ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialQueryService.Name, out var spatialObj) ||
                spatialObj is not Ludots.Core.Spatial.ISpatialQueryService spatialQueries)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.GasGraphRuntimeApi.Name, out var graphApiObj) ||
                graphApiObj is not GasGraphRuntimeApi graphApi)
            {
                throw new InvalidOperationException(
                    "Context-scored order resolution requires the engine-owned production GasGraphRuntimeApi.");
            }

            KnowledgeCommandTargetGate candidateGate = RequireCommandTargetGate();
            resolver = new ContextScoredOrderResolver(_world, contextGroups, graphPrograms, spatialQueries, graphApi, candidateGate.CanTarget);
            return true;
        }

        private bool TryCreateSkillMappingOverrideProvider(out SkillMappingOverrideResolver resolver)
        {
            resolver = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.AbilityDefinitionRegistry.Name, out var abilitiesObj) ||
                abilitiesObj is not AbilityDefinitionRegistry abilityDefinitions)
            {
                return false;
            }

            resolver = new SkillMappingOverrideResolver(_world, abilityDefinitions);
            return true;
        }

        private bool TryCreateAutoTargetResolver(out AutoTargetResolver resolver)
        {
            resolver = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.SpatialQueryService.Name, out var spatialObj) ||
                spatialObj is not ISpatialQueryService spatialQueries)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ControlDomainQuery.Name, out var domainsObj) ||
                domainsObj is not ControlDomainQuery controlDomains)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)} requires {CoreServiceKeys.ControlDomainQuery.Name} before auto-target routing can resolve knowledge-gated command targets.");
            }

            resolver = new AutoTargetResolver(_world, spatialQueries, controlDomains, RequireCommandTargetGate().CanTarget);
            return true;
        }

        private bool TryResolveHoveredCommandTarget(out Entity entity)
        {
            entity = Entity.Null;
            if (!TryGetCommandSourceOwner(out Entity viewer) ||
                !_context.TryGetHoveredEntity(viewer, out Entity candidate))
            {
                return false;
            }

            if (!RequireCommandTargetGate().CanTarget(viewer, candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
        }

        private KnowledgeCommandTargetGate RequireCommandTargetGate()
        {
            if (_commandTargetGate != null)
            {
                return _commandTargetGate;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.KnowledgeProjectionResolver.Name, out var knowledgeResolverObj) ||
                knowledgeResolverObj is not KnowledgeProjectionResolver knowledgeResolver)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)}: KnowledgeProjectionResolver is not registered in globals; " +
                    "command target gating cannot run without it.");
            }

            if (!_globals.TryGetValue(CoreServiceKeys.Clock.Name, out var clockObj) ||
                clockObj is not IClock clock)
            {
                throw new InvalidOperationException(
                    $"{nameof(LocalOrderSourceHelper)}: Clock is not registered in globals; " +
                    "command target gating cannot run without it.");
            }

            _commandTargetGate = new KnowledgeCommandTargetGate(_world, knowledgeResolver, clock);
            return _commandTargetGate;
        }

        private static string DescribeOrder(in Order order)
        {
            string target = order.Target == Entity.Null
                ? "<none>"
                : $"{order.Target.Id}:{order.Target.WorldId}:{order.Target.Version}";
            string spatial = order.Args.Spatial.Mode switch
            {
                OrderCollectionMode.None => "<none>",
                OrderCollectionMode.Single => $"single({order.Args.Spatial.WorldCm.X:0.##},{order.Args.Spatial.WorldCm.Z:0.##})",
                _ => $"list(count:{order.Args.Spatial.PointCount})"
            };

            return $"type:{order.OrderTypeId},player:{order.PlayerId},actor:{order.Actor.Id}:{order.Actor.WorldId}:{order.Actor.Version},target:{target},slot:{order.Args.I0},spatial:{spatial},submit:{order.SubmitMode}";
        }

        public sealed class SkillMappingOverrideResolver
        {
            private readonly World _world;
            private readonly AbilityDefinitionRegistry _abilityDefinitions;
            private readonly Dictionary<SkillMappingOverrideCacheKey, InputOrderMapping> _cache = new(64);

            public SkillMappingOverrideResolver(World world, AbilityDefinitionRegistry abilityDefinitions)
            {
                _world = world;
                _abilityDefinitions = abilityDefinitions;
            }

            public bool TryResolve(Entity actor, InputOrderMapping mapping, out InputOrderMapping overrideMapping)
            {
                overrideMapping = default!;
                if (!_world.IsAlive(actor) ||
                    !mapping.IsSkillMapping ||
                    !mapping.ArgsTemplate.I0.HasValue ||
                    !_world.Has<AbilityStateBuffer>(actor))
                {
                    return false;
                }

                int slotIndex = mapping.ArgsTemplate.I0.Value;
                ref var abilities = ref _world.Get<AbilityStateBuffer>(actor);
                if ((uint)slotIndex >= (uint)abilities.Count)
                {
                    return false;
                }

                bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
                AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
                bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(actor);
                ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(actor) : default;
                bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlots = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
                AbilitySlotState slot = AbilitySlotResolver.Resolve(
                    in abilities,
                    in formSlots,
                    hasForm,
                    in itemGrantedSlots,
                    hasItemGranted,
                    in grantedSlots,
                    hasGranted,
                    slotIndex);
                if (slot.AbilityId <= 0 ||
                    !_abilityDefinitions.TryGet(slot.AbilityId, out var abilityDefinition) ||
                    !abilityDefinition.HasInputBindingOverride)
                {
                    return false;
                }

                var cacheKey = new SkillMappingOverrideCacheKey(mapping, slot.AbilityId);
                if (_cache.TryGetValue(cacheKey, out overrideMapping))
                {
                    return true;
                }

                overrideMapping = mapping.Clone();
                ref readonly var inputOverride = ref abilityDefinition.InputBindingOverride;
                if (inputOverride.HasTrigger)
                {
                    overrideMapping.Trigger = inputOverride.Trigger;
                }

                if (inputOverride.HasHeldPolicy)
                {
                    overrideMapping.HeldPolicy = inputOverride.HeldPolicy;
                }

                if (inputOverride.HasCastModeOverride)
                {
                    overrideMapping.CastModeOverride = inputOverride.CastModeOverride;
                }

                if (inputOverride.HasAutoTargetPolicy)
                {
                    overrideMapping.AutoTargetPolicy = inputOverride.AutoTargetPolicy;
                }

                if (inputOverride.HasAutoTargetRangeCm)
                {
                    overrideMapping.AutoTargetRangeCm = inputOverride.AutoTargetRangeCm;
                }

                _cache.Add(cacheKey, overrideMapping);
                return true;
            }

            private readonly record struct SkillMappingOverrideCacheKey(
                InputOrderMapping Mapping,
                int AbilityId);
        }

        private sealed class AutoTargetResolver
        {
            private readonly World _world;
            private readonly ISpatialQueryService _spatialQueries;
            private readonly ControlDomainQuery _controlDomains;
            private readonly CommandIntentTargetGate _targetGate;

            public AutoTargetResolver(
                World world,
                ISpatialQueryService spatialQueries,
                ControlDomainQuery controlDomains,
                CommandIntentTargetGate targetGate)
            {
                _world = world;
                _spatialQueries = spatialQueries;
                _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
                _targetGate = targetGate ?? throw new ArgumentNullException(nameof(targetGate));
            }

            public bool TryResolve(Entity actor, AutoTargetPolicy policy, int rangeCm, out Entity target)
            {
                if (!_world.IsAlive(actor) ||
                    policy == AutoTargetPolicy.None ||
                    rangeCm <= 0 ||
                    !_world.Has<WorldPositionCm>(actor))
                {
                    target = Entity.Null;
                    return false;
                }

                WorldCmInt2 center = _world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
                return TryResolveNear(actor, policy, center, rangeCm, out target);
            }

            public bool TryResolveCursor(Entity actor, AutoTargetPolicy policy, int rangeCm, Vector3 cursorWorldCm, out Entity target)
            {
                if (!_world.IsAlive(actor) ||
                    policy == AutoTargetPolicy.None ||
                    rangeCm <= 0)
                {
                    target = Entity.Null;
                    return false;
                }

                WorldCmInt2 center = new(
                    (int)MathF.Round(cursorWorldCm.X, MidpointRounding.AwayFromZero),
                    (int)MathF.Round(cursorWorldCm.Z, MidpointRounding.AwayFromZero));
                return TryResolveNear(actor, policy, center, rangeCm, out target);
            }

            private bool TryResolveNear(Entity actor, AutoTargetPolicy policy, in WorldCmInt2 center, int rangeCm, out Entity target)
            {
                target = Entity.Null;
                if (!_controlDomains.TryResolveControlDomain(actor, out Entity viewerRep))
                {
                    return false;
                }

                int actorTeamId = _world.TryGet(actor, out Team actorTeam) ? actorTeam.Id : 0;
                Span<Entity> candidates = stackalloc Entity[128];
                int candidateCount = _spatialQueries.QueryRadius(center, rangeCm, candidates).Count;
                if (candidateCount <= 0)
                {
                    return false;
                }

                int bestDistanceSq = int.MaxValue;
                for (int i = 0; i < candidateCount; i++)
                {
                    Entity candidate = candidates[i];
                    if (!_world.IsAlive(candidate) ||
                        candidate == actor ||
                        !_world.Has<WorldPositionCm>(candidate) ||
                        !_targetGate(viewerRep, candidate))
                    {
                        continue;
                    }

                    if (policy == AutoTargetPolicy.NearestEnemyInRange)
                    {
                        if (actorTeamId == 0 || !_world.TryGet(candidate, out Team candidateTeam))
                        {
                            continue;
                        }

                        if (!RelationshipFilterUtil.Passes(RelationshipFilter.Hostile, actorTeamId, candidateTeam.Id))
                        {
                            continue;
                        }
                    }

                    WorldCmInt2 candidatePos = _world.Get<WorldPositionCm>(candidate).ToWorldCmInt2();
                    int dx = candidatePos.X - center.X;
                    int dy = candidatePos.Y - center.Y;
                    int distanceSq = (dx * dx) + (dy * dy);
                    if (distanceSq >= bestDistanceSq)
                    {
                        continue;
                    }

                    bestDistanceSq = distanceSq;
                    target = candidate;
                }

                return target != Entity.Null;
            }
        }

        private static bool RequiresActorOrderRouting(InputOrderMappingConfig config)
        {
            for (int i = 0; i < config.Mappings.Count; i++)
            {
                InputOrderMapping mapping = config.Mappings[i];
                if (mapping?.ActorOrderRouting is { Candidates.Count: > 0 })
                {
                    return true;
                }
            }

            return false;
        }

    }
}
