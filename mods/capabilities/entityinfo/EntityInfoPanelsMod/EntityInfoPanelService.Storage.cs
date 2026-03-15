using System;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private void ClearComponentState(int slot)
    {
        _componentSectionCounts[slot] = 0;
        _componentLineCounts[slot] = 0;
        int sectionBase = slot * MaxComponentSectionsPerPanel;
        for (int i = 0; i < MaxComponentSectionsPerPanel; i++)
        {
            _componentSectionTypeIds[sectionBase + i] = 0;
            _componentSectionLineStarts[sectionBase + i] = 0;
            _componentSectionLineCounts[sectionBase + i] = 0;
            _componentSectionNames[sectionBase + i] = string.Empty;
        }

        int lineBase = slot * MaxComponentLinesPerPanel;
        for (int i = 0; i < MaxComponentLinesPerPanel; i++)
        {
            _componentLines[lineBase + i] = string.Empty;
        }
    }

    private void ClearGasState(int slot)
    {
        _gasLineCounts[slot] = 0;
        int lineBase = slot * MaxGasLinesPerPanel;
        for (int i = 0; i < MaxGasLinesPerPanel; i++)
        {
            _gasLines[lineBase + i] = string.Empty;
        }
    }

    private bool TrimComponentSectionTail(int slot, int sectionCount)
    {
        bool dirty = false;
        int sectionBase = slot * MaxComponentSectionsPerPanel;
        for (int i = sectionCount; i < MaxComponentSectionsPerPanel; i++)
        {
            dirty |= SetInt(_componentSectionTypeIds, sectionBase + i, 0);
            dirty |= SetInt(_componentSectionLineStarts, sectionBase + i, 0);
            dirty |= SetInt(_componentSectionLineCounts, sectionBase + i, 0);
            dirty |= SetString(_componentSectionNames, sectionBase + i, string.Empty);
        }

        return dirty;
    }

    private bool TrimComponentLines(int slot, int lineCount)
    {
        bool dirty = SetInt(_componentLineCounts, slot, lineCount);
        int baseIndex = slot * MaxComponentLinesPerPanel;
        for (int i = lineCount; i < MaxComponentLinesPerPanel; i++)
        {
            dirty |= SetString(_componentLines, baseIndex + i, string.Empty);
        }

        return dirty;
    }

    private bool TrimGasLines(int slot, int lineCount)
    {
        bool dirty = SetInt(_gasLineCounts, slot, lineCount);
        int baseIndex = slot * MaxGasLinesPerPanel;
        for (int i = lineCount; i < MaxGasLinesPerPanel; i++)
        {
            dirty |= SetString(_gasLines, baseIndex + i, string.Empty);
        }

        return dirty;
    }

    private void SetComponentLine(int slot, int lineIndex, string text)
    {
        if ((uint)lineIndex < (uint)MaxComponentLinesPerPanel)
        {
            _componentLines[ComponentLineIndex(slot, lineIndex)] = text;
        }
    }

    private bool SetGasLine(int slot, int lineIndex, string text)
    {
        return (uint)lineIndex < (uint)MaxGasLinesPerPanel &&
               SetString(_gasLines, GasLineIndex(slot, lineIndex), text);
    }

    private static int SectionIndex(int slot, int sectionIndex) => (slot * MaxComponentSectionsPerPanel) + sectionIndex;
    private static int ComponentLineIndex(int slot, int lineIndex) => (slot * MaxComponentLinesPerPanel) + lineIndex;
    private static int GasLineIndex(int slot, int lineIndex) => (slot * MaxGasLinesPerPanel) + lineIndex;

    private static bool SetString(string[] array, int index, string text)
    {
        if (string.Equals(array[index], text, StringComparison.Ordinal))
        {
            return false;
        }

        array[index] = text;
        return true;
    }

    private static bool SetInt(int[] array, int index, int value)
    {
        if (array[index] == value)
        {
            return false;
        }

        array[index] = value;
        return true;
    }
}
