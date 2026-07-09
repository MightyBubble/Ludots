using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private const int PanelCapacity = 96;
    private const int MaxComponentSectionsPerPanel = 64;
    private const int MaxComponentLinesPerPanel = 256;
    private const int MaxGasLinesPerPanel = 320;
    private const int MaxEntityCollectionPreviewAttributes = 4;
    private const int MaxEntityCollectionCategories = 24;
    private const int MaxInsightStatsPerPanel = 8;
    private const int MaxInsightActionsPerPanel = 8;
    private const int ComponentToggleWordCount = 16;

    private readonly EntityInsightProfileCatalog _insightCatalog;
    private readonly EntityInsightTextResolver _insightTextResolver;
    private readonly EntityInsightIconFactory _insightIconFactory = new();
    private readonly IEntityInfoPanelTemplateCatalog _templates;
    private readonly AbilityDefinitionRegistry? _abilityDefinitions;
    private readonly TagOps _tagOps;

    private readonly bool[] _active = new bool[PanelCapacity];
    private readonly int[] _generation = new int[PanelCapacity];
    private readonly EntityInfoPanelKind[] _kinds = new EntityInfoPanelKind[PanelCapacity];
    private readonly EntityInfoPanelSurface[] _surfaces = new EntityInfoPanelSurface[PanelCapacity];
    private readonly EntityInfoPanelTarget[] _targets = new EntityInfoPanelTarget[PanelCapacity];
    private readonly EntityInfoPanelLayout[] _layouts = new EntityInfoPanelLayout[PanelCapacity];
    private readonly EntityInfoGasDetailFlags[] _gasFlags = new EntityInfoGasDetailFlags[PanelCapacity];
    private readonly string[] _templateIds = new string[PanelCapacity];
    private readonly EntityInfoPanelTemplateDescriptor[] _resolvedTemplates = new EntityInfoPanelTemplateDescriptor[PanelCapacity];
    private readonly bool[] _visible = new bool[PanelCapacity];
    private readonly Entity[] _resolvedTargets = new Entity[PanelCapacity];
    private readonly string[] _titles = new string[PanelCapacity];
    private readonly string[] _subtitles = new string[PanelCapacity];
    private readonly int[] _contentSerials = new int[PanelCapacity];
    private readonly ulong[] _componentDisabled = new ulong[PanelCapacity * ComponentToggleWordCount];

    private readonly int[] _componentSectionCounts = new int[PanelCapacity];
    private readonly int[] _componentSectionTypeIds = new int[PanelCapacity * MaxComponentSectionsPerPanel];
    private readonly int[] _componentSectionLineStarts = new int[PanelCapacity * MaxComponentSectionsPerPanel];
    private readonly int[] _componentSectionLineCounts = new int[PanelCapacity * MaxComponentSectionsPerPanel];
    private readonly string[] _componentSectionNames = new string[PanelCapacity * MaxComponentSectionsPerPanel];
    private readonly int[] _componentLineCounts = new int[PanelCapacity];
    private readonly string[] _componentLines = new string[PanelCapacity * MaxComponentLinesPerPanel];

    private readonly int[] _gasLineCounts = new int[PanelCapacity];
    private readonly string[] _gasLines = new string[PanelCapacity * MaxGasLinesPerPanel];
    private readonly Entity[] _entityCollectionContainers = new Entity[PanelCapacity];
    private readonly Entity[] _entityCollectionPrimaries = new Entity[PanelCapacity];
    private readonly Entity[] _entityCollectionOwners = new Entity[PanelCapacity];
    private readonly EntityCollectionHandle[] _entityCollectionHandles = new EntityCollectionHandle[PanelCapacity];
    private readonly EntityCollectionSourceKind[] _entityCollectionSourceKinds = new EntityCollectionSourceKind[PanelCapacity];
    private readonly string[] _entityCollectionSourceTitles = new string[PanelCapacity];
    private readonly string[] _entityCollectionSourceSummaries = new string[PanelCapacity];
    private readonly string[] _entityCollectionViewKeys = new string[PanelCapacity];
    private readonly string[] _entityCollectionSetKeys = new string[PanelCapacity];
    private readonly int[] _entityCollectionCounts = new int[PanelCapacity];
    private readonly uint[] _entityCollectionRevisions = new uint[PanelCapacity];
    private readonly int[] _entityCollectionCategoryCounts = new int[PanelCapacity];
    private readonly string[] _entityCollectionCategoryLabels = new string[PanelCapacity * MaxEntityCollectionCategories];
    private readonly int[] _entityCollectionCategoryMembers = new int[PanelCapacity * MaxEntityCollectionCategories];
    private readonly bool[] _entityCollectionCategoryContainsPrimary = new bool[PanelCapacity * MaxEntityCollectionCategories];
    private readonly int[] _insightProfileIndices = new int[PanelCapacity];
    private readonly int[] _insightStatCounts = new int[PanelCapacity];
    private readonly float[] _insightStatCurrentValues = new float[PanelCapacity * MaxInsightStatsPerPanel];
    private readonly float[] _insightStatBaseValues = new float[PanelCapacity * MaxInsightStatsPerPanel];
    private readonly int[] _insightActionCounts = new int[PanelCapacity];
    private readonly byte[] _insightActionFlags = new byte[PanelCapacity * MaxInsightActionsPerPanel];

    private readonly int[] _visibleUiSlots = new int[PanelCapacity];
    private readonly int[] _visibleOverlaySlots = new int[PanelCapacity];
    private readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();

    private int _visibleUiCount;
    private int _visibleOverlayCount;
    private int _uiRevision;
    private int _overlayRevision;
    private bool _pendingUiInvalidation;
    private bool _pendingOverlayInvalidation;
    private int _lastLocaleId;
    private World? _sampledWorld;
    private Dictionary<string, object>? _sampledGlobals;

    public EntityInfoPanelService(
        EntityInsightProfileCatalog? insightCatalog = null,
        PresentationTextCatalog? presentationTextCatalog = null,
        PresentationTextLocaleSelection? localeSelection = null,
        AbilityDefinitionRegistry? abilityDefinitions = null,
        TagOps? tagOps = null,
        IEntityInfoPanelTemplateCatalog? templates = null)
    {
        PresentationTextCatalog effectiveCatalog = presentationTextCatalog ?? PresentationTextCatalog.Empty;
        PresentationTextLocaleSelection effectiveLocaleSelection = localeSelection ?? new PresentationTextLocaleSelection(effectiveCatalog);

        _insightCatalog = insightCatalog ?? EntityInsightProfileCatalog.Empty;
        _insightTextResolver = new EntityInsightTextResolver(effectiveCatalog, effectiveLocaleSelection);
        _templates = templates ?? new EntityInfoPanelTemplateCatalog();
        _abilityDefinitions = abilityDefinitions;
        _tagOps = tagOps ?? new TagOps();
        _lastLocaleId = effectiveLocaleSelection.ActiveLocaleId;
    }

    public int UiRevision => _uiRevision;
    public int OverlayRevision => _overlayRevision;

    public EntityInfoPanelHandle Open(in EntityInfoPanelRequest request)
    {
        int slot = FindFreeSlot();
        if (slot < 0)
        {
            return EntityInfoPanelHandle.Invalid;
        }

        _active[slot] = true;
        _generation[slot] = Math.Max(1, _generation[slot] + 1);
        _kinds[slot] = request.Kind;
        _surfaces[slot] = request.Surface;
        _targets[slot] = request.Target;
        _layouts[slot] = request.Layout;
        _gasFlags[slot] = request.GasDetailFlags;
        _templateIds[slot] = ResolveTemplateId(request.TemplateId);
        _resolvedTemplates[slot] = ResolveTemplateDescriptor(_templateIds[slot]);
        _visible[slot] = request.Visible;
        _resolvedTargets[slot] = Entity.Null;
        _titles[slot] = string.Empty;
        _subtitles[slot] = string.Empty;
        _contentSerials[slot] = 1;
        ResetComponentToggleState(slot, enabled: true);
        ClearComponentState(slot);
        ClearGasState(slot);
        ClearEntityCollectionState(slot);
        ClearInsightState(slot);
        InvalidateSurface(request.Surface);
        return new EntityInfoPanelHandle(slot, _generation[slot]);
    }

    public bool Close(EntityInfoPanelHandle handle)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        EntityInfoPanelSurface surface = _surfaces[slot];
        _active[slot] = false;
        _visible[slot] = false;
        _surfaces[slot] = EntityInfoPanelSurface.None;
        _targets[slot] = default;
        _templateIds[slot] = string.Empty;
        _resolvedTemplates[slot] = null!;
        _resolvedTargets[slot] = Entity.Null;
        _titles[slot] = string.Empty;
        _subtitles[slot] = string.Empty;
        ResetComponentToggleState(slot, enabled: true);
        ClearComponentState(slot);
        ClearGasState(slot);
        ClearEntityCollectionState(slot);
        ClearInsightState(slot);
        InvalidateSurface(surface);
        return true;
    }

    public bool SetVisible(EntityInfoPanelHandle handle, bool visible)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        _visible[slot] = visible;
        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool UpdateLayout(EntityInfoPanelHandle handle, in EntityInfoPanelLayout layout)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        _layouts[slot] = layout;
        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool UpdateTarget(EntityInfoPanelHandle handle, in EntityInfoPanelTarget target)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        _targets[slot] = target;
        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool UpdateTemplate(EntityInfoPanelHandle handle, string templateId)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        string resolved = ResolveTemplateId(templateId);
        EntityInfoPanelTemplateDescriptor descriptor = ResolveTemplateDescriptor(resolved);
        if (string.Equals(_templateIds[slot], resolved, StringComparison.Ordinal))
        {
            return false;
        }

        _templateIds[slot] = resolved;
        _resolvedTemplates[slot] = descriptor;
        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool UpdateGasDetailFlags(EntityInfoPanelHandle handle, EntityInfoGasDetailFlags flags)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        _gasFlags[slot] = flags;
        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool SetComponentEnabled(EntityInfoPanelHandle handle, int componentTypeId, bool enabled)
    {
        if (!TryValidateHandle(handle, out int slot) ||
            componentTypeId < 0 ||
            componentTypeId >= ComponentToggleWordCount * 64)
        {
            return false;
        }

        int index = (slot * ComponentToggleWordCount) + (componentTypeId >> 6);
        ulong bit = 1UL << (componentTypeId & 63);
        if (enabled)
        {
            _componentDisabled[index] &= ~bit;
        }
        else
        {
            _componentDisabled[index] |= bit;
        }

        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public bool SetAllComponentsEnabled(EntityInfoPanelHandle handle, bool enabled)
    {
        if (!TryValidateHandle(handle, out int slot))
        {
            return false;
        }

        int baseIndex = slot * ComponentToggleWordCount;
        for (int i = 0; i < ComponentToggleWordCount; i++)
        {
            _componentDisabled[baseIndex + i] = enabled ? 0UL : ulong.MaxValue;
        }

        InvalidateSurface(_surfaces[slot]);
        return true;
    }

    public int GetVisibleUiCount() => _visibleUiCount;
    public int GetVisibleUiSlot(int index) => _visibleUiSlots[index];
    public EntityInfoPanelHandle GetHandle(int slot) => new(slot, _generation[slot]);
    public EntityInfoPanelKind GetKind(int slot) => _kinds[slot];
    public EntityInfoPanelLayout GetLayout(int slot) => _layouts[slot];
    public EntityInfoGasDetailFlags GetGasDetailFlags(int slot) => _gasFlags[slot];
    public string GetTemplateId(int slot) => _templateIds[slot] ?? string.Empty;
    public string GetTitle(int slot) => _titles[slot] ?? string.Empty;
    public string GetSubtitle(int slot) => _subtitles[slot] ?? string.Empty;
    public int GetActiveLocaleId() => _insightTextResolver.ActiveLocaleId;
    public int GetEntityCollectionCount(int slot) => _entityCollectionCounts[slot];
    public string GetEntityCollectionSourceTitle(int slot) => _entityCollectionSourceTitles[slot] ?? string.Empty;
    public string GetEntityCollectionSourceSummary(int slot) => _entityCollectionSourceSummaries[slot] ?? string.Empty;
    public string GetEntityCollectionViewKey(int slot) => _entityCollectionViewKeys[slot] ?? string.Empty;
    public string GetEntityCollectionSetKey(int slot) => _entityCollectionSetKeys[slot] ?? string.Empty;
    public int GetEntityCollectionCategoryCount(int slot) => _entityCollectionCategoryCounts[slot];
    public int GetComponentSectionCount(int slot) => _componentSectionCounts[slot];
    public int GetComponentSectionTypeId(int slot, int sectionIndex) => _componentSectionTypeIds[SectionIndex(slot, sectionIndex)];
    public string GetComponentSectionName(int slot, int sectionIndex) => _componentSectionNames[SectionIndex(slot, sectionIndex)] ?? string.Empty;
    public bool IsComponentExpanded(int slot, int sectionIndex) => IsComponentEnabled(slot, GetComponentSectionTypeId(slot, sectionIndex));
    public int GetComponentSectionLineCount(int slot, int sectionIndex) => _componentSectionLineCounts[SectionIndex(slot, sectionIndex)];

    public string GetComponentSectionLine(int slot, int sectionIndex, int lineIndex)
    {
        int section = SectionIndex(slot, sectionIndex);
        int localLine = _componentSectionLineStarts[section] + lineIndex;
        return _componentLines[ComponentLineIndex(slot, localLine)] ?? string.Empty;
    }

    public int GetGasLineCount(int slot) => _gasLineCounts[slot];
    public string GetGasLine(int slot, int lineIndex) => _gasLines[GasLineIndex(slot, lineIndex)] ?? string.Empty;

    public bool TryGetEntityCollectionRow(int slot, int index, out EntityCollectionPanelRow row)
    {
        row = default;
        if ((uint)slot >= (uint)PanelCapacity ||
            _kinds[slot] != EntityInfoPanelKind.EntityCollectionInspector ||
            index < 0 ||
            _sampledWorld == null ||
            _sampledGlobals == null)
        {
            return false;
        }

        if (!TryGetEntityCollectionEntityAt(slot, index, out Entity entity) ||
            !_sampledWorld.IsAlive(entity))
        {
            return false;
        }

        string name = ResolveEntityDisplayName(_sampledWorld, entity);
        string attributesSummary = BuildEntityAttributePreview(_sampledWorld, entity);
        EntityInfoPanelTemplateDescriptor template = ResolvePanelTemplate(slot);
        string templateSubtitle = ResolveTemplateSubtitle(_sampledWorld, entity, template, required: false);
        string templateBody = ResolveTemplateBody(_sampledWorld, entity, template, required: false);
        string accent = ResolveTemplateAccent(_sampledWorld, entity, template, required: false);
        row = new EntityCollectionPanelRow(
            index,
            entity.Id,
            name,
            attributesSummary,
            entity == _entityCollectionPrimaries[slot],
            template.Id,
            templateSubtitle,
            templateBody,
            accent);
        return true;
    }

    private bool TryGetEntityCollectionEntityAt(int slot, int index, out Entity entity)
    {
        entity = default;
        if (_sampledWorld == null ||
            _sampledGlobals == null ||
            (uint)slot >= (uint)PanelCapacity ||
            _kinds[slot] != EntityInfoPanelKind.EntityCollectionInspector ||
            index < 0)
        {
            return false;
        }

        if (!_sampledGlobals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) ||
            storeObj is not EntityCollectionStore store)
        {
            return false;
        }

        return store.TryGetEntityAt(_entityCollectionHandles[slot], index, out entity);
    }

    public bool TryGetEntityCollectionCategory(int slot, int index, out EntityCollectionCategorySummary summary)
    {
        summary = default;
        if ((uint)slot >= (uint)PanelCapacity ||
            index < 0 ||
            index >= _entityCollectionCategoryCounts[slot])
        {
            return false;
        }

        int categoryIndex = EntityCollectionCategoryIndex(slot, index);
        summary = new EntityCollectionCategorySummary(
            _entityCollectionCategoryLabels[categoryIndex] ?? string.Empty,
            _entityCollectionCategoryMembers[categoryIndex],
            _entityCollectionCategoryContainsPrimary[categoryIndex]);
        return true;
    }

    private bool IsComponentEnabled(int slot, int componentTypeId)
    {
        if (componentTypeId < 0 || componentTypeId >= ComponentToggleWordCount * 64)
        {
            return true;
        }

        int index = (slot * ComponentToggleWordCount) + (componentTypeId >> 6);
        ulong bit = 1UL << (componentTypeId & 63);
        return (_componentDisabled[index] & bit) == 0;
    }

    private bool TryValidateHandle(EntityInfoPanelHandle handle, out int slot)
    {
        slot = handle.Slot;
        return handle.IsValid &&
               (uint)slot < (uint)PanelCapacity &&
               _active[slot] &&
               _generation[slot] == handle.Generation;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < PanelCapacity; i++)
        {
            if (!_active[i])
            {
                return i;
            }
        }

        return -1;
    }

    private void ResetComponentToggleState(int slot, bool enabled)
    {
        int baseIndex = slot * ComponentToggleWordCount;
        ulong value = enabled ? 0UL : ulong.MaxValue;
        for (int i = 0; i < ComponentToggleWordCount; i++)
        {
            _componentDisabled[baseIndex + i] = value;
        }
    }

    private void InvalidateSurface(EntityInfoPanelSurface surface)
    {
        if ((surface & EntityInfoPanelSurface.Ui) != 0)
        {
            _pendingUiInvalidation = true;
        }

        if ((surface & EntityInfoPanelSurface.Overlay) != 0)
        {
            _pendingOverlayInvalidation = true;
        }
    }

    private static string ResolveTemplateId(string? templateId)
    {
        return string.IsNullOrWhiteSpace(templateId)
            ? EntityInfoPanelTemplateCatalog.DefaultTemplateId
            : templateId.Trim();
    }

    private EntityInfoPanelTemplateDescriptor ResolveTemplateDescriptor(string templateId)
    {
        if (!_templates.TryGet(templateId, out EntityInfoPanelTemplateDescriptor descriptor))
        {
            throw new InvalidOperationException($"Entity info panel template '{templateId}' is not registered.");
        }

        if (!string.IsNullOrWhiteSpace(descriptor.HeaderTokenKey))
        {
            _ = _insightTextResolver.ResolveRequiredTokenKey(descriptor.HeaderTokenKey);
        }

        if (!string.IsNullOrWhiteSpace(descriptor.EmptyTokenKey))
        {
            _ = _insightTextResolver.ResolveRequiredTokenKey(descriptor.EmptyTokenKey);
        }

        return descriptor;
    }

    private EntityInfoPanelTemplateDescriptor ResolvePanelTemplate(int slot)
    {
        EntityInfoPanelTemplateDescriptor? descriptor = _resolvedTemplates[slot];
        if (descriptor != null)
        {
            return descriptor;
        }

        string templateId = ResolveTemplateId(_templateIds[slot]);
        descriptor = ResolveTemplateDescriptor(templateId);
        _templateIds[slot] = templateId;
        _resolvedTemplates[slot] = descriptor;
        return descriptor;
    }

    private string ResolveTemplateSubtitle(World world, Entity entity, EntityInfoPanelTemplateDescriptor template, bool required)
    {
        if ((template.Sections & EntityInfoPanelTemplateSectionFlags.Subtitle) == 0)
        {
            return string.Empty;
        }

        return TryGetEntityInsightProfile(world, entity, out EntityInsightProfile profile)
            ? ResolveTextTokenId(profile.SubtitleTokenId)
            : ResolveMissingTemplateProfile(template, entity, required);
    }

    private string ResolveTemplateBody(World world, Entity entity, EntityInfoPanelTemplateDescriptor template, bool required)
    {
        if ((template.Sections & EntityInfoPanelTemplateSectionFlags.Body) == 0)
        {
            return string.Empty;
        }

        return TryGetEntityInsightProfile(world, entity, out EntityInsightProfile profile)
            ? ResolveTextTokenId(profile.BodyTokenId)
            : ResolveMissingTemplateProfile(template, entity, required);
    }

    private string ResolveTemplateAccent(World world, Entity entity, EntityInfoPanelTemplateDescriptor template, bool required)
    {
        if (!TryGetEntityInsightProfile(world, entity, out EntityInsightProfile profile))
        {
            _ = ResolveMissingTemplateProfile(template, entity, required);
            return string.Empty;
        }

        return profile.AccentColorHex;
    }

    private static string ResolveMissingTemplateProfile(EntityInfoPanelTemplateDescriptor template, Entity entity, bool required)
    {
        if (required || template.RequireInsightProfile)
        {
            throw new InvalidOperationException(
                $"Entity info panel template '{template.Id}' requires an insight profile for entity {entity.Id}.");
        }

        return string.Empty;
    }
}
