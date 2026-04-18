namespace Ludots.Core.Presentation.Events
{
    public enum PresentationEventKind : byte
    {
        None = 0,
        GameplayEvent = 1,
        TagEffectiveChanged = 2,
        EntitySpawned = 3,
        EntityDestroyed = 4,
        ProjectileSpawned = 5,

        // Performer domain events
        PerformerCreated = 10,
        PerformerDestroyed = 11,

        // GAS presentation events
        EffectApplied = 20,
        CastCommitted = 21,
        CastFailed = 22,

        // Global domain events
        GlobalDayNight = 30,
        GlobalRegionChanged = 31,
        GlobalWeather = 32,

        // Attribute domain events
        AttributeValueChanged = 40,
    }
}
