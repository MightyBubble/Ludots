using System.Text.Json.Serialization;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation;

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

        /// <summary>
        /// Initial local player for startup map load. Provided by CoreMod via game.json merge.
        /// </summary>
        public int StartupLocalPlayerId { get; set; }

        public List<string> StartupInputContexts { get; set; } = new List<string>();

        // Engine-level defaults (these stay in Core's game.json)
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;
        public bool WindowResizable { get; set; }
        public bool WindowStartMaximized { get; set; }
        public string WindowTitle { get; set; } = "Ludots Engine";
        public int TargetFps { get; set; } = 60;

        public int SimulationBudgetMsPerFrame { get; set; } = 4;
        public int SimulationMaxSlicesPerLogicFrame { get; set; } = 120;

        public GasRuntimeCapacityConfig GasRuntimeCapacity { get; set; } = null!;

        public int GridCellSizeCm { get; set; } = 100;

        public int WorldWidthInMacroTiles { get; set; } = 64;
        public int WorldHeightInMacroTiles { get; set; } = 64;

        public Physics2DConfig Physics2D { get; set; } = new Physics2DConfig();

        public CommandSourceAcquisitionConfig? CommandSource { get; set; }

        public PresentationRuntimeConfig Presentation { get; set; } = null!;

        public LogConfig Logging { get; set; } = new LogConfig();

        public BrowserRuntimeConfig BrowserRuntime { get; set; } = new BrowserRuntimeConfig();

        /// <summary>
        /// Game constants table - merged from all Mods via ConfigPipeline.
        /// Contains order type ids, response-chain order type ids, attributes, etc.
        /// </summary>
        public GameConstants Constants { get; set; } = new GameConstants();
    }

    public sealed class Physics2DConfig
    {
        public bool Enabled { get; set; }
    }

    public sealed class GasRuntimeCapacityConfig
    {
        public int AbilityExecSnapshotCapacity { get; set; }
        public int EffectLifetimeSnapshotCapacity { get; set; }
        public int EffectFanOutCommandCapacity { get; set; }
        public int OrderQueueCapacity { get; set; }
        public int ResponseChainOrderQueueCapacity { get; set; }
        public int OrderAdmissionResultCapacity { get; set; }
        public int OrderAdmissionRejectionCapacity { get; set; }
        public int OrderTerminalResultCapacity { get; set; }
        public int DeferredTriggerActiveEntityCapacity { get; set; }
        public int ProjectileCollisionCandidateCapacity { get; set; }
        public int ProjectileRuntimeEntityCapacity { get; set; }
        public int GraphOutputValueCapacity { get; set; }
        public int AbilityExecMaxWorkUnitsPerSlice { get; set; }
        public int EffectProcessingMaxWorkUnitsPerSlice { get; set; }

        public void Validate()
        {
            if (AbilityExecSnapshotCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.abilityExecSnapshotCapacity must be positive.");
            }

            if (EffectLifetimeSnapshotCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.effectLifetimeSnapshotCapacity must be positive.");
            }

            if (EffectFanOutCommandCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.effectFanOutCommandCapacity must be positive.");
            }

            if (OrderQueueCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.orderQueueCapacity must be positive.");
            }

            if (ResponseChainOrderQueueCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.responseChainOrderQueueCapacity must be positive.");
            }

            if (OrderTerminalResultCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.orderTerminalResultCapacity must be positive.");
            }

            if (OrderAdmissionResultCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.orderAdmissionResultCapacity must be positive.");
            }

            if (OrderAdmissionRejectionCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.orderAdmissionRejectionCapacity must be positive.");
            }

            if (DeferredTriggerActiveEntityCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.deferredTriggerActiveEntityCapacity must be positive.");
            }

            if (ProjectileCollisionCandidateCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.projectileCollisionCandidateCapacity must be positive.");
            }

            if (ProjectileRuntimeEntityCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.projectileRuntimeEntityCapacity must be positive.");
            }

            if (GraphOutputValueCapacity <= 0)
            {
                throw new System.InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.graphOutputValueCapacity must be positive.");
            }

            ValidateFiniteWorkBudget(
                AbilityExecMaxWorkUnitsPerSlice,
                "GameConfig.gasRuntimeCapacity.abilityExecMaxWorkUnitsPerSlice");
            ValidateFiniteWorkBudget(
                EffectProcessingMaxWorkUnitsPerSlice,
                "GameConfig.gasRuntimeCapacity.effectProcessingMaxWorkUnitsPerSlice");
        }

        private static void ValidateFiniteWorkBudget(int value, string path)
        {
            if (value <= 0 || value == int.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"{path} must be positive and finite.");
            }
        }
    }

    public sealed class BrowserRuntimeConfig
    {
        public bool Enabled { get; set; }

        public bool Required { get; set; }

        public string Provider { get; set; } = string.Empty;

        public string ProviderAssemblyPath { get; set; } = string.Empty;

        public string ProviderHostTypeName { get; set; } = string.Empty;

        public string ProviderProjectPath { get; set; } = string.Empty;

        public string RuntimeRootPath { get; set; } = string.Empty;

        public string CacheRootPath { get; set; } = string.Empty;

        public bool? UseCollectibleLoadContext { get; set; }

        public string[] ProcessSharedAssemblyNamePrefixes { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Game constants loaded from runtime configuration.
    /// Now fully data-driven via game.json merge.
    /// </summary>
    public class GameConstants
    {
        /// <summary>
        /// Order type ids loaded from the `orderTypeIds` constants table in game.json.
        /// </summary>
        [JsonPropertyName("orderTypeIds")]
        public Dictionary<string, int> OrderTypeIds { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Response-chain order type ids loaded from the `responseChainOrderTypeIds` constants table in game.json.
        /// </summary>
        [JsonPropertyName("responseChainOrderTypeIds")]
        public Dictionary<string, int> ResponseChainOrderTypeIds { get; set; } = new Dictionary<string, int>();

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
