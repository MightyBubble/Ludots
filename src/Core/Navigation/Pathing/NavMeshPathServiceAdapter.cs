using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class NavMeshPathServiceAdapter : IPathService
    {
        private readonly PathStore _store;
        private readonly Dictionary<string, NavQueryService> _queries;

        public NavMeshPathServiceAdapter(
            NavQueryServiceRegistry navRegistry,
            NavMeshProfileRegistry navProfiles,
            AgentProfileRegistry agentProfiles,
            PathingConfig pathing,
            PathStore store)
        {
            _ = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _ = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _ = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _ = pathing ?? throw new ArgumentNullException(nameof(pathing));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (pathing.AgentTypes == null || pathing.AgentTypes.Count == 0)
            {
                throw new InvalidOperationException("PathingConfig.agentTypes must define at least one agent type for navmesh path service bootstrap.");
            }

            _queries = new Dictionary<string, NavQueryService>(pathing.AgentTypes.Count, StringComparer.Ordinal);
            for (int i = 0; i < pathing.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = pathing.AgentTypes[i]
                    ?? throw new InvalidOperationException("PathingConfig.agentTypes contains null.");
                if (string.IsNullOrWhiteSpace(agent.Id))
                {
                    throw new InvalidOperationException("PathingConfig.agentTypes contains a missing id.");
                }

                if (!SelectsNavMesh(agent))
                {
                    continue;
                }

                AgentProfileConfig body = agentProfiles.Require(agent.ProfileId, $"PathingConfig.agentTypes[{agent.Id}]");
                if (!navProfiles.TryGetIndex(body.NavProfileId, out int profileIndex))
                {
                    throw new InvalidOperationException(
                        $"PathingConfig.agentTypes[{agent.Id}] agent profile '{body.Id}' references nav profile '{body.NavProfileId}', which is not registered.");
                }

                if (!navRegistry.TryCreateQuery(body.Layer, profileIndex, PathingAreaCostCompiler.Compile(agent.NavMesh), out NavQueryService query))
                {
                    throw new InvalidOperationException(
                        $"PathingConfig.agentTypes[{agent.Id}] cannot create a navmesh query for layer {body.Layer}, nav profile '{body.NavProfileId}'.");
                }

                if (!_queries.TryAdd(agent.Id, query))
                {
                    throw new InvalidOperationException($"PathingConfig.agentTypes contains duplicate id '{agent.Id}'.");
                }
            }
        }

        private static bool SelectsNavMesh(PathingAgentTypeConfig agent)
        {
            PathSelectionMode? mode = agent.Selection?.Mode;
            return mode == PathSelectionMode.PreferMesh || mode == PathSelectionMode.AutoCheapest;
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain != PathDomain.NavMesh)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                return false;
            }

            if (request.Start.Kind != PathEndpointKind.WorldCm || request.Goal.Kind != PathEndpointKind.WorldCm)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 3);
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.AgentTypeId) || !_queries.TryGetValue(request.AgentTypeId, out NavQueryService query))
            {
                throw new InvalidOperationException(
                    $"NavMesh path request agent type '{request.AgentTypeId}' is not in PathingConfig.agentTypes.");
            }
            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;
            int maxPortals = Math.Max(0, maxPoints - 2);

            var r = query.TryFindPath(
                startXcm: request.Start.Xcm,
                startZcm: request.Start.Ycm,
                goalXcm: request.Goal.Xcm,
                goalZcm: request.Goal.Ycm,
                maxPortals: maxPortals);

            if (r.Status == NavPathStatus.Ok)
            {
                int count = Math.Min(r.PathXcm.Length, maxPoints);
                if (!_store.TryAllocate(count, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                    return true;
                }

                _store.TryWrite(in handle, r.PathXcm, r.PathZcm, count);
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.Found,
                    handle,
                    expanded: 0,
                    errorCode: 0,
                    resolvedDomain: PathDomain.NavMesh);
                return true;
            }

            var status = r.Status switch
            {
                NavPathStatus.NotReachable => PathStatus.NoPath,
                NavPathStatus.NotReady => PathStatus.NotReady,
                NavPathStatus.InvalidInput => PathStatus.InvalidRequest,
                _ => PathStatus.Error
            };

            result = new PathResult(request.RequestId, request.Actor, status, default, expanded: 0, errorCode: (int)r.Status);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }

    }
}
