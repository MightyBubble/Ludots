using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    public sealed class GraphOpRegistry
    {
        private readonly Dictionary<string, GraphOpDescriptor> _descriptorByName;
        private readonly Dictionary<ushort, GraphOpDescriptor> _canonicalDescriptorByOpCode;
        private bool _frozen;

        public GraphOpRegistry()
            : this(StringComparer.OrdinalIgnoreCase)
        {
        }

        private GraphOpRegistry(IEqualityComparer<string> nameComparer)
        {
            _descriptorByName = new Dictionary<string, GraphOpDescriptor>(nameComparer);
            _canonicalDescriptorByOpCode = new Dictionary<ushort, GraphOpDescriptor>();
        }

        public int Count => _descriptorByName.Count;

        public bool IsFrozen => _frozen;

        public void Register(string name, ushort opCode)
        {
            Register(new GraphOpDescriptor(name, opCode));
        }

        public void Register(in GraphOpDescriptor descriptor)
        {
            EnsureMutable();

            GraphOpDescriptor normalized = NormalizeDescriptor(descriptor);

            if (_descriptorByName.TryGetValue(normalized.Name, out GraphOpDescriptor existing))
            {
                if (existing.HasSameCompilerShape(in normalized))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Graph op name '{normalized.Name}' is already registered to opcode {existing.OpCode}.");
            }

            _descriptorByName.Add(normalized.Name, normalized);
            if (!_canonicalDescriptorByOpCode.ContainsKey(normalized.OpCode))
            {
                _canonicalDescriptorByOpCode.Add(normalized.OpCode, normalized);
            }
        }

        public bool TryResolve(string? name, out ushort opCode)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                opCode = 0;
                return false;
            }

            if (_descriptorByName.TryGetValue(name, out GraphOpDescriptor descriptor))
            {
                opCode = descriptor.OpCode;
                return true;
            }

            opCode = 0;
            return false;
        }

        public ushort Resolve(string name)
        {
            if (TryResolve(name, out ushort opCode))
            {
                return opCode;
            }

            throw new InvalidOperationException($"Graph op '{name}' is not registered.");
        }

        public bool TryGetCanonicalName(ushort opCode, out string name)
        {
            if (_canonicalDescriptorByOpCode.TryGetValue(opCode, out GraphOpDescriptor descriptor))
            {
                name = descriptor.Name;
                return true;
            }

            name = string.Empty;
            return false;
        }

        public bool ContainsOpCode(ushort opCode)
        {
            return _canonicalDescriptorByOpCode.ContainsKey(opCode);
        }

        public bool TryResolveDescriptor(string? name, out GraphOpDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                descriptor = default;
                return false;
            }

            return _descriptorByName.TryGetValue(name, out descriptor);
        }

        public bool TryGetDescriptor(ushort opCode, out GraphOpDescriptor descriptor)
        {
            return _canonicalDescriptorByOpCode.TryGetValue(opCode, out descriptor);
        }

        public void Freeze()
        {
            _frozen = true;
        }

        public GraphOpRegistry CloneMutable()
        {
            var clone = new GraphOpRegistry(_descriptorByName.Comparer);
            foreach (var kvp in _descriptorByName)
            {
                clone._descriptorByName.Add(kvp.Key, kvp.Value);
            }

            foreach (var kvp in _canonicalDescriptorByOpCode)
            {
                clone._canonicalDescriptorByOpCode.Add(kvp.Key, kvp.Value);
            }

            return clone;
        }

        private GraphOpDescriptor NormalizeDescriptor(in GraphOpDescriptor descriptor)
        {
            if (!_canonicalDescriptorByOpCode.TryGetValue(descriptor.OpCode, out GraphOpDescriptor canonical))
            {
                return descriptor;
            }

            if (descriptor.HasDefaultCompilerShape)
            {
                return canonical.WithName(descriptor.Name);
            }

            if (!canonical.HasSameCompilerShape(in descriptor))
            {
                throw new InvalidOperationException(
                    $"Graph opcode {descriptor.OpCode} is already registered with a different compiler shape.");
            }

            return descriptor;
        }

        private void EnsureMutable()
        {
            if (_frozen)
            {
                throw new InvalidOperationException("GraphOpRegistry is frozen.");
            }
        }
    }
}
