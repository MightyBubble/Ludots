using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
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
        public const string LastGroundWorldDebugKey = "CoreInputMod.Debug.LastGroundWorldCm";
        public const string LastOrderDebugKey = "CoreInputMod.Debug.LastOrder";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly InputInteractionContextAccessor _context;
        private readonly IReadOnlyDictionary<string, int> _orderTypeIds;
        private readonly OrderTypeRegistry? _orderTypeRegistry;
        private readonly CompositeOrderPlanner? _planner;

        public int CastAbilityOrderTypeId { get; }
        public int MoveToOrderTypeId { get; }
        public int StopOrderTypeId { get; }

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
            mapping.SetSelectedEntityProvider((string setKey, out Entity entity) => _context.TryGetSelectedEntity(setKey, out entity));
            mapping.SetSelectedContainerProvider((string setKey, out Entity container) => _context.TryGetSelectedContainer(setKey, out container));
            mapping.SetSelectedEntityListProvider((string setKey, List<Entity> entities) => _context.TryGetSelectedEntities(setKey, entities));
            mapping.SetHoveredEntityProvider(TryResolveHoveredCommandTarget);
            var bindings = InteractionActionBindingsResolver.Require(_globals, nameof(LocalOrderSourceHelper));
            mapping.ConfirmActionId = bindings.ConfirmActionId;
            mapping.CancelActionId = bindings.CancelActionId;
            mapping.CommandActionId = bindings.CommandActionId;
            mapping.SetOrderSubmitHandler((in Order order) =>
            {
                _globals[LastOrderDebugKey] = DescribeOrder(in order);

                if (_planner != null && _planner.TrySubmit(in order))
                {
                    return;
                }
                _orders.TryEnqueue(order);
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

        private bool TryCreateContextScoredResolver(out ContextScoredOrderResolver resolver)
        {
            resolver = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.ContextGroupRegistry.Name, out var groupsObj) ||
                groupsObj is not ContextGroupRegistry contextGroups ||
                !_globals.TryGetValue(CoreServiceKeys.GraphProgramRegistry.Name, out var graphsObj) ||
                graphsObj is not Ludots.Core.GraphRuntime.GraphProgramRegistry graphPrograms ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialQueryService.Name, out var spatialObj) ||
                spatialObj is not Ludots.Core.Spatial.ISpatialQueryService spatialQueries ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialCoordinateConverter.Name, out var coordsObj) ||
                coordsObj is not Ludots.Core.Spatial.ISpatialCoordinateConverter spatialCoords)
            {
                return false;
            }

            var graphApi = GasGraphRuntimeApi.CreateProduction(
                _world,
                spatialQueries,
                spatialCoords,
                eventBus: null,
                effectRequests: null,
                _globals);
            resolver = new ContextScoredOrderResolver(_world, contextGroups, graphPrograms, spatialQueries, graphApi);
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

            resolver = new AutoTargetResolver(_world, _globals, spatialQueries);
            return true;
        }

        private bool TryResolveHoveredCommandTarget(out Entity entity)
        {
            entity = Entity.Null;
            if (!_context.TryGetHoveredEntity(out Entity candidate))
            {
                return false;
            }

            Entity viewer = _context.TryGetSelectionOwner(out Entity owner) ? owner : Entity.Null;
            if (!SelectionEligibility.CanTargetCommand(
                    _world,
                    _globals,
                    viewer,
                    candidate,
                    KnowledgePositionAccess.Live))
            {
                return false;
            }

            entity = candidate;
            return true;
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

        private sealed class SkillMappingOverrideResolver
        {
            private readonly World _world;
            private readonly AbilityDefinitionRegistry _abilityDefinitions;

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
                bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlots = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
                AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in grantedSlots, hasGranted, slotIndex);
                if (slot.AbilityId <= 0 ||
                    !_abilityDefinitions.TryGet(slot.AbilityId, out var abilityDefinition) ||
                    !abilityDefinition.HasInputBindingOverride)
                {
                    return false;
                }

                overrideMapping = CloneMapping(mapping);
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

                return true;
            }

            private static InputOrderMapping CloneMapping(InputOrderMapping mapping)
            {
                return new InputOrderMapping
                {
                    ActionId = mapping.ActionId,
                    Trigger = mapping.Trigger,
                    DoubleTapWindowSeconds = mapping.DoubleTapWindowSeconds,
                    OrderTypeKey = mapping.OrderTypeKey,
                    ArgsTemplate = new OrderArgsTemplate
                    {
                        I0 = mapping.ArgsTemplate.I0,
                        I1 = mapping.ArgsTemplate.I1,
                        I2 = mapping.ArgsTemplate.I2,
                        I3 = mapping.ArgsTemplate.I3,
                        F0 = mapping.ArgsTemplate.F0,
                        F1 = mapping.ArgsTemplate.F1,
                        F2 = mapping.ArgsTemplate.F2,
                        F3 = mapping.ArgsTemplate.F3,
                    },
                    RequireSelection = mapping.RequireSelection,
                    SelectionSetKey = mapping.SelectionSetKey,
                    SelectionType = mapping.SelectionType,
                    ModifierBehavior = mapping.ModifierBehavior,
                    IsSkillMapping = mapping.IsSkillMapping,
                    HeldPolicy = mapping.HeldPolicy,
                    CastModeOverride = mapping.CastModeOverride,
                    AutoTargetPolicy = mapping.AutoTargetPolicy,
                    AutoTargetRangeCm = mapping.AutoTargetRangeCm,
                    CursorTargetPolicy = mapping.CursorTargetPolicy,
                    CursorTargetRangeCm = mapping.CursorTargetRangeCm
                };
            }
        }

        private sealed class AutoTargetResolver
        {
            private readonly World _world;
            private readonly Dictionary<string, object> _globals;
            private readonly ISpatialQueryService _spatialQueries;

            public AutoTargetResolver(World world, Dictionary<string, object> globals, ISpatialQueryService spatialQueries)
            {
                _world = world;
                _globals = globals;
                _spatialQueries = spatialQueries;
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
                        !SelectionEligibility.CanTargetCommand(
                            _world,
                            _globals,
                            actor,
                            candidate,
                            KnowledgePositionAccess.Live))
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
