namespace Ludots.Core.Navigation.GraphCore
{
    public enum GraphPathStatus : byte
    {
        Success = 0,
        NotFound = 1,
        BufferTooSmall = 2,
        OverBudget = 3,
        InvalidInput = 4
    }

    public readonly struct GraphPathResult
    {
        public readonly GraphPathStatus Status;
        public readonly int NodeCount;
        public readonly int RequiredNodeCount;
        public readonly int Expanded;
        public readonly float TravelCost;

        public GraphPathResult(GraphPathStatus status, int nodeCount, int requiredNodeCount, int expanded)
        {
            Status = status;
            NodeCount = nodeCount;
            RequiredNodeCount = requiredNodeCount;
            Expanded = expanded;
            TravelCost = 0f;
        }

        public GraphPathResult(GraphPathStatus status, int nodeCount, int requiredNodeCount, int expanded, float travelCost)
        {
            Status = status;
            NodeCount = nodeCount;
            RequiredNodeCount = requiredNodeCount;
            Expanded = expanded;
            TravelCost = travelCost;
        }
    }
}
