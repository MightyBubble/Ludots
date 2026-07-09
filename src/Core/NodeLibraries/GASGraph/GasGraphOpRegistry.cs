using System;
using Ludots.Core.Modding;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public sealed class GasGraphOpDefinition
    {
        public GasGraphOpDefinition(
            int opCode,
            string key,
            GraphValueType outputType,
            GasGraphOpHandler handler,
            GraphValueType[] inputTypes,
            byte? fixedRegister = null)
        {
            OpCode = opCode;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            OutputType = outputType;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            InputTypes = inputTypes ?? Array.Empty<GraphValueType>();
            FixedRegister = fixedRegister;
        }

        public int OpCode { get; }
        public string Key { get; }
        public GraphValueType OutputType { get; }
        public GasGraphOpHandler Handler { get; }
        public GraphValueType[] InputTypes { get; }
        public byte? FixedRegister { get; }
    }

    public sealed class GasGraphOpRegistry
    {
        public const int FirstModOpCode = 1024;

        private readonly ExtensionKeyRegistry _keys = new(
            capacity: 128,
            firstDynamicId: FirstModOpCode,
            maxIdExclusive: GraphVmLimits.HandlerTableSize,
            comparer: StringComparer.Ordinal);
        private readonly GasGraphOpDefinition?[] _definitions = new GasGraphOpDefinition?[GraphVmLimits.HandlerTableSize];

        public int Register(
            string key,
            GraphValueType outputType,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes)
        {
            return Register(key, outputType, fixedRegister: null, handler, inputTypes);
        }

        public int Register(
            string key,
            GraphValueType outputType,
            byte? fixedRegister,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes)
        {
            if (Enum.TryParse(key, ignoreCase: false, out GraphNodeOp reserved) &&
                reserved != GraphNodeOp.None &&
                Enum.IsDefined(typeof(GraphNodeOp), reserved))
            {
                throw new InvalidOperationException(
                    $"Graph op key '{key}' is reserved by Core. Use a mod-qualified key.");
            }

            if (inputTypes != null && inputTypes.Length > 3)
            {
                throw new InvalidOperationException(
                    $"Graph op '{key}' declares {inputTypes.Length} inputs, but extension graph ops support at most 3 inputs.");
            }

            int opCode = _keys.RegisterDynamic(key);
            var definition = new GasGraphOpDefinition(
                opCode,
                key,
                outputType,
                handler,
                inputTypes ?? Array.Empty<GraphValueType>(),
                fixedRegister);

            if (_definitions[opCode] != null)
            {
                throw new InvalidOperationException($"Graph op key '{key}' is already registered.");
            }

            _definitions[opCode] = definition;
            return opCode;
        }

        public bool TryGet(string key, out GasGraphOpDefinition definition)
        {
            if (_keys.TryGetId(key, out int opCode) &&
                (uint)opCode < (uint)_definitions.Length &&
                _definitions[opCode] != null)
            {
                definition = _definitions[opCode]!;
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryGet(int opCode, out GasGraphOpDefinition definition)
        {
            if ((uint)opCode < (uint)_definitions.Length && _definitions[opCode] != null)
            {
                definition = _definitions[opCode]!;
                return true;
            }

            definition = null!;
            return false;
        }

        public void InstallHandlers(GasGraphOpHandler[] handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            for (int i = 0; i < _definitions.Length && i < handlers.Length; i++)
            {
                GasGraphOpDefinition? definition = _definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (handlers[i] != null)
                {
                    throw new InvalidOperationException(
                        $"Graph op id {i} is already bound to a handler. Cannot install '{definition.Key}'.");
                }

                handlers[i] = definition.Handler;
            }
        }

        public void Freeze() => _keys.Freeze();

        public void Clear()
        {
            Array.Clear(_definitions, 0, _definitions.Length);
            _keys.Clear();
        }
    }
}
