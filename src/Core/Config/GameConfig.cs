using System.Collections.Generic;
using Ludots.Core.Navigation2D.Config;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Game configuration that is merged from multiple sources via ConfigPipeline.
    /// Core's game.json only provides engine defaults + defaultCoreMod.
    /// All game-specific configuration comes from Mods.
    /// </summary>
    public class GameConfig
    {
        // List of paths to directories containing mods (with mod.json)
        public List<string> ModPaths { get; set; } = new List<string>();

        /// <summary>
        /// Default CoreMod to load (specified in Core's game.json).
        /// This CoreMod provides all game framework configuration.
        /// </summary>
        public string DefaultCoreMod { get; set; }

        /// <summary>
        /// Startup map ID - provided by CoreMod, not Core.
        /// </summary>
        public string StartupMapId { get; set; }

        public List<string> StartupInputContexts { get; set; } = new List<string>();

        // Engine-level defaults (these stay in Core's game.json)
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;
        public string WindowTitle { get; set; } = "Ludots Engine (Raylib)";
        public int TargetFps { get; set; } = 60;

        public int SimulationBudgetMsPerFrame { get; set; } = 4;
        public int SimulationMaxSlicesPerLogicFrame { get; set; } = 120;

        public int GridCellSizeCm { get; set; } = 100;

        public int WorldWidthInTiles { get; set; } = 64;
        public int WorldHeightInTiles { get; set; } = 64;

        public Navigation2DConfig Navigation2D { get; set; } = new Navigation2DConfig();

        /// <summary>
        /// Game constants table - merged from all Mods via ConfigPipeline.
        /// Contains orderTags, gasOrderTags, attributes, etc.
        /// </summary>
        public GameConstants Constants { get; set; } = new GameConstants();
    }

    /// <summary>
    /// Game constants that were previously hardcoded in OrderTags.cs, GasOrderTags.cs, etc.
    /// Now fully data-driven via game.json merge.
    /// </summary>
    public class GameConstants
    {
        /// <summary>
        /// Order tags (previously in OrderTags.cs): moveTo, attackTarget, stop, castAbility...
        /// </summary>
        public Dictionary<string, int> OrderTags { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// GAS order tags (previously in GasOrderTags.cs): chainPass, chainNegate, chainActivateEffect...
        /// </summary>
        public Dictionary<string, int> GasOrderTags { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Attribute names (previously in GameAttributes.cs): health, mana...
        /// </summary>
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Generic integer constants for extensibility
        /// </summary>
        public Dictionary<string, int> IntValues { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Generic string constants for extensibility
        /// </summary>
        public Dictionary<string, string> StringValues { get; set; } = new Dictionary<string, string>();
    }
}
