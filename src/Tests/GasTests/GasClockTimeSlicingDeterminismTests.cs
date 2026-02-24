using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class GasClockTimeSlicingDeterminismTests
    {
        [Test]
        public void WaitTicks_10Hz_IsDeterministicAcrossTimeSlicing()
        {
            var noSlice = RunWaitScenario(stepEveryFixedTicks: 6, waitTicks: 3, yieldsPerStep: 0, switchAtFixedFrame: -1);
            var sliced = RunWaitScenario(stepEveryFixedTicks: 6, waitTicks: 3, yieldsPerStep: 2, switchAtFixedFrame: -1);

            That(sliced.CompletedStep, Is.EqualTo(noSlice.CompletedStep));
            That(sliced.CompletedFixedFrame, Is.EqualTo(noSlice.CompletedFixedFrame));
        }

        [Test]
        public void WaitTicks_WithRuntimeSwitch_IsDeterministicAcrossTimeSlicing()
        {
            var noSlice = RunWaitScenario(stepEveryFixedTicks: 1, waitTicks: 10, yieldsPerStep: 0, switchAtFixedFrame: 3);
            var sliced = RunWaitScenario(stepEveryFixedTicks: 1, waitTicks: 10, yieldsPerStep: 2, switchAtFixedFrame: 3);

            That(sliced.CompletedStep, Is.EqualTo(noSlice.CompletedStep));
            That(sliced.CompletedFixedFrame, Is.EqualTo(noSlice.CompletedFixedFrame));
        }

        private static (int CompletedStep, int CompletedFixedFrame) RunWaitScenario(int stepEveryFixedTicks, int waitTicks, int yieldsPerStep, int switchAtFixedFrame)
        {
            Time.FixedDeltaTime = 1f / 60f;

            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(stepEveryFixedTicks);

            var clockSystem = new GasClockSystem(clock, policy);
            var switchSystem = new PolicySwitchSystem(clock, policy, switchAtFixedFrame, switchToStepEveryFixedTicks: 6);
            var waitSystem = new WaitTicksSystem(clock, waitTicks);
            var yieldSystem = new YieldPerStepSystem(yieldsPerStep);

            var systems = new Dictionary<SystemGroup, List<ISystem<float>>>
            {
                [SystemGroup.InputCollection] = new List<ISystem<float>> { clockSystem, switchSystem },
                [SystemGroup.EffectProcessing] = new List<ISystem<float>> { yieldSystem, waitSystem }
            };

            var sim = new PhaseOrderedCooperativeSimulation(systems);
            var pacemaker = new RealtimePacemaker();
            pacemaker.Reset();

            double fixedTotalStart = Time.FixedTotalTime;
            const int timeBudgetMs = 1000;
            const int maxSlicesPerLogicFrame = 1000;

            for (int guard = 0; guard < 10000 && !waitSystem.Done; guard++)
            {
                int stepsBefore = CountCompletedFixedSteps(fixedTotalStart);

                pacemaker.Update(Time.FixedDeltaTime, sim, timeBudgetMs, maxSlicesPerLogicFrame);
                while (CountCompletedFixedSteps(fixedTotalStart) == stepsBefore)
                {
                    pacemaker.Update(0f, sim, timeBudgetMs, maxSlicesPerLogicFrame);
                }
            }

            That(waitSystem.Done, Is.True);
            return (waitSystem.CompletedStep, waitSystem.CompletedFixedFrame);
        }

        private static int CountCompletedFixedSteps(double fixedTotalStart)
        {
            double delta = Time.FixedTotalTime - fixedTotalStart;
            if (delta <= 0) return 0;
            return (int)System.Math.Floor(delta / Time.FixedDeltaTime + 1e-9);
        }

        private sealed class WaitTicksSystem : ISystem<float>
        {
            private readonly IClock _clock;
            private readonly int _ticks;
            private int _deadline;
            private bool _started;

            public bool Done { get; private set; }
            public int CompletedStep { get; private set; } = -1;
            public int CompletedFixedFrame { get; private set; } = -1;

            public WaitTicksSystem(IClock clock, int ticks)
            {
                _clock = clock;
                _ticks = ticks;
            }

            public void Initialize() { }

            public void Update(in float dt)
            {
                if (Done) return;

                int stepNow = _clock.Now(ClockDomainId.Step);
                if (!_started)
                {
                    _started = true;
                    _deadline = stepNow + _ticks;
                }

                if (stepNow < _deadline) return;

                Done = true;
                CompletedStep = stepNow;
                CompletedFixedFrame = _clock.Now(ClockDomainId.FixedFrame);
            }

            public void BeforeUpdate(in float dt) { }
            public void AfterUpdate(in float dt) { }
            public void Dispose() { }
        }

        private sealed class YieldPerStepSystem : ISystem<float>, ITimeSlicedSystem
        {
            private readonly int _yieldsPerStep;
            private int _remainingYields;
            private bool _active;

            public YieldPerStepSystem(int yieldsPerStep)
            {
                _yieldsPerStep = yieldsPerStep;
            }

            public void Initialize() { }

            public void Update(in float dt)
            {
                UpdateSlice(dt, int.MaxValue);
            }

            public bool UpdateSlice(float dt, int timeBudgetMs)
            {
                if (_yieldsPerStep <= 0) return true;

                if (!_active)
                {
                    _active = true;
                    _remainingYields = _yieldsPerStep;
                }

                if (_remainingYields > 0)
                {
                    _remainingYields--;
                    return false;
                }

                _active = false;
                return true;
            }

            public void ResetSlice()
            {
                _active = false;
                _remainingYields = 0;
            }

            public void BeforeUpdate(in float dt) { }
            public void AfterUpdate(in float dt) { }
            public void Dispose() { }
        }

        private sealed class PolicySwitchSystem : ISystem<float>
        {
            private readonly IClock _clock;
            private readonly GasClockStepPolicy _policy;
            private readonly int _switchAtFixedFrame;
            private readonly int _switchToStepEveryFixedTicks;
            private bool _switched;

            public PolicySwitchSystem(IClock clock, GasClockStepPolicy policy, int switchAtFixedFrame, int switchToStepEveryFixedTicks)
            {
                _clock = clock;
                _policy = policy;
                _switchAtFixedFrame = switchAtFixedFrame;
                _switchToStepEveryFixedTicks = switchToStepEveryFixedTicks;
            }

            public void Initialize() { }

            public void Update(in float dt)
            {
                if (_switched) return;
                if (_switchAtFixedFrame < 0) return;
                if (_clock.Now(ClockDomainId.FixedFrame) != _switchAtFixedFrame) return;
                _policy.SetStepEveryFixedTicks(_switchToStepEveryFixedTicks);
                _switched = true;
            }

            public void BeforeUpdate(in float dt) { }
            public void AfterUpdate(in float dt) { }
            public void Dispose() { }
        }
    }
}

