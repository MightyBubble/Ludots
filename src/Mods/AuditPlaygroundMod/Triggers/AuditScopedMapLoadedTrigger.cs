using AuditPlaygroundMod.Commands;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;

namespace AuditPlaygroundMod.Triggers
{
    /// <summary>
    /// Map-scoped trigger path declared from map JSON TriggerTypes.
    /// Also serves as decorator injection target.
    /// </summary>
    public sealed class AuditScopedMapLoadedTrigger : Trigger
    {
        public AuditScopedMapLoadedTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            Priority = 20;
            AddAction(new AnchorCommand("audit.after_load"));
            AddAction(new ActivateSystemFactoryCommand("AuditMapControlPresentation"));
            AddAction(new IncrementGlobalCounterCommand("Audit.ScopedMapLoadedCount", "scoped map-loaded"));
        }
    }
}
