using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class GasEntityCommandPanelSource : IEntityCommandPanelSource
    {
        private readonly GameEngine _engine;
        private readonly Dictionary<int, string[]> _routeLabelCache = new();

        public GasEntityCommandPanelSource(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public const string SourceId = "gas.ability-slots";

        public bool TryGetRevision(Entity target, out uint revision)
        {
            revision = 0;
            if (!_engine.World.IsAlive(target) || !_engine.World.Has<AbilityStateBuffer>(target))
            {
                return false;
            }

            ref var baseSlots = ref _engine.World.Get<AbilityStateBuffer>(target);
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

            return true;
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
                GasPanelGroupKind.Current => "Current",
                GasPanelGroupKind.Base => "Base",
                GasPanelGroupKind.RoutePreview => ResolveRouteLabel(target, routeIndex),
                GasPanelGroupKind.Granted => "Granted",
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

                destination[slotIndex] = new EntityCommandPanelSlotView(
                    slotIndex,
                    effective.AbilityId,
                    effective.TemplateEntityId,
                    flags,
                    0,
                    0,
                    0);
            }

            return count;
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
                return $"Form {routeIndex + 1}";
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

            return $"Form {routeIndex + 1}";
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
                    labels[routeIndex] = $"Form {routeIndex + 1}: {required}";
                }
                else if (!string.IsNullOrWhiteSpace(blocked))
                {
                    labels[routeIndex] = $"Form {routeIndex + 1}: !{blocked}";
                }
                else
                {
                    labels[routeIndex] = $"Form {routeIndex + 1}";
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

        private enum GasPanelGroupKind : byte
        {
            Current,
            Base,
            RoutePreview,
            Granted
        }
    }
}
