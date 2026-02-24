namespace Ludots.Core.Config
{
    public static class ConfigSourcePaths
    {
        public static string CoreConfig(string relativePath) => $"Core:Configs/{relativePath}";

        public static string ModAssets(string modId, string relativePath) => $"{modId}:assets/{relativePath}";

        public static string ModConfigs(string modId, string relativePath) => $"{modId}:assets/Configs/{relativePath}";
    }
}

