using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphEcs
{
    public sealed class GraphPathfindingSystem : BaseSystem<World, float>
    {
        private readonly NodeGraph _graph;
        private readonly GraphRouteTable _routeTable;
        private static readonly QueryDescription Query = new QueryDescription().WithAll<GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>();
        private NodeGraphPathScratch _scratch;

        public int BufferTooSmallCount { get; private set; }
        public int LastRequiredNodeCount { get; private set; }

        public GraphPathfindingSystem(World world, NodeGraph graph, GraphRouteTable routeTable = null) : base(world)
        {
            _graph = graph ?? throw new System.ArgumentNullException(nameof(graph));
            _routeTable = routeTable;
            _scratch = new NodeGraphPathScratch();
        }

        public override void Update(in float dt)
        {
            var policy = new DefaultTraversalPolicy();
            var job = new Job
            {
                Graph = _graph,
                RouteTable = _routeTable,
                Scratch = _scratch,
                Policy = policy
            };

            World.InlineEntityQuery<Job, GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>(in Query, ref job);
            _scratch = job.Scratch;
            BufferTooSmallCount += job.BufferTooSmallCount;
            if (job.LastRequiredNodeCount > 0)
            {
                LastRequiredNodeCount = job.LastRequiredNodeCount;
            }
        }

        private struct Job : IForEachWithEntity<GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>
        {
            public NodeGraph Graph;
            public GraphRouteTable RouteTable;
            public NodeGraphPathScratch Scratch;
            public DefaultTraversalPolicy Policy;
            public int BufferTooSmallCount;
            public int LastRequiredNodeCount;

            public void Update(Entity entity, ref GraphPathRequest req, ref GraphPathBuffer buffer, ref GraphPathResultComponent result)
            {
                Span<int> nodes = buffer.Nodes;

                GraphPathResult r;
                if (req.Mode == GraphPathMode.FastRouteTable)
                {
                    if (RouteTable == null)
                    {
                        r = new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
                    }
                    else
                    {
                        r = RouteTable.ReconstructPath(req.StartNodeId, req.GoalNodeId, nodes);
                    }
                }
                else
                {
                    r = NodeGraphPathService.FindPathAStar(Graph, req.StartNodeId, req.GoalNodeId, nodes, ref Scratch, ref Policy);
                }

                if (r.Status == GraphPathStatus.BufferTooSmall)
                {
                    BufferTooSmallCount++;
                    LastRequiredNodeCount = r.RequiredNodeCount;
                }

                buffer.Count = r.Status == GraphPathStatus.Success ? r.NodeCount : 0;
                result = new GraphPathResultComponent(r);
            }
        }
    }
}
