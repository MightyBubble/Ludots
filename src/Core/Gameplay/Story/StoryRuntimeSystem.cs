using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Single input dispatch point for story runtimes (InputCollection phase).
    /// Domain runtimes expose pure APIs; this system owns action consumption order:
    /// an active sequence takes precedence, dialogue consumes only when no sequence plays.
    /// </summary>
    public sealed class StoryRuntimeSystem : ISystem<float>
    {
        private readonly Dialogue.DialogueRuntime _dialogue;
        private readonly Sequencer.SequencerRuntime _sequencer;
        private readonly GameEngine _engine;

        public StoryRuntimeSystem(GameEngine engine, Dialogue.DialogueRuntime dialogue, Sequencer.SequencerRuntime sequencer)
        {
            _engine = engine;
            _dialogue = dialogue;
            _sequencer = sequencer;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            ConsumeStoryInput();
            _dialogue.Update(dt);
            _sequencer.Update(dt);
        }

        private void ConsumeStoryInput()
        {
            var input = _engine.GetService(CoreServiceKeys.AuthoritativeInput);
            if (input == null)
            {
                return;
            }

            if (_sequencer.HasActiveSequence)
            {
                if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Skip))
                {
                    _sequencer.Skip();
                    return;
                }

                if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Advance))
                {
                    if (_sequencer.IsPaused)
                    {
                        _sequencer.Resume();
                    }
                    else
                    {
                        _sequencer.Pause();
                    }
                }

                return;
            }

            if (!_dialogue.TryGetActiveView(out Dialogue.DialogueView view))
            {
                return;
            }

            if (view.Choices.Count > 0)
            {
                if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Choice1)) _dialogue.ChooseOption(0);
                if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Choice2)) _dialogue.ChooseOption(1);
                if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Choice3)) _dialogue.ChooseOption(2);
                return;
            }

            if (input.PressedThisFrame(Dialogue.DialogueInputActionIds.Advance))
            {
                _dialogue.AdvanceDialogue();
            }
        }
    }
}
