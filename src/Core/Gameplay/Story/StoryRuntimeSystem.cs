using Arch.System;

namespace Ludots.Core.Gameplay.Story
{
    public sealed class StoryRuntimeSystem : ISystem<float>
    {
        private readonly Dialogue.DialogueRuntime _dialogue;
        private readonly Sequencer.SequencerRuntime _sequencer;

        public StoryRuntimeSystem(Dialogue.DialogueRuntime dialogue, Sequencer.SequencerRuntime sequencer)
        {
            _dialogue = dialogue;
            _sequencer = sequencer;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            _dialogue.Update(dt);
            _sequencer.Update(dt);
        }
    }
}
