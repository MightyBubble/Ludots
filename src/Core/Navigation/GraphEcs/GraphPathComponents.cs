using System.Runtime.CompilerServices;
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

    public struct GraphPathBuffer
    {
        public const int Capacity = 128;

        [InlineArray(Capacity)]
        public struct NodeArray
        {
            private int _element0;
        }

        public NodeArray Nodes;
        public int Count;
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
