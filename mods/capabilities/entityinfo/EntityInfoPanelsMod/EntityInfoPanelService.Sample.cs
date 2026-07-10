using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    public void Refresh(World world, Dictionary<string, object> globals)
    {
        _sampledWorld = world;
        _sampledGlobals = globals;
        bool uiDirty = _pendingUiInvalidation;
        bool overlayDirty = _pendingOverlayInvalidation;
        if (_lastLocaleId != _insightTextResolver.ActiveLocaleId)
        {
            _lastLocaleId = _insightTextResolver.ActiveLocaleId;
            uiDirty = true;
            overlayDirty = true;
        }

        int nextUiCount = 0;
        int nextOverlayCount = 0;

        for (int slot = 0; slot < PanelCapacity; slot++)
        {
            if (!_active[slot] || !_visible[slot])
            {
                continue;
            }

            if ((_surfaces[slot] & EntityInfoPanelSurface.Ui) != 0)
            {
                if (_visibleUiSlots[nextUiCount] != slot)
                {
                    uiDirty = true;
                }

                _visibleUiSlots[nextUiCount++] = slot;
            }

            if ((_surfaces[slot] & EntityInfoPanelSurface.Overlay) != 0)
            {
                if (_visibleOverlaySlots[nextOverlayCount] != slot)
                {
                    overlayDirty = true;
                }

                _visibleOverlaySlots[nextOverlayCount++] = slot;
            }

            bool panelDirty = SamplePanel(slot, world, globals);
            if (!panelDirty)
            {
                continue;
            }

            if ((_surfaces[slot] & EntityInfoPanelSurface.Ui) != 0)
            {
                uiDirty = true;
            }

            if ((_surfaces[slot] & EntityInfoPanelSurface.Overlay) != 0)
            {
                overlayDirty = true;
            }
        }

        if (_visibleUiCount != nextUiCount)
        {
            uiDirty = true;
        }

        if (_visibleOverlayCount != nextOverlayCount)
        {
            overlayDirty = true;
        }

        _visibleUiCount = nextUiCount;
        _visibleOverlayCount = nextOverlayCount;

        if (uiDirty)
        {
            _uiRevision++;
        }

        if (overlayDirty)
        {
            _overlayRevision++;
        }

        _pendingUiInvalidation = false;
        _pendingOverlayInvalidation = false;
    }

    private bool SamplePanel(int slot, World world, Dictionary<string, object> globals)
    {
        bool dirty = false;
        Entity resolved = ResolveTarget(_targets[slot], world, globals);
        if (_resolvedTargets[slot] != resolved)
        {
            _resolvedTargets[slot] = resolved;
            dirty = true;
        }

        switch (_kinds[slot])
        {
            case EntityInfoPanelKind.ComponentInspector:
            {
                string subtitle = resolved != Entity.Null && world.IsAlive(resolved)
                    ? ResolveEntityLabel(world, resolved)
                    : ResolveMissingSubtitle(_targets[slot]);
                dirty |= SetString(_titles, slot, "Entity Component Inspector");
                dirty |= SetString(_subtitles, slot, subtitle);
                dirty |= SampleComponentInspector(slot, world, resolved);
                break;
            }
            case EntityInfoPanelKind.GasInspector:
            {
                string subtitle = resolved != Entity.Null && world.IsAlive(resolved)
                    ? ResolveEntityLabel(world, resolved)
                    : ResolveMissingSubtitle(_targets[slot]);
                dirty |= SetString(_titles, slot, "Entity GAS Inspector");
                dirty |= SetString(_subtitles, slot, subtitle);
                dirty |= SampleGasInspector(slot, world, resolved);
                break;
            }
            case EntityInfoPanelKind.EntityCollectionInspector:
            {
                dirty |= SetString(_titles, slot, "Entity Collection Inspector");
                dirty |= SampleEntityCollectionInspector(slot, world, globals);
                break;
            }
            default:
                dirty |= SampleInsightBrief(slot, world, resolved);
                break;
        }

        if (dirty)
        {
            _contentSerials[slot]++;
        }

        return dirty;
    }

    private bool SampleComponentInspector(int slot, World world, Entity entity)
    {
        bool dirty = false;
        int sectionCount = 0;
        int lineCursor = 0;

        if (entity != Entity.Null && world.IsAlive(entity))
        {
            Signature signature = world.GetSignature(entity);
            Span<ComponentType> components = signature.Components;
            Span<ComponentType> sorted = components.Length <= 64 ? stackalloc ComponentType[components.Length] : new ComponentType[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                sorted[i] = components[i];
            }

            for (int i = 1; i < sorted.Length; i++)
            {
                ComponentType value = sorted[i];
                int j = i - 1;
                while (j >= 0 && sorted[j].Id > value.Id)
                {
                    sorted[j + 1] = sorted[j];
                    j--;
                }

                sorted[j + 1] = value;
            }

            int count = Math.Min(sorted.Length, MaxComponentSectionsPerPanel);
            for (int i = 0; i < count; i++)
            {
                ComponentType componentType = sorted[i];
                int section = SectionIndex(slot, i);
                int lineStart = lineCursor;
                dirty |= SetInt(_componentSectionTypeIds, section, componentType.Id);
                dirty |= SetString(_componentSectionNames, section, componentType.Type.Name);

                if (IsComponentEnabled(slot, componentType.Id))
                {
                    lineCursor += WriteComponentLines(entity, world, componentType, slot, lineCursor);
                }

                dirty |= SetInt(_componentSectionLineStarts, section, lineStart);
                dirty |= SetInt(_componentSectionLineCounts, section, lineCursor - lineStart);
                sectionCount++;
            }
        }

        dirty |= SetInt(_componentSectionCounts, slot, sectionCount);
        dirty |= TrimComponentSectionTail(slot, sectionCount);
        dirty |= TrimComponentLines(slot, lineCursor);
        return dirty;
    }

    private bool SampleGasInspector(int slot, World world, Entity entity)
    {
        bool dirty = false;
        int lineCount = 0;

        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            dirty |= SetGasLine(slot, lineCount++, "Target unavailable.");
            dirty |= TrimGasLines(slot, lineCount);
            return dirty;
        }

        bool hasCounts = world.TryGet(entity, out TagCountContainer counts);
        bool hasStaticTags = world.TryGet(entity, out GameplayTagContainer staticTags);
        bool hasEffective = world.TryGet(entity, out GameplayTagEffectiveCache effectiveCache);
        bool hasAttributes = world.TryGet(entity, out AttributeBuffer attributes);
        bool hasEffects = world.TryGet(entity, out ActiveEffectContainer activeEffects);

        dirty |= SetGasLine(slot, lineCount++, "[Tags]");
        bool wroteTag = false;
        for (int tagId = 1; tagId < TagRegistry.MaxTags && lineCount < MaxGasLinesPerPanel; tagId++)
        {
            ushort count = hasCounts ? counts.GetCount(tagId) : (ushort)0;
            bool effective = hasEffective && effectiveCache.Has(tagId);
            bool granted = hasStaticTags && staticTags.HasTag(tagId);
            if (count == 0 && !effective && !granted)
            {
                continue;
            }

            wroteTag = true;
            string tagName = TagRegistry.GetName(tagId);
            if (string.IsNullOrEmpty(tagName))
            {
                tagName = $"tag:{tagId}";
            }

            dirty |= SetGasLine(slot, lineCount++, $"  {tagName} | count={count} | effective={effective} | static={granted}");
        }

        if (!wroteTag)
        {
            dirty |= SetGasLine(slot, lineCount++, "  (none)");
        }

        dirty |= SetGasLine(slot, lineCount++, "[Attributes]");
        bool wroteAttribute = false;
        for (int attrId = 0; attrId < AttributeRegistry.MaxAttributes && lineCount < MaxGasLinesPerPanel; attrId++)
        {
            if (!hasAttributes)
            {
                break;
            }

            float baseValue = attributes.GetBase(attrId);
            float currentValue = attributes.GetCurrent(attrId);
            bool hasSource = hasEffects && HasModifierForAttribute(world, in activeEffects, attrId);
            if (baseValue == 0f && currentValue == 0f && !hasSource)
            {
                continue;
            }

            wroteAttribute = true;
            string attrName = AttributeRegistry.GetName(attrId);
            if (string.IsNullOrEmpty(attrName))
            {
                attrName = $"attr:{attrId}";
            }

            dirty |= SetGasLine(slot, lineCount++, $"  {attrName} | current={FormatNumber(currentValue)} | base={FormatNumber(baseValue)}");
            if ((_gasFlags[slot] & EntityInfoGasDetailFlags.ShowAttributeAggregateSources) != 0 && hasEffects)
            {
                dirty |= WriteAttributeSources(slot, ref lineCount, world, in activeEffects, attrId);
            }
        }

        if (!wroteAttribute)
        {
            dirty |= SetGasLine(slot, lineCount++, "  (none)");
        }

        if ((_gasFlags[slot] & EntityInfoGasDetailFlags.ShowModifierState) != 0 && hasEffects)
        {
            dirty |= SetGasLine(slot, lineCount++, "[Modifier State]");
            int before = lineCount;
            for (int effectIndex = 0; effectIndex < activeEffects.Count && lineCount < MaxGasLinesPerPanel; effectIndex++)
            {
                Entity effectEntity = activeEffects.GetEntity(effectIndex);
                if (!world.IsAlive(effectEntity))
                {
                    continue;
                }

                dirty |= SetGasLine(slot, lineCount++, $"  {DescribeEffect(world, effectEntity)}");
            }

            if (lineCount == before)
            {
                dirty |= SetGasLine(slot, lineCount++, "  (none)");
            }
        }

        dirty |= TrimGasLines(slot, lineCount);
        return dirty;
    }

    private bool SampleEntityCollectionInspector(int slot, World world, Dictionary<string, object> globals)
    {
        bool dirty = false;
        Entity container = Entity.Null;
        Entity primary = Entity.Null;
        Entity owner = Entity.Null;
        EntityCollectionHandle collectionHandle = EntityCollectionHandle.Invalid;
        EntityCollectionSourceKind sourceKind = EntityCollectionSourceKind.CollectionView;
        string sourceTitle = string.Empty;
        string sourceSummary = string.Empty;
        string viewKey = string.Empty;
        string setKey = string.Empty;
        int count = 0;
        uint revision = 0;

        EntityInfoPanelTarget target = _targets[slot];
        if (target.Kind == EntityInfoPanelTargetKind.EntityCollection &&
                 TryResolveEntityCollectionSource(world, globals, target, out EntityCollectionView collectionView, out collectionHandle))
        {
            owner = collectionView.Owner;
            primary = collectionView.PrimaryEntity;
            sourceKind = collectionView.SourceKind;
            count = collectionView.Count;
            revision = collectionView.Revision;
            sourceTitle = string.IsNullOrWhiteSpace(collectionView.Title)
                ? collectionView.Key
                : collectionView.Title;
            sourceSummary = string.IsNullOrWhiteSpace(collectionView.Summary)
                ? $"{collectionView.Key} | {collectionView.Count} entities"
                : collectionView.Summary;
            viewKey = collectionView.Key;
            setKey = collectionView.Key;
            dirty |= SetString(_subtitles, slot, sourceSummary);
        }
        else
        {
            sourceTitle = ResolveMissingCollectionTitle(target);
            sourceSummary = ResolveMissingCollectionSummary(target);
            dirty |= SetString(_subtitles, slot, sourceSummary);
        }

        if (_resolvedTargets[slot] != primary)
        {
            _resolvedTargets[slot] = primary;
            dirty = true;
        }

        bool sourceChanged = _entityCollectionContainers[slot] != container ||
                             _entityCollectionHandles[slot] != collectionHandle ||
                             _entityCollectionOwners[slot] != owner ||
                             _entityCollectionSourceKinds[slot] != sourceKind ||
                             _entityCollectionRevisions[slot] != revision;

        if (_entityCollectionContainers[slot] != container)
        {
            _entityCollectionContainers[slot] = container;
            dirty = true;
        }

        if (_entityCollectionOwners[slot] != owner)
        {
            _entityCollectionOwners[slot] = owner;
            dirty = true;
        }

        if (_entityCollectionHandles[slot] != collectionHandle)
        {
            _entityCollectionHandles[slot] = collectionHandle;
            dirty = true;
        }

        if (_entityCollectionSourceKinds[slot] != sourceKind)
        {
            _entityCollectionSourceKinds[slot] = sourceKind;
            dirty = true;
        }

        if (_entityCollectionPrimaries[slot] != primary)
        {
            _entityCollectionPrimaries[slot] = primary;
            dirty = true;
        }

        dirty |= SetString(_entityCollectionSourceTitles, slot, sourceTitle);
        dirty |= SetString(_entityCollectionSourceSummaries, slot, sourceSummary);
        dirty |= SetString(_entityCollectionViewKeys, slot, viewKey);
        dirty |= SetString(_entityCollectionSetKeys, slot, setKey);
        dirty |= SetInt(_entityCollectionCounts, slot, count);
        if (_entityCollectionRevisions[slot] != revision)
        {
            _entityCollectionRevisions[slot] = revision;
            dirty = true;
        }

        if (sourceChanged)
        {
            dirty |= RebuildEntityCollectionCategories(slot, world, primary, count);
        }

        return dirty;
    }

    private bool TryResolveEntityCollectionSource(
        World world,
        Dictionary<string, object> globals,
        in EntityInfoPanelTarget target,
        out EntityCollectionView view,
        out EntityCollectionHandle handle)
    {
        view = default;
        handle = EntityCollectionHandle.Invalid;
        if (target.FixedEntity == Entity.Null ||
            string.IsNullOrWhiteSpace(target.Key) ||
            !world.IsAlive(target.FixedEntity) ||
            !globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) ||
            storeObj is not EntityCollectionStore store)
        {
            return false;
        }

        return store.TryGet(target.FixedEntity, target.Key, out handle) &&
               store.TryGetView(handle, out view);
    }

    private static string ResolveMissingCollectionTitle(in EntityInfoPanelTarget target)
    {
        return target.Kind == EntityInfoPanelTargetKind.EntityCollection
            ? "Entity collection unavailable"
            : "Target collection unavailable";
    }

    private static string ResolveMissingCollectionSummary(in EntityInfoPanelTarget target)
    {
        return target.Kind == EntityInfoPanelTargetKind.EntityCollection
            ? $"Missing collection source '{target.Key}'."
            : "No entity collection target configured.";
    }

    private bool RebuildEntityCollectionCategories(int slot, World world, Entity primary, int count)
    {
        bool dirty = false;
        int categoryCount = 0;
        if (count > 0)
        {
            var order = new List<string>(Math.Min(count, MaxEntityCollectionCategories));
            var countsByLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            var primaryByLabel = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (!TryGetEntityCollectionEntityAt(slot, i, out Entity entity) ||
                    !world.IsAlive(entity))
                {
                    continue;
                }

                string label = ResolveEntityCollectionCategoryLabel(world, entity);
                if (countsByLabel.TryGetValue(label, out int members))
                {
                    countsByLabel[label] = members + 1;
                    primaryByLabel[label] = primaryByLabel[label] || entity == primary;
                    continue;
                }

                order.Add(label);
                countsByLabel[label] = 1;
                primaryByLabel[label] = entity == primary;
            }

            order.Sort((left, right) =>
            {
                int countCompare = countsByLabel[right].CompareTo(countsByLabel[left]);
                return countCompare != 0 ? countCompare : string.CompareOrdinal(left, right);
            });

            categoryCount = Math.Min(order.Count, MaxEntityCollectionCategories);
            for (int i = 0; i < categoryCount; i++)
            {
                string label = order[i];
                int categoryIndex = EntityCollectionCategoryIndex(slot, i);
                dirty |= SetString(_entityCollectionCategoryLabels, categoryIndex, label);
                dirty |= SetInt(_entityCollectionCategoryMembers, categoryIndex, countsByLabel[label]);
                if (_entityCollectionCategoryContainsPrimary[categoryIndex] != primaryByLabel[label])
                {
                    _entityCollectionCategoryContainsPrimary[categoryIndex] = primaryByLabel[label];
                    dirty = true;
                }
            }
        }

        dirty |= SetInt(_entityCollectionCategoryCounts, slot, categoryCount);
        for (int i = categoryCount; i < MaxEntityCollectionCategories; i++)
        {
            int categoryIndex = EntityCollectionCategoryIndex(slot, i);
            dirty |= SetString(_entityCollectionCategoryLabels, categoryIndex, string.Empty);
            dirty |= SetInt(_entityCollectionCategoryMembers, categoryIndex, 0);
            if (_entityCollectionCategoryContainsPrimary[categoryIndex])
            {
                _entityCollectionCategoryContainsPrimary[categoryIndex] = false;
                dirty = true;
            }
        }

        return dirty;
    }
}
