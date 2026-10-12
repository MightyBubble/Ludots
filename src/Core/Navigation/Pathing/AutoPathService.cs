using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class AutoPathService : IPathService
    {
        private readonly NodeGraph _graph;
        private readonly INodeGraphSpatialIndex _graphIndex;
        private readonly LoadedGraphRuntime _graphRuntime;
        private readonly NavQueryServiceRegistry? _navRegistry;
        private readonly NavMeshProfileRegistry? _navProfiles;
        private readonly AgentProfileRegistry _agentProfiles;
        private readonly PathStore _store;
        private readonly DirectPathService _directPathService;
        private readonly GraphEdgeCostOverlay? _edgeOverlay;
        private readonly bool _navMeshAvailable;

        private readonly Dictionary<string, CompiledAgentType> _agents;
        private readonly Dictionary<string, NavQueryService> _meshQueries;
        private readonly string _mapId;
        private readonly bool _meshScopeSpecified;

        private NodeGraphPathScratch _graphScratch = new NodeGraphPathScratch();
        private int[] _nodeIdsScratch = Array.Empty<int>();
        private int[] _xScratch = Array.Empty<int>();
        private int[] _yScratch = Array.Empty<int>();

        public AutoPathService(NodeGraph graph, NavQueryServiceRegistry navRegistry, NavMeshProfileRegistry navProfiles, AgentProfileRegistry agentProfiles, PathStore store, PathingConfig config, GraphEdgeCostOverlay? edgeOverlay = null, NavMapQueryScope meshScope = default)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _graphIndex = LoadedGraphRuntime.CreateSpatialIndex(_graph, preferredCellSizeCm: 0);
            _graphRuntime = null;
            _navRegistry = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _navProfiles = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directPathService = new DirectPathService(_store);
            _edgeOverlay = edgeOverlay;
            _navMeshAvailable = true;
            _agents = CompileAgents(config);
            _meshScopeSpecified = meshScope.IsSpecified;
            _mapId = meshScope.MapId;
            _meshQueries = InstallMeshQueries(config, meshScope);
        }

        public AutoPathService(LoadedGraphRuntime graphRuntime, NavQueryServiceRegistry navRegistry, NavMeshProfileRegistry navProfiles, AgentProfileRegistry agentProfiles, PathStore store, PathingConfig config, GraphEdgeCostOverlay? edgeOverlay = null, NavMapQueryScope meshScope = default)
        {
            _graphRuntime = graphRuntime ?? throw new ArgumentNullException(nameof(graphRuntime));
            _graph = null;
            _graphIndex = null;
            _navRegistry = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _navProfiles = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directPathService = new DirectPathService(_store);
            _edgeOverlay = edgeOverlay;
            _navMeshAvailable = true;
            _agents = CompileAgents(config);
            _meshScopeSpecified = meshScope.IsSpecified;
            _mapId = meshScope.MapId;
            _meshQueries = InstallMeshQueries(config, meshScope);
        }

        public AutoPathService(NavQueryServiceRegistry navRegistry, NavMeshProfileRegistry navProfiles, AgentProfileRegistry agentProfiles, PathStore store, PathingConfig config, GraphEdgeCostOverlay? edgeOverlay = null, NavMapQueryScope meshScope = default)
        {
            _graphRuntime = null;
            _graph = null;
            _graphIndex = null;
            _navRegistry = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _navProfiles = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directPathService = new DirectPathService(_store);
            _edgeOverlay = edgeOverlay;
            _navMeshAvailable = true;
            _agents = CompileAgents(config);
            _meshScopeSpecified = meshScope.IsSpecified;
            _mapId = meshScope.MapId;
            _meshQueries = InstallMeshQueries(config, meshScope);
        }

        public AutoPathService(NodeGraph graph, AgentProfileRegistry agentProfiles, PathStore store, PathingConfig config, GraphEdgeCostOverlay? edgeOverlay = null, NavMapQueryScope meshScope = default)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _graphIndex = LoadedGraphRuntime.CreateSpatialIndex(_graph, preferredCellSizeCm: 0);
            _graphRuntime = null;
            _navRegistry = null;
            _navProfiles = null;
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directPathService = new DirectPathService(_store);
            _edgeOverlay = edgeOverlay;
            _navMeshAvailable = false;
            _agents = CompileAgents(config);
            _meshScopeSpecified = meshScope.IsSpecified;
            _mapId = meshScope.MapId;
            _meshQueries = InstallMeshQueries(config, meshScope);
        }

        public AutoPathService(LoadedGraphRuntime graphRuntime, AgentProfileRegistry agentProfiles, PathStore store, PathingConfig config, GraphEdgeCostOverlay? edgeOverlay = null, NavMapQueryScope meshScope = default)
        {
            _graphRuntime = graphRuntime ?? throw new ArgumentNullException(nameof(graphRuntime));
            _graph = null;
            _graphIndex = null;
            _navRegistry = null;
            _navProfiles = null;
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directPathService = new DirectPathService(_store);
            _edgeOverlay = edgeOverlay;
            _navMeshAvailable = false;
            _agents = CompileAgents(config);
            _meshScopeSpecified = meshScope.IsSpecified;
            _mapId = meshScope.MapId;
            _meshQueries = InstallMeshQueries(config, meshScope);
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain == PathDomain.NavMesh)
            {
                var meshAgent = ResolveAgent(request.AgentTypeId);
                TrySolveMesh(in request, in meshAgent, out result, out _);
                return true;
            }

            if (request.Domain != PathDomain.Auto)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                return false;
            }

            var agent = ResolveAgent(request.AgentTypeId);
            if (agent.Selection.Mode == PathSelectionMode.Direct)
            {
                return _directPathService.TrySolve(in request, out result);
            }

            if (agent.Selection.Mode == PathSelectionMode.PreferGraph)
            {
                TrySolveGraph(in request, in agent, out result, out _);
                return true;
            }
            if (agent.Selection.Mode == PathSelectionMode.PreferMesh)
            {
                TrySolveMesh(in request, in agent, out result, out _);
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

        public bool TrySnapToNavigationSurface(string agentTypeId, int worldXcm, int worldZcm, out int snappedXcm, out int snappedZcm)
        {
            snappedXcm = worldXcm;
            snappedZcm = worldZcm;
            if (!_navMeshAvailable || _navRegistry == null)
            {
                return false;
            }

            CompiledAgentType agent = ResolveAgent(agentTypeId);
            if (!_navRegistry.TryCreateQuery(agent.NavLayer, agent.NavProfileIndex, agent.NavAreaCosts, out var query))
            {
                return false;
            }

            return query.TrySnapToSurface(worldXcm, worldZcm, out snappedXcm, out snappedZcm);
        }

        private CompiledAgentType ResolveAgent(string agentTypeId)
        {
            if (string.IsNullOrWhiteSpace(agentTypeId))
            {
                throw new InvalidOperationException("Path request requires an agent type id.");
            }

            if (_agents.TryGetValue(agentTypeId, out CompiledAgentType agent))
            {
                return agent;
            }

            throw new InvalidOperationException($"Path request agent type '{agentTypeId}' is not in PathingConfig.agentTypes.");
        }

        private Dictionary<string, CompiledAgentType> CompileAgents(PathingConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.AgentTypes == null || config.AgentTypes.Count == 0) throw new InvalidOperationException("PathingConfig.agentTypes is empty.");
            var agents = new Dictionary<string, CompiledAgentType>(config.AgentTypes.Count, StringComparer.OrdinalIgnoreCase);
            int compiled = 0;
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = config.AgentTypes[i];
                if (agent == null) continue;
                CompiledAgentType compiledAgent = CompileAgent(agent);
                agents[compiledAgent.Id] = compiledAgent;
                compiled++;
            }

            if (compiled == 0) throw new InvalidOperationException("PathingConfig.agentTypes has no valid entries.");
            return agents;
        }

        private bool TrySolveGraph(in PathRequest request, in CompiledAgentType agent, out PathResult result, out float travelCost)
        {
            travelCost = 0f;
            NodeGraph graph = ResolveGraph();
            INodeGraphSpatialIndex graphIndex = ResolveGraphIndex();
            TagRuleTraversalPolicy policy = agent.CreateGraphPolicy(graph);
            if (agent.UseDynamicOverlay)
            {
                if (_edgeOverlay == null)
                {
                    throw new InvalidOperationException($"Pathing agent '{agent.Id}' enables nodeGraph.useDynamicOverlay but GraphEdgeCostOverlay is not registered.");
                }

                _edgeOverlay.EnsureCapacity(graph.EdgeCount);
                policy.Overlay = _edgeOverlay;
                policy.UseOverlay = true;
            }

            int startNodeId;
            int goalNodeId;

            if (request.Start.Kind == PathEndpointKind.NodeId && request.Goal.Kind == PathEndpointKind.NodeId)
            {
                startNodeId = request.Start.NodeId;
                goalNodeId = request.Goal.NodeId;
            }
            else if (request.Start.Kind == PathEndpointKind.WorldCm && request.Goal.Kind == PathEndpointKind.WorldCm)
            {
                if (!graphIndex.TryFindNearest(new WorldCmInt2(request.Start.Xcm, request.Start.Ycm), agent.GraphProjectionMaxRadiusCm, out startNodeId, out _))
                {
                    result = new PathResult(
                        request.RequestId,
                        request.Actor,
                        _graphRuntime != null ? PathStatus.NotReady : PathStatus.NoPath,
                        default,
                        expanded: 0,
                        errorCode: 10);
                    return false;
                }
                if (!graphIndex.TryFindNearest(new WorldCmInt2(request.Goal.Xcm, request.Goal.Ycm), agent.GraphProjectionMaxRadiusCm, out goalNodeId, out _))
                {
                    result = new PathResult(
                        request.RequestId,
                        request.Actor,
                        _graphRuntime != null ? PathStatus.NotReady : PathStatus.NoPath,
                        default,
                        expanded: 0,
                        errorCode: 11);
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

            EnsureCapacity(ref _nodeIdsScratch, Math.Min(graph.NodeCount, maxPoints));
            var nodesSpan = _nodeIdsScratch.AsSpan();

            var scratch = _graphScratch;
            var r = NodeGraphPathService.FindPathAStar(graph, startNodeId, goalNodeId, nodesSpan, ref scratch, ref policy, maxExpanded);
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

            var xs = graph.PosXcm;
            var ys = graph.PosYcm;
            for (int i = 0; i < count; i++)
            {
                int nodeId = _nodeIdsScratch[i];
                _xScratch[i] = xs[nodeId];
                _yScratch[i] = ys[nodeId];
            }

            _store.TryWrite(in handle, _xScratch, _yScratch, count);
            travelCost = r.TravelCost;
            result = new PathResult(
                request.RequestId,
                request.Actor,
                PathStatus.Found,
                handle,
                r.Expanded,
                errorCode: 0,
                resolvedDomain: PathDomain.NodeGraph);
            return true;
        }

        private bool TrySolveMesh(in PathRequest request, in CompiledAgentType agent, out PathResult result, out float travelCost)
        {
            travelCost = 0f;

            if (_meshScopeSpecified && (_meshQueries == null || !_meshQueries.ContainsKey(agent.Id)))
            {
                throw new InvalidOperationException(
                    NavMapQueryScope.MissingQueryMessage(_mapId, agent.Id, agent.NavProfileId));
            }

            if (!_navMeshAvailable)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, expanded: 0, errorCode: 22);
                return false;
            }

            NavQueryServiceRegistry navRegistry = _navRegistry
                ?? throw new InvalidOperationException("Auto path service is missing its required nav query registry.");

            if (request.Start.Kind != PathEndpointKind.WorldCm || request.Goal.Kind != PathEndpointKind.WorldCm)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, expanded: 0, errorCode: 20);
                return false;
            }

            NavQueryService query;
            if (_meshScopeSpecified)
            {
                if (_meshQueries == null || !_meshQueries.TryGetValue(agent.Id, out query))
                {
                    throw new InvalidOperationException(
                        NavMapQueryScope.MissingQueryMessage(_mapId, agent.Id, agent.NavProfileId));
                }
            }
            else if (!navRegistry.TryCreateQuery(agent.NavLayer, agent.NavProfileIndex, agent.NavAreaCosts, out query))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.NotReady, default, expanded: 0, errorCode: 21);
                return false;
            }

            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;
            int maxPortals = Math.Max(0, maxPoints - 2);

            var r = query.TryFindPath(request.Start.Xcm, request.Start.Ycm, request.Goal.Xcm, request.Goal.Ycm, maxPortals);
            bool partial = r.Status == NavPathStatus.Partial;
            if (r.Status != NavPathStatus.Ok && !partial)
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
            result = new PathResult(
                request.RequestId,
                request.Actor,
                partial ? PathStatus.Partial : PathStatus.Found,
                handle,
                expanded: 0,
                errorCode: partial ? (int)NavPathStatus.Partial : 0,
                resolvedDomain: PathDomain.NavMesh,
                resolvedGoal: partial
                    ? new WorldCmInt2(r.ResolvedGoalXcm, r.ResolvedGoalZcm)
                    : new WorldCmInt2(request.Goal.Xcm, request.Goal.Ycm));
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

        private Dictionary<string, NavQueryService> InstallMeshQueries(PathingConfig config, NavMapQueryScope scope)
        {
            if (!scope.IsSpecified)
            {
                return null;
            }

            var queries = new Dictionary<string, NavQueryService>(StringComparer.OrdinalIgnoreCase);
            if (!_navMeshAvailable || _navRegistry == null)
            {
                return queries;
            }

            var meshProfiles = new HashSet<string>(scope.NavProfileIds, StringComparer.Ordinal);
            if (config.AgentTypes == null)
            {
                return queries;
            }

            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = config.AgentTypes[i];
                if (agent == null || !NavMapAgentBinding.SelectsMesh(agent) || !_agents.TryGetValue(agent.Id, out CompiledAgentType compiled))
                {
                    continue;
                }

                if (!meshProfiles.Contains(compiled.NavProfileId))
                {
                    continue;
                }

                if (!_navRegistry.TryCreateQuery(compiled.NavLayer, compiled.NavProfileIndex, compiled.NavAreaCosts, out NavQueryService query))
                {
                    throw new InvalidOperationException(
                        $"地图 {scope.MapId} 的移动类型 \"{compiled.Id}\"（能力档 \"{compiled.NavProfileId}\"）无法建立网格查询。");
                }

                queries[compiled.Id] = query;
            }

            return queries;
        }

        private CompiledAgentType CompileAgent(PathingAgentTypeConfig cfg)
        {
            AgentProfileConfig agentProfile = _agentProfiles.Require(cfg.ProfileId, $"PathingConfig.agentTypes[{cfg.Id}]");
            if (cfg.Selection == null)
            {
                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{cfg.Id}] has no selection. AutoPath cannot compile an agent type without selection.");
            }

            int profileIndex = -1;
            if (_navMeshAvailable && (_navProfiles == null || !_navProfiles.TryGetIndex(agentProfile.NavProfileId, out profileIndex)))
            {
                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{cfg.Id}] agent profile '{agentProfile.Id}' references unknown nav profile '{agentProfile.NavProfileId}'.");
            }

            var areaCosts = PathingAreaCostCompiler.Compile(cfg.NavMesh);
            var edgeFilter = PathingNodeGraphPolicyCompiler.CompileEdgeFilter(cfg.NodeGraph);
            var edgeRules = PathingNodeGraphPolicyCompiler.CompileEdgeRules(cfg.NodeGraph);
            bool useEdgeFilter = cfg.NodeGraph != null && (cfg.NodeGraph.RequiredTagsAll.Count > 0 || cfg.NodeGraph.ForbiddenTagsAny.Count > 0);

            int projection = cfg.NodeGraph?.ProjectionMaxRadiusCm ?? 200000;
            if (projection <= 0) projection = 0;

            return new CompiledAgentType(
                id: cfg.Id,
                navLayer: agentProfile.Layer,
                navProfileId: agentProfile.NavProfileId,
                navProfileIndex: profileIndex,
                navAreaCosts: areaCosts,
                useGraphEdgeFilter: useEdgeFilter,
                graphEdgeFilter: edgeFilter,
                graphEdgeRules: edgeRules,
                graphProjectionMaxRadiusCm: projection,
                useDynamicOverlay: cfg.NodeGraph?.UseDynamicOverlay ?? false,
                agentDraftCm: agentProfile.DraftCm,
                agentBeamCm: agentProfile.BeamCm,
                selection: cfg.Selection);
        }

        private static void EnsureCapacity<T>(ref T[] array, int required)
        {
            if (array.Length >= required) return;
            int next = array.Length == 0 ? 4 : array.Length * 2;
            if (next < required) next = required;
            Array.Resize(ref array, next);
        }

        private NodeGraph ResolveGraph()
        {
            return _graphRuntime != null ? _graphRuntime.CurrentGraph : _graph;
        }

        private INodeGraphSpatialIndex ResolveGraphIndex()
        {
            return _graphRuntime != null ? _graphRuntime.CurrentSpatialIndex : _graphIndex;
        }

        private readonly struct CompiledAgentType
        {
            public readonly string Id;
            public readonly int NavLayer;
            public readonly string NavProfileId;
            public readonly int NavProfileIndex;
            public readonly NavAreaCostTable NavAreaCosts;
            public readonly bool UseGraphEdgeFilter;
            public readonly TagFilter256 GraphEdgeFilter;
            public readonly TagRuleTraversalPolicy.TagRule[] GraphEdgeRules;
            public readonly int GraphProjectionMaxRadiusCm;
            public readonly bool UseDynamicOverlay;
            public readonly float AgentDraftCm;
            public readonly float AgentBeamCm;
            public readonly PathingSelectionConfig Selection;

            public CompiledAgentType(
                string id,
                int navLayer,
                string navProfileId,
                int navProfileIndex,
                NavAreaCostTable navAreaCosts,
                bool useGraphEdgeFilter,
                TagFilter256 graphEdgeFilter,
                TagRuleTraversalPolicy.TagRule[] graphEdgeRules,
                int graphProjectionMaxRadiusCm,
                bool useDynamicOverlay,
                float agentDraftCm,
                float agentBeamCm,
                PathingSelectionConfig selection)
            {
                Id = id;
                NavLayer = navLayer;
                NavProfileId = navProfileId;
                NavProfileIndex = navProfileIndex;
                NavAreaCosts = navAreaCosts;
                UseGraphEdgeFilter = useGraphEdgeFilter;
                GraphEdgeFilter = graphEdgeFilter;
                GraphEdgeRules = graphEdgeRules;
                GraphProjectionMaxRadiusCm = graphProjectionMaxRadiusCm;
                UseDynamicOverlay = useDynamicOverlay;
                AgentDraftCm = agentDraftCm;
                AgentBeamCm = agentBeamCm;
                Selection = selection;
            }

            public TagRuleTraversalPolicy CreateGraphPolicy(NodeGraph graph)
            {
                return new TagRuleTraversalPolicy(graph)
                {
                    UseEdgeFilter = UseGraphEdgeFilter,
                    EdgeFilter = GraphEdgeFilter,
                    EdgeRules = GraphEdgeRules,
                    AgentDraftCm = AgentDraftCm,
                    AgentBeamCm = AgentBeamCm
                };
            }
        }
    }
}
