using Ludots.Core.Scripting;
using TimeflowShowcaseMod.Runtime;

namespace TimeflowShowcaseMod;

public static class TimeflowShowcaseServiceKeys
{
    public static readonly ServiceKey<TimeflowShowcaseRuntime> Runtime = new("TimeflowShowcaseRuntime");
}
