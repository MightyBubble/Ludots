using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public sealed class GraphVmDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Entry { get; set; } = string.Empty;
        public List<GraphVmNode> Nodes { get; set; } = new();
        public List<GraphVmControlEdge> ControlEdges { get; set; } = new();
        public List<GraphVmValueEdge> ValueEdges { get; set; } = new();
    }

    public sealed class GraphVmNode
    {
        public string Id { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public byte Slot { get; set; }
        public int IntValue { get; set; }
    }

    public sealed class GraphVmControlEdge
    {
        public GraphVmControlEdge()
        {
        }

        public GraphVmControlEdge(string from, string fromPort, string to)
        {
            From = from;
            FromPort = fromPort;
            To = to;
        }

        public string From { get; set; } = string.Empty;
        public string FromPort { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    public sealed class GraphVmValueEdge
    {
        public GraphVmValueEdge()
        {
        }

        public GraphVmValueEdge(string from, string fromPort, string to, string toPort)
        {
            From = from;
            FromPort = fromPort;
            To = to;
            ToPort = toPort;
        }

        public string From { get; set; } = string.Empty;
        public string FromPort { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string ToPort { get; set; } = string.Empty;
    }
}
