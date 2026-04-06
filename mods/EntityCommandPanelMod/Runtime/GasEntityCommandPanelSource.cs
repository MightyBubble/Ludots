using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class GasEntityCommandPanelSource : IEntityCommandPanelSource, IEntityCommandPanelActionSource, IEntityCommandPanelSupplementalSource
    {
        private readonly GameEngine _engine;
        private readonly Dictionary<int, string[]> _routeLabelCache = new();
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private readonly EffectTemplateRegistry? _effectTemplates;
        private readonly OrderTypeRegistry? _orderTypes;
        private readonly IClock? _clock;

        public GasEntityCommandPanelSource(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            _effectTemplates = engine.GetService(CoreServiceKeys.EffectTemplateRegistry);
            _orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry);
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
                for (int slotIndex = 0; slotIndex < displayedSlots; slotIndex++)
                {
                    string actionId = ResolvePrimarySkillActionId(inputMapping, slotIndex);
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
            ref var grantedSlots = ref _engine.World.TryGetRef<GrantedSlotBuffer>(target, out bool hasGrantedSlots);
            bool hasActorTags = _engine.World.TryGet(target, out GameplayTagContainer actorTags);
            AbilityDefinitionRegistry? abilityDefinitions = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            string interactionModeKey = inputMapping?.InteractionMode.ToString() ?? string.Empty;

            if (!TryResolveGroup(target, groupIndex, out var kind, out int routeIndex, out AbilityFormSetDefinition formSet, out _))
            {
                return 0;
            }

            int count = Math.Min(destination.Length, ResolveDisplayedSlotCount(target, in baseSlots));
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
                        if (hasFormSlots && formSlots.HasOverride(slotIndex))
                        {
                            effective = formSlots.GetOverride(slotIndex);
                            flags |= EntityCommandSlotStateFlags.FormOverride;
                        }

                        if (hasGrantedSlots && grantedSlots.HasOverride(slotIndex))
                        {
                            effective = grantedSlots.GetOverride(slotIndex);
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

                string actionId = ResolvePrimarySkillActionId(inputMapping, slotIndex);
                string displayLabel = string.Empty;
                string detailLabel = BuildEmptyDetailLabel(actionId);
                short cooldownPermille = 0;
                if (effective.AbilityId > 0 &&
                    abilityDefinitions != null &&
                    abilityDefinitions.TryGet(effective.AbilityId, out var abilityDefinition))
                {
                    string abilityInteractionModeKey = ResolveAbilityInteractionModeKey(interactionModeKey, in abilityDefinition);
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

                    cooldownPermille = ResolveCooldownPermille(target, in abilityDefinition);

                    string fallbackLabel = ResolveFallbackLabel(effective.AbilityId, effective.TemplateEntityId);
                    string fallbackDetail = BuildDefaultDetailLabel(actionId, abilityInteractionModeKey);
                    if (abilityDefinition.HasPresentation && abilityDefinition.Presentation != null)
                    {
                        displayLabel = abilityDefinition.Presentation.ResolveDisplayName(fallbackLabel);
                        detailLabel = abilityDefinition.Presentation.ResolveHintText(abilityInteractionModeKey, fallbackDetail);
                    }
                    else
                    {
                        displayLabel = fallbackLabel;
                        detailLabel = fallbackDetail;
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
                    cooldownPermille,
                    0,
                    0,
                    displayLabel,
                    detailLabel,
                    actionId);
            }

            return count;
        }

        private static string ResolveAbilityInteractionModeKey(string interactionModeKey, in AbilityDefinition abilityDefinition)
        {
            if (abilityDefinition.HasInputBindingOverride &&
                abilityDefinition.InputBindingOverride.HasCastModeOverride)
            {
                return abilityDefinition.InputBindingOverride.CastModeOverride.ToString();
            }

            return interactionModeKey;
        }

        private short ResolveCooldownPermille(Entity target, in AbilityDefinition abilityDefinition)
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

                int totalTicks = ResolveCooldownDurationTicks(in abilityDefinition.ExecSpec, tagId);
                int permille = totalTicks > 0
                    ? Math.Clamp((int)Math.Round(remainingTicks * 1000d / totalTicks), 0, 1000)
                    : 1000;
                bestPermille = Math.Max(bestPermille, permille);
            }

            return (short)bestPermille;
        }

        private static int ResolveCooldownDurationTicks(in AbilityExecSpec execSpec, int tagId)
        {
            int durationTicks = 0;
            for (int itemIndex = 0; itemIndex < execSpec.ItemCount; itemIndex++)
            {
                ExecItemKind kind = execSpec.GetKind(itemIndex);
                if (kind != ExecItemKind.TagClip && kind != ExecItemKind.TagClipTarget)
                {
                    continue;
                }

                if (execSpec.GetTagId(itemIndex) != tagId)
                {
                    continue;
                }

                durationTicks = Math.Max(durationTicks, execSpec.GetDurationTicks(itemIndex));
            }

            return durationTicks;
        }

        public bool ActivateSlot(Entity target, int groupIndex, int slotIndex)
        {
            if (groupIndex != 0 ||
                slotIndex < 0 ||
                !_engine.World.IsAlive(target))
            {
                return false;
            }

            InputOrderMappingSystem? inputMapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            if (inputMapping == null)
            {
                return false;
            }

            string actionId = ResolvePrimarySkillActionId(inputMapping, slotIndex);
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            return inputMapping.TryActivateMappedAction(actionId, preferUiAiming: true);
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

        private static string ResolveOrderActionDetail(in Order order)
        {
            return order.OrderTypeId == 100 && order.Args.I0 >= 0
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

        private static string ResolveActionLabel(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return string.Empty;
            }

            if (actionId.StartsWith("Skill", StringComparison.OrdinalIgnoreCase) &&
                actionId.Length > "Skill".Length)
            {
                return actionId["Skill".Length..].ToUpperInvariant();
            }

            return actionId;
        }

        private static string ResolvePrimarySkillActionId(InputOrderMappingSystem? inputMapping, int slotIndex)
        {
            if (inputMapping == null || slotIndex < 0)
            {
                return string.Empty;
            }

            string best = string.Empty;
            int bestPriority = int.MaxValue;
            foreach (string actionId in inputMapping.GetMappedActionIds())
            {
                InputOrderMapping? mapping = inputMapping.GetMapping(actionId);
                if (!IsSkillSlotMapping(mapping, slotIndex))
                {
                    continue;
                }

                int priority = ResolveActionPriority(actionId);
                if (priority < bestPriority ||
                    (priority == bestPriority && string.CompareOrdinal(actionId, best) < 0))
                {
                    best = actionId;
                    bestPriority = priority;
                }
            }

            return best;
        }

        private static bool IsSkillSlotMapping(InputOrderMapping? mapping, int slotIndex)
        {
            return mapping != null &&
                   mapping.IsSkillMapping &&
                   mapping.ArgsTemplate.I0.HasValue &&
                   mapping.ArgsTemplate.I0.Value == slotIndex;
        }

        private static int ResolveActionPriority(string actionId)
        {
            return actionId switch
            {
                "SkillQ" => 0,
                "SkillW" => 1,
                "SkillE" => 2,
                "SkillR" => 3,
                _ => 100
            };
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
    }
}
