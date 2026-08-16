namespace Ludots.Core.Config
{
    public static class ConfigSourcePaths
    {
        public static string CoreAsset(string relativePath) => $"Core:{relativePath}";

        public static string ModAssets(string modId, string relativePath) => $"{modId}:assets/{relativePath}";
    }
}
