using Arch.System;
using Ludots.Core.Config; // For Time class if needed, or pass it in. Assuming Time is static global.
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Engine.Pacemaker
{
    /// <summary>
    /// Controls the "heartbeat" of the simulation logic (GAS, Physics, AI).
    /// Decouples wall-clock time from simulation time.
    /// </summary>
    public interface IPacemaker
    {
        /// <summary>
        /// Called every frame with the wall-clock delta time.
        /// The pacemaker decides when and how many times to run the simulation group.
        /// </summary>
        /// <param name="dt">Wall clock delta time (scaled)</param>
        /// <param name="simulationGroup">The group of systems to execute (FixedUpdate)</param>
        void Update(float dt, ISystem<float> simulationGroup);
        void Update(float dt, ICooperativeSimulation cooperativeSimulation, int timeBudgetMs, int maxSlicesPerLogicFrame);
    }

    /// <summary>
    /// Standard Realtime Loop: Uses an accumulator to run FixedUpdate at a fixed frequency.
    /// </summary>
    public class RealtimePacemaker : IPacemaker
    {
        private double _accumulator;
        private bool _stepInProgress;
        private int _slicesInCurrentStep;
        private bool _budgetFused;

        public bool IsBudgetFused => _budgetFused;
        
        /// <summary>
        /// Interpolation alpha [0, 1] for smooth visual rendering.
        /// Represents the fraction of the current fixed timestep that has elapsed.
        /// 
        /// Used by visual sync systems to interpolate between physics states:
        /// - 0 = Just after FixedUpdate (use previous position)
        /// - 1 = Just before next FixedUpdate (use current position)
        /// 
        /// Formula: accumulator / FixedDeltaTime
        /// </summary>
        public float InterpolationAlpha
        {
            get
            {
                if (Time.FixedDeltaTime <= 0f) return 1f;
                return (float)System.Math.Min(_accumulator / Time.FixedDeltaTime, 1.0);
            }
        }

        public void Update(float dt, ISystem<float> simulationGroup)
        {
            _accumulator += dt;
            while (_accumulator >= Time.FixedDeltaTime)
            {
                simulationGroup.Update(Time.FixedDeltaTime);
                _accumulator -= Time.FixedDeltaTime;
                Time.FixedTotalTime += Time.FixedDeltaTime;
            }
        }

        public void Update(float dt, ICooperativeSimulation cooperativeSimulation, int timeBudgetMs, int maxSlicesPerLogicFrame)
        {
            if (_budgetFused) return;
            _accumulator += dt;
            if (timeBudgetMs <= 0) timeBudgetMs = 1;
            if (maxSlicesPerLogicFrame <= 0) maxSlicesPerLogicFrame = 1;

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = timeBudgetMs * (System.Diagnostics.Stopwatch.Frequency / 1000);

            if (!_stepInProgress && _accumulator >= Time.FixedDeltaTime)
            {
                _slicesInCurrentStep = 0;
            }

            while (_stepInProgress || _accumulator >= Time.FixedDeltaTime)
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                long remainingTicks = budgetTicks - elapsed;
                if (remainingTicks <= 0) break;

                int remainingMs = (int)(remainingTicks * 1000 / System.Diagnostics.Stopwatch.Frequency);
                if (remainingMs <= 0) remainingMs = 1;

                bool completed = cooperativeSimulation.Step(Time.FixedDeltaTime, remainingMs);
                _stepInProgress = !completed;

                if (completed)
                {
                    _accumulator -= Time.FixedDeltaTime;
                    Time.FixedTotalTime += Time.FixedDeltaTime;
                    _slicesInCurrentStep = 0;
                }
                else
                {
                    _slicesInCurrentStep++;
                    if (_slicesInCurrentStep >= maxSlicesPerLogicFrame)
                    {
                        _budgetFused = true;
                        Log.Warn(in LogChannels.Engine, $"BudgetFuse: cooperative step exceeded max slices ({maxSlicesPerLogicFrame}).");
                        cooperativeSimulation.Reset();
                        _stepInProgress = false;
                        _accumulator = 0;
                    }
                    break;
                }
            }
        }
        
        public void Reset()
        {
            _accumulator = 0;
            _stepInProgress = false;
            _slicesInCurrentStep = 0;
            _budgetFused = false;
        }
    }

    /// <summary>
    /// Turn-Based Loop: Simulation only advances when explicitly triggered.
    /// </summary>
    public class TurnBasedPacemaker : IPacemaker
    {
        private int _stepsToRun = 0;
        private bool _stepInProgress;
        private int _slicesInCurrentStep;
        private bool _budgetFused;

        public bool IsBudgetFused => _budgetFused;

        /// <summary>
        /// Triggers the simulation to advance by one FixedDeltaTime step.
        /// </summary>
        public void Step()
        {
            _stepsToRun++;
        }

        public void Update(float dt, ISystem<float> simulationGroup)
        {
            while (_stepsToRun > 0)
            {
                simulationGroup.Update(Time.FixedDeltaTime);
                Time.FixedTotalTime += Time.FixedDeltaTime;
                _stepsToRun--;
            }
        }

        public void Update(float dt, ICooperativeSimulation cooperativeSimulation, int timeBudgetMs, int maxSlicesPerLogicFrame)
        {
            if (_budgetFused) return;
            while (_stepsToRun > 0)
            {
                int budget = timeBudgetMs <= 0 ? int.MaxValue : timeBudgetMs;
                if (maxSlicesPerLogicFrame <= 0) maxSlicesPerLogicFrame = 1;
                bool completed = cooperativeSimulation.Step(Time.FixedDeltaTime, budget);
                _stepInProgress = !completed;

                if (!completed)
                {
                    _slicesInCurrentStep++;
                    if (_slicesInCurrentStep >= maxSlicesPerLogicFrame)
                    {
                        _budgetFused = true;
                        Log.Warn(in LogChannels.Engine, $"BudgetFuse: cooperative step exceeded max slices ({maxSlicesPerLogicFrame}).");
                        cooperativeSimulation.Reset();
                        _stepInProgress = false;
                        _stepsToRun = 0;
                    }
                    break;
                }

                Time.FixedTotalTime += Time.FixedDeltaTime;
                _stepsToRun--;
                _slicesInCurrentStep = 0;
            }
        }

        public void Reset()
        {
            _stepsToRun = 0;
            _stepInProgress = false;
            _slicesInCurrentStep = 0;
            _budgetFused = false;
        }
    }
}
