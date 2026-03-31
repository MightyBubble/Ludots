using System;
using EntityInfoPanelsMod.Insight;

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

    private void ClearInsightState(int slot)
    {
        _insightProfileIndices[slot] = 0;
        _insightStatCounts[slot] = 0;
        _insightActionCounts[slot] = 0;

        int statBase = slot * MaxInsightStatsPerPanel;
        for (int i = 0; i < MaxInsightStatsPerPanel; i++)
        {
            _insightStatCurrentValues[statBase + i] = 0f;
            _insightStatBaseValues[statBase + i] = 0f;
        }

        int actionBase = slot * MaxInsightActionsPerPanel;
        for (int i = 0; i < MaxInsightActionsPerPanel; i++)
        {
            _insightActionFlags[actionBase + i] = 0;
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

    private static int InsightStatIndex(int slot, int statIndex) => (slot * MaxInsightStatsPerPanel) + statIndex;
    private static int InsightActionIndex(int slot, int actionIndex) => (slot * MaxInsightActionsPerPanel) + actionIndex;

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

    private bool SetInsightProfileIndex(int slot, int profileIndex)
    {
        return SetInt(_insightProfileIndices, slot, profileIndex);
    }

    private bool SetInsightStatCount(int slot, int count)
    {
        return SetInt(_insightStatCounts, slot, count);
    }

    private bool SetInsightActionCount(int slot, int count)
    {
        return SetInt(_insightActionCounts, slot, count);
    }

    private bool SetInsightStatValue(float[] array, int index, float value)
    {
        if (Math.Abs(array[index] - value) <= 0.0001f)
        {
            return false;
        }

        array[index] = value;
        return true;
    }

    private bool SetInsightActionFlags(int slot, int actionIndex, EntityInsightActionRuntimeFlags flags)
    {
        int index = InsightActionIndex(slot, actionIndex);
        byte value = (byte)flags;
        if (_insightActionFlags[index] == value)
        {
            return false;
        }

        _insightActionFlags[index] = value;
        return true;
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
