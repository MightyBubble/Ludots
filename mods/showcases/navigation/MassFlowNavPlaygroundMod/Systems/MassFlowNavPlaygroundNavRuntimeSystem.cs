using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundNavRuntimeSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private int _lastProfilePopulation = -1;

        public MassFlowNavPlaygroundNavRuntimeSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(_engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase) ||
                _engine.GetService(CoreServiceKeys.Navigation2DRuntime) is not Navigation2DRuntime navRuntime)
            {
                return;
            }

            if (!navRuntime.FlowEnabled)
            {
                navRuntime.FlowEnabled = true;
            }

            ApplyPerformanceProfile(state, navRuntime);
        }

        private void ApplyPerformanceProfile(MassFlowNavPlaygroundState state, Navigation2DRuntime navRuntime)
        {
            int population = Math.Max(1000, state.DesiredUnitCount);
            if (population == _lastProfilePopulation)
            {
                return;
            }

            int flowIterations = population >= 20000 ? 256 : population >= 10000 ? 384 : 512;
            int maxNeighbors = population >= 20000 ? 6 : 8;
            int maxCandidateChecks = population >= 20000 ? 16 : 24;
            int navHz = population >= 20000 ? 12 : 15;

            navRuntime.FlowIterationsPerTick = flowIterations;
            navRuntime.Config.FlowIterationsPerTick = flowIterations;
            navRuntime.Config.Steering.QueryBudget.MaxNeighborsPerAgent = maxNeighbors;
            navRuntime.Config.Steering.QueryBudget.MaxCandidateChecksPerAgent = maxCandidateChecks;
            navRuntime.Config.Steering.SmartStop.Enabled = false;
            navRuntime.Config.Spatial.UpdateMode = Ludots.Core.Navigation2D.Config.Navigation2DSpatialUpdateMode.Adaptive;
            navRuntime.Config.Spatial.RebuildCellMigrationsThreshold = 128;
            navRuntime.Config.Spatial.RebuildAccumulatedCellMigrationsThreshold = 1024;

            var temporal = navRuntime.Config.Steering.TemporalCoherence;
            temporal.Enabled = true;
            temporal.RequireSteadyStateWorld = false;
            temporal.MaxReuseTicks = 12;
            temporal.PositionToleranceCm = 40;
            temporal.VelocityToleranceCmPerSec = 320;
            temporal.PreferredVelocityToleranceCmPerSec = 80;
            temporal.NeighborPositionQuantizationCm = 40;
            temporal.NeighborVelocityQuantizationCmPerSec = 320;

            if (_engine.GetService(CoreServiceKeys.Navigation2DTickPolicy) is Navigation2DTickPolicy tickPolicy)
            {
                tickPolicy.SetTargetHz(navHz);
                tickPolicy.SetMaxStepsPerFixedTick(2);
            }

            _lastProfilePopulation = population;
        }
    }
}
