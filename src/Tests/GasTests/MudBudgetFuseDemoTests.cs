using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class MudBudgetFuseDemoTests
    {
        [Test]
        public void MudDemo_BudgetFuse_EmitsVisibleLog()
        {
            Time.FixedDeltaTime = 0.02f;

            var systems = new Dictionary<SystemGroup, List<ISystem<float>>>
            {
                [SystemGroup.InputCollection] = new List<ISystem<float>> { new NoopSystem() },
                [SystemGroup.EffectProcessing] = new List<ISystem<float>> { new MudCombatTimeSlicedSystem() },
                [SystemGroup.EventDispatch] = new List<ISystem<float>> { new NoopSystem() }
            };

            var sim = new PhaseOrderedCooperativeSimulation(systems);
            var pacemaker = new RealtimePacemaker();
            pacemaker.Reset();

            for (int renderFrame = 0; renderFrame < 12; renderFrame++)
            {
                TestContext.Progress.WriteLine($"[MUD] RenderFrame={renderFrame}");
                pacemaker.Update(0.016f, sim, timeBudgetMs: 1, maxSlicesPerLogicFrame: 5);
                if (pacemaker.IsBudgetFused) break;
            }

            That(pacemaker.IsBudgetFused, Is.True);
        }

        private sealed class NoopSystem : ISystem<float>
        {
            public void Initialize() { }
            public void Update(in float dt) { }
            public void BeforeUpdate(in float dt) { }
            public void AfterUpdate(in float dt) { }
            public void Dispose() { }
        }

        private sealed class MudCombatTimeSlicedSystem : ISystem<float>, ITimeSlicedSystem
        {
            private int _step;
            private int _subStep;

            public void Initialize()
            {
                TestContext.Progress.WriteLine("[MUD] Init combat demo");
            }

            public void Update(in float dt)
            {
                UpdateSlice(dt, int.MaxValue);
            }

            public bool UpdateSlice(float dt, int timeBudgetMs)
            {
                if (_step == 0)
                {
                    TestContext.Progress.WriteLine("[MUD] You enter the dungeon.");
                    _step = 1;
                    return false;
                }

                if (_step == 1)
                {
                    TestContext.Progress.WriteLine("[MUD] A goblin appears!");
                    _step = 2;
                    _subStep = 0;
                    return false;
                }

                if (_step == 2)
                {
                    if (_subStep == 0)
                    {
                        TestContext.Progress.WriteLine("[MUD] You cast Firebolt (apply direct damage).");
                        _subStep = 1;
                        return false;
                    }
                    if (_subStep == 1)
                    {
                        TestContext.Progress.WriteLine("[MUD] Goblin is burning (apply DOT).");
                        _subStep = 2;
                        return false;
                    }
                    if (_subStep == 2)
                    {
                        TestContext.Progress.WriteLine("[MUD] DOT tick #1 (periodic damage).");
                        _subStep = 3;
                        return false;
                    }
                    if (_subStep == 3)
                    {
                        TestContext.Progress.WriteLine("[MUD] Goblin tries to flee (reaction chain).");
                        _subStep = 4;
                        return false;
                    }
                    if (_subStep == 4)
                    {
                        TestContext.Progress.WriteLine("[MUD] You apply Slow (status effect).");
                        _subStep = 5;
                        return false;
                    }
                    TestContext.Progress.WriteLine("[MUD] Combat resolves.");
                    _step = 3;
                    return true;
                }

                return true;
            }

            public void ResetSlice()
            {
            }

            public void BeforeUpdate(in float dt) { }
            public void AfterUpdate(in float dt) { }
            public void Dispose() { }
        }
    }
}

