using System;
using Ludots.Core.MassNavigation.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkMassNavigationRuntimeAccessor
    {
        private readonly MassNavigationRuntimeBinding _binding;
        private MassNavigationMovePlanExecutionSink? _executionSink;
        private MassNavigationSimulationRuntime? _executionSinkRuntime;

        public RoadNetworkMassNavigationRuntimeAccessor(MassNavigationRuntimeBinding binding)
        {
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        public MassNavigationSimulationRuntime RequireSimulation(string consumer)
        {
            if (!_binding.IsReady ||
                _binding.Current is not MassNavigationSimulationRuntime simulation)
            {
                throw new InvalidOperationException(
                    $"{consumer} requires a prepared current MassNavigation runtime before touching RoadNetwork units.");
            }

            if (!RoadNetworkShowcaseIds.IsShowcaseMap(_binding.CurrentMapId.Value))
            {
                throw new InvalidOperationException(
                    $"{consumer} cannot use MassNavigation runtime for non-road map '{_binding.CurrentMapId.Value}'.");
            }

            return simulation;
        }

        public MassNavigationMovePlanExecutionSink RequireExecutionSink(string consumer)
        {
            MassNavigationSimulationRuntime simulation = RequireSimulation(consumer);
            if (_executionSink != null &&
                ReferenceEquals(_executionSinkRuntime, simulation))
            {
                return _executionSink;
            }

            _executionSink = new MassNavigationMovePlanExecutionSink(simulation);
            _executionSinkRuntime = simulation;
            return _executionSink;
        }
    }
}
