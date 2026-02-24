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

            pacemaker.Update(0f, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 100);
            That(Time.FixedTotalTime, Is.EqualTo(startFixedTime + Time.FixedDeltaTime));
        }

        private sealed class YieldingTimeSlicedSystem : ISystem<float>, ITimeSlicedSystem
        {
            private int _remainingYields;

            public YieldingTimeSlicedSystem(int yieldCount)
            {
                _remainingYields = yieldCount;
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
                if (_remainingYields > 0)
                {
                    _remainingYields--;
                    return false;
                }
                return true;
            }

            public void ResetSlice()
            {
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
