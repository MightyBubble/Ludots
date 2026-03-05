using AuditPlaygroundMod.Commands;
using Ludots.Core.Scripting;

namespace AuditPlaygroundMod.Triggers
{
    /// <summary>
    /// Legacy global trigger path: should still fire on map events.
    /// </summary>
    public sealed class AuditGlobalMapLoadedTrigger : Trigger
    {
        public AuditGlobalMapLoadedTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            Priority = 80;
            AddAction(new IncrementGlobalCounterCommand("Audit.GlobalMapLoadedCount", "global map-loaded"));
        }
    }
}
