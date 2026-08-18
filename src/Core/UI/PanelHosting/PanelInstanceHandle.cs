using System;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>Stable reference to one live panel instance; stale handles are rejected loudly.</summary>
    public readonly record struct PanelInstanceHandle(int Id, int Generation)
    {
        public bool IsValid => Id >= 0 && Generation != 0;

        public static PanelInstanceHandle Invalid { get; } = new(-1, 0);
    }

    /// <summary>Read-only view of one live panel instance, for surface adapters.</summary>
    public readonly record struct PanelHostInstanceInfo(
        PanelInstanceHandle Handle,
        string TemplateId,
        string Anchor,
        Arch.Core.Entity Scope,
        uint Revision);
}
