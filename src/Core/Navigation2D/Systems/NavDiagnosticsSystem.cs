using System;
using System.Collections.Generic;
using System.Text;
using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavDiagnosticsSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription PerfQuery = new QueryDescription().WithAll<Navigation2DPerfStats>();
        private static readonly QueryDescription GroupQuery = new QueryDescription().WithAll<NavGroupRuntimeState>();
        private static readonly QueryDescription SolverModeQuery = new QueryDescription().WithAll<NavSolverModeComponent>();

        private readonly NavDiagnosticsSnapshot _snapshot;
        private readonly SimulationTimingSnapshot _timingSnapshot;
        private readonly Navigation2DContractCatalog _catalog;
        private readonly Navigation2DRuntime _runtime;
        private readonly PresentationTimingDiagnostics? _presentationTiming;
        private long _lastAllocatedBytes;

        public NavDiagnosticsSystem(
            World world,
            NavDiagnosticsSnapshot snapshot,
            SimulationTimingSnapshot timingSnapshot,
            Navigation2DRuntime runtime,
            Navigation2DContractCatalog catalog,
            PresentationTimingDiagnostics? presentationTiming) : base(world)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _timingSnapshot = timingSnapshot ?? throw new ArgumentNullException(nameof(timingSnapshot));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _presentationTiming = presentationTiming;
        }

        public override void Update(in float dt)
        {
            Navigation2DPerfStats navPerf = default;
            World.Query(in PerfQuery, (Entity _, ref Navigation2DPerfStats stats) => navPerf = stats);

            int retryCount = 0;
            int timeoutCount = 0;
            int abandonCount = 0;
            int activeGroups = 0;
            int arrivedGroups = 0;
            var activeRules = new Dictionary<int, int>();
            World.Query(in GroupQuery, (Entity _, ref NavGroupRuntimeState state) =>
            {
                activeGroups++;
                if (state.IsArrived != 0)
                {
                    arrivedGroups++;
                }

                retryCount += state.RetryCount;
                timeoutCount += state.TimeoutCount;
                abandonCount += state.AbandonCount;
                if (state.ActiveRuleId > 0)
                {
                    activeRules.TryGetValue(state.ActiveRuleId, out int current);
                    activeRules[state.ActiveRuleId] = current + 1;
                }
            });

            int preciseOrcaAgents = 0;
            int crowdFlowAgents = 0;
            int hybridAgents = 0;
            World.Query(in SolverModeQuery, (Entity _, ref NavSolverModeComponent mode) =>
            {
                switch (mode.SolverMode)
                {
                    case NavSolverMode.PreciseOrca:
                        preciseOrcaAgents++;
                        break;
                    case NavSolverMode.CrowdFlow:
                        crowdFlowAgents++;
                        break;
                    default:
                        hybridAgents++;
                        break;
                }
            });

            double presentationMs = 0d;
            if (_presentationTiming != null)
            {
                presentationMs =
                    _presentationTiming.UiInputMs +
                    _presentationTiming.UiRenderMs +
                    _presentationTiming.UiUploadMs +
                    _presentationTiming.ScreenOverlayBuildMs +
                    _presentationTiming.ScreenOverlayDrawMs +
                    _presentationTiming.CameraCullingMs +
                    _presentationTiming.CameraPresenterMs +
                    _presentationTiming.WorldHudProjectionMs +
                    _presentationTiming.TerrainRenderMs +
                    _presentationTiming.TerrainChunkBuildMs +
                    _presentationTiming.PrimitiveRenderMs;
            }

            _snapshot.ActiveAgents = navPerf.ActiveAgents;
            _snapshot.ActiveGroups = activeGroups > 0 ? activeGroups : navPerf.ActiveGroups;
            _snapshot.ArrivedGroups = arrivedGroups;
            _snapshot.RetryCount = retryCount;
            _snapshot.TimeoutCount = timeoutCount;
            _snapshot.AbandonCount = abandonCount;
            _snapshot.PreciseOrcaAgents = preciseOrcaAgents;
            _snapshot.CrowdFlowAgents = crowdFlowAgents;
            _snapshot.HybridAgents = hybridAgents;
            _snapshot.ActiveFlowDomains = _runtime.FlowDomains?.ActiveLeaseCount ?? 0;
            _snapshot.AssignedFlowDomains = _runtime.FlowDomains?.ActiveAssignmentCount ?? 0;
            _snapshot.UnassignedFlowDomainRequests = _runtime.FlowDomains?.UnassignedRequestCountFrame ?? 0;
            _snapshot.FixedHz = _timingSnapshot.FixedHz > 0 ? _timingSnapshot.FixedHz : navPerf.FixedHz;
            _snapshot.NavigationHz = _timingSnapshot.NavigationHz > 0 ? _timingSnapshot.NavigationHz : navPerf.NavigationHz;
            _snapshot.NavigationStepsLastFixedTick = _timingSnapshot.NavigationStepsLastFixedTick > 0
                ? _timingSnapshot.NavigationStepsLastFixedTick
                : navPerf.NavigationStepsLastFixedTick;
            _snapshot.PhysicsHz = _timingSnapshot.PhysicsHz;
            _snapshot.PhysicsStepsLastFixedTick = _timingSnapshot.PhysicsStepsLastFixedTick;
            _snapshot.NavigationMs = _timingSnapshot.NavigationMs > 0d ? _timingSnapshot.NavigationMs : navPerf.NavigationUpdateMs;
            _snapshot.NavigationSyncMs = _timingSnapshot.NavigationSyncMs;
            _snapshot.NavigationCellMapMs = _timingSnapshot.NavigationCellMapMs;
            _snapshot.NavigationFlowMs = _timingSnapshot.NavigationFlowMs;
            _snapshot.NavigationSmartStopMs = _timingSnapshot.NavigationSmartStopMs;
            _snapshot.NavigationSteeringMs = _timingSnapshot.NavigationSteeringMs;
            _snapshot.PhysicsMs = _timingSnapshot.PhysicsMs;
            _snapshot.PresentationMs = presentationMs;
            _snapshot.FrameMs = _snapshot.NavigationMs + _snapshot.PhysicsMs + presentationMs;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            _snapshot.FrameAllocBytes = _lastAllocatedBytes > 0 ? Math.Max(0, allocatedBytes - _lastAllocatedBytes) : 0;
            _lastAllocatedBytes = allocatedBytes;
            _snapshot.HeapBytes = GC.GetTotalMemory(forceFullCollection: false);
            _snapshot.ActiveRuleSummary = BuildRuleSummary(activeRules);
            _snapshot.FlowDomainSummary = _runtime.FlowDomains?.BuildSummary() ?? "disabled";
        }

        private string BuildRuleSummary(Dictionary<int, int> activeRules)
        {
            if (activeRules.Count == 0)
            {
                return "none";
            }

            var builder = new StringBuilder();
            bool first = true;
            var ruleIds = new List<int>(activeRules.Keys);
            ruleIds.Sort();
            for (int i = 0; i < ruleIds.Count; i++)
            {
                int ruleId = ruleIds[i];
                int count = activeRules[ruleId];
                if (!_catalog.TryGetSolverRule(ruleId, out NavSolverRuleDefinition rule))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(" | ");
                }

                first = false;
                builder.Append(rule.Key).Append(':').Append(count);
            }

            return builder.Length > 0 ? builder.ToString() : "none";
        }
    }
}
