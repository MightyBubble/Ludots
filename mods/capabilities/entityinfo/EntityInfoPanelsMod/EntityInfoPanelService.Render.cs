using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    public void RenderOverlay(ScreenOverlayBuffer overlay, Vector2 resolution)
    {
        int screenWidth = Math.Max(1, (int)resolution.X);
        int screenHeight = Math.Max(1, (int)resolution.Y);

        for (int i = 0; i < _visibleOverlayCount; i++)
        {
            int slot = _visibleOverlaySlots[i];
            EntityInfoPanelLayout layout = _layouts[slot];
            ResolveRect(layout, screenWidth, screenHeight, out int x, out int y, out int width, out int height);

            overlay.AddRect(
                x,
                y,
                width,
                height,
                new Vector4(0.035f, 0.075f, 0.11f, 0.92f),
                new Vector4(0.17f, 0.25f, 0.34f, 1f),
                ComposeStableId(slot, 1),
                ComposeHash(width, height, _contentSerials[slot], 1));

            overlay.AddText(x + 12, y + 10, _titles[slot] ?? string.Empty, 16, new Vector4(0.965f, 0.886f, 0.686f, 1f), ComposeStableId(slot, 2), ComposeTextSerial(_titles[slot], 16));
            overlay.AddText(x + 12, y + 30, _subtitles[slot] ?? string.Empty, 12, new Vector4(0.63f, 0.71f, 0.79f, 1f), ComposeStableId(slot, 3), ComposeTextSerial(_subtitles[slot], 12));

            int lineY = y + 52;
            int bottom = y + height - 14;
            switch (_kinds[slot])
            {
                case EntityInfoPanelKind.ComponentInspector:
                    RenderComponentOverlay(overlay, slot, x + 12, ref lineY, bottom);
                    break;
                case EntityInfoPanelKind.GasInspector:
                    RenderGasOverlay(overlay, slot, x + 12, ref lineY, bottom);
                    break;
                case EntityInfoPanelKind.EntityCollectionInspector:
                    RenderEntityCollectionOverlay(overlay, slot, x + 12, ref lineY, bottom);
                    break;
                default:
                    RenderInsightOverlay(overlay, slot, x + 12, ref lineY, bottom);
                    break;
            }
        }
    }

    private void RenderComponentOverlay(ScreenOverlayBuffer overlay, int slot, int x, ref int y, int bottom)
    {
        int sectionCount = _componentSectionCounts[slot];
        for (int section = 0; section < sectionCount && y <= bottom; section++)
        {
            string sectionName = GetComponentSectionName(slot, section);
            bool expanded = IsComponentExpanded(slot, section);
            string header = $"{(expanded ? "[-]" : "[+]")} {sectionName}";
            overlay.AddText(x, y, header, 13, new Vector4(0.965f, 0.886f, 0.686f, 1f), ComposeStableId(slot, 100 + section), ComposeTextSerial(header, 13));
            y += 18;

            if (!expanded)
            {
                continue;
            }

            int lineCount = GetComponentSectionLineCount(slot, section);
            for (int line = 0; line < lineCount && y <= bottom; line++)
            {
                string text = GetComponentSectionLine(slot, section, line);
                overlay.AddText(x + 12, y, text, 12, new Vector4(0.82f, 0.86f, 0.91f, 1f), ComposeStableId(slot, 1000 + (section * 64) + line), ComposeTextSerial(text, 12));
                y += 16;
            }
        }
    }

    private void RenderGasOverlay(ScreenOverlayBuffer overlay, int slot, int x, ref int y, int bottom)
    {
        int lineCount = _gasLineCounts[slot];
        for (int line = 0; line < lineCount && y <= bottom; line++)
        {
            string text = GetGasLine(slot, line);
            bool sectionLine = text.StartsWith("[", StringComparison.Ordinal);
            overlay.AddText(
                x,
                y,
                text,
                sectionLine ? 13 : 12,
                sectionLine ? new Vector4(0.965f, 0.886f, 0.686f, 1f) : new Vector4(0.82f, 0.86f, 0.91f, 1f),
                ComposeStableId(slot, 2000 + line),
                ComposeTextSerial(text, sectionLine ? 13 : 12));
            y += sectionLine ? 18 : 16;
        }
    }

    private void RenderEntityCollectionOverlay(ScreenOverlayBuffer overlay, int slot, int x, ref int y, int bottom)
    {
        string summary = $"[{_entityCollectionViewKeys[slot]} -> {_entityCollectionAliasKeys[slot]}] count={_entityCollectionCounts[slot]}";
        overlay.AddText(x, y, summary, 13, new Vector4(0.965f, 0.886f, 0.686f, 1f), ComposeStableId(slot, 3000), ComposeTextSerial(summary, 13));
        y += 18;

        int categoryCount = Math.Min(4, _entityCollectionCategoryCounts[slot]);
        if (categoryCount > 0)
        {
            var labels = new List<string>(categoryCount);
            for (int i = 0; i < categoryCount; i++)
            {
                if (!TryGetEntityCollectionCategory(slot, i, out EntityCollectionCategorySummary category))
                {
                    continue;
                }

                labels.Add(category.ContainsPrimary
                    ? $"{category.Label} x{category.Count}*"
                    : $"{category.Label} x{category.Count}");
            }

            if (labels.Count > 0)
            {
                string categoryLine = string.Join(" | ", labels);
                overlay.AddText(x, y, categoryLine, 12, new Vector4(0.82f, 0.86f, 0.91f, 1f), ComposeStableId(slot, 3001), ComposeTextSerial(categoryLine, 12));
                y += 16;
            }
        }

        int maxRows = Math.Min(_entityCollectionCounts[slot], Math.Max(0, (bottom - y) / 18));
        for (int i = 0; i < maxRows && y <= bottom; i++)
        {
            if (!TryGetEntityCollectionRow(slot, i, out EntityCollectionPanelRow row))
            {
                continue;
            }

            string text = $"#{row.EntityId} {row.Name} | {row.AttributesSummary}";
            overlay.AddText(
                x,
                y,
                text,
                12,
                row.IsPrimary ? new Vector4(0.965f, 0.886f, 0.686f, 1f) : new Vector4(0.82f, 0.86f, 0.91f, 1f),
                ComposeStableId(slot, 3010 + i),
                ComposeTextSerial(text, 12));
            y += 16;
        }
    }

    private void RenderInsightOverlay(ScreenOverlayBuffer overlay, int slot, int x, ref int y, int bottom)
    {
        string genre = GetInsightGenreLabel(slot);
        if (!string.IsNullOrWhiteSpace(genre))
        {
            overlay.AddText(x, y, genre, 13, new Vector4(0.965f, 0.886f, 0.686f, 1f), ComposeStableId(slot, 3000), ComposeTextSerial(genre, 13));
            y += 18;
        }

        int statCount = Math.Min(2, GetInsightStatCount(slot));
        for (int statIndex = 0; statIndex < statCount && y <= bottom; statIndex++)
        {
            string statText = $"{GetInsightStatLabel(slot, statIndex)}: {GetInsightStatValueText(slot, statIndex)}";
            overlay.AddText(x, y, statText, 12, new Vector4(0.82f, 0.86f, 0.91f, 1f), ComposeStableId(slot, 3010 + statIndex), ComposeTextSerial(statText, 12));
            y += 16;
        }

        int tipCount = Math.Min(2, GetInsightTipCount(slot));
        for (int tipIndex = 0; tipIndex < tipCount && y <= bottom; tipIndex++)
        {
            string tipText = GetInsightTipText(slot, tipIndex);
            overlay.AddText(x, y, tipText, 12, new Vector4(0.93f, 0.82f, 0.67f, 1f), ComposeStableId(slot, 3030 + tipIndex), ComposeTextSerial(tipText, 12));
            y += 16;
        }
    }

    private static void ResolveRect(EntityInfoPanelLayout layout, int screenWidth, int screenHeight, out int x, out int y, out int width, out int height)
    {
        width = Math.Max(120, (int)layout.Width);
        height = Math.Max(96, (int)layout.Height);
        x = layout.Anchor switch
        {
            EntityInfoPanelAnchor.TopRight or EntityInfoPanelAnchor.BottomRight => screenWidth - width - (int)layout.OffsetX,
            EntityInfoPanelAnchor.TopCenter or EntityInfoPanelAnchor.BottomCenter => ((screenWidth - width) / 2) + (int)layout.OffsetX,
            EntityInfoPanelAnchor.Center => ((screenWidth - width) / 2) + (int)layout.OffsetX,
            _ => (int)layout.OffsetX,
        };
        y = layout.Anchor switch
        {
            EntityInfoPanelAnchor.BottomLeft or EntityInfoPanelAnchor.BottomRight => screenHeight - height - (int)layout.OffsetY,
            EntityInfoPanelAnchor.BottomCenter => screenHeight - height - (int)layout.OffsetY,
            EntityInfoPanelAnchor.TopCenter => (int)layout.OffsetY,
            EntityInfoPanelAnchor.Center => ((screenHeight - height) / 2) + (int)layout.OffsetY,
            _ => (int)layout.OffsetY,
        };
    }

    private static Entity ResolveTarget(EntityInfoPanelTarget target, World world, Dictionary<string, object> globals)
    {
        Entity entity = target.Kind switch
        {
            EntityInfoPanelTargetKind.FixedEntity => target.FixedEntity,
            EntityInfoPanelTargetKind.GlobalEntityKey when globals.TryGetValue(target.Key, out object? value) && value is Entity resolved => resolved,
            EntityInfoPanelTargetKind.CurrentSelectionView when SelectionContextRuntime.TryGetCurrentPrimary(world, globals, out Entity currentPrimary) => currentPrimary,
            _ => Entity.Null,
        };

        return entity != Entity.Null && world.IsAlive(entity) ? entity : Entity.Null;
    }

    private static int ComposeStableId(int slot, int discriminator)
    {
        int hash = 17;
        hash = unchecked((hash * 31) + slot + 1);
        hash = unchecked((hash * 31) + discriminator);
        hash &= int.MaxValue;
        return hash == 0 ? 1 : hash;
    }

    private static int ComposeTextSerial(string? text, int fontSize)
    {
        int hash = 23;
        hash = unchecked((hash * 31) + fontSize);
        hash = unchecked((hash * 31) + (text?.GetHashCode(StringComparison.Ordinal) ?? 0));
        hash &= int.MaxValue;
        return hash == 0 ? 1 : hash;
    }

    private static int ComposeHash(int a, int b, int c, int d)
    {
        int hash = 29;
        hash = unchecked((hash * 31) + a);
        hash = unchecked((hash * 31) + b);
        hash = unchecked((hash * 31) + c);
        hash = unchecked((hash * 31) + d);
        hash &= int.MaxValue;
        return hash == 0 ? 1 : hash;
    }
}
