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
    /// the most recent context declaring <see cref="InteractionContextInstance.ActiveCollectionKeyId"/>;
    /// its carrier entity owns the active collection and its declared command intent profile
    /// routes the members. No engine-reserved key, no steady-state fallback: no declaring
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
            int scratchCapacity)
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
            if (scratchCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scratchCapacity), "Command intent scratch capacity must be positive.");
            }

            _actorScratch = new Entity[scratchCapacity];
            _routeScratch = new CommandIntentRoute[scratchCapacity];
            _dispatchScratch = new Entity[scratchCapacity];
            _orderScratch = new Order[scratchCapacity];
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            int count = _submissions.Count;
            if (count == 0 && _submissions.CastCount == 0)
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

            LastDrainedCount += _submissions.CastCount;
            _submissions.Clear();
        }

        /// <summary>
        /// Cast side of the §12 bridge: actors are the same active-context-declared collection
        /// members; each authorized member receives one cast order with Args.I0 = slot. The cast
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

            if (!TryResolveRoutingContext(submission.Rep, out InteractionContextInstance routingContext))
            {
                return Reject("cast: no active context declares activeCollectionKey");
            }

            if (!_world.IsAlive(routingContext.ContextEntity))
            {
                return Reject("cast: routing context carrier is dead");
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

            if (!_entityCollections.TryGet(routingContext.ContextEntity, routingContext.ActiveCollectionKeyId, out EntityCollectionHandle handle) ||
                !_entityCollections.TryGetView(handle, out EntityCollectionView view))
            {
                return Reject("cast: active collection is not mounted");
            }

            if (view.Count > _actorScratch.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.CAST_INTENT.ERR.ActorScratchCapacityExceeded: active collection holds {view.Count} actors, capacity {_actorScratch.Length}.");
            }

            int actorCount = _entityCollections.CopyEntities(handle, 0, _actorScratch);
            if (actorCount <= 0)
            {
                return Reject("cast: active collection is empty");
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

            if (!TryResolveRoutingContext(submission.Rep, out InteractionContextInstance routingContext))
            {
                return Reject("no active context declares activeCollectionKey");
            }

            if (!_world.IsAlive(routingContext.ContextEntity))
            {
                return Reject("routing context carrier is dead");
            }

            if (routingContext.CommandIntentProfileId == 0)
            {
                return Reject("routing context declares no commandIntentId");
            }

            Entity ownerEntity = routingContext.ContextEntity;
            if (!_entityCollections.TryGet(ownerEntity, routingContext.ActiveCollectionKeyId, out EntityCollectionHandle handle))
            {
                return Reject("active collection is not mounted on the context carrier");
            }

            if (!_entityCollections.TryGetView(handle, out EntityCollectionView view))
            {
                return Reject("active collection view is unavailable");
            }

            if (view.Count > _actorScratch.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.COMMAND_INTENT.ERR.ActorScratchCapacityExceeded: active collection holds {view.Count} actors, capacity {_actorScratch.Length}; " +
                    "raise gasRuntimeCapacity.commandIntentScratchCapacity.");
            }

            int actorCount = _entityCollections.CopyEntities(handle, 0, _actorScratch);
            if (actorCount <= 0)
            {
                return Reject("active collection is empty");
            }

            var facts = new CommandIntentTargetFacts(
                submission.HasTarget && _world.IsAlive(submission.Target) ? submission.Target : Entity.Null,
                submission.HasTarget && _world.IsAlive(submission.Target));
            var groundWorldCm = new Vector3(submission.GroundCm.X, 0f, submission.GroundCm.Y);

            Span<Entity> actors = _actorScratch.AsSpan(0, actorCount);
            Span<CommandIntentRoute> routes = _routeScratch.AsSpan(0, actorCount);
            _intentProfiles.RouteGroup(routingContext.CommandIntentProfileId, actors, ownerEntity, in facts, routes);

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
                new CastDispatchContext(_world, groundWorldCm, GroupKey(routingContext)),
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
        private bool TryResolveRoutingContext(Entity rep, out InteractionContextInstance routingContext)
        {
            if (_world.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances instances))
            {
                for (int i = instances.Count - 1; i >= 0; i--)
                {
                    InteractionContextInstance candidate = instances[i];
                    if (candidate.ActiveCollectionKeyId != 0 && _world.IsAlive(candidate.ContextEntity))
                    {
                        routingContext = candidate;
                        return true;
                    }
                }
            }

            if (_world.TryGet<InteractionContextInstance>(rep, out InteractionContextInstance baseInstance) &&
                baseInstance.ActiveCollectionKeyId != 0 &&
                _world.IsAlive(baseInstance.ContextEntity))
            {
                routingContext = baseInstance;
                return true;
            }

            routingContext = default;
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

        private static long GroupKey(in InteractionContextInstance routingContext)
        {
            Entity carrier = routingContext.ContextEntity;
            return carrier == default
                ? 0
                : ((long)(uint)carrier.Id << 32) | (uint)carrier.Version;
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
