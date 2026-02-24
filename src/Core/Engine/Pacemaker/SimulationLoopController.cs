using System;

namespace Ludots.Core.Engine.Pacemaker
{
    public enum SimulationLoopMode
    {
        Realtime = 0,
        TurnBased = 1
    }

    public sealed class SimulationLoopController
    {
        private readonly GameEngine _engine;
        private readonly RealtimePacemaker _realtime = new RealtimePacemaker();
        private readonly TurnBasedPacemaker _turnBased = new TurnBasedPacemaker();

        public SimulationLoopController(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public SimulationLoopMode Mode { get; private set; } = SimulationLoopMode.Realtime;

        public void SetRealtime()
        {
            Mode = SimulationLoopMode.Realtime;
            _realtime.Reset();
            _engine.Pacemaker = _realtime;
        }

        public void SetTurnBased()
        {
            Mode = SimulationLoopMode.TurnBased;
            _turnBased.Reset();
            _engine.Pacemaker = _turnBased;
        }

        public void PauseSimulation()
        {
            SetTurnBased();
        }

        public void Step(int fixedTicks = 1)
        {
            if (fixedTicks < 1) throw new ArgumentOutOfRangeException(nameof(fixedTicks));
            if (Mode != SimulationLoopMode.TurnBased)
            {
                SetTurnBased();
            }

            for (int i = 0; i < fixedTicks; i++)
            {
                _turnBased.Step();
            }
        }
    }
}

