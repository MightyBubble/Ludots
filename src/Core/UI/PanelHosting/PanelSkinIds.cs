namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Engine-level panel skin vocabulary as compact byte ids for graph instruction
    /// encoding (CreatePanel op). The byte budget caps the catalog by design: skins
    /// are engine capabilities, not per-mod content. Ludots.UI.Panels' catalog names
    /// itself from these constants — single source of truth, no drift.
    /// </summary>
    public static class PanelSkinIds
    {
        public const byte Unspecified = 255;
        public const byte Default = 0;
        public const byte Markup = 1;
        public const byte Compose = 2;
        public const byte Reactive = 3;
        public const byte Web = 4;

        public static readonly string[] Names =
        {
            "default",
            "markup",
            "compose",
            "reactive",
            "web",
        };

        public static byte ToId(string? skinName)
        {
            if (string.IsNullOrWhiteSpace(skinName))
            {
                return Unspecified;
            }

            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], skinName.Trim(), System.StringComparison.Ordinal))
                {
                    return (byte)i;
                }
            }

            throw new System.InvalidOperationException(
                $"Unknown panel skin '{skinName}'. Known skins: {string.Join(", ", Names)}.");
        }

        public static string? ToName(byte skinId)
        {
            return skinId == Unspecified ? null : Names[skinId];
        }
    }
}
