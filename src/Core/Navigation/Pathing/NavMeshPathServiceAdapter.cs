using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing.Config;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class NavMeshPathServiceAdapter : IPathService
    {
        private readonly NavQueryServiceRegistry _navRegistry;
        private readonly NavMeshProfileRegistry _navProfiles;
        private readonly AgentProfileRegistry _agentProfiles;
        private readonly PathingConfig _pathing;
        private readonly PathStore _store;
        private readonly Dictionary<string, int> _agentIndex;

        public NavMeshPathServiceAdapter(
            NavQueryServiceRegistry navRegistry,
            NavMeshProfileRegistry navProfiles,
            AgentProfileRegistry agentProfiles,
            PathingConfig pathing,
            PathStore store)
        {
            _navRegistry = navRegistry ?? throw new ArgumentNullException(nameof(navRegistry));
            _navProfiles = navProfiles ?? throw new ArgumentNullException(nameof(navProfiles));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
            _pathing = pathing ?? throw new ArgumentNullException(nameof(pathing));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (pathing.AgentTypes == null || pathing.AgentTypes.Count == 0)
            {
                throw new InvalidOperationException("PathingConfig.agentTypes must define at least one agent type for navmesh path service bootstrap.");
            }

            _agentIndex = new Dictionary<string, int>(pathing.AgentTypes.Count, StringComparer.Ordinal);
            for (int i = 0; i < pathing.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig agent = pathing.AgentTypes[i]
                    ?? throw new InvalidOperationException("PathingConfig.agentTypes contains null.");
                if (string.IsNullOrWhiteSpace(agent.Id) || !_agentIndex.TryAdd(agent.Id, i))
                {
                    throw new InvalidOperationException($"PathingConfig.agentTypes contains a missing or duplicate id '{agent?.Id}'.");
                }
            }
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

            NavQueryService query = ResolveQuery(request.AgentTypeId);
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

        private NavQueryService ResolveQuery(string agentTypeId)
        {
            if (string.IsNullOrWhiteSpace(agentTypeId))
            {
                throw new InvalidOperationException("NavMesh path request requires an agent type id.");
            }

            if (!_agentIndex.TryGetValue(agentTypeId, out int index))
            {
                throw new InvalidOperationException($"NavMesh path request agent type '{agentTypeId}' is not in PathingConfig.agentTypes.");
            }

            PathingAgentTypeConfig agent = _pathing.AgentTypes[index];
            AgentProfileConfig body = _agentProfiles.Require(agent.ProfileId, $"PathingConfig.agentTypes[{agent.Id}]");
            if (!_navProfiles.TryGetIndex(body.NavProfileId, out int profileIndex))
            {
                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{agent.Id}] agent profile '{body.Id}' references nav profile '{body.NavProfileId}', which is not registered.");
            }

            if (!_navRegistry.TryCreateQuery(body.Layer, profileIndex, PathingAreaCostCompiler.Compile(agent.NavMesh), out NavQueryService query))
            {
                throw new InvalidOperationException(
                    $"PathingConfig.agentTypes[{agent.Id}] cannot create a navmesh query for layer {body.Layer}, nav profile '{body.NavProfileId}'.");
            }

            return query;
        }
    }
}
