using System;

namespace Ludots.Core.UI.PanelProjection
{
    public static class PanelLayoutCatalog
    {
        public const string CompactStatus = "compactStatus";
        public const string ControlStrip = "controlStrip";
        public const string Radar = "radar";
        public const string SelectionCard = "selectionCard";
        public const string EntityLedger = "entityLedger";
        public const string EventFeed = "eventFeed";
        public const string CommandDeck = "commandDeck";
        public const string SubsystemNav = "subsystemNav";

        public static bool IsSupported(string? layout)
        {
            return layout switch
            {
                CompactStatus or ControlStrip or Radar or SelectionCard or
                    EntityLedger or EventFeed or CommandDeck or SubsystemNav => true,
                _ => false,
            };
        }

        public static string Require(string? layout, string templateId)
        {
            if (string.IsNullOrWhiteSpace(layout) || !IsSupported(layout.Trim()))
            {
                throw new InvalidOperationException(
                    $"Panel template '{templateId}' layout must be one of: " +
                    "compactStatus, controlStrip, radar, selectionCard, entityLedger, eventFeed, commandDeck, subsystemNav.");
            }

            return layout.Trim();
        }
    }
}
