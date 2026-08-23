namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Canonical screen-anchor vocabulary for panel hosting, shared by graph authoring
    /// validation and the built-in presentation. Anchors outside this catalog are
    /// rejected at graph compile time instead of crashing the presentation mid-game.
    /// </summary>
    public static class PanelAnchorCatalog
    {
        public const string Prefix = "screen.";

        public static readonly string[] All =
        {
            "screen.topLeft",
            "screen.topCenter",
            "screen.topRight",
            "screen.bottomLeft",
            "screen.bottomCenter",
            "screen.bottomRight",
        };

        public static bool IsSupported(string? anchor)
        {
            if (string.IsNullOrWhiteSpace(anchor))
            {
                return false;
            }

            string trimmed = anchor.Trim();
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i], trimmed, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static string Describe() => string.Join(", ", All);
    }
}
