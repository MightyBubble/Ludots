using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Modding;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Delegate signature for builtin phase handlers.
    /// Matches the same effect/template context available to graph programs.
    /// Runtime-only services are exposed through <see cref="BuiltinHandlerRuntimeScope"/>.
    /// </summary>
    public delegate void BuiltinHandlerFn(
        World world,
        Entity effectEntity,
        ref EffectContext context,
        in EffectConfigParams mergedParams,
        in EffectTemplateData templateData);

    /// <summary>
    /// Registry mapping semantic handler keys to C# handler functions.
    /// Builtin enum ids are preserved; mod handlers receive startup-assigned ids.
    /// </summary>
    public sealed class BuiltinHandlerRegistry
    {
        public const int FirstModHandlerId = 1024;
        public const int MaxHandlers = 2048;

        private readonly BuiltinHandlerFn[] _handlers = new BuiltinHandlerFn[MaxHandlers];
        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModHandlerId,
            maxIdExclusive: MaxHandlers,
            comparer: StringComparer.Ordinal);

        /// <summary>Register a builtin handler function for the given ID.</summary>
        public void Register(BuiltinHandlerId id, BuiltinHandlerFn fn)
        {
            if (id == BuiltinHandlerId.None)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "BuiltinHandlerId.None cannot be registered.");
            }

            if (!Enum.IsDefined(typeof(BuiltinHandlerId), id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"Builtin handler id {(int)id} is not defined by Core.");
            }

            RegisterFixed(id.ToString(), (int)id, fn);
        }

        /// <summary>
        /// Register a mod-authored C# handler and return its runtime id.
        /// Keys should be mod-qualified, for example "MyMod.ApplyBurn".
        /// </summary>
        public int Register(string key, BuiltinHandlerFn fn)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BuiltinHandlerId reserved) &&
                reserved != BuiltinHandlerId.None &&
                Enum.IsDefined(typeof(BuiltinHandlerId), reserved))
            {
                throw new InvalidOperationException(
                    $"Builtin handler key '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            if (fn == null) throw new ArgumentNullException(nameof(fn));
            if (_keys.IsFrozen)
            {
                throw new InvalidOperationException($"Builtin handler registry is frozen. Cannot register '{key}'.");
            }

            if (_keys.TryGetId(key, out _))
            {
                throw new InvalidOperationException($"Builtin handler key '{key}' is already registered.");
            }

            int id = _keys.RegisterDynamic(key);
            _handlers[id] = fn;
            return id;
        }

        public void RegisterFixed(string key, int id, BuiltinHandlerFn fn)
        {
            if (fn == null) throw new ArgumentNullException(nameof(fn));
            if ((uint)id >= MaxHandlers)
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"Builtin handler id {id} exceeds MaxHandlers ({MaxHandlers}).");
            }

            if (_keys.IsFrozen &&
                (!_keys.TryGetId(key, out int existingId) ||
                existingId != id ||
                _handlers[id] == null ||
                !_handlers[id]!.Equals(fn)))
            {
                throw new InvalidOperationException($"Builtin handler registry is frozen. Cannot register '{key}'.");
            }

            _keys.RegisterFixed(key, id);
            _handlers[id] = fn;
        }

        public int GetId(string key)
        {
            return _keys.GetId(key);
        }

        public bool TryGetId(string key, out int id)
        {
            return _keys.TryGetId(key, out id);
        }

        public string GetKey(int id)
        {
            return _keys.GetKey(id);
        }

        public void Freeze()
        {
            _keys.Freeze();
        }

        public void Clear()
        {
            Array.Clear(_handlers, 0, _handlers.Length);
            _keys.Clear();
        }

        /// <summary>Invoke the handler for the given ID. Throws if not registered.</summary>
        public void Invoke(
            BuiltinHandlerId id,
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData,
            BuiltinHandlerExecutionContext? runtimeContext = null)
        {
            Invoke((int)id, world, effectEntity, ref context, in mergedParams, in templateData, runtimeContext);
        }

        public void Invoke(
            int handlerId,
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData,
            BuiltinHandlerExecutionContext? runtimeContext = null)
        {
            if ((uint)handlerId >= MaxHandlers || _handlers[handlerId] == null)
                throw new InvalidOperationException($"No builtin handler registered for id {handlerId} ('{GetKey(handlerId)}').");

            using var scope = BuiltinHandlerRuntimeScope.Push(runtimeContext);
            _handlers[handlerId](world, effectEntity, ref context, in mergedParams, in templateData);
        }

        /// <summary>Check if a handler is registered.</summary>
        public bool IsRegistered(BuiltinHandlerId id)
        {
            int idx = (int)id;
            return (uint)idx < MaxHandlers && _handlers[idx] != null;
        }

        public bool IsRegistered(int handlerId)
        {
            return (uint)handlerId < MaxHandlers && _handlers[handlerId] != null;
        }
    }
}
