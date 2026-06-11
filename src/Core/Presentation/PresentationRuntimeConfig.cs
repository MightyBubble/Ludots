namespace Ludots.Core.Presentation
{
    /// <summary>
    /// Engine-level presentation runtime capacity knobs merged from game.json.
    /// Defaults are sized for playable showcase scenes rather than tiny unit-test maps.
    /// </summary>
    public sealed class PresentationRuntimeConfig
    {
        public const int DefaultPerformerInstanceCapacity = 2048;
        public const int DefaultPerformerAnimatorSlotsPerInstance = 1;

        public int PerformerInstanceCapacity { get; set; } = DefaultPerformerInstanceCapacity;

        public int PerformerAnimatorSlotsPerInstance { get; set; } = DefaultPerformerAnimatorSlotsPerInstance;

        public string HostAssetBackendId { get; set; } = string.Empty;

        public int GetEffectivePerformerInstanceCapacity()
        {
            return PerformerInstanceCapacity > 0 ? PerformerInstanceCapacity : DefaultPerformerInstanceCapacity;
        }

        public int GetEffectivePerformerAnimatorSlotsPerInstance()
        {
            return PerformerAnimatorSlotsPerInstance > 0
                ? PerformerAnimatorSlotsPerInstance
                : DefaultPerformerAnimatorSlotsPerInstance;
        }
    }
}
