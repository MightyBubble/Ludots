namespace Ludots.Platform.Abstractions
{
    public enum PresentationEventKind : byte
    {
        None = 0,
        GameplayEvent = 1,
        TagEffectiveChanged = 2,
        EntitySpawned = 3,
        EntityDestroyed = 4,
        ProjectileSpawned = 5,

        // Presenter domain events
        PresenterCreated = 10,
        PresenterDestroyed = 11,
        TimerExpired = 12,

        // GAS presentation events
        EffectApplied = 20,
        CastCommitted = 21,
        CastFailed = 22,
        EffectActivated = 23,
        CastStarted = 24,
        CastFinished = 25,
        CastInterrupted = 26,
        EffectExpired = 27,
        EffectCancelled = 28,

        // Global domain events
        GlobalDayNight = 30,
        GlobalRegionChanged = 31,
        GlobalWeather = 32,

        // Attribute domain events
        AttributeValueChanged = 40,

        // Ability aim presentation events
        AbilityAimUpdated = 60,
        AbilityAimEnded = 61,
        AbilityAimBegun = 62,
        AbilityAimSlotAdvanced = 63,
        MovePathUpdated = 64,
        MovePathEnded = 65,
        MovePathBegun = 66,

        // Entity collection presentation events
        EntityCollectionMemberAdded = 70,
        EntityCollectionMemberRemoved = 71,

        // World presentation fact events
        WorldOverlayUpdated = 72,
        WorldOverlayEnded = 73,
        WorldHudUpdated = 74,
        WorldHudEnded = 75,
        WorldSplineUpdated = 76,
        WorldSplineEnded = 77,

        // Interaction context lifecycle events (#1398 S2b, constitution §8.2): key is the
        // interaction context profile id; PayloadA is the instance scope tag, PayloadB the
        // parent context profile id (0 = no parent).
        ContextActivated = 78,
        ContextDeactivated = 79,
    }
}
