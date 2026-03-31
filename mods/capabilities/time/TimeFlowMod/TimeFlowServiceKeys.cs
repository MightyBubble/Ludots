using Ludots.Core.Scripting;

namespace TimeFlowMod;

public static class TimeFlowServiceKeys
{
    public static readonly ServiceKey<TimeFlowProfileRegistry> Registry =
        new("TimeFlowMod.Registry");

    public static readonly ServiceKey<TimeFlowService> Service =
        new("TimeFlowMod.Service");
}
