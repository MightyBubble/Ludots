using Arch.System;
using InteractionShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcaseSelectionDockSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly InteractionShowcaseRuntime _runtime;

        public InteractionShowcaseSelectionDockSystem(GameEngine engine, InteractionShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            if (!InteractionShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value) ||
                _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupSave1ActionId))
            {
                _runtime.SaveControlGroup(_engine, 1);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupSave2ActionId))
            {
                _runtime.SaveControlGroup(_engine, 2);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupSave3ActionId))
            {
                _runtime.SaveControlGroup(_engine, 3);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupSave4ActionId))
            {
                _runtime.SaveControlGroup(_engine, 4);
            }

            if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupRecall1ActionId))
            {
                _runtime.RecallControlGroup(_engine, 1);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupRecall2ActionId))
            {
                _runtime.RecallControlGroup(_engine, 2);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupRecall3ActionId))
            {
                _runtime.RecallControlGroup(_engine, 3);
            }
            else if (input.PressedThisFrame(InteractionShowcaseIds.SelectionGroupRecall4ActionId))
            {
                _runtime.RecallControlGroup(_engine, 4);
            }
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }
}
