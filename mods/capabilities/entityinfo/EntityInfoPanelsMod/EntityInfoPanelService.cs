using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private const int PanelCapacity = 96;
    private const int MaxComponentSectionsPerPanel = 64;
    private const int MaxComponentLinesPerPanel = 256;
    private const int MaxGasLinesPerPanel = 320;
    private const int ComponentToggleWordCount = 16;

    private readonly bool[] _active = new bool[PanelCapacity];
    private readonly int[] _generation = new int[PanelCapacity];
    private readonly EntityInfoPanelKind[] _kinds = new EntityInfoPanelKind[PanelCapacity];
    private readonly EntityInfoPanelSurface[] _surfaces = new EntityInfoPanelSurface[PanelCapacity];
    private readonly EntityInfoPanelTarget[] _targets = new EntityInfoPanelTarget[PanelCapacity];
    private readonly EntityInfoPanelLayout[] _layouts = new EntityInfoPanelLayout[PanelCapacity];
    private readonly EntityInfoGasDetailFlags[] _gasFlags = new EntityInfoGasDetailFlags[PanelCapacity];
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

    private readonly int[] _visibleUiSlots = new int[PanelCapacity];
    private readonly int[] _visibleOverlaySlots = new int[PanelCapacity];
    private readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();

    private int _visibleUiCount;
    private int _visibleOverlayCount;
    private int _uiRevision;
    private int _overlayRevision;
    private bool _pendingUiInvalidation;
    private bool _pendingOverlayInvalidation;

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
        _visible[slot] = request.Visible;
        _resolvedTargets[slot] = Entity.Null;
        _titles[slot] = string.Empty;
        _subtitles[slot] = string.Empty;
        _contentSerials[slot] = 1;
        ClearComponentState(slot);
        ClearGasState(slot);
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
        _resolvedTargets[slot] = Entity.Null;
        _titles[slot] = string.Empty;
        _subtitles[slot] = string.Empty;
        ClearComponentState(slot);
        ClearGasState(slot);
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
    public string GetTitle(int slot) => _titles[slot] ?? string.Empty;
    public string GetSubtitle(int slot) => _subtitles[slot] ?? string.Empty;
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
}
