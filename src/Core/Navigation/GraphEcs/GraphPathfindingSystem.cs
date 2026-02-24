using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphEcs
{
    public sealed class GraphPathfindingSystem : BaseSystem<World, float>
    {
        private readonly NodeGraph _graph;
        private readonly GraphRouteTable _routeTable;
        private readonly QueryDescription _query = new QueryDescription().WithAll<GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>();
        private NodeGraphPathScratch _scratch;

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

            World.InlineEntityQuery<Job, GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>(in _query, ref job);
            _scratch = job.Scratch;
        }

        private struct Job : IForEachWithEntity<GraphPathRequest, GraphPathBuffer, GraphPathResultComponent>
        {
            public NodeGraph Graph;
            public GraphRouteTable RouteTable;
            public NodeGraphPathScratch Scratch;
            public DefaultTraversalPolicy Policy;

            public void Update(Entity entity, ref GraphPathRequest req, ref GraphPathBuffer buffer, ref GraphPathResultComponent result)
            {
                if (buffer == null) buffer = new GraphPathBuffer();

                GraphPathResult r;
                if (req.Mode == GraphPathMode.FastRouteTable)
                {
                    if (RouteTable == null)
                    {
                        r = new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
                    }
                    else
                    {
                        buffer.EnsureCapacity(RouteTable.NodeCount);
                        r = RouteTable.ReconstructPath(req.StartNodeId, req.GoalNodeId, buffer.Nodes);
                    }
                }
                else
                {
                    buffer.EnsureCapacity(Graph.NodeCount);
                    r = NodeGraphPathService.FindPathAStar(Graph, req.StartNodeId, req.GoalNodeId, buffer.Nodes, ref Scratch, ref Policy);
                }

                buffer.Count = r.Status == GraphPathStatus.Success ? r.NodeCount : 0;
                result = new GraphPathResultComponent(r);
            }
        }
    }
}

