using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Presentation.Minimap
{
    public readonly record struct MinimapSkinDescriptor(
        string Id,
        Vector4 PanelBackground,
        Vector4 PanelBorder,
        Vector4 FieldBackground,
        Vector4 FieldBorder,
        Vector4 Title,
        Vector4 Band,
        Vector4 Footer,
        Vector4 GridMinor,
        Vector4 GridMajor,
        Vector4 SliderTrack,
        Vector4 SliderFill,
        Vector4 SliderThumb,
        Vector4 ToggleActiveFill,
        Vector4 ToggleInactiveFill,
        Vector4 ToggleActiveBorder,
        Vector4 ToggleInactiveBorder,
        Vector4 ToggleActiveText,
        Vector4 ToggleInactiveText)
    {
        public static MinimapSkinDescriptor Require(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !MinimapSkinCatalog.TryGet(id, out MinimapSkinDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"Unknown minimap skin '{id}'. Known skins: {string.Join(", ", MinimapSkinCatalog.KnownIds)}.");
            }

            return descriptor;
        }
    }

    public static class MinimapSkinCatalog
    {
        private static readonly IReadOnlyDictionary<string, MinimapSkinDescriptor> Skins =
            new Dictionary<string, MinimapSkinDescriptor>(StringComparer.Ordinal)
            {
                ["default"] = Create(
                    "default", new(0.02f, 0.05f, 0.07f, 1f), new(0.48f, 0.70f, 0.86f, 1f),
                    new(0.01f, 0.04f, 0.06f, 1f), new(0.42f, 0.65f, 0.80f, 1f),
                    new(0.98f, 0.99f, 1f, 1f), new(1f, 0.84f, 0.42f, 1f), new(0.78f, 0.86f, 0.93f, 1f),
                    new(0.24f, 0.39f, 0.49f, 0.70f), new(0.48f, 0.66f, 0.78f, 0.88f),
                    new(0.15f, 0.25f, 0.31f, 0.95f), new(0.93f, 0.72f, 0.28f, 0.98f), new(1f, 0.93f, 0.62f, 1f),
                    new(0.20f, 0.40f, 0.50f, 0.96f), new(0.07f, 0.13f, 0.17f, 0.96f),
                    new(0.82f, 0.89f, 0.48f, 1f), new(0.34f, 0.53f, 0.64f, 1f),
                    new(0.98f, 0.96f, 0.72f, 1f), new(0.70f, 0.80f, 0.88f, 1f)),
                ["ink-wash"] = Create(
                    "ink-wash", new(0.10f, 0.09f, 0.08f, 0.96f), new(0.68f, 0.25f, 0.18f, 1f),
                    new(0.16f, 0.15f, 0.13f, 1f), new(0.56f, 0.49f, 0.39f, 1f),
                    new(0.96f, 0.91f, 0.80f, 1f), new(0.88f, 0.32f, 0.20f, 1f), new(0.78f, 0.73f, 0.64f, 1f),
                    new(0.43f, 0.39f, 0.32f, 0.55f), new(0.70f, 0.62f, 0.50f, 0.78f),
                    new(0.28f, 0.24f, 0.19f, 0.95f), new(0.80f, 0.42f, 0.20f, 0.98f), new(0.98f, 0.84f, 0.60f, 1f),
                    new(0.42f, 0.25f, 0.18f, 0.96f), new(0.16f, 0.14f, 0.12f, 0.96f),
                    new(0.88f, 0.32f, 0.20f, 1f), new(0.56f, 0.49f, 0.39f, 1f),
                    new(0.98f, 0.84f, 0.60f, 1f), new(0.74f, 0.69f, 0.60f, 1f)),
                ["fantasy"] = Create(
                    "fantasy", new(0.12f, 0.08f, 0.04f, 0.98f), new(0.84f, 0.62f, 0.22f, 1f),
                    new(0.19f, 0.12f, 0.06f, 1f), new(0.72f, 0.46f, 0.16f, 1f),
                    new(1f, 0.91f, 0.62f, 1f), new(0.60f, 0.86f, 1f, 1f), new(0.82f, 0.72f, 0.50f, 1f),
                    new(0.48f, 0.32f, 0.16f, 0.60f), new(0.82f, 0.60f, 0.25f, 0.86f),
                    new(0.28f, 0.18f, 0.08f, 0.95f), new(0.80f, 0.54f, 0.20f, 0.98f), new(1f, 0.88f, 0.52f, 1f),
                    new(0.44f, 0.28f, 0.10f, 0.96f), new(0.18f, 0.11f, 0.05f, 0.96f),
                    new(1f, 0.79f, 0.28f, 1f), new(0.72f, 0.46f, 0.16f, 1f),
                    new(1f, 0.91f, 0.62f, 1f), new(0.82f, 0.72f, 0.50f, 1f)),
                ["minimal"] = Create(
                    "minimal", new(0.95f, 0.95f, 0.95f, 0.98f), new(0.12f, 0.12f, 0.12f, 1f),
                    new(0.99f, 0.99f, 0.99f, 1f), new(0.28f, 0.28f, 0.28f, 1f),
                    new(0.05f, 0.05f, 0.05f, 1f), new(0.14f, 0.35f, 0.72f, 1f), new(0.25f, 0.25f, 0.25f, 1f),
                    new(0.72f, 0.72f, 0.72f, 0.55f), new(0.42f, 0.42f, 0.42f, 0.80f),
                    new(0.72f, 0.72f, 0.72f, 0.95f), new(0.14f, 0.35f, 0.72f, 0.98f), new(0.10f, 0.10f, 0.10f, 1f),
                    new(0.82f, 0.82f, 0.82f, 0.96f), new(0.92f, 0.92f, 0.92f, 0.96f),
                    new(0.14f, 0.35f, 0.72f, 1f), new(0.28f, 0.28f, 0.28f, 1f),
                    new(1f, 1f, 1f, 1f), new(0.18f, 0.18f, 0.18f, 1f)),
                ["sci-fi"] = Create(
                    "sci-fi", new(0.02f, 0.02f, 0.06f, 0.98f), new(0.20f, 0.86f, 0.96f, 1f),
                    new(0.01f, 0.03f, 0.10f, 1f), new(0.10f, 0.72f, 0.88f, 1f),
                    new(0.76f, 0.96f, 1f, 1f), new(1f, 0.32f, 0.72f, 1f), new(0.48f, 0.72f, 0.84f, 1f),
                    new(0.12f, 0.36f, 0.52f, 0.65f), new(0.24f, 0.68f, 0.82f, 0.90f),
                    new(0.04f, 0.16f, 0.24f, 0.95f), new(1f, 0.30f, 0.68f, 0.98f), new(0.84f, 1f, 1f, 1f),
                    new(0.10f, 0.38f, 0.52f, 0.96f), new(0.02f, 0.08f, 0.14f, 0.96f),
                    new(1f, 0.32f, 0.72f, 1f), new(0.10f, 0.72f, 0.88f, 1f),
                    new(0.84f, 1f, 1f, 1f), new(0.48f, 0.72f, 0.84f, 1f)),
            };

        public static IEnumerable<string> KnownIds => Skins.Keys;

        public static bool TryGet(string id, out MinimapSkinDescriptor descriptor)
        {
            return Skins.TryGetValue(id.Trim(), out descriptor);
        }

        public static MinimapSkinDescriptor Require(string id) => MinimapSkinDescriptor.Require(id);

        private static MinimapSkinDescriptor Create(
            string id,
            Vector4 panelBackground,
            Vector4 panelBorder,
            Vector4 fieldBackground,
            Vector4 fieldBorder,
            Vector4 title,
            Vector4 band,
            Vector4 footer,
            Vector4 gridMinor,
            Vector4 gridMajor,
            Vector4 sliderTrack,
            Vector4 sliderFill,
            Vector4 sliderThumb,
            Vector4 toggleActiveFill,
            Vector4 toggleInactiveFill,
            Vector4 toggleActiveBorder,
            Vector4 toggleInactiveBorder,
            Vector4 toggleActiveText,
            Vector4 toggleInactiveText)
        {
            return new MinimapSkinDescriptor(
                id, panelBackground, panelBorder, fieldBackground, fieldBorder, title, band, footer,
                gridMinor, gridMajor, sliderTrack, sliderFill, sliderThumb, toggleActiveFill,
                toggleInactiveFill, toggleActiveBorder, toggleInactiveBorder, toggleActiveText, toggleInactiveText);
        }
    }
}
