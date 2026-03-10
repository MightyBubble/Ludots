using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Generic GAS selection-response system.
    /// Abilities submit SelectionRequest and wait for the next confirm click.
    /// This system resolves the request through a SelectionRuleRegistry and returns
    /// the matching entities plus the confirmed world point via SelectionResponse.
    /// </summary>
    public sealed class GasSelectionResponseSystem : ISystem<float>
    {
        private static readonly InteractionActionBindings DefaultBindings = new InteractionActionBindings();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISpatialQueryService _spatial;
        private readonly SelectionRuleRegistry _rules;

        public Action<SelectionRequest, WorldCmInt2>? OnSelectionTriggered { get; set; }

        public GasSelectionResponseSystem(
            World world,
            Dictionary<string, object> globals,
            ISpatialQueryService spatial,
            SelectionRuleRegistry? rules = null)
        {
            _world = world;
            _globals = globals;
            _spatial = spatial;
            _rules = rules ?? ResolveRules(globals) ?? SelectionRuleRegistry.CreateWithDefaults();
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input) return;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) || rayObj is not IScreenRayProvider rayProvider) return;
            if (!_globals.TryGetValue(CoreServiceKeys.SelectionRequestQueue.Name, out var reqObj) || reqObj is not SelectionRequestQueue requests) return;
            if (!_globals.TryGetValue(CoreServiceKeys.SelectionResponseBuffer.Name, out var respObj) || respObj is not SelectionResponseBuffer responses) return;
            if (!requests.TryPeek(out var request)) return;

            var bindings = ResolveBindings();
            if (!input.PressedThisFrame(bindings.ConfirmActionId)) return;

            var mouse = input.ReadAction<System.Numerics.Vector2>(bindings.PointerPositionActionId);
            var ray = rayProvider.GetRay(mouse);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm)) return;

            OnSelectionTriggered?.Invoke(request, worldCm);

            unsafe
            {
                var response = BuildResponse(request, worldCm);
                if (!responses.TryAdd(response))
                {
                    throw new InvalidOperationException($"GasSelectionResponseSystem: selection response buffer overflow while recording request {request.RequestId}.");
                }
            }

            if (!requests.TryDequeue(out var dequeued) || dequeued.RequestId != request.RequestId)
            {
                throw new InvalidOperationException($"GasSelectionResponseSystem: selection request queue lost FIFO order for request {request.RequestId}.");
            }
        }

        private unsafe SelectionResponse BuildResponse(in SelectionRequest request, in WorldCmInt2 worldCm)
        {
            if (!_rules.TryGet(request.RequestTagId, out var rule))
            {
                throw new InvalidOperationException($"GasSelectionResponseSystem: no selection rule registered for request type id {request.RequestTagId}.");
            }

            var response = default(SelectionResponse);
            response.RequestId = request.RequestId;
            response.ResponseTagId = request.RequestTagId;
            response.SetWorldPoint(worldCm);
            if (_world.IsAlive(request.TargetContext))
            {
                response.TargetContext = request.TargetContext;
            }

            int originTeamId = 0;
            if (_world.IsAlive(request.Origin))
            {
                ref var team = ref _world.TryGetRef<Team>(request.Origin, out bool hasTeam);
                if (hasTeam)
                {
                    originTeamId = team.Id;
                }
            }

            int radiusCm = request.PayloadA > 0 ? request.PayloadA : rule.RadiusCm;
            int maxCount = request.PayloadB > 0 ? request.PayloadB : rule.MaxCount;
            if (maxCount <= 0) maxCount = 64;
            if (maxCount > 64) maxCount = 64;

            switch (rule.Mode)
            {
                case SelectionRuleMode.SingleNearest:
                {
                    var entity = FindNearestEntity(worldCm, radiusCm, rule.RelationshipFilter, originTeamId);
                    if (_world.IsAlive(entity))
                    {
                        response.Count = 1;
                        response.SetEntity(0, entity);
                    }
                    return response;
                }

                case SelectionRuleMode.Radius:
                {
                    if (radiusCm <= 0)
                    {
                        return response;
                    }

                    Span<Entity> buffer = stackalloc Entity[Math.Min(Math.Max(maxCount * 4, maxCount), 512)];
                    var result = _spatial.QueryRadius(worldCm, radiusCm, buffer);
                    int written = 0;
                    for (int i = 0; i < result.Count && written < maxCount; i++)
                    {
                        var entity = buffer[i];
                        if (!_world.IsAlive(entity)) continue;
                        if (!PassesRelationship(entity, rule.RelationshipFilter, originTeamId)) continue;

                        response.SetEntity(written, entity);
                        written++;
                    }

                    response.Count = written;
                    return response;
                }

                default:
                    return response;
            }
        }

        private Entity FindNearestEntity(in WorldCmInt2 worldCm, int radiusCm, RelationshipFilter filter, int originTeamId)
        {
            if (radiusCm <= 0)
            {
                return default;
            }

            Span<Entity> buffer = stackalloc Entity[256];
            var result = _spatial.QueryRadius(worldCm, radiusCm, buffer);
            int count = result.Count;
            if (count <= 0) return default;

            Entity best = default;
            long bestD2 = long.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var entity = buffer[i];
                if (!_world.IsAlive(entity)) continue;
                if (!PassesRelationship(entity, filter, originTeamId)) continue;

                ref var pos = ref _world.TryGetRef<WorldPositionCm>(entity, out bool hasPos);
                if (!hasPos) continue;

                var cmPos = pos.Value.ToWorldCmInt2();
                long dx = cmPos.X - worldCm.X;
                long dy = cmPos.Y - worldCm.Y;
                long d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = entity;
                }
            }

            return best;
        }

        private bool PassesRelationship(Entity entity, RelationshipFilter filter, int originTeamId)
        {
            if (filter == RelationshipFilter.All)
            {
                return true;
            }

            ref var team = ref _world.TryGetRef<Team>(entity, out bool hasTeam);
            if (!hasTeam)
            {
                return false;
            }

            return RelationshipFilterUtil.Passes(filter, originTeamId, team.Id);
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) && obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        private static SelectionRuleRegistry? ResolveRules(Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.SelectionRuleRegistry.Name, out var obj) && obj is SelectionRuleRegistry rules)
            {
                return rules;
            }

            return null;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
