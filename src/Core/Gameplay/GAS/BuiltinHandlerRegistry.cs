using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Delegate signature for builtin phase handlers.
    /// Matches the same context available to Graph programs.
    /// </summary>
    public delegate void BuiltinHandlerFn(
        World world,
        Entity effectEntity,
        ref EffectContext context,
        in EffectConfigParams mergedParams,
        in EffectTemplateData templateData);

    /// <summary>
    /// Registry mapping <see cref="BuiltinHandlerId"/> to C# handler functions.
    /// Fixed-size array, zero GC. Registered once at startup via <see cref="GasController"/>.
    /// </summary>
    public sealed class BuiltinHandlerRegistry
    {
        public const int MaxHandlers = 64;

        private readonly BuiltinHandlerFn[] _handlers = new BuiltinHandlerFn[MaxHandlers];

        /// <summary>Register a builtin handler function for the given ID.</summary>
        public void Register(BuiltinHandlerId id, BuiltinHandlerFn fn)
        {
            int idx = (int)id;
            if ((uint)idx >= MaxHandlers)
                throw new ArgumentOutOfRangeException(nameof(id), $"BuiltinHandlerId {id} ({idx}) exceeds MaxHandlers ({MaxHandlers}).");
            _handlers[idx] = fn;
        }

        /// <summary>Invoke the handler for the given ID. Throws if not registered.</summary>
        public void Invoke(
            BuiltinHandlerId id,
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            int idx = (int)id;
            if ((uint)idx >= MaxHandlers || _handlers[idx] == null)
                throw new InvalidOperationException($"No builtin handler registered for {id} ({idx}).");
            _handlers[idx](world, effectEntity, ref context, in mergedParams, in templateData);
        }

        /// <summary>Check if a handler is registered.</summary>
        public bool IsRegistered(BuiltinHandlerId id)
        {
            int idx = (int)id;
            return (uint)idx < MaxHandlers && _handlers[idx] != null;
        }
    }
}
