using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class PacemakerTimeSlicingTests
    {
        [Test]
        public void RealtimePacemaker_DoesNotAdvanceFixedTime_UntilCooperativeStepCompletes()
        {
            Time.FixedDeltaTime = 0.02f;
            double startFixedTime = Time.FixedTotalTime;

            var systems = new Dictionary<SystemGroup, List<ISystem<float>>>
            {
                [SystemGroup.EffectProcessing] = new List<ISystem<float>>
                {
                    new YieldingTimeSlicedSystem(yieldCount: 2)
                }
            };

            var sim = new PhaseOrderedCooperativeSimulation(systems);
            var pacemaker = new RealtimePacemaker();
            pacemaker.Reset();

            pacemaker.Update(Time.FixedDeltaTime, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 100);
            That(Time.FixedTotalTime, Is.EqualTo(startFixedTime));

            pacemaker.Update(0f, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 100);
            That(Time.FixedTotalTime, Is.EqualTo(startFixedTime));

            pacemaker.Update(float.Epsilon, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 100);
            That(Time.FixedTotalTime, Is.EqualTo(startFixedTime));

            pacemaker.Update(float.Epsilon, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 100);
            That(Time.FixedTotalTime, Is.EqualTo(startFixedTime + Time.FixedDeltaTime));
        }

        [Test]
        public void CooperativeSimulation_DoesNotSplitOrdinarySystems_OnBudgetBoundary()
        {
            Time.FixedDeltaTime = 0.02f;
            var order = new List<string>();

            var systems = new Dictionary<SystemGroup, List<ISystem<float>>>
            {
                [SystemGroup.InputCollection] = new List<ISystem<float>>
                {
                    new RecordingSystem("input-a", order, spinMs: 3),
                    new RecordingSystem("input-b", order),
                },
                [SystemGroup.EffectProcessing] = new List<ISystem<float>>
                {
                    new YieldingTimeSlicedSystem(yieldCount: 1, order, "effect-slice"),
                    new RecordingSystem("effect-after", order),
                }
            };

            var sim = new PhaseOrderedCooperativeSimulation(systems);

            bool completed = sim.Step(Time.FixedDeltaTime, timeBudgetMs: 1);

            That(completed, Is.False);
            That(order, Is.EqualTo(new[] { "input-a", "input-b", "effect-slice" }));

            completed = sim.Step(Time.FixedDeltaTime, timeBudgetMs: 1);

            That(completed, Is.True);
            That(order, Is.EqualTo(new[] { "input-a", "input-b", "effect-slice", "effect-slice", "effect-after" }));
        }

        private sealed class RecordingSystem : ISystem<float>
        {
            private readonly string _name;
            private readonly List<string> _order;
            private readonly int _spinMs;

            public RecordingSystem(string name, List<string> order, int spinMs = 0)
            {
                _name = name;
                _order = order;
                _spinMs = spinMs;
            }

            public void Initialize()
            {
            }

            public void Update(in float dt)
            {
                _order.Add(_name);
                if (_spinMs <= 0)
                {
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < _spinMs)
                {
                }
            }

            public void BeforeUpdate(in float dt)
            {
            }

            public void AfterUpdate(in float dt)
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class YieldingTimeSlicedSystem : ISystem<float>, ITimeSlicedSystem
        {
            private readonly int _yieldCount;
            private int _remainingYields;
            private bool _active;
            private readonly List<string>? _order;
            private readonly string _name;

            public YieldingTimeSlicedSystem(int yieldCount, List<string>? order = null, string name = "")
            {
                _yieldCount = yieldCount;
                _order = order;
                _name = name;
            }

            public void Initialize()
            {
            }

            public void Update(in float dt)
            {
                UpdateSlice(dt, int.MaxValue);
            }

            public bool UpdateSlice(float dt, int timeBudgetMs)
            {
                if (!_active)
                {
                    _active = true;
                    _remainingYields = _yieldCount;
                }

                if (_order != null)
                {
                    _order.Add(_name);
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

            public void BeforeUpdate(in float dt)
            {
            }

            public void AfterUpdate(in float dt)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
