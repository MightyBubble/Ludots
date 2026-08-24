using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Scripting;

namespace NarrativeChainShowcaseMod.Runtime
{
    /// <summary>
    /// Deferred chain transitions that must not fire from inside a trigger handler:
    /// opening dialogue -> cinematic handoff, and the simulated survey objective signal.
    /// </summary>
    internal sealed class NarrativeChainAdvanceSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeChainRuntime _runtime;
        private bool _disposed;

        public NarrativeChainAdvanceSystem(GameEngine engine, NarrativeChainRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_disposed)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.NarrativeDirector) is not NarrativeDirector director)
            {
                return;
            }

            if (_runtime.PendingCinematic && !director.HasActiveDialogue && !director.HasActiveCinematic)
            {
                _runtime.PendingCinematic = false;
                director.StartCinematic(NarrativeChainIds.RevealCinematicId);
                _runtime.Record("dialogue", "cinematic_started", NarrativeChainIds.RevealCinematicId);
            }

            if (_runtime.ObjectiveDelayFrames > 0)
            {
                _runtime.ObjectiveDelayFrames--;
            }
            else if (_runtime.ObjectiveDelayFrames == 0)
            {
                _runtime.ObjectiveDelayFrames = -1;
                _runtime.EmitObjectiveSignal(_engine);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
