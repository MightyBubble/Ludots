using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Navigation.Pathing;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Drains the graph-side command intent submission buffer (constitution §12): every entry
    /// pushed by the <c>SubmitCommandIntent</c> op during the previous trigger phase is routed
    /// here, in the order kernel's own system-group phase — the op never routes inline, so
    /// graph execution and order routing never reenter each other.
    /// <para>
    /// Routing reads only declared data: the acting rep's active interaction context chain is
    /// walked LIFO (op-activated instances newest-first, then the base mounted instance) for
    /// the most recent context declaring a command intent profile; actor sets ride the
    /// submission itself (v2). No engine-reserved key, no steady-state fallback: no declaring
    /// context on the chain is a named rejection, never a silent route elsewhere.
    /// </para>
    /// </summary>
    public sealed class CommandIntentBufferDrainSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly CommandIntentSubmissionBuffer _submissions;
        private readonly CommandIntentProfileRegistry _intentProfiles;
        private readonly CastDispatchProfileRegistry _dispatchProfiles;
        private readonly EntityCollectionStore _entityCollections;
        private readonly OrderQueue _orders;
        private readonly PlayerEntityLookup _players;
        private readonly ControlDomainQuery _controlDomains;
        private readonly Entity[] _actorScratch;
        private readonly CommandIntentRoute[] _routeScratch;
        private readonly Entity[] _dispatchScratch;
        private readonly Order[] _orderScratch;
        private readonly Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry? _orderTypes;
        private readonly Ludots.Core.Spatial.Eqs.EqsQueryRegistry? _eqsQueries;
        private readonly Ludots.Core.Gameplay.GAS.Orders.CompositeOrderPlanner? _engage;
        private readonly Func<IPathService?>? _pathServiceAccessor;
        private readonly Func<PathStore?>? _pathStoreAccessor;
        private readonly Ludots.Core.Spatial.Eqs.EqsItem[] _eqsScratch = new Ludots.Core.Spatial.Eqs.EqsItem[256];
        private readonly bool[] _eqsCandidateUsed = new bool[256];

        /// <summary>Diagnostic counters from the last drain; not world state, never persisted.</summary>
        public int LastDrainedCount;
        public int LastAcceptedCount;
        public int LastRejectedCount;
        public string? LastRejectionReason;

        public CommandIntentBufferDrainSystem(
            World world,
            CommandIntentSubmissionBuffer submissions,
            CommandIntentProfileRegistry intentProfiles,
            CastDispatchProfileRegistry dispatchProfiles,
            Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry? orderTypes,
            EntityCollectionStore entityCollections,
            OrderQueue orders,
            PlayerEntityLookup players,
            ControlDomainQuery controlDomains,
            int scratchCapacity,
            Ludots.Core.Spatial.Eqs.EqsQueryRegistry? eqsQueries = null,
            Ludots.Core.Gameplay.GAS.AbilityDefinitionRegistry? abilities = null,
            int castAbilityOrderTypeId = 0,
            int moveToOrderTypeId = 0,
            Func<IPathService?>? pathServiceAccessor = null,
            Func<PathStore?>? pathStoreAccessor = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
            _intentProfiles = intentProfiles ?? throw new ArgumentNullException(nameof(intentProfiles));
            _dispatchProfiles = dispatchProfiles ?? throw new ArgumentNullException(nameof(dispatchProfiles));
            _orderTypes = orderTypes;
            _entityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _controlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
            _pathServiceAccessor = pathServiceAccessor;
            _pathStoreAccessor = pathStoreAccessor;
            if (scratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scratchCapacity), "Command intent scratch capacity must be positive.");
            }

            _actorScratch = new Entity[scratchCapacity];
            _routeScratch = new CommandIntentRoute[scratchCapacity];
            _dispatchScratch = new Entity[scratchCapacity];
            _orderScratch = new Order[scratchCapacity];
            _eqsQueries = eqsQueries;
            _engage = eqsQueries != null && abilities != null && castAbilityOrderTypeId > 0 && moveToOrderTypeId > 0
                ? new Ludots.Core.Gameplay.GAS.Orders.CompositeOrderPlanner(world, orders, abilities, castAbilityOrderTypeId, moveToOrderTypeId)
                : null;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            int count = _submissions.Count;
            if (count == 0 && _submissions.CastCount == 0 && _submissions.EngageCount == 0)
            {
                return;
            }

            LastDrainedCount = count;
            LastAcceptedCount = 0;
            LastRejectedCount = 0;
            LastRejectionReason = null;
            for (int i = 0; i < count; i++)
            {
                if (TryRouteSubmission(_submissions[i]))
                {
                    LastAcceptedCount++;
                }
                else
                {
                    LastRejectedCount++;
                }
            }

            for (int i = 0; i < _submissions.CastCount; i++)
            {
                if (TryRouteCastSubmission(_submissions.Cast(i)))
                {
                    LastAcceptedCount++;
                }
                else
                {
                    LastRejectedCount++;
                }
            }

            for (int i = 0; i < _submissions.EngageCount; i++)
            {
                if (TryRouteEngageSubmission(_submissions.Engage(i)))
                {
                    LastAcceptedCount++;
                }
                else
                {
                    LastRejectedCount++;
                }
            }

            LastDrainedCount += _submissions.CastCount;
            LastDrainedCount += _submissions.EngageCount;
            _submissions.Clear();
        }

        /// <summary>
        /// Cast side of the §12 bridge: actors are the intent-carried member set (empty = the
        /// acting rep alone, v2); each authorized member receives one cast order with Args.I0 = slot. The cast
        /// order-type key resolves through the OrderTypeRegistry at drain time (cold path).
        /// </summary>
        private bool TryRouteCastSubmission(in CastIntentSubmission submission)
        {
            if (!_world.IsAlive(submission.Rep))
            {
                return Reject("cast acting rep is dead");
            }

            if (!_world.TryGet<PlayerOwner>(submission.Rep, out PlayerOwner owner) || owner.PlayerId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.CAST_INTENT.ERR.RepHasNoPlayerOwner: rep {submission.Rep} submitted a cast intent but carries no PlayerOwner.");
            }

            var orderTypes = _orderTypes
                ?? throw new InvalidOperationException(
                    "ORDER.CAST_INTENT.ERR.OrderTypeRegistryUnavailable: cast intent drain requires the OrderTypeRegistry.");
            string orderTypeKey = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(submission.OrderTypeKeyId);
            if (string.IsNullOrWhiteSpace(orderTypeKey) ||
                !orderTypes.TryGetId(orderTypeKey, out int orderTypeId) ||
                orderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.CAST_INTENT.ERR.UnknownCastOrderType: SubmitCast references order type key '{orderTypeKey}' which is not registered.");
            }

            int actorCount = ResolveActors(submission.Rep, submission.MemberOffset, submission.MemberCount);
            if (actorCount <= 0)
            {
                return Reject("cast: intent carries no actors — the graph must attach its actor set");
            }

            bool allAccepted = true;
            for (int i = 0; i < actorCount; i++)
            {
                Entity actor = _actorScratch[i];
                if (!InputOrderActorAuthorization.IsAuthorized(_world, _players, _controlDomains, actor, owner.PlayerId))
                {
                    LastRejectionReason = "cast: actor failed authorization";
                    allAccepted = false;
                    continue;
                }

                var args = new OrderArgs
                {
                    I0 = submission.Slot,
                };
                if (submission.HasGround)
                {
                    args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    args.Spatial.Mode = OrderCollectionMode.Single;
                    args.Spatial.WorldCm = new System.Numerics.Vector3(submission.GroundCm.X, 0f, submission.GroundCm.Y);
                }

                var order = new Order
                {
                    OrderTypeId = orderTypeId,
                    PlayerId = owner.PlayerId,
                    Actor = actor,
                    CommandSource = Entity.Null,
                    Target = submission.HasTarget && _world.IsAlive(submission.Target) ? submission.Target : Entity.Null,
                    Args = args,
                };
                OrderSubmitResult result = _orders.SubmitAssigned(ref order);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    LastRejectionReason = $"cast: order submit returned {result}";
                    allAccepted = false;
                }
            }

            return allAccepted;
        }

        /// <summary>
        /// Engage side of the §12 bridge: actors are the intent-carried member set (empty =
        /// the acting rep alone, v2); the profile's EQS query runs around the target
        /// and each authorized member gets a move-then-cast plan — moveTo its assigned ring
        /// point with the cast as an order continuation, plus a per-target slot claim so a
        /// later batch excludes occupied points.
        /// </summary>
        private bool TryRouteEngageSubmission(in Ludots.Core.Gameplay.GAS.Orders.EngageIntentSubmission submission)
        {
            if (!_world.IsAlive(submission.Rep))
            {
                return Reject("engage acting rep is dead");
            }

            if (!_world.TryGet<PlayerOwner>(submission.Rep, out PlayerOwner owner) || owner.PlayerId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.ENGAGE_INTENT.ERR.RepHasNoPlayerOwner: rep {submission.Rep} submitted an engage intent but carries no PlayerOwner.");
            }

            if (_engage == null)
            {
                return Reject("engage: composite order planner unavailable (missing EQS registry, abilities, or cast/move order type ids)");
            }

            if (_eqsQueries == null ||
                !_eqsQueries.TryGet(submission.ProfileKeyId, out var query))
            {
                throw new InvalidOperationException(
                    $"ORDER.ENGAGE_INTENT.ERR.UnknownProfile: engage profile key id {submission.ProfileKeyId} is not registered; declare the query in Spatial/eqs_queries.json.");
            }

            var orderTypes = _orderTypes
                ?? throw new InvalidOperationException(
                    "ORDER.ENGAGE_INTENT.ERR.OrderTypeRegistryUnavailable: engage intent drain requires the OrderTypeRegistry.");
            string orderTypeKey = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(submission.OrderTypeKeyId);
            if (string.IsNullOrWhiteSpace(orderTypeKey) ||
                !orderTypes.TryGetId(orderTypeKey, out int orderTypeId) ||
                orderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.ENGAGE_INTENT.ERR.UnknownCastOrderType: SubmitEngageBatch references order type key '{orderTypeKey}' which is not registered.");
            }

            if (!_world.IsAlive(submission.Target) ||
                !_world.Has<Ludots.Core.Components.WorldPositionCm>(submission.Target))
            {
                return Reject("engage: target is dead or carries no world position");
            }

            int actorCount = ResolveActors(submission.Rep, submission.MemberOffset, submission.MemberCount);
            if (actorCount <= 0)
            {
                return Reject("engage: intent carries no actors — the graph must attach its actor set");
            }

            var targetPos = _world.Get<Ludots.Core.Components.WorldPositionCm>(submission.Target).Value.ToWorldCmInt2();
            IPathService? pathService = _pathServiceAccessor?.Invoke();
            PathStore? pathStore = _pathStoreAccessor?.Invoke();

            if (!_world.Has<Ludots.Core.Gameplay.GAS.Components.EngageSlotClaims>(submission.Target))
            {
                _world.Add(submission.Target, new Ludots.Core.Gameplay.GAS.Components.EngageSlotClaims());
            }

            ref var claims = ref _world.Get<Ludots.Core.Gameplay.GAS.Components.EngageSlotClaims>(submission.Target);

            bool allAccepted = true;
            for (int i = 0; i < actorCount; i++)
            {
                Entity actor = _actorScratch[i];
                if (!InputOrderActorAuthorization.IsAuthorized(_world, _players, _controlDomains, actor, owner.PlayerId))
                {
                    LastRejectionReason = "engage: actor failed authorization";
                    allAccepted = false;
                    continue;
                }

                if (!_world.TryGet<Ludots.Core.Components.WorldPositionCm>(actor, out var actorPosition))
                {
                    LastRejectionReason = "engage: actor carries no world position";
                    allAccepted = false;
                    continue;
                }

                var eqsContext = new Ludots.Core.Spatial.Eqs.EqsContext(
                    targetPos,
                    _world,
                    pathService: pathService,
                    pathStore: pathStore,
                    sourceWorldCm: actorPosition.Value.ToWorldCmInt2(),
                    sourceEntity: actor);
                int candidateCount = query.Run(eqsContext, _eqsScratch);
                if (candidateCount <= 0)
                {
                    LastRejectionReason = "engage: EQS profile produced no candidates";
                    allAccepted = false;
                    continue;
                }

                if (candidateCount > _eqsScratch.Length)
                {
                    candidateCount = _eqsScratch.Length;
                }

                System.Array.Clear(_eqsCandidateUsed, 0, candidateCount);

                int best = -1;
                float bestScore = float.MinValue;
                for (int c = 0; c < candidateCount; c++)
                {
                    if (_eqsScratch[c].Filtered || _eqsCandidateUsed[c])
                    {
                        continue;
                    }

                    if (claims.IsClaimed(_world, _eqsScratch[c].Position.X, _eqsScratch[c].Position.Y, claimRadiusCm: 32))
                    {
                        continue;
                    }

                    if (_eqsScratch[c].Score > bestScore)
                    {
                        bestScore = _eqsScratch[c].Score;
                        best = c;
                    }
                }

                if (best < 0)
                {
                    LastRejectionReason = "engage: no unclaimed EQS candidate left for actor";
                    allAccepted = false;
                    continue;
                }

                _eqsCandidateUsed[best] = true;
                int pointX = _eqsScratch[best].Position.X;
                int pointY = _eqsScratch[best].Position.Y;

                var order = new Order
                {
                    OrderTypeId = orderTypeId,
                    PlayerId = owner.PlayerId,
                    Actor = actor,
                    CommandSource = Entity.Null,
                    Target = submission.Target,
                    Args = new OrderArgs { I0 = submission.Slot },
                };

                Ludots.Core.Gameplay.GAS.Orders.OrderContinuationStateInstaller.EnsureInstalled(_world, actor);

                var anchor = new Vector3(pointX, 0f, pointY);
                OrderSubmitResult result = _engage.SubmitWithMoveAnchor(order, anchor);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    LastRejectionReason = $"engage: planner returned {result}";
                    allAccepted = false;
                    continue;
                }

                claims.Claim(_world, pointX, pointY, actor);
            }

            return allAccepted;
        }

        private bool TryRouteSubmission(in CommandIntentSubmission submission)
        {
            if (!_world.IsAlive(submission.Rep))
            {
                return Reject("acting rep is dead");
            }

            if (!_world.TryGet<PlayerOwner>(submission.Rep, out PlayerOwner owner) || owner.PlayerId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.RepHasNoPlayerOwner: rep {submission.Rep} submitted a command intent but carries no PlayerOwner; " +
                    "map binding publishes player owners on player representatives.");
            }

            if (!TryResolveIntentProfileContext(submission.Rep, out int commandIntentProfileId))
            {
                return Reject("no active context declares commandIntentId");
            }

            Entity ownerEntity = submission.Rep;
            int actorCount = ResolveActors(submission.Rep, submission.MemberOffset, submission.MemberCount);
            if (actorCount <= 0)
            {
                return Reject("intent carries no actors — the graph must attach its actor set");
            }

            var facts = new CommandIntentTargetFacts(
                submission.HasTarget && _world.IsAlive(submission.Target) ? submission.Target : Entity.Null,
                submission.HasTarget && _world.IsAlive(submission.Target));
            var groundWorldCm = new Vector3(submission.GroundCm.X, 0f, submission.GroundCm.Y);

            Span<Entity> actors = _actorScratch.AsSpan(0, actorCount);
            Span<CommandIntentRoute> routes = _routeScratch.AsSpan(0, actorCount);
            _intentProfiles.RouteGroup(commandIntentProfileId, actors, ownerEntity, in facts, routes);

            int routedCount = 0;
            for (int i = 0; i < actorCount; i++)
            {
                if (routes[i].HasRoute && routes[i].OrderTypeId > 0)
                {
                    _actorScratch[routedCount] = actors[i];
                    _routeScratch[routedCount] = routes[i];
                    routedCount++;
                }
            }

            if (routedCount <= 0)
            {
                return Reject("no intent rule matched any actor");
            }

            if (!_world.TryGet<InteractionPref>(submission.Rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.RepHasNoInteractionPref: rep {submission.Rep} submitted a command intent but carries no InteractionPref; " +
                    "map binding seeds the player default from Input/interaction_prefs.json.");
            }

            int dispatchProfileId = pref.ResolveCastDispatchProfile(abilityTemplateId: 0);
            if (dispatchProfileId == 0)
            {
                throw new InvalidOperationException(
                    "ORDER.COMMAND_INTENT.ERR.NoDefaultDispatchProfile: the acting representative's InteractionPref declares no default cast dispatch profile.");
            }

            Span<Entity> routedActors = _actorScratch.AsSpan(0, routedCount);
            Span<Entity> dispatchSpan = _dispatchScratch.AsSpan(0, routedCount);
            int dispatchCount = _dispatchProfiles.SelectDispatchTargets(
                dispatchProfileId,
                routedActors,
                new CastDispatchContext(_world, groundWorldCm, GroupKey(submission.Rep)),
                dispatchSpan,
                out CastDispatchRouting routing);
            if (dispatchCount <= 0)
            {
                return Reject("dispatch profile selected no actor");
            }

            if (routing.Sequential && dispatchCount > 1)
            {
                throw new InvalidOperationException(
                    "ORDER.COMMAND_INTENT.ERR.SequentialDispatchMulti: command intent dispatch profile returned multiple actors for a sequential router.");
            }

            for (int i = 0; i < dispatchCount; i++)
            {
                if (!InputOrderActorAuthorization.IsAuthorized(_world, _players, _controlDomains, dispatchSpan[i], owner.PlayerId))
                {
                    return Reject("dispatched actor failed authorization");
                }
            }

            if (dispatchCount > _orderScratch.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.OrderScratchCapacityExceeded: dispatched {dispatchCount} actors, capacity {_orderScratch.Length}.");
            }

            for (int i = 0; i < dispatchCount; i++)
            {
                Entity dispatchedActor = dispatchSpan[i];
                int routeIndex = IndexOfRoute(routedActors, _routeScratch.AsSpan(0, routedCount), dispatchedActor);
                if (routeIndex < 0)
                {
                    return Reject("dispatched actor has no resolved route");
                }

                _orderScratch[i] = BuildOrder(dispatchedActor, owner.PlayerId, in _routeScratch[routeIndex], in facts, groundWorldCm);
            }

            Span<Order> dispatchOrders = _orderScratch.AsSpan(0, dispatchCount);
            if (routing.SharedOrderId && dispatchCount > 1)
            {
                OrderSubmitResult result = _orders.TryEnqueueSharedBatch(dispatchOrders);
                return OrderSubmitResultSemantics.IsAccepted(result)
                    ? true
                    : Reject($"shared batch submit returned {result}");
            }

            bool allAccepted = true;
            for (int i = 0; i < dispatchCount; i++)
            {
                Order order = dispatchOrders[i];
                OrderSubmitResult result = _orders.SubmitAssigned(ref order);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    LastRejectionReason = $"order submit returned {result}";
                    allAccepted = false;
                }
            }

            return allAccepted;
        }

        /// <summary>
        /// LIFO walk of the active context chain: op-activated instances newest-first, then the
        /// base mounted instance. Only contexts declaring an active collection key count; a dead
        /// carrier instance is skipped as an unresolved step (fail-closed per instance, the
        /// pre-reclaim window must not silently route through a dead carrier's collections).
        /// </summary>
        /// <summary>
        /// v2: intents carry their own actor set. The actor span is exactly what the submitting
        /// graph attached (constitution §12 — direct possession is a data shape expressed by a
        /// self-roster graph, never a kernel rule); an empty span routes nothing and each lane
        /// rejects by name.
        /// </summary>
        private int ResolveActors(Entity rep, int memberOffset, int memberCount)
        {
            if (memberCount == 0)
            {
                return 0;
            }

            if (memberCount > _actorScratch.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.INTENT.ERR.ActorScratchCapacityExceeded: intent carries {memberCount} actors, capacity {_actorScratch.Length}; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity.");
            }

            _submissions.Members(memberOffset, memberCount).CopyTo(_actorScratch);
            return memberCount;
        }

        private bool TryResolveIntentProfileContext(Entity rep, out int commandIntentProfileId)
        {
            if (_world.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances instances))
            {
                for (int i = instances.Count - 1; i >= 0; i--)
                {
                    if (instances[i].CommandIntentProfileId != 0)
                    {
                        commandIntentProfileId = instances[i].CommandIntentProfileId;
                        return true;
                    }
                }
            }

            if (_world.TryGet<InteractionContextInstance>(rep, out InteractionContextInstance baseInstance) &&
                baseInstance.CommandIntentProfileId != 0)
            {
                commandIntentProfileId = baseInstance.CommandIntentProfileId;
                return true;
            }

            commandIntentProfileId = 0;
            return false;
        }

        private static Order BuildOrder(
            Entity actor,
            int playerId,
            in CommandIntentRoute route,
            in CommandIntentTargetFacts facts,
            Vector3 groundWorldCm)
        {
            var args = new OrderArgs();
            Entity target = Entity.Null;
            switch (route.TargetShape)
            {
                case CommandIntentTargetShape.None:
                    break;
                case CommandIntentTargetShape.WorldPositionCm:
                    args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    args.Spatial.Mode = OrderCollectionMode.Single;
                    args.Spatial.WorldCm = groundWorldCm;
                    break;
                case CommandIntentTargetShape.Entity:
                    target = facts.Target;
                    break;
                case CommandIntentTargetShape.WorldPositionAndEntity:
                    args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    args.Spatial.Mode = OrderCollectionMode.Single;
                    args.Spatial.WorldCm = groundWorldCm;
                    target = facts.Target;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"ORDER.COMMAND_INTENT.ERR.UnsupportedTargetShape: route for order type {route.OrderTypeId} has unsupported target shape '{route.TargetShape}'.");
            }

            return new Order
            {
                OrderTypeId = route.OrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                CommandSource = Entity.Null,
                Target = target,
                Args = args,
            };
        }

        private static int IndexOfRoute(ReadOnlySpan<Entity> routedActors, ReadOnlySpan<CommandIntentRoute> routes, Entity actor)
        {
            for (int i = 0; i < routedActors.Length; i++)
            {
                if (routedActors[i] == actor)
                {
                    return i;
                }
            }

            return -1;
        }

        private static long GroupKey(Entity rep)
        {
            return rep == default
                ? 0
                : ((long)(uint)rep.Id << 32) | (uint)rep.Version;
        }

        private bool Reject(string reason)
        {
            LastRejectionReason = reason;
            return false;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
