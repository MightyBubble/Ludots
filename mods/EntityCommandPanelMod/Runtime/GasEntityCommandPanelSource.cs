using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class GasEntityCommandPanelSource : IEntityCommandPanelSource, IEntityCommandPanelActionSource, IEntityCommandPanelSupplementalSource
    {
        private readonly GameEngine _engine;
        private readonly Dictionary<int, string[]> _routeLabelCache = new();
        private readonly string[] _skillActionIds = new string[AbilityStateBuffer.CAPACITY];
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private readonly EffectTemplateRegistry? _effectTemplates;
        private readonly OrderTypeRegistry? _orderTypes;
        private readonly ProgressionRequirementEvaluator? _progressionRequirements;
        private readonly IClock? _clock;

        public GasEntityCommandPanelSource(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            _effectTemplates = engine.GetService(CoreServiceKeys.EffectTemplateRegistry);
            _orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry);
            _progressionRequirements = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator);
            _clock = engine.GetService(CoreServiceKeys.Clock);
        }

        public const string SourceId = "gas.ability-slots";

        public bool TryGetRevision(Entity target, out uint revision)
        {
            revision = 0;
            if (!_engine.World.IsAlive(target))
            {
                return false;
            }

            int displayedSlots = 0;
            if (_engine.World.Has<AbilityStateBuffer>(target))
            {
                ref var baseSlots = ref _engine.World.Get<AbilityStateBuffer>(target);
                displayedSlots = ResolveDisplayedSlotCount(target, in baseSlots);
                revision = HashCombine(revision, (uint)baseSlots.Count);
                for (int i = 0; i < baseSlots.Count; i++)
                {
                    var slot = baseSlots.Get(i);
                    revision = HashSlot(revision, in slot);
                }

                if (_engine.World.Has<AbilityFormSlotBuffer>(target))
                {
                    ref var formSlots = ref _engine.World.Get<AbilityFormSlotBuffer>(target);
                    for (int i = 0; i < AbilityFormSlotBuffer.CAPACITY; i++)
                    {
                        var slot = formSlots.GetOverride(i);
                        revision = HashSlot(revision, in slot);
                    }
                }

                if (_engine.World.Has<Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer>(target))
                {
                    ref var itemGrantedSlots = ref _engine.World.Get<Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer>(target);
                    for (int i = 0; i < Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer.CAPACITY; i++)
                    {
                        var slot = itemGrantedSlots.GetOverride(i);
                        revision = HashSlot(revision, in slot);
                    }
                }

                if (_engine.World.Has<GrantedSlotBuffer>(target))
                {
                    ref var grantedSlots = ref _engine.World.Get<GrantedSlotBuffer>(target);
                    for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
                    {
                        var slot = grantedSlots.GetOverride(i);
                        revision = HashSlot(revision, in slot);
                    }
                }

                if (_engine.World.Has<AbilityFormSetRef>(target))
                {
                    revision = HashCombine(revision, (uint)_engine.World.Get<AbilityFormSetRef>(target).FormSetId);
                }

                revision = HashProgressionRequirementRevisions(revision, target, in baseSlots);
            }

            if (_engine.World.Has<AbilityExecInstance>(target))
            {
                ref var exec = ref _engine.World.Get<AbilityExecInstance>(target);
                revision = HashAbilityExec(revision, in exec);
            }

            if (_engine.World.Has<ActiveEffectContainer>(target))
            {
                ref var effects = ref _engine.World.Get<ActiveEffectContainer>(target);
                revision = HashActiveEffects(revision, in effects);
            }

            if (_engine.World.Has<OrderBuffer>(target))
            {
                ref var orders = ref _engine.World.Get<OrderBuffer>(target);
                revision = HashOrderBuffer(revision, in orders);
            }

            bool hasActorTags = _engine.World.TryGet(target, out GameplayTagContainer actorTags);
            if (hasActorTags)
            {
                revision = HashTagContainer(revision, in actorTags);
            }

            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            if (inputMapping != null)
            {
                revision = HashCombine(revision, (uint)inputMapping.InteractionMode);
                inputMapping.CopyPrimarySkillActionIds(_skillActionIds.AsSpan(0, displayedSlots));
                for (int slotIndex = 0; slotIndex < displayedSlots; slotIndex++)
                {
                    string actionId = _skillActionIds[slotIndex] ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(actionId))
                    {
                        revision = HashCombine(revision, (uint)actionId.GetHashCode(StringComparison.Ordinal));
                    }
                }
            }

            return true;
        }

        public int CopyStatuses(Entity target, Span<EntityCommandPanelStatusView> destination)
        {
            if (!_engine.World.IsAlive(target) || destination.IsEmpty)
            {
                return 0;
            }

            int count = 0;
            if (_engine.World.Has<AbilityExecInstance>(target) &&
                TryBuildAbilityStatus(target, out EntityCommandPanelStatusView abilityStatus))
            {
                destination[count++] = abilityStatus;
                if (count >= destination.Length)
                {
                    return count;
                }
            }

            if (_engine.World.Has<ActiveEffectContainer>(target))
            {
                ref var effects = ref _engine.World.Get<ActiveEffectContainer>(target);
                for (int i = 0; i < effects.Count && count < destination.Length; i++)
                {
                    if (TryBuildEffectStatus(effects.GetEntity(i), out EntityCommandPanelStatusView effectStatus))
                    {
                        destination[count++] = effectStatus;
                    }
                }
            }

            return count;
        }

        public int CopyQueueItems(Entity target, Span<EntityCommandPanelQueueItemView> destination)
        {
            if (!_engine.World.IsAlive(target) || destination.IsEmpty || !_engine.World.Has<OrderBuffer>(target))
            {
                return 0;
            }

            ref var orders = ref _engine.World.Get<OrderBuffer>(target);
            int count = 0;

            if (orders.HasActive)
            {
                destination[count++] = BuildQueueItem(in orders.ActiveOrder, EntityCommandPanelQueueStage.Active);
                if (count >= destination.Length)
                {
                    return count;
                }
            }

            for (int i = 0; i < orders.QueuedCount && count < destination.Length; i++)
            {
                QueuedOrder queued = orders.GetQueued(i);
                destination[count++] = BuildQueueItem(in queued, EntityCommandPanelQueueStage.Queued);
            }

            if (count < destination.Length && orders.HasPending)
            {
                destination[count++] = BuildQueueItem(in orders.PendingOrder, EntityCommandPanelQueueStage.Pending);
            }

            return count;
        }

        public int GetGroupCount(Entity target)
        {
            if (!_engine.World.IsAlive(target) || !_engine.World.Has<AbilityStateBuffer>(target))
            {
                return 0;
            }

            int count = 2;
            if (_engine.World.Has<AbilityFormSetRef>(target))
            {
                var formSetRef = _engine.World.Get<AbilityFormSetRef>(target);
                var registry = _engine.GetService(CoreServiceKeys.AbilityFormSetRegistry);
                if (registry != null && formSetRef.FormSetId > 0 && registry.TryGet(formSetRef.FormSetId, out var formSet))
                {
                    count += formSet.Routes.Count;
                }
            }

            if (_engine.World.Has<GrantedSlotBuffer>(target))
            {
                ref var grantedSlots = ref _engine.World.Get<GrantedSlotBuffer>(target);
                if (HasGrantedOverrides(in grantedSlots))
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group)
        {
            group = default;
            if (!_engine.World.IsAlive(target) || !_engine.World.Has<AbilityStateBuffer>(target))
            {
                return false;
            }

            ref var baseSlots = ref _engine.World.Get<AbilityStateBuffer>(target);
            int slotCount = ResolveDisplayedSlotCount(target, in baseSlots);
            if (!TryResolveGroup(target, groupIndex, out var kind, out int routeIndex, out _, out int localGroupId))
            {
                return false;
            }

            string label = kind switch
            {
                GasPanelGroupKind.Current => "Live Loadout",
                GasPanelGroupKind.Base => "Base Kit",
                GasPanelGroupKind.RoutePreview => ResolveRouteLabel(target, routeIndex),
                GasPanelGroupKind.Granted => "Unlocked",
                _ => string.Empty
            };

            group = new EntityCommandPanelGroupView(localGroupId, label, (byte)slotCount);
            return true;
        }

        public int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination)
        {
            if (!_engine.World.IsAlive(target) || !_engine.World.Has<AbilityStateBuffer>(target) || destination.IsEmpty)
            {
                return 0;
            }

            ref var baseSlots = ref _engine.World.Get<AbilityStateBuffer>(target);
            ref var formSlots = ref _engine.World.TryGetRef<AbilityFormSlotBuffer>(target, out bool hasFormSlots);
            ref var itemGrantedSlots = ref _engine.World.TryGetRef<Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer>(target, out bool hasItemGrantedSlots);
            ref var grantedSlots = ref _engine.World.TryGetRef<GrantedSlotBuffer>(target, out bool hasGrantedSlots);
            bool hasActorTags = _engine.World.TryGet(target, out GameplayTagContainer actorTags);
            AbilityDefinitionRegistry? abilityDefinitions = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            InteractionModeType interactionMode = inputMapping?.InteractionMode ?? InteractionModeType.TargetFirst;

            if (!TryResolveGroup(target, groupIndex, out var kind, out int routeIndex, out AbilityFormSetDefinition formSet, out _))
            {
                return 0;
            }

            int count = Math.Min(destination.Length, ResolveDisplayedSlotCount(target, in baseSlots));
            Span<string> actionIds = _skillActionIds.AsSpan(0, count);
            if (inputMapping != null)
            {
                inputMapping.CopyPrimarySkillActionIds(actionIds);
            }
            else
            {
                actionIds.Clear();
            }

            for (int slotIndex = 0; slotIndex < count; slotIndex++)
            {
                AbilitySlotState effective = default;
                EntityCommandSlotStateFlags flags = EntityCommandSlotStateFlags.None;
                AbilitySlotState baseSlot = baseSlots.Get(slotIndex);
                bool hasBase = HasContent(in baseSlot);

                if (hasBase)
                {
                    effective = baseSlot;
                    flags |= EntityCommandSlotStateFlags.Base;
                }

                switch (kind)
                {
                    case GasPanelGroupKind.Current:
                        AbilitySlotResolver.TryResolve(
                            in baseSlots,
                            in formSlots,
                            hasFormSlots,
                            in itemGrantedSlots,
                            hasItemGrantedSlots,
                            in grantedSlots,
                            hasGrantedSlots,
                            slotIndex,
                            out effective);
                        if (hasFormSlots && formSlots.HasOverride(slotIndex))
                        {
                            flags |= EntityCommandSlotStateFlags.FormOverride;
                        }

                        if (hasItemGrantedSlots && itemGrantedSlots.HasOverride(slotIndex))
                        {
                            flags |= EntityCommandSlotStateFlags.ItemGrantedOverride;
                        }

                        if (hasGrantedSlots && grantedSlots.HasOverride(slotIndex))
                        {
                            flags |= EntityCommandSlotStateFlags.GrantedOverride;
                        }
                        break;

                    case GasPanelGroupKind.Base:
                        break;

                    case GasPanelGroupKind.RoutePreview:
                        if ((uint)routeIndex < (uint)formSet.Routes.Count)
                        {
                            var route = formSet.Routes[routeIndex];
                            for (int i = 0; i < route.SlotOverrides.Count; i++)
                            {
                                var routeOverride = route.SlotOverrides[i];
                                if (routeOverride.SlotIndex != slotIndex)
                                {
                                    continue;
                                }

                                effective = new AbilitySlotState
                                {
                                    AbilityId = routeOverride.AbilityId
                                };
                                flags |= EntityCommandSlotStateFlags.FormOverride;
                                break;
                            }
                        }
                        break;

                    case GasPanelGroupKind.Granted:
                        if (hasGrantedSlots && grantedSlots.HasOverride(slotIndex))
                        {
                            effective = grantedSlots.GetOverride(slotIndex);
                            flags |= EntityCommandSlotStateFlags.GrantedOverride;
                        }
                        break;
                }

                if (!HasContent(in effective))
                {
                    flags |= EntityCommandSlotStateFlags.Empty;
                }

                if (effective.TemplateEntityId != 0)
                {
                    flags |= EntityCommandSlotStateFlags.TemplateBacked;
                }

                string actionId = actionIds[slotIndex] ?? string.Empty;
                string displayLabel = string.Empty;
                string detailLabel = string.Empty;
                short lockoutPermille = 0;
                AbilityDefinition abilityDefinition = default;
                bool hasAbilityDefinition = effective.AbilityId > 0 &&
                    abilityDefinitions != null &&
                    abilityDefinitions.TryGet(effective.AbilityId, out abilityDefinition);
                Entity templateEntity = ResolveTemplateEntity(in effective);
                var progressionPreview = EvaluateProgressionPreview(target, target, default, hasAbilityDefinition, in abilityDefinition, templateEntity);
                if (progressionPreview.Show == ProgressionRequirementPreviewResult.Failed)
                {
                    destination[slotIndex] = BuildEmptySlot(slotIndex, actionId);
                    continue;
                }

                if (progressionPreview.Show == ProgressionRequirementPreviewResult.Deferred ||
                    progressionPreview.Use == ProgressionRequirementPreviewResult.Deferred)
                {
                    flags |= EntityCommandSlotStateFlags.PendingTarget;
                }

                if (progressionPreview.Use == ProgressionRequirementPreviewResult.Failed)
                {
                    flags |= EntityCommandSlotStateFlags.Blocked;
                }

                if (hasAbilityDefinition)
                {
                    if (abilityDefinition.HasActivationBlockTags &&
                        IsBlocked(abilityDefinition.ActivationBlockTags, in actorTags, hasActorTags))
                    {
                        flags |= EntityCommandSlotStateFlags.Blocked;
                    }

                    if (abilityDefinition.HasToggleSpec &&
                        abilityDefinition.ToggleSpec.ToggleTagId > 0 &&
                        hasActorTags &&
                        actorTags.HasTag(abilityDefinition.ToggleSpec.ToggleTagId))
                    {
                        flags |= EntityCommandSlotStateFlags.Active;
                    }

                    lockoutPermille = ResolveLockoutPermille(target, in abilityDefinition);

                    AbilityPresentationConfig? presentation = abilityDefinition.HasPresentation
                        ? abilityDefinition.Presentation
                        : null;
                    if (presentation != null && !string.IsNullOrWhiteSpace(presentation.DisplayName))
                    {
                        displayLabel = presentation.DisplayName;
                    }
                    else
                    {
                        displayLabel = ResolveFallbackLabel(effective.AbilityId, effective.TemplateEntityId);
                    }

                    string abilityInteractionModeKey = string.Empty;
                    if (presentation != null &&
                        presentation.ModeHintOverrides.Count > 0)
                    {
                        abilityInteractionModeKey = ResolveAbilityInteractionModeKey(interactionMode, in abilityDefinition);
                        if (presentation.ModeHintOverrides.TryGetValue(abilityInteractionModeKey, out string? overrideHint) &&
                            !string.IsNullOrWhiteSpace(overrideHint))
                        {
                            detailLabel = overrideHint;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(detailLabel))
                    {
                        if (presentation != null && !string.IsNullOrWhiteSpace(presentation.HintText))
                        {
                            detailLabel = presentation.HintText;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(abilityInteractionModeKey))
                            {
                                abilityInteractionModeKey = ResolveAbilityInteractionModeKey(interactionMode, in abilityDefinition);
                            }

                            detailLabel = BuildDefaultDetailLabel(actionId, abilityInteractionModeKey);
                        }
                    }
                }
                else if (!HasContent(in effective))
                {
                    detailLabel = BuildEmptyDetailLabel(actionId);
                }

                destination[slotIndex] = new EntityCommandPanelSlotView(
                    slotIndex,
                    effective.AbilityId,
                    effective.TemplateEntityId,
                    flags,
                    lockoutPermille,
                    0,
                    0,
                    displayLabel,
                    detailLabel,
                    actionId);
            }

            return count;
        }

        private static EntityCommandPanelSlotView BuildEmptySlot(int slotIndex, string actionId)
        {
            return new EntityCommandPanelSlotView(
                slotIndex,
                0,
                0,
                EntityCommandSlotStateFlags.Empty,
                0,
                0,
                0,
                string.Empty,
                BuildEmptyDetailLabel(actionId),
                actionId);
        }

        private ProgressionRequirementPreview EvaluateProgressionPreview(
            Entity actor,
            Entity subject,
            Entity explicitScopeHost,
            bool hasAbilityDefinition,
            in AbilityDefinition definition,
            Entity templateEntity)
        {
            var preview = new ProgressionRequirementPreview
            {
                Show = hasAbilityDefinition
                    ? EvaluateProgressionShowRequirement(actor, subject, explicitScopeHost, in definition)
                    : ProgressionRequirementPreviewResult.Satisfied,
                Use = hasAbilityDefinition
                    ? EvaluateProgressionUseRequirement(actor, subject, explicitScopeHost, in definition)
                    : ProgressionRequirementPreviewResult.Satisfied
            };

            if (_engine.World.IsAlive(templateEntity) &&
                _engine.World.Has<AbilityProgressionRequirements>(templateEntity))
            {
                ref readonly var requirements = ref _engine.World.Get<AbilityProgressionRequirements>(templateEntity);
                preview.Show = CombinePreview(preview.Show, EvaluateProgressionRequirement(actor, subject, explicitScopeHost, requirements.ShowRequirementId));
                preview.Use = CombinePreview(preview.Use, EvaluateProgressionRequirement(actor, subject, explicitScopeHost, requirements.UseRequirementId));
            }

            return preview;
        }

        private static ProgressionRequirementPreviewResult CombinePreview(
            ProgressionRequirementPreviewResult current,
            ProgressionRequirementPreviewResult next)
        {
            if (current == ProgressionRequirementPreviewResult.Failed ||
                next == ProgressionRequirementPreviewResult.Failed)
            {
                return ProgressionRequirementPreviewResult.Failed;
            }

            if (current == ProgressionRequirementPreviewResult.Deferred ||
                next == ProgressionRequirementPreviewResult.Deferred)
            {
                return ProgressionRequirementPreviewResult.Deferred;
            }

            return ProgressionRequirementPreviewResult.Satisfied;
        }

        private ProgressionRequirementPreviewResult EvaluateProgressionShowRequirement(Entity actor, Entity subject, Entity explicitScopeHost, in AbilityDefinition definition)
        {
            if (!definition.HasShowProgressionRequirement)
            {
                return ProgressionRequirementPreviewResult.Satisfied;
            }

            return EvaluateProgressionRequirement(actor, subject, explicitScopeHost, definition.ShowProgressionRequirementId);
        }

        private ProgressionRequirementPreviewResult EvaluateProgressionUseRequirement(Entity actor, Entity subject, Entity explicitScopeHost, in AbilityDefinition definition)
        {
            if (!definition.HasUseProgressionRequirement)
            {
                return ProgressionRequirementPreviewResult.Satisfied;
            }

            return EvaluateProgressionRequirement(actor, subject, explicitScopeHost, definition.UseProgressionRequirementId);
        }

        private ProgressionRequirementPreviewResult EvaluateProgressionRequirement(Entity actor, Entity subject, Entity explicitScopeHost, int requirementId)
        {
            if (requirementId <= 0)
            {
                return ProgressionRequirementPreviewResult.Satisfied;
            }

            ProgressionRequirementEvaluator evaluator = RequireProgressionRequirements();
            if (!_engine.World.IsAlive(explicitScopeHost) && evaluator.RequiresExplicitScope(requirementId))
            {
                return ProgressionRequirementPreviewResult.Deferred;
            }

            var context = new RoleResolverContext(
                actor: actor,
                subject: subject,
                explicitScopeHost: explicitScopeHost);
            return evaluator.Evaluate(requirementId, in context)
                ? ProgressionRequirementPreviewResult.Satisfied
                : ProgressionRequirementPreviewResult.Failed;
        }

        private ProgressionRequirementEvaluator RequireProgressionRequirements()
        {
            if (_progressionRequirements == null)
            {
                throw new InvalidOperationException("Ability progression requirement is configured, but ProgressionRequirementEvaluator is not registered.");
            }

            return _progressionRequirements;
        }

        private static string ResolveAbilityInteractionModeKey(InteractionModeType interactionMode, in AbilityDefinition abilityDefinition)
        {
            if (abilityDefinition.HasInputBindingOverride &&
                abilityDefinition.InputBindingOverride.HasCastModeOverride)
            {
                return ResolveInteractionModeKey(abilityDefinition.InputBindingOverride.CastModeOverride);
            }

            return ResolveInteractionModeKey(interactionMode);
        }

        private short ResolveLockoutPermille(Entity target, in AbilityDefinition abilityDefinition)
        {
            if (!_engine.World.IsAlive(target) ||
                !abilityDefinition.HasActivationBlockTags ||
                abilityDefinition.ActivationBlockTags.BlockedAny.IsEmpty ||
                !_engine.World.Has<TimedTagBuffer>(target))
            {
                return 0;
            }

            IClock? clock = _engine.GetService(CoreServiceKeys.Clock);
            if (clock == null)
            {
                return 0;
            }

            ref var timed = ref _engine.World.Get<TimedTagBuffer>(target);
            int bestPermille = 0;
            for (int i = 0; i < timed.Count; i++)
            {
                int tagId = timed.GetTagId(i);
                if (tagId <= 0 || !abilityDefinition.ActivationBlockTags.BlockedAny.HasTag(tagId))
                {
                    continue;
                }

                GasClockId clockId = timed.GetClockId(i);
                int now = clock.Now(clockId.ToDomainId());
                int remainingTicks = Math.Max(0, timed.GetExpireAt(i) - now);
                if (remainingTicks <= 0)
                {
                    continue;
                }

                int totalTicks = ResolveLockoutDurationTicks(in abilityDefinition, tagId);
                int permille = totalTicks > 0
                    ? Math.Clamp((int)Math.Round(remainingTicks * 1000d / totalTicks), 0, 1000)
                    : 1000;
                bestPermille = Math.Max(bestPermille, permille);
            }

            return (short)bestPermille;
        }

        private int ResolveLockoutDurationTicks(in AbilityDefinition abilityDefinition, int tagId)
        {
            if (_effectTemplates == null)
            {
                return 0;
            }

            int durationTicks = 0;
            ref readonly AbilityExecSpec execSpec = ref abilityDefinition.ExecSpec;
            for (int itemIndex = 0; itemIndex < execSpec.ItemCount; itemIndex++)
            {
                ExecItemKind kind = execSpec.GetKind(itemIndex);
                if (kind != ExecItemKind.EffectSignal && kind != ExecItemKind.EffectClip)
                {
                    continue;
                }

                int templateId = execSpec.GetTemplateId(itemIndex);
                if (templateId <= 0 || !_effectTemplates.TryGetRef(templateId, out _))
                {
                    continue;
                }

                ref readonly EffectTemplateData template = ref _effectTemplates.GetRef(templateId);
                EffectConfigParams mergedParams = BuildExecItemMergedParams(
                    in template,
                    in abilityDefinition,
                    execSpec.GetCallerParamsIdx(itemIndex));

                durationTicks = Math.Max(
                    durationTicks,
                    ResolveTemplateLockoutDurationTicks(in template, in mergedParams, tagId, depth: 0));
            }

            return durationTicks;
        }

        private static EffectConfigParams BuildExecItemMergedParams(
            in EffectTemplateData template,
            in AbilityDefinition abilityDefinition,
            byte callerParamsIdx)
        {
            EffectConfigParams mergedParams = template.ConfigParams;
            if (abilityDefinition.HasExecCallerParamsPool &&
                callerParamsIdx != 0xFF &&
                callerParamsIdx < abilityDefinition.ExecCallerParamsPool.Count)
            {
                mergedParams.MergeFrom(in abilityDefinition.ExecCallerParamsPool.Get(callerParamsIdx));
            }

            return mergedParams;
        }

        private int ResolveTemplateLockoutDurationTicks(
            in EffectTemplateData template,
            in EffectConfigParams mergedParams,
            int tagId,
            int depth)
        {
            int durationTicks = 0;
            if (TemplateGrantsTag(in template, tagId))
            {
                int effectiveDurationTicks = ConfigParamsMerger.ResolveDurationTicks(in template, in mergedParams);
                if (effectiveDurationTicks > 0)
                {
                    durationTicks = Math.Max(durationTicks, effectiveDurationTicks);
                }
            }

            if (_effectTemplates == null || depth >= 4)
            {
                return durationTicks;
            }

            int payloadTemplateId = ConfigParamsMerger.ResolvePayloadEffectTemplateId(
                in template.TargetDispatch,
                in mergedParams);
            if (payloadTemplateId <= 0 || !_effectTemplates.TryGetRef(payloadTemplateId, out _))
            {
                return durationTicks;
            }

            ref readonly EffectTemplateData payloadTemplate = ref _effectTemplates.GetRef(payloadTemplateId);
            durationTicks = Math.Max(
                durationTicks,
                ResolveTemplateLockoutDurationTicks(
                    in payloadTemplate,
                    in payloadTemplate.ConfigParams,
                    tagId,
                    depth + 1));

            return durationTicks;
        }

        private static bool TemplateGrantsTag(in EffectTemplateData template, int tagId)
        {
            for (int i = 0; i < template.GrantedTags.Count; i++)
            {
                if (template.GrantedTags.Get(i).TagId == tagId)
                {
                    return true;
                }
            }

            return false;
        }

        public InputOrderActivationResult ActivateSlot(Entity target, int groupIndex, int slotIndex)
        {
            if (groupIndex != 0 ||
                slotIndex < 0 ||
                !_engine.World.IsAlive(target) ||
                !_engine.World.Has<AbilityStateBuffer>(target))
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedInvalidActor);
            }

            if (!CanActivateProgressionRequirement(target, slotIndex))
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedByRule);
            }

            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            if (inputMapping == null)
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedInvalidOrderType);
            }

            if ((uint)slotIndex >= (uint)_skillActionIds.Length)
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedValidation);
            }

            inputMapping.CopyPrimarySkillActionIds(_skillActionIds.AsSpan(0, slotIndex + 1));
            string actionId = _skillActionIds[slotIndex] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedInvalidOrderType);
            }

            int playerId = inputMapping.PlayerId;
            if (playerId <= 0)
            {
                return InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedInvalidActor);
            }

            return inputMapping.ActivateMappedAction(
                actionId,
                new InputOrderActivationContext(target, playerId),
                preferUiAiming: true);
        }

        public bool WouldEnterUiAiming(Entity target, int slotIndex)
        {
            if (slotIndex < 0 ||
                !_engine.World.IsAlive(target) ||
                !_engine.World.Has<AbilityStateBuffer>(target))
            {
                return false;
            }

            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            if (inputMapping == null ||
                (uint)slotIndex >= (uint)_skillActionIds.Length)
            {
                return false;
            }

            inputMapping.CopyPrimarySkillActionIds(_skillActionIds.AsSpan(0, slotIndex + 1));
            string actionId = _skillActionIds[slotIndex] ?? string.Empty;
            return inputMapping.WouldEnterUiAiming(actionId, target);
        }

        private bool CanActivateProgressionRequirement(Entity target, int slotIndex)
        {
            ref readonly var baseSlots = ref _engine.World.Get<AbilityStateBuffer>(target);
            AbilitySlotState slot = ResolveEffectiveSlot(target, in baseSlots, slotIndex);
            Entity templateEntity = ResolveTemplateEntity(in slot);
            AbilityDefinition definition = default;
            bool hasDefinition = slot.AbilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(slot.AbilityId, out definition);

            var preview = EvaluateProgressionPreview(target, target, default, hasDefinition, in definition, templateEntity);
            return preview.Show != ProgressionRequirementPreviewResult.Failed &&
                   preview.Use != ProgressionRequirementPreviewResult.Failed;
        }

        private bool TryBuildAbilityStatus(Entity target, out EntityCommandPanelStatusView status)
        {
            status = default;
            if (!_engine.World.TryGet(target, out AbilityExecInstance exec))
            {
                return false;
            }

            int totalTicks = ResolveExecTotalTicks(exec.AbilityId, exec.IsToggleDeactivating);
            if (totalTicks <= 0)
            {
                totalTicks = Math.Max(exec.CurrentTick, 1);
            }

            int remainingTicks = Math.Max(0, totalTicks - exec.CurrentTick);
            string label = ResolveAbilityExecLabel(exec.AbilityId);
            string detail = $"{ResolveExecStateLabel(exec.State)} · {remainingTicks} steps left";
            string accent = ResolveAbilityExecAccent(exec.AbilityId);
            status = new EntityCommandPanelStatusView(
                EntityCommandPanelStatusKind.ActiveAbility,
                ClampPermille(exec.CurrentTick, totalTicks),
                label,
                detail,
                accent);
            return true;
        }

        private bool TryBuildEffectStatus(Entity effectEntity, out EntityCommandPanelStatusView status)
        {
            status = default;
            if (!_engine.World.IsAlive(effectEntity) ||
                !_engine.World.Has<GameplayEffect>(effectEntity) ||
                !_engine.World.Has<EffectTemplateRef>(effectEntity))
            {
                return false;
            }

            ref readonly var effect = ref _engine.World.Get<GameplayEffect>(effectEntity);
            if (effect.TotalTicks <= 0 || effect.RemainingTicks <= 0)
            {
                return false;
            }

            int templateId = _engine.World.Get<EffectTemplateRef>(effectEntity).TemplateId;
            string label = ResolveEffectLabel(templateId);
            string detail = $"{ResolveEffectPresetLabel(templateId)} - {effect.RemainingTicks}/{effect.TotalTicks} ticks left";
            status = new EntityCommandPanelStatusView(
                EntityCommandPanelStatusKind.ActiveEffect,
                ClampPermille(effect.TotalTicks - effect.RemainingTicks, effect.TotalTicks),
                label,
                detail,
                ResolveEffectAccent(templateId));
            return true;
        }

        private EntityCommandPanelQueueItemView BuildQueueItem(in QueuedOrder queuedOrder, EntityCommandPanelQueueStage stage)
        {
            string label = ResolveOrderLabel(in queuedOrder.Order);
            string detail = BuildOrderDetail(in queuedOrder.Order, stage, queuedOrder.ExpireStep);
            string accent = stage switch
            {
                EntityCommandPanelQueueStage.Active => "#58B7FF",
                EntityCommandPanelQueueStage.Queued => "#F2C36B",
                EntityCommandPanelQueueStage.Pending => "#F59E0B",
                _ => "#8FA6BD"
            };

            return new EntityCommandPanelQueueItemView(stage, label, detail, accent);
        }

        private int ResolveExecTotalTicks(int abilityId, bool isToggleDeactivating)
        {
            if (abilityId <= 0 || _abilityDefinitions == null || !_abilityDefinitions.TryGet(abilityId, out var definition))
            {
                return 0;
            }

            AbilityExecSpec spec = isToggleDeactivating && definition.HasToggleSpec
                ? definition.ToggleSpec.DeactivateExecSpec
                : definition.ExecSpec;
            return ComputeSpecTotalTicks(in spec);
        }

        private static int ComputeSpecTotalTicks(in AbilityExecSpec spec)
        {
            int total = 0;
            for (int i = 0; i < spec.ItemCount; i++)
            {
                int endTick = spec.GetTick(i) + Math.Max(0, spec.GetDurationTicks(i));
                if (endTick > total)
                {
                    total = endTick;
                }
            }

            return total;
        }

        private string ResolveAbilityExecLabel(int abilityId)
        {
            if (abilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(abilityId, out var definition) &&
                definition.HasPresentation &&
                definition.Presentation != null)
            {
                return definition.Presentation.ResolveDisplayName(ResolveFallbackLabel(abilityId, 0));
            }

            return ResolveFallbackLabel(abilityId, 0);
        }

        private string ResolveAbilityExecAccent(int abilityId)
        {
            if (abilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(abilityId, out var definition) &&
                definition.HasPresentation &&
                definition.Presentation != null &&
                !string.IsNullOrWhiteSpace(definition.Presentation.AccentColorHex))
            {
                return definition.Presentation.AccentColorHex;
            }

            return "#58B7FF";
        }

        private string ResolveEffectLabel(int templateId)
        {
            string raw = EffectTemplateIdRegistry.GetName(templateId);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return templateId > 0 ? $"Effect#{templateId}" : "Effect";
            }

            int lastDot = raw.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < raw.Length ? raw[(lastDot + 1)..] : raw;
        }

        private string ResolveEffectPresetLabel(int templateId)
        {
            if (_effectTemplates != null && templateId > 0 && _effectTemplates.TryGet(templateId, out var template))
            {
                return template.PresetType switch
                {
                    EffectPresetType.Buff => "Buff",
                    EffectPresetType.Relation => "Relation",
                    EffectPresetType.CreateUnit => "Create Unit",
                    _ => template.PresetType.ToString()
                };
            }

            return "Effect";
        }

        private string ResolveEffectAccent(int templateId)
        {
            if (_effectTemplates != null && templateId > 0 && _effectTemplates.TryGet(templateId, out var template))
            {
                return template.PresetType switch
                {
                    EffectPresetType.Buff => "#34D399",
                    EffectPresetType.Relation => "#F59E0B",
                    EffectPresetType.CreateUnit => "#A78BFA",
                    _ => "#58B7FF"
                };
            }

            return "#58B7FF";
        }

        private string ResolveOrderLabel(in Order order)
        {
            if (TryResolveCastAbilityLabel(in order, out string castAbilityLabel))
            {
                return castAbilityLabel;
            }

            int orderTypeId = order.OrderTypeId;
            if (_orderTypes != null && orderTypeId > 0 && _orderTypes.TryGet(orderTypeId, out var config))
            {
                if (!string.IsNullOrWhiteSpace(config.Label))
                {
                    return config.Label;
                }

                if (!string.IsNullOrWhiteSpace(config.Key))
                {
                    return config.Key;
                }
            }

            return orderTypeId > 0 ? $"Order#{orderTypeId}" : "Order";
        }

        private bool TryResolveCastAbilityLabel(in Order order, out string label)
        {
            label = string.Empty;
            if (!_engine.World.IsAlive(order.Actor) ||
                !_engine.World.Has<AbilityStateBuffer>(order.Actor) ||
                order.Args.I0 < 0)
            {
                return false;
            }

            if (_orderTypes == null ||
                !_orderTypes.TryGet(order.OrderTypeId, out var orderConfig) ||
                !string.Equals(orderConfig.Key, "castAbility", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ref var slots = ref _engine.World.Get<AbilityStateBuffer>(order.Actor);
            if ((uint)order.Args.I0 >= AbilityStateBuffer.CAPACITY)
            {
                return false;
            }

            AbilitySlotState slot = slots.Get(order.Args.I0);
            if (slot.AbilityId <= 0 ||
                _abilityDefinitions == null ||
                !_abilityDefinitions.TryGet(slot.AbilityId, out var definition))
            {
                return false;
            }

            string fallbackLabel = ResolveFallbackLabel(slot.AbilityId, slot.TemplateEntityId);
            label = definition.HasPresentation && definition.Presentation != null
                ? definition.Presentation.ResolveDisplayName(fallbackLabel)
                : fallbackLabel;
            return !string.IsNullOrWhiteSpace(label);
        }

        private string BuildOrderDetail(in Order order, EntityCommandPanelQueueStage stage, int expireStep)
        {
            string stageLabel = stage switch
            {
                EntityCommandPanelQueueStage.Active => "Active",
                EntityCommandPanelQueueStage.Queued => "Queued",
                EntityCommandPanelQueueStage.Pending => "Pending",
                _ => "Order"
            };

            string targetLabel = ResolveOrderTargetLabel(in order);
            string actionLabel = ResolveOrderActionDetail(in order);
            if (expireStep > 0 && _clock != null)
            {
                int currentStep = _clock.Now(ClockDomainId.Step);
                int remaining = Math.Max(0, expireStep - currentStep);
                if (!string.IsNullOrWhiteSpace(actionLabel) && !string.IsNullOrWhiteSpace(targetLabel))
                {
                    return $"{stageLabel} - {actionLabel} - {targetLabel} - expires in {remaining} ticks";
                }

                if (!string.IsNullOrWhiteSpace(actionLabel))
                {
                    return $"{stageLabel} - {actionLabel} - expires in {remaining} ticks";
                }

                if (!string.IsNullOrWhiteSpace(targetLabel))
                {
                    return $"{stageLabel} - {targetLabel} - expires in {remaining} ticks";
                }

                return $"{stageLabel} - expires in {remaining} ticks";
            }

            if (!string.IsNullOrWhiteSpace(actionLabel) && !string.IsNullOrWhiteSpace(targetLabel))
            {
                return $"{stageLabel} - {actionLabel} - {targetLabel}";
            }

            if (!string.IsNullOrWhiteSpace(actionLabel))
            {
                return $"{stageLabel} - {actionLabel}";
            }

            return string.IsNullOrWhiteSpace(targetLabel)
                ? stageLabel
                : $"{stageLabel} - {targetLabel}";
        }

        private string ResolveOrderTargetLabel(in Order order)
        {
            if (_engine.World.IsAlive(order.Target) &&
                _engine.World.TryGet(order.Target, out Name name) &&
                !string.IsNullOrWhiteSpace(name.Value))
            {
                return name.Value;
            }

            if (order.Args.Spatial.Kind == OrderSpatialKind.WorldCm)
            {
                int x = (int)order.Args.Spatial.WorldCm.X;
                int z = (int)order.Args.Spatial.WorldCm.Z;
                return $"@ {x},{z}";
            }

            return string.Empty;
        }

        private string ResolveOrderActionDetail(in Order order)
        {
            return _orderTypes != null &&
                   _orderTypes.TryGet(order.OrderTypeId, out var config) &&
                   string.Equals(config.Key, "castAbility", StringComparison.OrdinalIgnoreCase) &&
                   order.Args.I0 >= 0
                ? $"slot {order.Args.I0}"
                : string.Empty;
        }

        private static string ResolveExecStateLabel(AbilityExecRunState state)
        {
            return state switch
            {
                AbilityExecRunState.Running => "Executing",
                AbilityExecRunState.GateWaiting => "Waiting",
                AbilityExecRunState.Committed => "Committed",
                AbilityExecRunState.Finished => "Finished",
                AbilityExecRunState.Interrupted => "Interrupted",
                AbilityExecRunState.Failed => "Failed",
                _ => "Ability"
            };
        }

        private bool TryResolveGroup(
            Entity target,
            int groupIndex,
            out GasPanelGroupKind kind,
            out int routeIndex,
            out AbilityFormSetDefinition formSet,
            out int localGroupId)
        {
            kind = GasPanelGroupKind.Base;
            routeIndex = -1;
            formSet = default;
            localGroupId = groupIndex;

            int normalizedIndex = Math.Max(0, groupIndex);
            if (normalizedIndex == 0)
            {
                kind = GasPanelGroupKind.Current;
                return true;
            }

            if (normalizedIndex == 1)
            {
                kind = GasPanelGroupKind.Base;
                return true;
            }

            int nextIndex = normalizedIndex - 2;
            if (_engine.World.Has<AbilityFormSetRef>(target))
            {
                var formSetRef = _engine.World.Get<AbilityFormSetRef>(target);
                var registry = _engine.GetService(CoreServiceKeys.AbilityFormSetRegistry);
                if (registry != null && formSetRef.FormSetId > 0 && registry.TryGet(formSetRef.FormSetId, out formSet))
                {
                    if ((uint)nextIndex < (uint)formSet.Routes.Count)
                    {
                        kind = GasPanelGroupKind.RoutePreview;
                        routeIndex = nextIndex;
                        return true;
                    }

                    nextIndex -= formSet.Routes.Count;
                }
            }

            if (nextIndex == 0 &&
                _engine.World.Has<GrantedSlotBuffer>(target) &&
                HasGrantedOverrides(in _engine.World.Get<GrantedSlotBuffer>(target)))
            {
                kind = GasPanelGroupKind.Granted;
                return true;
            }

            return false;
        }

        private int ResolveDisplayedSlotCount(Entity target, in AbilityStateBuffer baseSlots)
        {
            int count = baseSlots.Count;

            if (_engine.World.Has<AbilityFormSetRef>(target))
            {
                var formSetRef = _engine.World.Get<AbilityFormSetRef>(target);
                var registry = _engine.GetService(CoreServiceKeys.AbilityFormSetRegistry);
                if (registry != null && formSetRef.FormSetId > 0 && registry.TryGet(formSetRef.FormSetId, out var formSet))
                {
                    for (int routeIndex = 0; routeIndex < formSet.Routes.Count; routeIndex++)
                    {
                        var route = formSet.Routes[routeIndex];
                        for (int i = 0; i < route.SlotOverrides.Count; i++)
                        {
                            count = Math.Max(count, route.SlotOverrides[i].SlotIndex + 1);
                        }
                    }
                }
            }

            if (_engine.World.Has<GrantedSlotBuffer>(target))
            {
                ref var granted = ref _engine.World.Get<GrantedSlotBuffer>(target);
                for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
                {
                    if (granted.HasOverride(i))
                    {
                        count = Math.Max(count, i + 1);
                    }
                }
            }

            if (_engine.World.Has<Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer>(target))
            {
                ref var itemGranted = ref _engine.World.Get<Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer>(target);
                for (int i = 0; i < Ludots.Core.Gameplay.Items.ItemGrantedSlotBuffer.CAPACITY; i++)
                {
                    if (itemGranted.HasOverride(i))
                    {
                        count = Math.Max(count, i + 1);
                    }
                }
            }

            if (_engine.World.Has<AbilityFormSlotBuffer>(target))
            {
                ref var formSlots = ref _engine.World.Get<AbilityFormSlotBuffer>(target);
                for (int i = 0; i < AbilityFormSlotBuffer.CAPACITY; i++)
                {
                    if (formSlots.HasOverride(i))
                    {
                        count = Math.Max(count, i + 1);
                    }
                }
            }

            return Math.Clamp(count, 0, AbilityStateBuffer.CAPACITY);
        }

        private string ResolveRouteLabel(Entity target, int routeIndex)
        {
            if (!_engine.World.Has<AbilityFormSetRef>(target))
            {
                return $"Preview {routeIndex + 1}";
            }

            int formSetId = _engine.World.Get<AbilityFormSetRef>(target).FormSetId;
            if (!_routeLabelCache.TryGetValue(formSetId, out string[]? cached))
            {
                cached = BuildRouteLabels(formSetId);
                _routeLabelCache[formSetId] = cached;
            }

            if ((uint)routeIndex < (uint)cached.Length)
            {
                return cached[routeIndex];
            }

            return $"Preview {routeIndex + 1}";
        }

        private string[] BuildRouteLabels(int formSetId)
        {
            var registry = _engine.GetService(CoreServiceKeys.AbilityFormSetRegistry);
            if (registry == null || formSetId <= 0 || !registry.TryGet(formSetId, out var formSet))
            {
                return Array.Empty<string>();
            }

            string[] labels = new string[formSet.Routes.Count];
            for (int routeIndex = 0; routeIndex < formSet.Routes.Count; routeIndex++)
            {
                var route = formSet.Routes[routeIndex];
                GameplayTagContainer requiredAll = route.RequiredAll;
                GameplayTagContainer blockedAny = route.BlockedAny;
                string required = FormatTagContainer(in requiredAll);
                string blocked = FormatTagContainer(in blockedAny);

                if (!string.IsNullOrWhiteSpace(required))
                {
                    labels[routeIndex] = $"Preview: {required}";
                }
                else if (!string.IsNullOrWhiteSpace(blocked))
                {
                    labels[routeIndex] = $"Preview: not {blocked}";
                }
                else
                {
                    labels[routeIndex] = $"Preview {routeIndex + 1}";
                }
            }

            return labels;
        }

        private static string FormatTagContainer(in GameplayTagContainer tags)
        {
            if (tags.IsEmpty)
            {
                return string.Empty;
            }

            var parts = new List<string>(4);
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                if (!tags.HasTag(tagId))
                {
                    continue;
                }

                string name = TagRegistry.GetName(tagId);
                if (string.IsNullOrWhiteSpace(name))
                {
                    parts.Add($"Tag{tagId}");
                    continue;
                }

                int lastDot = name.LastIndexOf('.');
                parts.Add(lastDot >= 0 ? name[(lastDot + 1)..] : name);
            }

            return string.Join(" + ", parts);
        }

        private static bool HasGrantedOverrides(in GrantedSlotBuffer grantedSlots)
        {
            for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
            {
                if (grantedSlots.HasOverride(i))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasContent(in AbilitySlotState slot)
        {
            return slot.AbilityId != 0 || slot.TemplateEntityId != 0;
        }

        private static bool IsBlocked(in AbilityActivationBlockTags blockTags, in GameplayTagContainer actorTags, bool hasActorTags)
        {
            if (!blockTags.RequiredAll.IsEmpty && (!hasActorTags || !actorTags.ContainsAll(in blockTags.RequiredAll)))
            {
                return true;
            }

            return hasActorTags &&
                   !blockTags.BlockedAny.IsEmpty &&
                   actorTags.Intersects(in blockTags.BlockedAny);
        }

        private static string ResolveFallbackLabel(int abilityId, int templateEntityId)
        {
            if (abilityId > 0)
            {
                string raw = AbilityIdRegistry.GetName(abilityId);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    int lastDot = raw.LastIndexOf('.');
                    return lastDot >= 0 && lastDot + 1 < raw.Length ? raw[(lastDot + 1)..] : raw;
                }

                return $"Ability#{abilityId}";
            }

            if (templateEntityId > 0)
            {
                return $"Template#{templateEntityId}";
            }

            return "(empty)";
        }

        private static string BuildDefaultDetailLabel(string actionId, string interactionModeKey)
        {
            string actionLabel = ResolveActionLabel(actionId);
            string modeLabel = ResolveInteractionModeLabel(interactionModeKey);

            if (!string.IsNullOrWhiteSpace(actionLabel) && !string.IsNullOrWhiteSpace(modeLabel))
            {
                return $"{actionLabel} · {modeLabel}";
            }

            if (!string.IsNullOrWhiteSpace(actionLabel))
            {
                return actionLabel;
            }

            return string.IsNullOrWhiteSpace(modeLabel) ? "Ready" : modeLabel;
        }

        private static string BuildEmptyDetailLabel(string actionId)
        {
            string actionLabel = ResolveActionLabel(actionId);
            return string.IsNullOrWhiteSpace(actionLabel)
                ? "No ability assigned"
                : $"{actionLabel} · No ability assigned";
        }

        private static string ResolveInteractionModeLabel(string interactionModeKey)
        {
            return interactionModeKey switch
            {
                nameof(InteractionModeType.TargetFirst) => "Target First",
                nameof(InteractionModeType.SmartCast) => "Smart Cast",
                nameof(InteractionModeType.AimCast) => "Aim Then Confirm",
                nameof(InteractionModeType.SmartCastWithIndicator) => "Release To Cast",
                nameof(InteractionModeType.PressReleaseAimCast) => "Release Then Confirm",
                nameof(InteractionModeType.ContextScored) => "Context Scored",
                _ => string.Empty
            };
        }

        private static string ResolveInteractionModeKey(InteractionModeType mode)
        {
            return mode switch
            {
                InteractionModeType.TargetFirst => nameof(InteractionModeType.TargetFirst),
                InteractionModeType.SmartCast => nameof(InteractionModeType.SmartCast),
                InteractionModeType.AimCast => nameof(InteractionModeType.AimCast),
                InteractionModeType.SmartCastWithIndicator => nameof(InteractionModeType.SmartCastWithIndicator),
                InteractionModeType.PressReleaseAimCast => nameof(InteractionModeType.PressReleaseAimCast),
                InteractionModeType.ContextScored => nameof(InteractionModeType.ContextScored),
                _ => mode.ToString()
            };
        }

        private static string ResolveActionLabel(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return string.Empty;
            }

            if (actionId.StartsWith("Skill", StringComparison.OrdinalIgnoreCase) &&
                actionId.Length > "Skill".Length)
            {
                if (string.Equals(actionId, "SkillQ", StringComparison.Ordinal))
                {
                    return "Q";
                }

                if (string.Equals(actionId, "SkillW", StringComparison.Ordinal))
                {
                    return "W";
                }

                if (string.Equals(actionId, "SkillE", StringComparison.Ordinal))
                {
                    return "E";
                }

                if (string.Equals(actionId, "SkillR", StringComparison.Ordinal))
                {
                    return "R";
                }

                return actionId["Skill".Length..].ToUpperInvariant();
            }

            return actionId;
        }

        private static short ClampPermille(int current, int total)
        {
            if (total <= 0)
            {
                return 0;
            }

            int value = (int)Math.Round(current * 1000d / total, MidpointRounding.AwayFromZero);
            return (short)Math.Clamp(value, 0, 1000);
        }

        private uint HashProgressionRequirementRevisions(uint current, Entity target, in AbilityStateBuffer baseSlots)
        {
            if (_progressionRequirements == null)
            {
                return current;
            }

            int displayedSlots = ResolveDisplayedSlotCount(target, in baseSlots);
            for (int slotIndex = 0; slotIndex < displayedSlots; slotIndex++)
            {
                AbilitySlotState slot = ResolveEffectiveSlot(target, in baseSlots, slotIndex);
                Entity templateEntity = ResolveTemplateEntity(in slot);
                AbilityDefinition definition = default;
                bool hasDefinition = slot.AbilityId > 0 &&
                    _abilityDefinitions != null &&
                    _abilityDefinitions.TryGet(slot.AbilityId, out definition);

                var context = new RoleResolverContext(actor: target, subject: target);
                if (hasDefinition && definition.HasShowProgressionRequirement)
                {
                    current = HashProgressionRequirementRevision(current, definition.ShowProgressionRequirementId, in context);
                }

                if (hasDefinition && definition.HasUseProgressionRequirement)
                {
                    current = HashProgressionRequirementRevision(current, definition.UseProgressionRequirementId, in context);
                }

                if (_engine.World.IsAlive(templateEntity) &&
                    _engine.World.Has<AbilityProgressionRequirements>(templateEntity))
                {
                    ref readonly var requirements = ref _engine.World.Get<AbilityProgressionRequirements>(templateEntity);
                    current = HashProgressionRequirementRevision(current, requirements.ShowRequirementId, in context);
                    current = HashProgressionRequirementRevision(current, requirements.UseRequirementId, in context);
                }
            }

            return current;
        }

        private uint HashProgressionRequirementRevision(uint current, int requirementId, in RoleResolverContext context)
        {
            if (requirementId <= 0 || _progressionRequirements == null)
            {
                return current;
            }

            current = HashCombine(current, _progressionRequirements.ComputeScopeRevision(requirementId, in context));
            if (_progressionRequirements.UsesGraphValidation(requirementId))
            {
                if (_clock == null)
                {
                    throw new InvalidOperationException("Graph-backed progression command requirements require the engine clock for UI cache invalidation.");
                }

                current = HashCombine(current, (uint)_clock.Now(ClockDomainId.FixedFrame));
                current = HashCombine(current, (uint)_clock.Now(ClockDomainId.Step));
            }

            return current;
        }

        private AbilitySlotState ResolveEffectiveSlot(Entity target, in AbilityStateBuffer baseSlots, int slotIndex)
        {
            return AbilitySlotResolver.TryResolve(_engine.World, target, slotIndex, out AbilitySlotState slot)
                ? slot
                : default;
        }

        private Entity ResolveTemplateEntity(in AbilitySlotState slot)
        {
            if (slot.TemplateEntityId <= 0)
            {
                return default;
            }

            Entity templateEntity = EntityUtil.Reconstruct(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
            return _engine.World.IsAlive(templateEntity) ? templateEntity : default;
        }

        private static uint HashAbilityExec(uint current, in AbilityExecInstance exec)
        {
            current = HashCombine(current, (uint)exec.AbilityId);
            current = HashCombine(current, (uint)exec.AbilitySlot);
            current = HashCombine(current, (uint)exec.CurrentTick);
            current = HashCombine(current, (uint)exec.StartAbsoluteTick);
            current = HashCombine(current, (uint)exec.NextItemIndex);
            current = HashCombine(current, (uint)exec.State);
            current = HashCombine(current, (uint)exec.ActiveClockId);
            return current;
        }

        private uint HashActiveEffects(uint current, in ActiveEffectContainer effects)
        {
            current = HashCombine(current, (uint)effects.Count);
            for (int i = 0; i < effects.Count; i++)
            {
                Entity effectEntity = effects.GetEntity(i);
                current = HashCombine(current, (uint)effectEntity.Id);
                current = HashCombine(current, (uint)effectEntity.Version);
                if (!_engine.World.IsAlive(effectEntity) ||
                    !_engine.World.Has<GameplayEffect>(effectEntity))
                {
                    continue;
                }

                ref readonly var effect = ref _engine.World.Get<GameplayEffect>(effectEntity);
                current = HashCombine(current, (uint)effect.TotalTicks);
                current = HashCombine(current, (uint)effect.RemainingTicks);
                current = HashCombine(current, (uint)effect.State);
                if (_engine.World.Has<EffectTemplateRef>(effectEntity))
                {
                    current = HashCombine(current, (uint)_engine.World.Get<EffectTemplateRef>(effectEntity).TemplateId);
                }
            }

            return current;
        }

        private static uint HashOrderBuffer(uint current, in OrderBuffer buffer)
        {
            current = HashCombine(current, (uint)buffer.ActiveIndex);
            current = HashCombine(current, (uint)buffer.QueuedCount);
            current = HashCombine(current, buffer.HasPending ? 1u : 0u);
            if (buffer.HasActive)
            {
                current = HashQueuedOrder(current, in buffer.ActiveOrder);
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                QueuedOrder queued = buffer.GetQueued(i);
                current = HashQueuedOrder(current, in queued);
            }

            if (buffer.HasPending)
            {
                current = HashQueuedOrder(current, in buffer.PendingOrder);
            }

            return current;
        }

        private static uint HashQueuedOrder(uint current, in QueuedOrder queued)
        {
            current = HashCombine(current, (uint)queued.Priority);
            current = HashCombine(current, (uint)queued.ExpireStep);
            current = HashCombine(current, (uint)queued.InsertStep);
            current = HashCombine(current, (uint)queued.Order.OrderId);
            current = HashCombine(current, (uint)queued.Order.OrderTypeId);
            current = HashCombine(current, (uint)queued.Order.Target.Id);
            current = HashCombine(current, (uint)queued.Order.Target.Version);
            current = HashCombine(current, (uint)queued.Order.Args.Spatial.Kind);
            current = HashCombine(current, (uint)queued.Order.Args.Spatial.WorldCm.X);
            current = HashCombine(current, (uint)queued.Order.Args.Spatial.WorldCm.Z);
            return current;
        }

        private static uint HashSlot(uint current, in AbilitySlotState slot)
        {
            current = HashCombine(current, (uint)slot.AbilityId);
            current = HashCombine(current, (uint)slot.TemplateEntityId);
            current = HashCombine(current, (uint)slot.TemplateEntityWorldId);
            current = HashCombine(current, (uint)slot.TemplateEntityVersion);
            return current;
        }

        private static uint HashCombine(uint current, uint value)
        {
            unchecked
            {
                return ((current ^ value) * 16777619u) + 1u;
            }
        }

        private static uint HashTagContainer(uint current, in GameplayTagContainer tags)
        {
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                if (tags.HasTag(tagId))
                {
                    current = HashCombine(current, (uint)tagId);
                }
            }

            return current;
        }

        private enum GasPanelGroupKind : byte
        {
            Current,
            Base,
            RoutePreview,
            Granted
        }

        private enum ProgressionRequirementPreviewResult : byte
        {
            Satisfied,
            Failed,
            Deferred
        }

        private struct ProgressionRequirementPreview
        {
            public ProgressionRequirementPreviewResult Show;
            public ProgressionRequirementPreviewResult Use;
        }
    }
}
