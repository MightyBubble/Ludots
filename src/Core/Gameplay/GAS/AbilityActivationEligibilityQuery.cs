using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.GAS
{
    public enum AbilityActivationRefusalReason : byte
    {
        None = 0,
        ActorNotAlive = 1,
        AbilityStateMissing = 2,
        AbilitySlotInvalid = 3,
        AbilityDefinitionMissing = 4,
        RequiredActivationTagMissing = 5,
        BlockedActivationTagPresent = 6,
        ProgressionRequirementFailed = 7,
        ActuatorNotReady = 8,
        AimGateNotReady = 9,
        ActivationPreconditionFailed = 10,
    }

    public interface IAbilityActivationActuatorGate
    {
        AbilityActivationRefusalReason Evaluate(Entity actor, int abilityId);
    }

    public readonly struct AbilityActivationEligibilityRequest
    {
        public readonly Entity Actor;
        public readonly int AbilitySlotIndex;
        public readonly Entity TargetEntity;
        public readonly Entity TargetContext;
        public readonly IntVector2 TargetPointCm;
        public readonly bool HasTargetPoint;
        public readonly bool AllowProgressionDeferral;

        public AbilityActivationEligibilityRequest(
            Entity actor,
            int abilitySlotIndex,
            Entity targetEntity = default,
            Entity targetContext = default,
            IntVector2 targetPositionCm = default,
            bool hasTargetPosition = false,
            bool allowProgressionDeferral = true)
        {
            Actor = actor;
            AbilitySlotIndex = abilitySlotIndex;
            TargetEntity = targetEntity;
            TargetContext = targetContext;
            TargetPointCm = targetPositionCm;
            HasTargetPoint = hasTargetPosition;
            AllowProgressionDeferral = allowProgressionDeferral;
        }
    }

    public readonly struct AbilityActivationEligibilityResult
    {
        public readonly AbilityActivationRefusalReason RefusalReason;
        public readonly AbilitySlotState EffectiveSlot;
        public readonly AbilityDefinition Definition;
        public readonly Entity TemplateEntity;
        public readonly int UseProgressionRequirementId;
        public readonly bool HasDefinition;
        public readonly bool HasTemplateEntity;
        public readonly bool IsToggleDeactivation;
        public readonly bool ProgressionRequirementDeferred;

        public bool IsEligible => RefusalReason == AbilityActivationRefusalReason.None;
        public int EffectiveAbilityId => EffectiveSlot.AbilityId > 0
            ? EffectiveSlot.AbilityId
            : EffectiveSlot.TemplateEntityId;

        internal AbilityActivationEligibilityResult(
            AbilityActivationRefusalReason refusalReason,
            in AbilitySlotState effectiveSlot,
            in AbilityDefinition definition,
            Entity templateEntity,
            int useProgressionRequirementId,
            bool hasDefinition,
            bool hasTemplateEntity,
            bool isToggleDeactivation,
            bool progressionRequirementDeferred)
        {
            RefusalReason = refusalReason;
            EffectiveSlot = effectiveSlot;
            Definition = definition;
            TemplateEntity = templateEntity;
            UseProgressionRequirementId = useProgressionRequirementId;
            HasDefinition = hasDefinition;
            HasTemplateEntity = hasTemplateEntity;
            IsToggleDeactivation = isToggleDeactivation;
            ProgressionRequirementDeferred = progressionRequirementDeferred;
        }
    }

    /// <summary>
    /// GAS-owned, read-only activation judgment shared by execution and read-only consumers.
    /// The query performs no order submission, effect publication, tag write, resource deduction,
    /// or ECS structural change.
    /// </summary>
    public sealed class AbilityActivationEligibilityQuery
    {
        private readonly World _world;
        private readonly AbilityDefinitionRegistry _definitions;
        private readonly TagOps _tagOps;
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly ProgressionRequirementEvaluator _progressionRequirements;
        private readonly IAbilityActivationActuatorGate _actuatorGate;

        public AbilityActivationEligibilityQuery(
            World world,
            AbilityDefinitionRegistry definitions,
            TagOps tagOps,
            GraphProgramRegistry graphPrograms = null,
            IGraphRuntimeApi graphApi = null,
            ProgressionRequirementEvaluator progressionRequirements = null,
            IAbilityActivationActuatorGate actuatorGate = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _progressionRequirements = progressionRequirements;
            _actuatorGate = actuatorGate;
        }

        public AbilityActivationEligibilityResult Evaluate(in AbilityActivationEligibilityRequest request)
        {
            AbilitySlotState slot = default;
            AbilityDefinition definition = default;
            Entity templateEntity = default;

            if (!_world.IsAlive(request.Actor))
            {
                return Denied(AbilityActivationRefusalReason.ActorNotAlive, in slot, in definition, templateEntity);
            }

            Entity targetEntity = _world.IsAlive(request.TargetEntity)
                ? request.TargetEntity
                : default;
            Entity targetContext = _world.IsAlive(request.TargetContext)
                ? request.TargetContext
                : default;

            if (!_world.Has<AbilityStateBuffer>(request.Actor))
            {
                return Denied(AbilityActivationRefusalReason.AbilityStateMissing, in slot, in definition, templateEntity);
            }

            if (!AbilitySlotResolver.TryResolve(
                    _world,
                    request.Actor,
                    request.AbilitySlotIndex,
                    out slot))
            {
                return Denied(AbilityActivationRefusalReason.AbilitySlotInvalid, in slot, in definition, templateEntity);
            }

            bool hasDefinition = slot.AbilityId > 0 &&
                _definitions != null &&
                _definitions.TryGet(slot.AbilityId, out definition);
            bool hasTemplateEntity = false;
            if (slot.TemplateEntityId > 0)
            {
                templateEntity = EntityUtil.Reconstruct(
                    slot.TemplateEntityId,
                    slot.TemplateEntityWorldId,
                    slot.TemplateEntityVersion);
                hasTemplateEntity = _world.IsAlive(templateEntity);
            }

            if (!hasDefinition && !hasTemplateEntity)
            {
                return Denied(
                    AbilityActivationRefusalReason.AbilityDefinitionMissing,
                    in slot,
                    in definition,
                    templateEntity);
            }

            bool isToggleDeactivation = hasDefinition &&
                definition.HasToggleSpec &&
                definition.ToggleSpec.ToggleTagId > 0 &&
                _world.Has<GameplayTagContainer>(request.Actor) &&
                _world.Get<GameplayTagContainer>(request.Actor).HasTag(definition.ToggleSpec.ToggleTagId);
            if (isToggleDeactivation)
            {
                return Allowed(
                    in slot,
                    in definition,
                    templateEntity,
                    useProgressionRequirementId: 0,
                    hasDefinition,
                    hasTemplateEntity,
                    isToggleDeactivation: true,
                    progressionRequirementDeferred: false);
            }

            AbilityActivationBlockTags blockTags = default;
            bool hasBlockTags = false;
            if (hasDefinition && definition.HasActivationBlockTags)
            {
                blockTags = definition.ActivationBlockTags;
                hasBlockTags = true;
            }
            else if (hasTemplateEntity && _world.Has<AbilityActivationBlockTags>(templateEntity))
            {
                blockTags = _world.Get<AbilityActivationBlockTags>(templateEntity);
                hasBlockTags = true;
            }

            if (hasBlockTags)
            {
                AbilityActivationRefusalReason blockTagRefusal =
                    AbilityActivationBlockTagEvaluator.Evaluate(
                        _world,
                        request.Actor,
                        _tagOps,
                        in blockTags);
                if (blockTagRefusal != AbilityActivationRefusalReason.None)
                {
                    return Denied(
                        blockTagRefusal,
                        in slot,
                        in definition,
                        templateEntity,
                        hasDefinition,
                        hasTemplateEntity);
                }
            }

            AbilityExecSpec execSpec = hasDefinition
                ? definition.ExecSpec
                : _world.Has<AbilityExecSpec>(templateEntity)
                    ? _world.Get<AbilityExecSpec>(templateEntity)
                    : default;
            int useRequirementId = ResolveUseProgressionRequirementId(
                hasDefinition,
                in definition,
                hasTemplateEntity,
                templateEntity);
            bool progressionDeferred = false;
            if (useRequirementId > 0)
            {
                if (_progressionRequirements == null)
                {
                    throw new InvalidOperationException(
                        "Ability progression requirement is configured, but ProgressionRequirementEvaluator is not registered.");
                }

                if (request.AllowProgressionDeferral &&
                    _progressionRequirements.RequiresExplicitScope(useRequirementId) &&
                    targetContext == default &&
                    AbilityCanResolveTargetContextBeforeSideEffects(in execSpec))
                {
                    progressionDeferred = true;
                }
                else if (!EvaluateProgressionRequirement(
                             request.Actor,
                             targetEntity,
                             targetContext,
                             useRequirementId))
                {
                    return Denied(
                        AbilityActivationRefusalReason.ProgressionRequirementFailed,
                        in slot,
                        in definition,
                        templateEntity,
                        hasDefinition,
                        hasTemplateEntity,
                        useRequirementId);
                }
            }

            if (_actuatorGate != null)
            {
                AbilityActivationRefusalReason actuatorRefusal = _actuatorGate.Evaluate(
                    request.Actor,
                    slot.AbilityId > 0 ? slot.AbilityId : slot.TemplateEntityId);
                if (actuatorRefusal != AbilityActivationRefusalReason.None)
                {
                    if (actuatorRefusal != AbilityActivationRefusalReason.ActuatorNotReady &&
                        actuatorRefusal != AbilityActivationRefusalReason.AimGateNotReady)
                    {
                        throw new InvalidOperationException(
                            $"Ability actuator gate returned unsupported refusal reason {actuatorRefusal}.");
                    }

                    return Denied(
                        actuatorRefusal,
                        in slot,
                        in definition,
                        templateEntity,
                        hasDefinition,
                        hasTemplateEntity,
                        useRequirementId,
                        progressionDeferred);
                }
            }

            AbilityActivationPrecondition precondition = default;
            bool hasPrecondition = false;
            if (hasDefinition && definition.HasActivationPrecondition)
            {
                precondition = definition.ActivationPrecondition;
                hasPrecondition = true;
            }
            else if (hasTemplateEntity && _world.Has<AbilityActivationPrecondition>(templateEntity))
            {
                precondition = _world.Get<AbilityActivationPrecondition>(templateEntity);
                hasPrecondition = true;
            }

            if (hasPrecondition &&
                !AbilityActivationPreconditionEvaluator.Evaluate(
                    _world,
                    request.Actor,
                    targetEntity,
                    targetContext,
                    request.HasTargetPoint ? request.TargetPointCm : default,
                    slot.AbilityId > 0 ? slot.AbilityId : slot.TemplateEntityId,
                    in precondition,
                    _graphPrograms,
                    _graphApi))
            {
                return Denied(
                    AbilityActivationRefusalReason.ActivationPreconditionFailed,
                    in slot,
                    in definition,
                    templateEntity,
                    hasDefinition,
                    hasTemplateEntity,
                    useRequirementId,
                    progressionDeferred);
            }

            return Allowed(
                in slot,
                in definition,
                templateEntity,
                useRequirementId,
                hasDefinition,
                hasTemplateEntity,
                isToggleDeactivation: false,
                progressionDeferred);
        }

        private int ResolveUseProgressionRequirementId(
            bool hasDefinition,
            in AbilityDefinition definition,
            bool hasTemplateEntity,
            Entity templateEntity)
        {
            if (hasDefinition && definition.HasUseProgressionRequirement)
            {
                return definition.UseProgressionRequirementId;
            }

            if (hasTemplateEntity && _world.Has<AbilityProgressionRequirements>(templateEntity))
            {
                return _world.Get<AbilityProgressionRequirements>(templateEntity).UseRequirementId;
            }

            return 0;
        }

        private bool EvaluateProgressionRequirement(
            Entity actor,
            Entity subject,
            Entity explicitScopeHost,
            int requirementId)
        {
            Entity resolvedSubject = _world.IsAlive(subject) ? subject : actor;
            Entity resolvedExplicitScopeHost = _world.IsAlive(explicitScopeHost)
                ? explicitScopeHost
                : default;
            var context = new RoleResolverContext(
                actor: actor,
                subject: resolvedSubject,
                explicitScopeHost: resolvedExplicitScopeHost);
            return _progressionRequirements.Evaluate(requirementId, in context);
        }

        private static bool AbilityCanResolveTargetContextBeforeSideEffects(in AbilityExecSpec spec)
        {
            for (int i = 0; i < spec.ItemCount; i++)
            {
                ExecItemKind kind = spec.GetKind(i);
                if (kind == ExecItemKind.InputGate || kind == ExecItemKind.TargetCollectionGate)
                {
                    return true;
                }

                if (kind != ExecItemKind.None)
                {
                    return false;
                }
            }

            return false;
        }

        private static AbilityActivationEligibilityResult Allowed(
            in AbilitySlotState slot,
            in AbilityDefinition definition,
            Entity templateEntity,
            int useProgressionRequirementId,
            bool hasDefinition,
            bool hasTemplateEntity,
            bool isToggleDeactivation,
            bool progressionRequirementDeferred)
        {
            return new AbilityActivationEligibilityResult(
                AbilityActivationRefusalReason.None,
                in slot,
                in definition,
                templateEntity,
                useProgressionRequirementId,
                hasDefinition,
                hasTemplateEntity,
                isToggleDeactivation,
                progressionRequirementDeferred);
        }

        private static AbilityActivationEligibilityResult Denied(
            AbilityActivationRefusalReason refusalReason,
            in AbilitySlotState slot,
            in AbilityDefinition definition,
            Entity templateEntity,
            bool hasDefinition = false,
            bool hasTemplateEntity = false,
            int useProgressionRequirementId = 0,
            bool progressionRequirementDeferred = false)
        {
            return new AbilityActivationEligibilityResult(
                refusalReason,
                in slot,
                in definition,
                templateEntity,
                useProgressionRequirementId,
                hasDefinition,
                hasTemplateEntity,
                isToggleDeactivation: false,
                progressionRequirementDeferred);
        }
    }
}
