using System;

namespace Ludots.Core.GraphRuntime
{
    public enum GraphActionHost : byte
    {
        None = 0,
        BehaviorTree = 1,
        Hfsm = 2,
        Level = 3,
        Script = 4,
        MapTrigger = 5
    }

    public static class GraphActionHostYieldPolicy
    {
        public static bool AllowsYield(GraphActionHost host)
            => host is GraphActionHost.BehaviorTree or GraphActionHost.Script;

        public static bool TryParse(string? text, out GraphActionHost host)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                host = GraphActionHost.None;
                return false;
            }

            if (Enum.TryParse(text.Trim(), ignoreCase: false, out host) &&
                host != GraphActionHost.None &&
                Enum.IsDefined(typeof(GraphActionHost), host))
            {
                return true;
            }

            host = GraphActionHost.None;
            return false;
        }
    }
}
