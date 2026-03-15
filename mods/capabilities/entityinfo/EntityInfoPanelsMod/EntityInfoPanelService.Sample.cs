using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    public void Refresh(World world, Dictionary<string, object> globals)
    {
        bool uiDirty = _pendingUiInvalidation;
        bool overlayDirty = _pendingOverlayInvalidation;
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

        string title = _kinds[slot] == EntityInfoPanelKind.ComponentInspector
            ? "Entity Component Inspector"
            : "Entity GAS Inspector";
        string subtitle = resolved != Entity.Null && world.IsAlive(resolved)
            ? ResolveEntityLabel(world, resolved)
            : ResolveMissingSubtitle(_targets[slot]);

        dirty |= SetString(_titles, slot, title);
        dirty |= SetString(_subtitles, slot, subtitle);
        dirty |= _kinds[slot] == EntityInfoPanelKind.ComponentInspector
            ? SampleComponentInspector(slot, world, resolved)
            : SampleGasInspector(slot, world, resolved);

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
}
