using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerBehaviorSlotRegistry
    {
        public const string Body = "body";
        public const string Attachment = "attachment";
        public const string Minimap = "minimap";
        public const string Grounding = "grounding";
        public const string Animator = "animator";
        public const string Material = "material";
        public const string Sound = "sound";
        public const string Spline = "spline";
        public const string Attribute = "attribute";
        public const string Tag = "tag";
        public const string Orientation = "orientation";
        public const string Hud = "hud";
        public const string MotionScale = "motion_scale";
        public const string MotionAlpha = "motion_alpha";

        private static readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal)
        {
            [Body] = 0,
            [Attachment] = 1,
            [Minimap] = 2,
            [Grounding] = 3,
            [Animator] = 4,
            [Material] = 5,
            [Sound] = 6,
            [Spline] = 7,
            [Attribute] = 8,
            [Tag] = 9,
            [Orientation] = 10,
            [Hud] = 11,
            [MotionScale] = 12,
            [MotionAlpha] = 13,
        };

        public static int Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Performer behavior slot key must be a canonical non-empty string.", nameof(key));
            }

            if (!Slots.TryGetValue(key, out int slot))
            {
                throw new InvalidOperationException(
                    $"Unknown performer behavior slot '{key}'. Register it in {nameof(PerformerBehaviorSlotRegistry)} instead of relying on load order.");
            }

            return slot;
        }
    }
}
