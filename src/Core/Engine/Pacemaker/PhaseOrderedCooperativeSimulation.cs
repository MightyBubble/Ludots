using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Engine.Pacemaker
{
    public sealed class PhaseOrderedCooperativeSimulation : ICooperativeSimulation
    {
        private static readonly SystemGroup[] PhaseOrder =
        [
            SystemGroup.SchemaUpdate,
            SystemGroup.InputCollection,
            SystemGroup.PostMovement,
            SystemGroup.AbilityActivation,
            SystemGroup.EffectProcessing,
            SystemGroup.AttributeCalculation,
            SystemGroup.DeferredTriggerCollection,
            SystemGroup.Cleanup,
            SystemGroup.EventDispatch,
            SystemGroup.ClearPresentationFlags
        ];

        private readonly Dictionary<SystemGroup, List<ISystem<float>>> _systemGroups;
        private readonly Action<float> _onStepCompleted;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        private bool _stepActive;
        private int _phaseIndex;
        private int _systemIndex;

        public PhaseOrderedCooperativeSimulation(
            Dictionary<SystemGroup, List<ISystem<float>>> systemGroups,
            Action<float> onStepCompleted = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
        {
            _systemGroups = systemGroups;
            _onStepCompleted = onStepCompleted;
            _timingDiagnostics = timingDiagnostics;
        }

        public bool Step(float fixedDt, int timeBudgetMs)
        {
            if (!_stepActive)
            {
                _stepActive = true;
                _phaseIndex = 0;
                _systemIndex = 0;
            }

            if (timeBudgetMs <= 0) timeBudgetMs = 1;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = timeBudgetMs * (System.Diagnostics.Stopwatch.Frequency / 1000);

            while (_phaseIndex < PhaseOrder.Length)
            {
                var phase = PhaseOrder[_phaseIndex];
                if (_systemGroups.TryGetValue(phase, out var systems))
                {
                    for (int i = _systemIndex; i < systems.Count; i++)
                    {
                        var sys = systems[i];
                        long systemStart = _timingDiagnostics?.SystemBreakdownEnabled == true
                            ? System.Diagnostics.Stopwatch.GetTimestamp()
                            : 0L;
                        if (sys is ITimeSlicedSystem timeSliced)
                        {
                            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                            int remainingMs = (int)((budgetTicks - elapsed) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                            if (remainingMs <= 0) remainingMs = 1;
                            if (!timeSliced.UpdateSlice(fixedDt, remainingMs))
                            {
                                ObserveSystemTiming(sys, systemStart);
                                _systemIndex = i;
                                return false;
                            }
                        }
                        else
                        {
                            sys.Update(fixedDt);
                        }
                        ObserveSystemTiming(sys, systemStart);

                        _systemIndex = i + 1;
                    }
                }

                _phaseIndex++;
                _systemIndex = 0;
            }

            _stepActive = false;
            _phaseIndex = 0;
            _systemIndex = 0;
            _onStepCompleted?.Invoke(fixedDt);
            return true;
        }

        private void ObserveSystemTiming(ISystem<float> system, long startTimestamp)
        {
            if (_timingDiagnostics?.SystemBreakdownEnabled != true)
            {
                return;
            }

            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            _timingDiagnostics.ObserveSimulationSystem(system.GetType().Name, elapsedMs);
        }

        public void Reset()
        {
            _stepActive = false;
            _phaseIndex = 0;
            _systemIndex = 0;

            foreach (var kvp in _systemGroups)
            {
                var list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is ITimeSlicedSystem timeSliced)
                    {
                        timeSliced.ResetSlice();
                    }
                }
            }
        }
    }
}
