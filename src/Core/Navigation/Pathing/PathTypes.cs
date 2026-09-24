using Arch.Core;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.Pathing
{
    public enum PathDomain : byte
    {
        None = 0,
        NodeGraph = 1,
        NavMesh = 2,
        Auto = 3
    }

    public enum PathStatus : byte
    {
        Found = 0,
        NoPath = 1,
        BudgetExceeded = 2,
        NotReady = 3,
        InvalidRequest = 4,
        Error = 5,

        /// <summary>
        /// A walkable prefix of the route was solved, but the requested goal is not reachable.
        /// A usable path exists; its end point is the nearest legal stand point toward the goal
        /// and is reported as <see cref="PathResult.ResolvedGoal"/>. Consumers decide policy
        /// (walk there, or reject the order) from configuration, not from an exception.
        /// </summary>
        Partial = 6
    }

    public enum PathEndpointKind : byte
    {
        None = 0,
        NodeId = 1,
        WorldCm = 2
    }

    public readonly struct PathEndpoint
    {
        public readonly PathEndpointKind Kind;
        public readonly int NodeId;
        public readonly int Xcm;
        public readonly int Ycm;

        private PathEndpoint(PathEndpointKind kind, int nodeId, int xcm, int ycm)
        {
            Kind = kind;
            NodeId = nodeId;
            Xcm = xcm;
            Ycm = ycm;
        }

        public static PathEndpoint FromNodeId(int nodeId) => new(PathEndpointKind.NodeId, nodeId, 0, 0);
        public static PathEndpoint FromWorldCm(int xcm, int ycm) => new(PathEndpointKind.WorldCm, 0, xcm, ycm);
    }

    public readonly struct PathBudget
    {
        public readonly int MaxExpanded;
        public readonly int MaxPoints;

        public PathBudget(int maxExpanded, int maxPoints)
        {
            MaxExpanded = maxExpanded;
            MaxPoints = maxPoints;
        }
    }

    public readonly struct PathHandle
    {
        public readonly int Index;
        public readonly uint Generation;

        public PathHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Index >= 0 && Generation != 0;
    }

    public readonly struct PathRequest
    {
        public readonly int RequestId;
        public readonly Entity Actor;
        public readonly PathDomain Domain;
        public readonly string AgentTypeId;
        public readonly PathEndpoint Start;
        public readonly PathEndpoint Goal;
        public readonly PathBudget Budget;

        public PathRequest(int requestId, Entity actor, PathDomain domain, PathEndpoint start, PathEndpoint goal, PathBudget budget)
        {
            RequestId = requestId;
            Actor = actor;
            Domain = domain;
            AgentTypeId = string.Empty;
            Start = start;
            Goal = goal;
            Budget = budget;
        }

        public PathRequest(int requestId, Entity actor, PathDomain domain, string agentTypeId, PathEndpoint start, PathEndpoint goal, PathBudget budget)
        {
            RequestId = requestId;
            Actor = actor;
            Domain = domain;
            AgentTypeId = agentTypeId;
            Start = start;
            Goal = goal;
            Budget = budget;
        }
    }

    public readonly struct PathResult
    {
        public readonly int RequestId;
        public readonly Entity Actor;
        public readonly PathStatus Status;
        public readonly PathHandle Handle;
        public readonly int Expanded;
        public readonly int ErrorCode;
        public readonly PathDomain ResolvedDomain;

        /// <summary>
        /// The point the returned path actually ends at. Equals the requested goal when
        /// <see cref="Status"/> is <see cref="PathStatus.Found"/>; differs from it when the status
        /// is <see cref="PathStatus.Partial"/>, where it is the nearest legal stand point toward
        /// the goal. Undefined when no path was produced.
        /// </summary>
        public readonly WorldCmInt2 ResolvedGoal;

        public PathResult(
            int requestId,
            Entity actor,
            PathStatus status,
            PathHandle handle,
            int expanded,
            int errorCode,
            PathDomain resolvedDomain = PathDomain.None)
            : this(requestId, actor, status, handle, expanded, errorCode, resolvedDomain, default)
        {
        }

        public PathResult(
            int requestId,
            Entity actor,
            PathStatus status,
            PathHandle handle,
            int expanded,
            int errorCode,
            PathDomain resolvedDomain,
            WorldCmInt2 resolvedGoal)
        {
            RequestId = requestId;
            Actor = actor;
            Status = status;
            Handle = handle;
            Expanded = expanded;
            ErrorCode = errorCode;
            ResolvedDomain = resolvedDomain;
            ResolvedGoal = resolvedGoal;
        }
    }
}
