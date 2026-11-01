using System;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.UI
{
    public static class EntityCommandPanelShowcaseTheme
    {
        public const string PrefKey = "pref.panel.theme";
        public const string ClassicId = "EntityCommandPanel.Showcase.Classic";
        public const string Dota2Id = "EntityCommandPanel.Showcase.Dota2";
        public const string LolId = "EntityCommandPanel.Showcase.LoL";
        public const string Sc2Id = "EntityCommandPanel.Showcase.SC2";

        public static string ReadActive(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            InteractionPrefCatalog catalog = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                ?? throw new InvalidOperationException("Entity command panel theme requires InteractionPrefCatalog.");
            int keyId = catalog.Keys.GetId(PrefKey);
            if (keyId == catalog.Keys.InvalidId)
            {
                return ClassicId;
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity rep) ||
                rep == Entity.Null ||
                !engine.World.IsAlive(rep) ||
                !engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    $"preference '{PrefKey}' is declared but the sole possessed rep is missing.");
            }

            int resolved = pref.ResolvePref(0, keyId);
            if (resolved <= 0)
            {
                throw new InvalidOperationException(
                    $"preference '{PrefKey}' is not seeded on the possessed rep.");
            }

            string valueName = catalog.ValueName(resolved);
            if (!string.Equals(valueName, ClassicId, StringComparison.Ordinal) &&
                !string.Equals(valueName, Dota2Id, StringComparison.Ordinal) &&
                !string.Equals(valueName, LolId, StringComparison.Ordinal) &&
                !string.Equals(valueName, Sc2Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"preference '{PrefKey}' value '{valueName}' is not a showcase theme.");
            }

            return Normalize(valueName);
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
