using System;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.UI
{
    public static class EntityCommandPanelShowcaseTheme
    {
        public const string ContextKey = "EntityCommandPanel.ShowcaseTheme";
        public const string ClassicId = "EntityCommandPanel.Showcase.Classic";
        public const string Dota2Id = "EntityCommandPanel.Showcase.Dota2";
        public const string LolId = "EntityCommandPanel.Showcase.LoL";
        public const string Sc2Id = "EntityCommandPanel.Showcase.SC2";

        public static bool IsThemeButton(string? buttonId)
        {
            return string.Equals(buttonId, Dota2Id, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, LolId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, Sc2Id, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string? themeId, string fallback = ClassicId)
        {
            if (string.Equals(themeId, Dota2Id, StringComparison.OrdinalIgnoreCase))
            {
                return Dota2Id;
            }

            if (string.Equals(themeId, LolId, StringComparison.OrdinalIgnoreCase))
            {
                return LolId;
            }

            if (string.Equals(themeId, Sc2Id, StringComparison.OrdinalIgnoreCase))
            {
                return Sc2Id;
            }

            return fallback;
        }

        public static string ResolveLabel(string? themeId)
        {
            string normalized = Normalize(themeId);
            if (string.Equals(normalized, Dota2Id, StringComparison.Ordinal))
            {
                return "Dota2";
            }

            if (string.Equals(normalized, Sc2Id, StringComparison.Ordinal))
            {
                return "SC2";
            }

            if (string.Equals(normalized, LolId, StringComparison.Ordinal))
            {
                return "LoL";
            }

            return "Classic";
        }

        public static EntityCommandPanelAnchor ResolveSandboxAnchor(string? themeId)
        {
            string normalized = Normalize(themeId, LolId);
            if (string.Equals(normalized, Sc2Id, StringComparison.Ordinal))
            {
                return new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomRight, 20f, 18f);
            }

            return new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 18f);
        }

        public static EntityCommandPanelSize ResolveSandboxSize(string? themeId)
        {
            string normalized = Normalize(themeId, LolId);
            if (string.Equals(normalized, Dota2Id, StringComparison.Ordinal))
            {
                return new EntityCommandPanelSize(1236f, 332f);
            }

            if (string.Equals(normalized, Sc2Id, StringComparison.Ordinal))
            {
                return new EntityCommandPanelSize(744f, 430f);
            }

            if (string.Equals(normalized, LolId, StringComparison.Ordinal))
            {
                return new EntityCommandPanelSize(948f, 248f);
            }

            return new EntityCommandPanelSize(460f, 276f);
        }
    }
}
