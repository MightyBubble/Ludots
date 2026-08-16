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

    public delegate EffectOperationMetadata BuiltinOperationMetadataResolver(
        in EffectTemplateData templateData);

    /// <summary>
    /// Registry mapping semantic handler keys to C# handler functions.
    /// Builtin enum ids are preserved; mod handlers receive startup-assigned ids.
    /// Every handler must declare its side-effect operation metadata.
    /// </summary>
    public sealed class BuiltinHandlerRegistry
    {
        public const int FirstModHandlerId = 1024;
        public const int MaxHandlers = 2048;

        private readonly BuiltinHandlerFn[] _handlers = new BuiltinHandlerFn[MaxHandlers];
        private readonly EffectOperationMetadata[] _operationMetadata = new EffectOperationMetadata[MaxHandlers];
        private readonly BuiltinOperationMetadataResolver?[] _operationMetadataResolvers =
            new BuiltinOperationMetadataResolver?[MaxHandlers];
        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModHandlerId,
            maxIdExclusive: MaxHandlers,
            comparer: StringComparer.Ordinal);

        public BuiltinHandlerRegistry()
        {
            foreach (BuiltinHandlerId id in Enum.GetValues<BuiltinHandlerId>())
            {
                if (id == BuiltinHandlerId.None)
                {
                    continue;
                }

                _keys.RegisterFixed(id.ToString(), (int)id);
            }
        }

        /// <summary>Register a builtin handler and its side-effect contract for the given ID.</summary>
        public void Register(
            BuiltinHandlerId id,
            BuiltinHandlerFn fn,
            in EffectOperationMetadata operationMetadata,
            BuiltinOperationMetadataResolver? operationMetadataResolver = null)
        {
            if (id == BuiltinHandlerId.None)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "BuiltinHandlerId.None cannot be registered.");
            }

            if (!Enum.IsDefined(typeof(BuiltinHandlerId), id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"Builtin handler id {(int)id} is not defined by Core.");
            }

            int idx = (int)id;
            if (fn == null)
                throw new ArgumentNullException(nameof(fn));
            if (operationMetadata.Kind == EffectOperationKind.None)
                throw new ArgumentException($"BuiltinHandlerId {id} requires operation metadata.", nameof(operationMetadata));
            if (_handlers[idx] != null)
                throw new InvalidOperationException($"BuiltinHandlerId {id} ({idx}) is already registered.");
            _handlers[idx] = fn;
            _operationMetadata[idx] = operationMetadata;
            _operationMetadataResolvers[idx] = operationMetadataResolver;
        }

        /// <summary>
        /// Register a mod-authored C# handler and return its runtime id.
        /// Keys should be mod-qualified, for example "MyMod.ApplyBurn".
        /// </summary>
        public int Register(
            string key,
            BuiltinHandlerFn fn,
            in EffectOperationMetadata operationMetadata,
            BuiltinOperationMetadataResolver? operationMetadataResolver = null)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BuiltinHandlerId reserved) &&
                reserved != BuiltinHandlerId.None &&
                Enum.IsDefined(typeof(BuiltinHandlerId), reserved))
            {
                throw new InvalidOperationException(
                    $"Builtin handler key '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            if (fn == null) throw new ArgumentNullException(nameof(fn));
            if (operationMetadata.Kind == EffectOperationKind.None)
                throw new ArgumentException($"Builtin handler '{key}' requires operation metadata.", nameof(operationMetadata));
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
            _operationMetadata[id] = operationMetadata;
            _operationMetadataResolvers[id] = operationMetadataResolver;
            return id;
        }

        public int GetId(string key)
        {
            return _keys.GetId(key);
        }

        public bool TryGetId(string key, out int id)
        {
            if (Enum.TryParse(key, ignoreCase: false, out BuiltinHandlerId builtin) &&
                builtin != BuiltinHandlerId.None &&
                Enum.IsDefined(typeof(BuiltinHandlerId), builtin))
            {
                id = (int)builtin;
                return true;
            }

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
            Array.Clear(_operationMetadata, 0, _operationMetadata.Length);
            Array.Clear(_operationMetadataResolvers, 0, _operationMetadataResolvers.Length);
            _keys.Clear();
            foreach (BuiltinHandlerId id in Enum.GetValues<BuiltinHandlerId>())
            {
                if (id == BuiltinHandlerId.None)
                {
                    continue;
                }

                _keys.RegisterFixed(id.ToString(), (int)id);
            }
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

        public bool TryGetOperationMetadata(BuiltinHandlerId id, out EffectOperationMetadata operationMetadata)
        {
            return TryGetOperationMetadata((int)id, out operationMetadata);
        }

        public bool TryGetOperationMetadata(int handlerId, out EffectOperationMetadata operationMetadata)
        {
            if ((uint)handlerId < MaxHandlers &&
                _handlers[handlerId] != null &&
                _operationMetadata[handlerId].Kind != EffectOperationKind.None)
            {
                operationMetadata = _operationMetadata[handlerId];
                return true;
            }

            operationMetadata = default;
            return false;
        }

        public bool TryResolveOperationMetadata(
            BuiltinHandlerId id,
            in EffectTemplateData templateData,
            out EffectOperationMetadata operationMetadata)
        {
            return TryResolveOperationMetadata((int)id, in templateData, out operationMetadata);
        }

        public bool TryResolveOperationMetadata(
            int handlerId,
            in EffectTemplateData templateData,
            out EffectOperationMetadata operationMetadata)
        {
            if ((uint)handlerId >= MaxHandlers || _handlers[handlerId] == null)
            {
                operationMetadata = default;
                return false;
            }

            BuiltinOperationMetadataResolver? resolver = _operationMetadataResolvers[handlerId];
            operationMetadata = resolver != null
                ? resolver(in templateData)
                : _operationMetadata[handlerId];
            return operationMetadata.Kind != EffectOperationKind.None;
        }
    }
}
