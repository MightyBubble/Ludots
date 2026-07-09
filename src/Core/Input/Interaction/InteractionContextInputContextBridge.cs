using System;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Bridges ability/interaction-owned frame input contexts into the live input handler IMC stack.
    /// </summary>
    public sealed class InteractionContextInputContextBridge : IInteractionContextTransition
    {
        private readonly InteractionContextStack _stack;
        private readonly Func<PlayerInputHandler?> _handlerProvider;

        public InteractionContextInputContextBridge(
            InteractionContextStack stack,
            Func<PlayerInputHandler?> handlerProvider)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
            _handlerProvider = handlerProvider ?? throw new ArgumentNullException(nameof(handlerProvider));
        }

        public void OnFramePushed(in InteractionContextFrame frame)
        {
            string inputContext = ResolveInputContext(frame.InputContextId);
            if (inputContext.Length == 0)
            {
                return;
            }

            PlayerInputHandler? handler = _handlerProvider();
            if (handler == null)
            {
                return;
            }

            RequireHandlerContext(handler, inputContext);
            handler.PushContext(inputContext);
        }

        public void OnFrameRemoved(in InteractionContextFrame frame)
        {
            string inputContext = ResolveInputContext(frame.InputContextId);
            if (inputContext.Length == 0)
            {
                return;
            }

            PlayerInputHandler? handler = _handlerProvider();
            if (handler == null)
            {
                return;
            }

            RequireHandlerContext(handler, inputContext);
            handler.PopContext(inputContext);
        }

        private string ResolveInputContext(int inputContextId)
        {
            return inputContextId <= _stack.InputContextIdRegistry.InvalidId
                ? string.Empty
                : _stack.InputContextIdRegistry.GetName(inputContextId);
        }

        private static void RequireHandlerContext(PlayerInputHandler handler, string inputContext)
        {
            if (!handler.HasContext(inputContext))
            {
                throw new InvalidOperationException(
                    $"Interaction context frame requested input context '{inputContext}', but the active PlayerInputHandler config does not define it.");
            }
        }
    }
}
