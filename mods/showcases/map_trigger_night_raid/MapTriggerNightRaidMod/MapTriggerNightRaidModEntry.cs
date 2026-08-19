using Ludots.Core.Modding;

namespace MapTriggerNightRaidMod;

/// <summary>
/// Presentation-only entry: the entire night-raid level flow (waves, phase,
/// victory panel) lives in the map JSON + MapTriggerGraph data; this class
/// registers no systems, no triggers, and no level-flow logic.
/// </summary>
public sealed class MapTriggerNightRaidModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MapTriggerNightRaidMod] Loaded - night raid flow is 100% map + MapTriggerGraph data");
    }

    public void OnUnload() { }
}
