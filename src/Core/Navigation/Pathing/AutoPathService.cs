using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class AutoPathService : IPathService
    {
        private readonly NodeGraph _graph;
        private readonly INodeGraphSpatialIndex _graphIndex;
        private readonly NavQueryServiceRegistry _navRegistry;
        private readonly NavMeshProfileRegistry _navProfiles;
        private readonly PathStore _store;

        private readonly Dictionary<string, CompiledAgentType> _agents;
        private readonly CompiledAgentType _defaultAgent;

        private NodeGraphPathScratch _graphScratch = new NodeGraphPathScratch();
        private int[] _nodeIdsScratch = Array.Empty<int>();
        private int[] _xScratch = Array.Empty<int>();
        private int[] _yScratch = Array.Empty<int>();

        public AutoPathService(NodeGraph graph, NavQueryServiceRegistry navRegistry, NavMeshProfileRegistry navProfiles, PathStore store, PathingConfig config)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _graphIndex = new LinearScanNodeGraphSpatialIndex(_graph);
            _navRegistry = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _navProfiles = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.AgentTypes == null || config.AgentTypes.Count == 0) throw new InvalidOperationException("PathingConfig.agentTypes is empty.");

            _agents = new Dictionary<string, CompiledAgentType>(config.AgentTypes.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                var a = config.AgentTypes[i];
                if (a == null) continue;
                var compiled = CompileAgent(a);
                _agents[compiled.Id] = compiled;
                if (i == 0) _defaultAgent = compiled;
            }

            if (_defaultAgent.Id == null) throw new InvalidOperationException("PathingConfig.agentTypes has no valid entries.");
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain != PathDomain.Auto)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                return false;
            }

            var agent = ResolveAgent(request.AgentTypeId);
            if (agent.Selection.Mode == PathSelectionMode.PreferGraph)
            {
                if (TrySolveGraph(in request, in agent, out result, out _)) return true;
                if (TrySolveMesh(in request, in agent, out result, out _)) return true;
                return true;
            }
            if (agent.Selection.Mode == PathSelectionMode.PreferMesh)
            {
                if (TrySolveMesh(in request, in agent, out result, out _)) return true;
                if (TrySolveGraph(in request, in agent, out result, out _)) return true;
                return true;
            }

            bool graphOk = TrySolveGraph(in request, in agent, out var graphResult, out float graphCost);
            bool meshOk = TrySolveMesh(in request, in agent, out var meshResult, out float meshCost);

            if (graphOk && meshOk)
            {
                float graphScore = agent.Selection.GraphBias + agent.Selection.GraphCostWeight * graphCost;
                float meshScore = agent.Selection.MeshBias + agent.Selection.MeshCostWeight * meshCost;
                result = graphScore <= meshScore ? graphResult : meshResult;
                return true;
            }

            if (graphOk)
            {
                result = graphResult;
                return true;
            }

            if (meshOk)
            {
                result = meshResult;
                return true;
            }

            result = MergeFailures(graphResult, meshResult, in request);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }

        private CompiledAgentType ResolveAgent(string agentTypeId)
        {
            if (!string.IsNullOrWhiteSpace(agentTypeId) && _agents.TryGetValue(agentTypeId, out var a)) return a;
            return _defaultAgent;
        }

        private bool TrySolveGraph(in PathRequest request, in CompiledAgentType agent, out PathResult result, out float travelCost)
        {
            travelCost = 0f;

            int startNodeId;
            int goalNodeId;

            if (request.Start.Kind == PathEndpointKind.NodeId && request.Goal.Kind == PathEndpointKind.NodeId)
            {
                startNodeId = request.Start.NodeId;
                goalNodeId = request.Goal.NodeId;
            }
            else if (request.Start.Kind == PathEndpointKind.WorldCm && request.Goal.Kind == PathEndpointKind.WorldCm)
            {
                if (!_graphIndex.TryFindNearest(new WorldCmInt2(request.Start.Xcm, request.Start.Ycm), agent.GraphProjectionMaxRadiusCm, out startNodeId, out _))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, expanded: 0, errorCode: 10);
                    return false;
                }
                if (!_graphIndex.TryFindNearest(new WorldCmInt2(request.Goal.Xcm, request.Goal.Ycm), agent.GraphProjectionMaxRadiusCm, out goalNodeId, out _))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, expanded: 0, errorCode: 11);
                    return false;
                }
            }
            else
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, expanded: 0, errorCode: 12);
                return false;
            }

            int maxExpanded = request.Budget.MaxExpanded > 0 ? request.Budget.MaxExpanded : int.MaxValue;
            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;

            EnsureCapacity(ref _nodeIdsScratch, Math.Min(_graph.NodeCount, maxPoints));
            var nodesSpan = _nodeIdsScratch.AsSpan();

            var scratch = _graphScratch;
            var policy = agent.GraphPolicy;
            var r = NodeGraphPathService.FindPathAStar(_graph, startNodeId, goalNodeId, nodesSpan, ref scratch, ref policy, maxExpanded);
            _graphScratch = scratch;

            if (r.Status != GraphPathStatus.Success)
            {
                var status = r.Status switch
                {
                    GraphPathStatus.NotFound => PathStatus.NoPath,
                    GraphPathStatus.OverBudget => PathStatus.BudgetExceeded,
                    GraphPathStatus.InvalidInput => PathStatus.InvalidRequest,
                    _ => PathStatus.Error
                };
                result = new PathResult(request.RequestId, request.Actor, status, default, r.Expanded, errorCode: (int)r.Status);
                return false;
            }

            int count = Math.Min(r.NodeCount, maxPoints);
            if (!_store.TryAllocate(count, out var handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, r.Expanded, errorCode: 4);
                return true;
            }

            EnsureCapacity(ref _xScratch, count);
            EnsureCapacity(ref _yScratch, count);

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;
            for (int i = 0; i < count; i++)
            {
                int nodeId = _nodeIdsScratch[i];
                _xScratch[i] = xs[nodeId];
                _yScratch[i] = ys[nodeId];
            }

            _store.TryWrite(in handle, _xScratch, _yScratch, count);
            travelCost = r.TravelCost;
            result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, r.Expanded, errorCode: 0);
            return true;
        }

        private bool TrySolveMesh(in PathRequest request, in CompiledAgentType agent, out PathResult result, out float travelCost)
        {
            travelCost = 0f;

            if (request.Start.Kind != PathEndpointKind.WorldCm || request.Goal.Kind != PathEndpointKind.WorldCm)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, expanded: 0, errorCode: 20);
                return false;
            }

            if (!_navRegistry.TryCreateQuery(agent.NavLayer, agent.NavProfileIndex, agent.NavAreaCosts, out var query))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.NotReady, default, expanded: 0, errorCode: 21);
                return false;
            }

            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;
            int maxPortals = Math.Max(0, maxPoints - 2);

            var r = query.TryFindPath(request.Start.Xcm, request.Start.Ycm, request.Goal.Xcm, request.Goal.Ycm, maxPortals);
            if (r.Status != NavPathStatus.Ok)
            {
                var status = r.Status switch
                {
                    NavPathStatus.NotReachable => PathStatus.NoPath,
                    NavPathStatus.NotReady => PathStatus.NotReady,
                    NavPathStatus.InvalidInput => PathStatus.InvalidRequest,
                    _ => PathStatus.Error
                };
                result = new PathResult(request.RequestId, request.Actor, status, default, expanded: 0, errorCode: (int)r.Status);
                return false;
            }

            int count = Math.Min(r.PathXcm.Length, maxPoints);
            if (!_store.TryAllocate(count, out var handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                return true;
            }

            _store.TryWrite(in handle, r.PathXcm, r.PathZcm, count);
            travelCost = r.TravelCost.ToFloat();
            result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, expanded: 0, errorCode: 0);
            return true;
        }

        private static PathResult MergeFailures(in PathResult graph, in PathResult mesh, in PathRequest request)
        {
            if (graph.Status == PathStatus.NotReady || mesh.Status == PathStatus.NotReady)
            {
                return new PathResult(request.RequestId, request.Actor, PathStatus.NotReady, default, 0, errorCode: 30);
            }
            if (graph.Status == PathStatus.BudgetExceeded || mesh.Status == PathStatus.BudgetExceeded)
            {
                return new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 31);
            }
            if (graph.Status == PathStatus.InvalidRequest || mesh.Status == PathStatus.InvalidRequest)
            {
                return new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 32);
            }
            return new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, 0, errorCode: 33);
        }

        private CompiledAgentType CompileAgent(PathingAgentTypeConfig cfg)
        {
            if (!_navProfiles.TryGetIndex(cfg.ProfileId, out int profileIndex))
            {
                throw new InvalidOperationException($"Unknown navmesh profileId: {cfg.ProfileId}");
            }
            var areaCosts = BuildAreaCosts(cfg.NavMesh);

            var policy = new TagRuleTraversalPolicy(_graph)
            {
                UseEdgeFilter = cfg.NodeGraph != null && (cfg.NodeGraph.RequiredTagsAll.Count > 0 || cfg.NodeGraph.ForbiddenTagsAny.Count > 0),
                EdgeFilter = PathingNodeGraphPolicyCompiler.CompileEdgeFilter(cfg.NodeGraph),
                EdgeRules = PathingNodeGraphPolicyCompiler.CompileEdgeRules(cfg.NodeGraph)
            };

            int projection = cfg.NodeGraph?.ProjectionMaxRadiusCm ?? 200000;
            if (projection <= 0) projection = 0;

            return new CompiledAgentType(
                id: cfg.Id,
                navLayer: cfg.Layer,
                navProfileIndex: profileIndex,
                navAreaCosts: areaCosts,
                graphPolicy: policy,
                graphProjectionMaxRadiusCm: projection,
                selection: cfg.Selection ?? new PathingSelectionConfig());
        }

        private static NavAreaCostTable BuildAreaCosts(PathingNavMeshConfig cfg)
        {
            var arr = new Fix64[256];
            for (int i = 0; i < arr.Length; i++) arr[i] = Fix64.OneValue;
            if (cfg?.AreaCosts != null)
            {
                for (int i = 0; i < cfg.AreaCosts.Count; i++)
                {
                    var a = cfg.AreaCosts[i];
                    if (a == null) continue;
                    if (a.AreaId < 0 || a.AreaId > 255) throw new InvalidOperationException($"Invalid areaId: {a.AreaId}");
                    if (a.Cost <= 0f || float.IsNaN(a.Cost)) throw new InvalidOperationException($"Invalid cost for areaId={a.AreaId}");
                    arr[a.AreaId] = Fix64.FromFloat(a.Cost);
                }
            }
            return new NavAreaCostTable(arr);
        }

        private static void EnsureCapacity<T>(ref T[] array, int required)
        {
            if (array.Length >= required) return;
            int next = array.Length == 0 ? 4 : array.Length * 2;
            if (next < required) next = required;
            Array.Resize(ref array, next);
        }

        private readonly struct CompiledAgentType
        {
            public readonly string Id;
            public readonly int NavLayer;
            public readonly int NavProfileIndex;
            public readonly NavAreaCostTable NavAreaCosts;
            public readonly TagRuleTraversalPolicy GraphPolicy;
            public readonly int GraphProjectionMaxRadiusCm;
            public readonly PathingSelectionConfig Selection;

            public CompiledAgentType(string id, int navLayer, int navProfileIndex, NavAreaCostTable navAreaCosts, TagRuleTraversalPolicy graphPolicy, int graphProjectionMaxRadiusCm, PathingSelectionConfig selection)
            {
                Id = id;
                NavLayer = navLayer;
                NavProfileIndex = navProfileIndex;
                NavAreaCosts = navAreaCosts;
                GraphPolicy = graphPolicy;
                GraphProjectionMaxRadiusCm = graphProjectionMaxRadiusCm;
                Selection = selection;
            }
        }
    }
}

