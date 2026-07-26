using System;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Concrete implementations of builtin phase handlers.
    /// Registered once at startup and executed through <see cref="Systems.EffectPhaseExecutor"/>.
    /// </summary>
    public static class BuiltinHandlers
    {
        private static void RejectNonTransactionalPersistentSideEffect(string operation)
        {
            if (BuiltinHandlerRuntimeScope.Current?.EffectSideEffects?.IsActive == true)
            {
                throw new InvalidOperationException(
                    $"{EffectPhaseSideEffectTransaction.UnsupportedSideEffectError}: operation={operation}.");
            }
        }

        public static void RegisterAll(BuiltinHandlerRegistry registry)
        {
            registry.Register(BuiltinHandlerId.ApplyModifiers, HandleApplyModifiers);
            registry.Register(BuiltinHandlerId.ApplyForce, HandleApplyForce);
            registry.Register(BuiltinHandlerId.SpatialQuery, HandleSpatialQuery);
            registry.Register(BuiltinHandlerId.DispatchPayload, HandleDispatchPayload);
            registry.Register(BuiltinHandlerId.ReResolveAndDispatch, HandleReResolveAndDispatch);
            registry.Register(BuiltinHandlerId.CreateProjectile, HandleCreateProjectile);
            registry.Register(BuiltinHandlerId.CreateUnit, HandleCreateUnit);
            registry.Register(BuiltinHandlerId.ApplyDisplacement, HandleApplyDisplacement);
            registry.Register(BuiltinHandlerId.ApplyRelation, HandleApplyRelation);
            registry.Register(BuiltinHandlerId.RevealArea, HandleRevealArea);
            registry.Register(BuiltinHandlerId.DecayRevealArea, HandleDecayRevealArea);
            registry.Register(BuiltinHandlerId.ExecuteExchange, HandleExecuteExchange);
            registry.Register(BuiltinHandlerId.CompleteProgression, HandleCompleteProgression);
            registry.Register(BuiltinHandlerId.SubmitOrderFromBlackboard, HandleSubmitOrderFromBlackboard);
            EntityLifecycleBuiltinHandlers.RegisterAll(registry);
        }

        public static void HandleApplyModifiers(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;
            if (!world.Has<AttributeBuffer>(context.Target)) return;

            var runtime = BuiltinHandlerRuntimeScope.Current;
            var modifiers = runtime?.HasModifierOverride == true
                ? runtime.ModifierOverride
                : templateData.Modifiers;
            int primaryAttrId = modifiers.Count > 0 ? modifiers.Get(0).AttributeId : -1;
            if (runtime?.EffectSideEffects?.IsActive == true)
            {
                float stagedBefore = primaryAttrId >= 0 &&
                    runtime.EffectSideEffects.TryReadAttributeCurrent(context.Target, primaryAttrId, out float currentBefore)
                        ? currentBefore
                        : 0f;
                runtime.EffectSideEffects.StageModifiers(context.Target, in modifiers);
                float stagedAfter = primaryAttrId >= 0 &&
                    runtime.EffectSideEffects.TryReadAttributeCurrent(context.Target, primaryAttrId, out float currentAfter)
                        ? currentAfter
                        : 0f;
                runtime.RecordAttributeDelta(primaryAttrId, stagedAfter - stagedBefore);
                return;
            }

            float before = primaryAttrId >= 0 ? world.Get<AttributeBuffer>(context.Target).GetCurrent(primaryAttrId) : 0f;
            AttributeMutationOps.ApplyModifiers(world, context.Target, in modifiers, runtime?.TagOps);
            float after = primaryAttrId >= 0 ? world.Get<AttributeBuffer>(context.Target).GetCurrent(primaryAttrId) : 0f;
            runtime?.RecordAttributeDelta(primaryAttrId, after - before);
        }

        public static void HandleApplyForce(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;
            if (!world.Has<AttributeBuffer>(context.Target)) return;

            mergedParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx);
            mergedParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy);

            var transaction = BuiltinHandlerRuntimeScope.Current?.EffectSideEffects;
            if (transaction?.IsActive == true)
            {
                if (templateData.PresetAttribute0 > 0)
                    transaction.StageAttributeAdd(context.Target, templateData.PresetAttribute0, fx);
                if (templateData.PresetAttribute1 > 0)
                    transaction.StageAttributeAdd(context.Target, templateData.PresetAttribute1, fy);
                return;
            }

            if (templateData.PresetAttribute0 > 0)
                AttributeMutationOps.AddCurrent(world, context.Target, templateData.PresetAttribute0, fx, BuiltinHandlerRuntimeScope.Current?.TagOps);
            if (templateData.PresetAttribute1 > 0)
                AttributeMutationOps.AddCurrent(world, context.Target, templateData.PresetAttribute1, fy, BuiltinHandlerRuntimeScope.Current?.TagOps);
        }

        public static void HandleSpatialQuery(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpatialQueries == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = TargetResolverFanOutHelper.ResolveTargets(
                world,
                in context,
                in templateData.TargetQuery,
                in mergedParams,
                runtime.SpatialQueries,
                runtime.ResolverBuffer);

            runtime.SetResolvedCandidateCount(candidateCount);
        }

        public static void HandleDispatchPayload(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.FanOutBudget == null || runtime.FanOutCommands == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = runtime.ResolvedCandidateCount;
            if (candidateCount <= 0)
            {
                return;
            }

            int dropped = 0;
            TargetResolverFanOutHelper.ValidateAndCollect(
                world,
                in context,
                in templateData.TargetQuery,
                in templateData.TargetFilter,
                in templateData.TargetDispatch,
                in mergedParams,
                runtime.ResolverBuffer,
                candidateCount,
                runtime.FanOutBudget,
                runtime.FanOutCommands,
                ref dropped);

            runtime.AddDropped(dropped);
            runtime.ClearResolvedCandidates();
        }

        public static void HandleReResolveAndDispatch(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpatialQueries == null || runtime.FanOutBudget == null || runtime.FanOutCommands == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = TargetResolverFanOutHelper.ResolveTargets(
                world,
                in context,
                in templateData.TargetQuery,
                in mergedParams,
                runtime.SpatialQueries,
                runtime.ResolverBuffer);
            if (candidateCount <= 0)
            {
                runtime.ClearResolvedCandidates();
                return;
            }

            int dropped = 0;
            TargetResolverFanOutHelper.ValidateAndCollect(
                world,
                in context,
                in templateData.TargetQuery,
                in templateData.TargetFilter,
                in templateData.TargetDispatch,
                in mergedParams,
                runtime.ResolverBuffer,
                candidateCount,
                runtime.FanOutBudget,
                runtime.FanOutCommands,
                ref dropped);

            runtime.AddDropped(dropped);
            runtime.ClearResolvedCandidates();
        }

        public static void HandleCreateProjectile(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpawnRequests == null)
            {
                throw new InvalidOperationException("CreateProjectile requires RuntimeEntitySpawnQueue in BuiltinHandlerExecutionContext.");
            }

            ref readonly var proj = ref templateData.Projectile;
            if (proj.Speed <= 0) return;

            bool hasLaunchOrigin = world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source);
            Fix64Vec2 launchOrigin = hasLaunchOrigin
                ? world.Get<WorldPositionCm>(context.Source).Value
                : Fix64Vec2.Zero;
            bool hasTargetPoint = TryResolveProjectileTargetPoint(world, in context, in mergedParams, out var targetPointCm);
            bool hasDirection = TryResolveProjectileDirection(
                world,
                context.Source,
                context.Target,
                hasLaunchOrigin,
                launchOrigin,
                hasTargetPoint,
                targetPointCm,
                out var direction);
            if (proj.TravelMode == ProjectileTravelMode.Direction && !hasDirection)
            {
                throw new InvalidOperationException(
                    $"CreateProjectile direction mode requires a resolvable direction: source={context.Source.Id}, target={context.Target.Id}.");
            }

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                Source = context.Source,
                TargetContext = context.TargetContext,
                Projectile = new ProjectileState
                {
                    Speed = Fix64.FromInt(proj.Speed),
                    Range = proj.Range,
                    ArcHeight = proj.ArcHeight,
                    ImpactEffectTemplateId = proj.ImpactEffectTemplateId,
                    HitEffectTemplateId = proj.HitEffectTemplateId,
                    PresentationEffectTemplateId = proj.PresentationEffectTemplateId,
                    TravelMode = proj.TravelMode,
                    ImpactPolicy = proj.ImpactPolicy,
                    CollisionHalfWidthCm = proj.CollisionHalfWidthCm,
                    CollisionRelationFilter = proj.CollisionRelationFilter,
                    CollisionExcludeSource = (byte)(proj.CollisionExcludeSource ? 1 : 0),
                    MaxHitCount = proj.MaxHitCount,
                    Source = context.Source,
                    Target = context.Target,
                    LaunchOriginCm = launchOrigin,
                    HasLaunchOrigin = (byte)(hasLaunchOrigin ? 1 : 0),
                    TargetPointCm = targetPointCm,
                    HasTargetPoint = (byte)(hasTargetPoint ? 1 : 0),
                    Direction = direction,
                    HasDirection = (byte)(hasDirection ? 1 : 0),
                },
                HasProjectileState = 1,
            };

            if (hasLaunchOrigin)
            {
                request.WorldPositionCm = launchOrigin;
                request.HasWorldPosition = 1;
            }

            if (runtime.EffectSideEffects?.IsActive == true)
            {
                runtime.EffectSideEffects.StageSpawnRequest(in request);
            }
            else if (!runtime.SpawnRequests.TryEnqueue(request))
            {
                throw new InvalidOperationException("RuntimeEntitySpawnQueue capacity exceeded while handling CreateProjectile.");
            }
        }

        public static void HandleCreateUnit(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpawnRequests == null)
            {
                throw new InvalidOperationException("CreateUnit requires RuntimeEntitySpawnQueue in BuiltinHandlerExecutionContext.");
            }

            ref readonly var unit = ref templateData.UnitCreation;
            if (unit.Count <= 0) return;

            var origin = ResolveCreateUnitOrigin(world, in context, in mergedParams);

            for (int i = 0; i < unit.Count; i++)
            {
                ComputeUnitCreationPlacement(
                    in unit,
                    context.Source,
                    unit.UnitTypeId,
                    i,
                    out Fix64Vec2 offsetCm,
                    out float facingAngleRad,
                    out bool hasFacing);

                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = unit.UseTemplateSpawn ? RuntimeEntitySpawnKind.Template : RuntimeEntitySpawnKind.UnitType,
                    Source = context.Source,
                    TargetContext = context.TargetContext,
                    WorldPositionCm = origin + offsetCm,
                    HasWorldPosition = 1,
                    FacingAngleRad = facingAngleRad,
                    HasFacing = (byte)(hasFacing ? 1 : 0),
                    UnitTypeId = unit.UnitTypeId,
                    TemplateId = unit.TemplateId,
                    OnSpawnEffectTemplateId = unit.OnSpawnEffectTemplateId,
                    CopySourceTeam = 1,
                    CopySourcePlayerOwner = (byte)(unit.CopySourcePlayerOwner ? 1 : 0),
                    LinkSourceAsParent = (byte)(unit.LinkSourceAsParent ? 1 : 0),
                };

                if (runtime.EffectSideEffects?.IsActive == true)
                {
                    runtime.EffectSideEffects.StageSpawnRequest(in request);
                }
                else if (!runtime.SpawnRequests.TryEnqueue(request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue capacity exceeded while handling CreateUnit.");
                }
            }
        }

        public static void HandleApplyDisplacement(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleApplyDisplacement));
            if (!world.IsAlive(context.Target)) return;

            ref readonly var disp = ref templateData.Displacement;
            if (disp.TotalDurationTicks <= 0 || disp.TotalDistanceCm <= 0) return;

            Entity directionTargetEntity = context.TargetContext;
            Fix64Vec2 targetPointCm = default;
            bool hasTargetPoint = false;

            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                hasTargetPoint = true;
            }
            else if (EffectTargetPointResolver.TryResolvePreservedTargetPoint(in mergedParams, out targetPointCm))
            {
                hasTargetPoint = true;
            }
            else if (world.IsAlive(context.Source) && world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var sourceExec = ref world.Get<AbilityExecInstance>(context.Source);
                if (sourceExec.HasTargetPos != 0)
                {
                    targetPointCm = sourceExec.TargetPosCm;
                    hasTargetPoint = true;
                }
            }

            EntityCreationHelper.CreateDisplacement(world,
                new DisplacementState
                {
                    TargetEntity = context.Target,
                    SourceEntity = context.Source,
                    DirectionTargetEntity = directionTargetEntity,
                    DirectionMode = disp.DirectionMode,
                    FixedDirectionRad = Fix64.FromInt(disp.FixedDirectionDeg) * Fix64.Deg2Rad,
                    TargetPointCm = targetPointCm,
                    HasTargetPoint = hasTargetPoint,
                    TotalDistanceCm = disp.TotalDistanceCm,
                    RemainingDistanceCm = Fix64.FromInt(disp.TotalDistanceCm),
                    TotalDurationTicks = disp.TotalDurationTicks,
                    RemainingTicks = disp.TotalDurationTicks,
                    OverrideNavigation = disp.OverrideNavigation,
                });
        }

        public static void HandleApplyRelation(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleApplyRelation));
            ref readonly var relation = ref templateData.Relation;
            Entity subject = ResolveRelationEntity(in context, relation.Subject);
            if (!world.IsAlive(subject))
            {
                return;
            }

            switch (relation.Operation)
            {
                case RelationOperation.SetParent:
                {
                    Entity parent = ResolveRelationEntity(in context, relation.Parent);
                    if (!world.IsAlive(parent))
                    {
                        return;
                    }

                    RelationOps.SetParent(world, subject, parent);
                    if (relation.SnapSubjectToParentPosition)
                    {
                        SnapSubjectToParentPosition(world, subject, parent);
                    }
                    break;
                }
                case RelationOperation.RemoveParent:
                    RelationOps.RemoveParent(world, subject);
                    break;
                case RelationOperation.EnsureLink:
                {
                    Entity target = ResolveRelationEntity(in context, relation.Parent);
                    if (!world.IsAlive(target))
                    {
                        return;
                    }

                    var runtime = BuiltinHandlerRuntimeScope.Current;
                    if (runtime?.Relationships == null)
                    {
                        throw new InvalidOperationException("Relation operation EnsureLink requires RelationshipRuntime in BuiltinHandlerExecutionContext.");
                    }

                    if (relation.RelationshipTypeId < 0)
                    {
                        throw new InvalidOperationException("Relation operation EnsureLink requires a registered relationship type id.");
                    }

                    runtime.Relationships.EnsureLink(subject, target, relation.RelationshipTypeId);
                    break;
                }
            }
        }

        public static void HandleRevealArea(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleRevealArea));
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.KnowledgeAreaReveal == null)
            {
                throw new InvalidOperationException("RevealArea requires KnowledgeAreaRevealRuntime in BuiltinHandlerExecutionContext.");
            }

            if (!TryResolveRevealAreaCenter(world, in context, in mergedParams, out WorldCmInt2 center))
            {
                return;
            }

            runtime.KnowledgeAreaReveal.Reveal(
                ResolveRevealViewer(world, in context),
                context.Source,
                center,
                in templateData.RevealArea,
                runtime.CurrentStep);
        }

        public static void HandleDecayRevealArea(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleDecayRevealArea));
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.KnowledgeAreaReveal == null)
            {
                throw new InvalidOperationException("DecayRevealArea requires KnowledgeAreaRevealRuntime in BuiltinHandlerExecutionContext.");
            }

            if (!TryResolveRevealAreaCenter(world, in context, in mergedParams, out WorldCmInt2 center))
            {
                return;
            }

            runtime.KnowledgeAreaReveal.DecayArea(
                ResolveRevealViewer(world, in context),
                context.Source,
                center,
                in templateData.RevealArea,
                runtime.CurrentStep);
        }

        public static void HandleExecuteExchange(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleExecuteExchange));
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.Exchange == null)
            {
                throw new InvalidOperationException("ExecuteExchange requires ExchangeRuntime in BuiltinHandlerExecutionContext.");
            }

            if (!mergedParams.TryGetInt(EffectParamKeys.ExchangeOperationId, out int operationId) || operationId <= 0)
            {
                throw new InvalidOperationException("ExecuteExchange requires _ep.exchangeOperationId.");
            }

            mergedParams.TryGetInt(EffectParamKeys.ExchangeScopeKey, out int scopeKey);
            ScopeKey exchangeScope = scopeKey > 0 ? ScopeKey.Named(scopeKey) : default;
            var exchangeContext = new ExchangeExecutionContext(
                context.Source,
                context.Target,
                context.TargetContext,
                exchangeScope);
            ExchangeExecutionResult result = runtime.Exchange.TryExecute(
                new ExchangeOperationKey(operationId, exchangeScope),
                in exchangeContext);
            runtime.RecordExchangeResult(result);
            if (!result.Succeeded)
            {
                return;
            }
        }

        public static void HandleCompleteProgression(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleCompleteProgression));
            if (templateData.ProgressionId <= 0)
            {
                throw new InvalidOperationException("CompleteProgression requires a valid progression id.");
            }

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.ProgressionEvaluator == null)
            {
                throw new InvalidOperationException("CompleteProgression requires ProgressionRequirementEvaluator in BuiltinHandlerExecutionContext.");
            }

            var evaluationContext = new RoleResolverContext(
                actor: context.Source,
                subject: world.IsAlive(context.Target) ? context.Target : context.Source,
                explicitScopeHost: context.TargetContext);
            if (!runtime.ProgressionEvaluator.TryResolveScopeHost(templateData.ProgressionScope, in evaluationContext, out Entity scopeHost))
            {
                throw new InvalidOperationException("CompleteProgression could not resolve its configured progression scope.");
            }

            if (!runtime.ProgressionEvaluator.TryApply(scopeHost, templateData.ProgressionId, templateData.ProgressionChange))
            {
                throw new InvalidOperationException("CompleteProgression requires the resolved scope host to author ProgressionStateBuffer before effects run.");
            }
        }

        public static void HandleSubmitOrderFromBlackboard(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            RejectNonTransactionalPersistentSideEffect(nameof(HandleSubmitOrderFromBlackboard));
            ref readonly SubmitOrderFromBlackboardDescriptor descriptor = ref templateData.SubmitOrderFromBlackboard;
            Entity sourceEntity = ResolveRelationEntity(in context, descriptor.SourceSlot);
            Entity orderActor = ResolveRelationEntity(in context, descriptor.TargetSlot);
            if (!world.IsAlive(sourceEntity) || !world.IsAlive(orderActor))
            {
                throw new InvalidOperationException(
                    "SubmitOrderFromBlackboard requires live source and order actor entities.");
            }

            if (!BlackboardStoredTargetOps.TryRead(world, sourceEntity, in descriptor.StoredTargetKeys, out BlackboardStoredTargetSnapshot storedTarget) ||
                !storedTarget.HasTarget)
            {
                return;
            }

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.OrderTypeRegistry == null)
            {
                throw new InvalidOperationException("SubmitOrderFromBlackboard requires OrderTypeRegistry in BuiltinHandlerExecutionContext.");
            }
            if (runtime.OrderIntake == null)
            {
                throw new InvalidOperationException("SubmitOrderFromBlackboard requires the formal OrderQueue intake in BuiltinHandlerExecutionContext.");
            }

            if (runtime.StepRateHz <= 0)
            {
                throw new InvalidOperationException("SubmitOrderFromBlackboard requires a positive StepRateHz in BuiltinHandlerExecutionContext.");
            }

            Order order = BuildOrderFromStoredTarget(
                in storedTarget,
                in descriptor,
                orderActor,
                ResolvePlayerId(world, orderActor));

            if (!world.Has<OrderBuffer>(orderActor))
            {
                throw new InvalidOperationException(
                    $"SubmitOrderFromBlackboard requires OrderBuffer on order actor entity {orderActor.Id}.");
            }

            OrderSubmitResult result = runtime.OrderIntake.SubmitAssigned(ref order);
            if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                throw new InvalidOperationException(
                    $"SubmitOrderFromBlackboard intake rejected order {order.OrderId} with result '{result}'.");
            }
        }

        private static Order BuildOrderFromStoredTarget(
            in BlackboardStoredTargetSnapshot storedTarget,
            in SubmitOrderFromBlackboardDescriptor descriptor,
            Entity orderActor,
            int playerId)
        {
            switch (storedTarget.Kind)
            {
                case BlackboardStoredTargetKind.Point:
                case BlackboardStoredTargetKind.HexCell:
                    if (descriptor.PointMoveOrderTypeId <= 0)
                    {
                        throw new InvalidOperationException(
                            "SubmitOrderFromBlackboard requires a positive point move order type id compiled at load time.");
                    }

                    if (!BlackboardStoredTargetOps.TryResolveWorldPositionCm(in storedTarget, out Vector3 worldPositionCm))
                    {
                        throw new InvalidOperationException(
                            $"SubmitOrderFromBlackboard cannot resolve world position for stored target kind '{storedTarget.Kind}'.");
                    }

                    return new Order
                    {
                        OrderTypeId = descriptor.PointMoveOrderTypeId,
                        PlayerId = playerId,
                        Actor = orderActor,
                        SubmitMode = descriptor.SubmitMode,
                        Args = new OrderArgs
                        {
                            Spatial = new OrderSpatial
                            {
                                Kind = OrderSpatialKind.WorldCm,
                                Mode = OrderCollectionMode.Single,
                                WorldCm = worldPositionCm,
                            },
                        },
                    };

                case BlackboardStoredTargetKind.Entity:
                    if (descriptor.EntityOrderTypeId <= 0)
                    {
                        throw new InvalidOperationException(
                            "SubmitOrderFromBlackboard requires a positive entity order type id compiled at load time.");
                    }

                    if (storedTarget.TargetEntity == Entity.Null)
                    {
                        throw new InvalidOperationException(
                            "SubmitOrderFromBlackboard entity stored target requires a non-null target entity.");
                    }

                    return new Order
                    {
                        OrderTypeId = descriptor.EntityOrderTypeId,
                        PlayerId = playerId,
                        Actor = orderActor,
                        Target = storedTarget.TargetEntity,
                        SubmitMode = descriptor.SubmitMode,
                        Args = new OrderArgs { I0 = descriptor.EntityOrderIntArg0 },
                    };

                default:
                    throw new InvalidOperationException(
                        $"SubmitOrderFromBlackboard does not support stored target kind '{storedTarget.Kind}'.");
            }
        }

        private static int ResolvePlayerId(World world, Entity entity)
        {
            if (world.TryGet(entity, out PlayerOwner owner) && owner.PlayerId > 0)
            {
                return owner.PlayerId;
            }

            throw new InvalidOperationException(
                $"SubmitOrderFromBlackboard requires PlayerOwner on order actor entity {entity.Id}.");
        }

        private static Fix64Vec2 ResolveCreateUnitOrigin(World world, in EffectContext context, in EffectConfigParams mergedParams)
        {
            return EffectTargetPointResolver.ResolveOrThrow(
                world,
                in context,
                in mergedParams,
                EffectTargetPointResolveOptions.CreateUnit,
                "CreateUnit requires target point or source WorldPositionCm.");
        }

        private static Entity ResolveRelationEntity(in EffectContext context, RelationEntitySlot slot)
        {
            return slot switch
            {
                RelationEntitySlot.Source => context.Source,
                RelationEntitySlot.Target => context.Target,
                RelationEntitySlot.TargetContext => context.TargetContext,
                _ => Entity.Null,
            };
        }

        private static bool TryResolveRevealAreaCenter(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            out WorldCmInt2 center)
        {
            if (EffectTargetPointResolver.TryResolve(
                    world,
                    in context,
                    in mergedParams,
                    EffectTargetPointResolveOptions.CreateUnit,
                    out Fix64Vec2 point))
            {
                center = point.ToWorldCmInt2();
                return true;
            }

            center = default;
            return false;
        }

        private static Entity ResolveRevealViewer(World world, in EffectContext context)
        {
            if (world.IsAlive(context.Source))
            {
                return context.Source;
            }

            if (world.IsAlive(context.Target))
            {
                return context.Target;
            }

            if (world.IsAlive(context.TargetContext))
            {
                return context.TargetContext;
            }

            return Entity.Null;
        }

        private static void SnapSubjectToParentPosition(World world, Entity subject, Entity parent)
        {
            if (!world.Has<WorldPositionCm>(parent))
            {
                return;
            }

            Fix64Vec2 parentPosition = world.Get<WorldPositionCm>(parent).Value;
            Fix64Vec2 previousPosition = world.Has<PreviousWorldPositionCm>(parent)
                ? world.Get<PreviousWorldPositionCm>(parent).Value
                : parentPosition;

            if (world.Has<WorldPositionCm>(subject))
            {
                ref var subjectPosition = ref world.Get<WorldPositionCm>(subject);
                subjectPosition.Value = parentPosition;
            }
            else
            {
                world.Add(subject, new WorldPositionCm { Value = parentPosition });
            }

            if (world.Has<PreviousWorldPositionCm>(subject))
            {
                ref var previousSubjectPosition = ref world.Get<PreviousWorldPositionCm>(subject);
                previousSubjectPosition.Value = previousPosition;
            }
            else
            {
                world.Add(subject, new PreviousWorldPositionCm { Value = previousPosition });
            }
        }

        private static bool TryResolveProjectileTargetPoint(World world, in EffectContext context, in EffectConfigParams mergedParams, out Fix64Vec2 targetPointCm)
        {
            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                return true;
            }

            if (EffectTargetPointResolver.TryResolvePreservedTargetPoint(in mergedParams, out targetPointCm))
            {
                return true;
            }

            if (world.IsAlive(context.Target) && world.Has<WorldPositionCm>(context.Target))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.Target).Value;
                return true;
            }

            targetPointCm = default;
            return false;
        }

        private static bool TryResolveProjectileDirection(
            World world,
            Entity source,
            Entity target,
            bool hasLaunchOrigin,
            in Fix64Vec2 launchOrigin,
            bool hasTargetPoint,
            in Fix64Vec2 targetPointCm,
            out Fix64Vec2 direction)
        {
            if (hasLaunchOrigin && world.IsAlive(target) && world.Has<WorldPositionCm>(target))
            {
                var delta = world.Get<WorldPositionCm>(target).Value - launchOrigin;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    direction = delta.Normalized();
                    return true;
                }
            }

            if (hasLaunchOrigin && hasTargetPoint)
            {
                var delta = targetPointCm - launchOrigin;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    direction = delta.Normalized();
                    return true;
                }
            }

            if (world.IsAlive(source) && world.Has<FacingDirection>(source))
            {
                direction = WorldPlane2D.Fix64DirectionFromFacingRad(world.Get<FacingDirection>(source).AngleRad);
                return true;
            }

            direction = Fix64Vec2.UnitX;
            return false;
        }

        private static Fix64Vec2 ComputeScatterOffsetCm(Entity source, int unitTypeId, int index, int radiusCm)
        {
            if (radiusCm <= 0)
            {
                return Fix64Vec2.Zero;
            }

            unchecked
            {
                uint seed = (uint)(
                    (source.Id * 73856093) ^
                    (source.WorldId * 19349663) ^
                    ((index + 1) * 83492791) ^
                    (unitTypeId * 265443576));

                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;

                Fix64 angleDeg = Fix64.FromInt((int)(seed % 360u));
                Fix64 angleRad = angleDeg * Fix64.Deg2Rad;

                Fix64 fraction = Fix64.HalfValue + Fix64.FromInt((int)((seed >> 9) & 1023)) / Fix64.FromInt(2047);
                Fix64 radius = Fix64.FromInt(radiusCm) * fraction;

                return WorldPlane2D.Fix64OffsetCmFromFacingRad(angleRad, radius);
            }
        }

        private static void ComputeUnitCreationPlacement(
            in UnitCreationDescriptor unit,
            Entity source,
            int unitTypeId,
            int index,
            out Fix64Vec2 offsetCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            switch (unit.PlacementPattern)
            {
                case UnitCreationPlacementPattern.Circle:
                    ComputeCirclePlacement(in unit, index, out offsetCm, out facingAngleRad, out hasFacing);
                    return;
                default:
                    offsetCm = ComputeScatterOffsetCm(source, unitTypeId, index, unit.OffsetRadius);
                    facingAngleRad = 0f;
                    hasFacing = false;
                    return;
            }
        }

        private static void ComputeCirclePlacement(
            in UnitCreationDescriptor unit,
            int index,
            out Fix64Vec2 offsetCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            int count = unit.Count <= 0 ? 1 : unit.Count;
            Fix64 radius = Fix64.FromInt(unit.PlacementRadiusCm > 0 ? unit.PlacementRadiusCm : unit.OffsetRadius);
            Fix64 angleStep = Fix64.FromInt(360) / Fix64.FromInt(count);
            Fix64 angleDeg = Fix64.FromInt(unit.PlacementStartAngleDeg) + angleStep * Fix64.FromInt(index);
            Fix64 angleRad = angleDeg * Fix64.Deg2Rad;

            offsetCm = WorldPlane2D.Fix64OffsetCmFromFacingRad(angleRad, radius);

            hasFacing = unit.FacingPattern != UnitCreationFacingPattern.PreserveTemplate;
            Fix64 quarterTurn = Fix64.Pi / Fix64.FromInt(2);
            facingAngleRad = unit.FacingPattern switch
            {
                UnitCreationFacingPattern.RadialOutward => angleRad.ToFloat(),
                UnitCreationFacingPattern.TangentClockwise => (angleRad - quarterTurn).ToFloat(),
                UnitCreationFacingPattern.TangentCounterClockwise => (angleRad + quarterTurn).ToFloat(),
                _ => 0f
            };
        }
    }

    public static class EntityCreationHelper
    {
        public static Entity CreateDisplacement(World world, in DisplacementState state)
        {
            return world.Create(state);
        }
    }
}
