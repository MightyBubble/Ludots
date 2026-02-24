using System;
using System.Collections.Generic;

namespace Ludots.ModLauncher.Config
{
    public sealed class ModLauncherConfig
    {
        public List<string> ExtraModDirectories { get; set; } = new List<string>();

        public string? SelectedPresetId { get; set; }

        public Dictionary<string, ModPreset> Presets { get; set; } = new Dictionary<string, ModPreset>(StringComparer.OrdinalIgnoreCase);

        public LauncherViewMode ViewMode { get; set; } = LauncherViewMode.Cards;
    }

    public sealed class ModPreset
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> ActiveModNames { get; set; } = new List<string>();
        public bool IncludeDependencies { get; set; } = true;
    }

    public enum LauncherViewMode
    {
        Cards = 0,
        List = 1
    }
}

