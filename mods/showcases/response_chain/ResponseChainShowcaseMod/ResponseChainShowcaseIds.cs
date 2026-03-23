using System;

namespace ResponseChainShowcaseMod
{
    internal static class ResponseChainShowcaseIds
    {
        public const string MapId = "response_chain_showcase";
        public const string InputContextId = "ResponseChainShowcase.Controls";
        public const string ResetActionId = "ResetShowcase";
        public const string ResetRequestKey = "ResponseChainShowcase.ResetRequested";
        public const string RuntimeInstalledKey = "ResponseChainShowcase.Installed";

        public const string ConductorName = "Conductor";
        public const string ComboRaiderName = "Combo Raider";
        public const string CounterRaiderName = "Counter Raider";
        public const string ScholarName = "Scholar";
        public const string ProtectorName = "Protector";

        public const string ComboOpenerEffect = "Effect.Showcase.Combo.Opener";
        public const string ComboFollowUpEffect = "Effect.Showcase.Combo.FollowUp";
        public const string CounterSwingEffect = "Effect.Showcase.Counter.Swing";
        public const string CounterTakeHitEffect = "Effect.Showcase.Counter.TakeHit";
        public const string CounterRiposteEffect = "Effect.Showcase.Counter.Riposte";
        public const string CounterFlourishEffect = "Effect.Showcase.Counter.Flourish";
        public const string RedirectBoltEffect = "Effect.Showcase.Redirect.Bolt";
        public const string RedirectHitScholarEffect = "Effect.Showcase.Redirect.HitScholar";
        public const string RedirectToGuardEffect = "Effect.Showcase.Redirect.ToGuard";
        public const string RedirectFlourishEffect = "Effect.Showcase.Redirect.Flourish";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
