using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphEcs
{
    public enum GraphPathMode : byte
    {
        PreciseAStar = 0,
        FastRouteTable = 1
    }

    public readonly struct GraphPathRequest
    {
        public readonly int StartNodeId;
        public readonly int GoalNodeId;
        public readonly GraphPathMode Mode;

        public GraphPathRequest(int startNodeId, int goalNodeId, GraphPathMode mode)
        {
            StartNodeId = startNodeId;
            GoalNodeId = goalNodeId;
            Mode = mode;
        }
    }

    public sealed class GraphPathBuffer
    {
        public int[] Nodes;
        public int Count;

        public GraphPathBuffer(int capacity = 128)
        {
            Nodes = capacity <= 0 ? System.Array.Empty<int>() : new int[capacity];
            Count = 0;
        }

        public void EnsureCapacity(int required)
        {
            if (required <= Nodes.Length) return;
            int newCap = Nodes.Length == 0 ? 4 : Nodes.Length * 2;
            if (newCap < required) newCap = required;
            System.Array.Resize(ref Nodes, newCap);
        }
    }

    public readonly struct GraphPathResultComponent
    {
        public readonly GraphPathResult Result;

        public GraphPathResultComponent(GraphPathResult result)
        {
            Result = result;
        }
    }
}

