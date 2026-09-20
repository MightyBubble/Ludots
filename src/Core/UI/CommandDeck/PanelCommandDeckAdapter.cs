using System;
using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// CommandDeck → PanelHost data adapter (#1609): projects a deck snapshot into the
    /// panel data vocabulary so any panel template can render a deck — pins for the deck
    /// frame facts, one list item per slot with the slot's identity and state. This is the
    /// A-line twin of the WebUI CommandDeck topic producer: same snapshot, same field
    /// names, no second vocabulary. Click wiring rides the standard Button chain — the
    /// recommended template shape declares one event (e.g. Ui.CommandDeck.Activate) whose
    /// payload binds the per-slot <c>actionId</c>/<c>slotKey</c> fields baked here.
    /// </summary>
    public static class PanelCommandDeckAdapter
    {
        /// <summary>Reserved list name for deck slots in panel data.</summary>
        public const string ListName = "deck";

        /// <summary>Pin names a deck panel declares to receive the frame facts.</summary>
        public const string RevisionPin = "revision";
        public const string EntryCountPin = "entryCount";
        public const string VisiblePin = "visible";

        /// <summary>Frame facts as panel variable values (pin name → float).</summary>
        public static Dictionary<string, float> ToValues(CommandDeckSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [RevisionPin] = snapshot.Revision,
                [EntryCountPin] = snapshot.Entries.Count,
                [VisiblePin] = snapshot.Visible ? 1f : 0f,
            };
        }

        /// <summary>Slots as panel list items. String fields carry identity and state;
        /// floats carry normalized ratios/counts; bools carry the blocked flag.</summary>
        public static PanelListProjection ToListProjection(CommandDeckSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var items = new List<PanelListItemProjection>(snapshot.Entries.Count);
            for (int i = 0; i < snapshot.Entries.Count; i++)
            {
                CommandDeckEntry entry = snapshot.Entries[i];
                items.Add(new PanelListItemProjection(
                    Floats(entry),
                    Bools(entry),
                    Strings(entry)));
            }

            return new PanelListProjection(ListName, items);
        }

        private static Dictionary<string, float> Floats(in CommandDeckEntry entry) => new(StringComparer.Ordinal)
        {
            ["slotIndex"] = entry.SlotIndex,
            ["lockout"] = entry.LockoutPermille / 1000f,
            ["chargeRatio"] = entry.ChargesMax > 0
                ? Math.Clamp(entry.ChargesCurrent / (float)entry.ChargesMax, 0f, 1f)
                : 0f,
            ["ownerCount"] = entry.OwnerCount,
        };

        private static Dictionary<string, bool> Bools(in CommandDeckEntry entry) => new(StringComparer.Ordinal)
        {
            ["blocked"] = !string.IsNullOrEmpty(entry.BlockedReason),
        };

        private static Dictionary<string, string> Strings(in CommandDeckEntry entry) => new(StringComparer.Ordinal)
        {
            ["label"] = entry.DisplayLabel,
            ["actionId"] = entry.ActionId,
            ["slotKey"] = entry.SlotIndex.ToString(),
            ["categoryId"] = entry.CategoryId,
            ["status"] = entry.Status,
            ["blockedReason"] = entry.BlockedReason,
            ["route"] = entry.RouteProfileId,
        };
    }
}
