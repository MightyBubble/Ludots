using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphRegisterReserveReason : byte
    {
        None = 0,
        EntityPreset = 1,
        HostAbi = 2
    }

    public readonly struct GraphRegisterReservation
    {
        public GraphRegisterReservation(GraphValueType bank, byte index, GraphRegisterReserveReason reason)
        {
            Bank = bank;
            Index = index;
            Reason = reason;
        }

        public GraphValueType Bank { get; }
        public byte Index { get; }
        public GraphRegisterReserveReason Reason { get; }
    }

    /// <summary>
    /// Compile-time register owner: per-kind reserved slots, used-set, Alloc, and AllocScratch.
    /// <see cref="GraphVmLimits"/> supplies capacity only.
    /// </summary>
    public sealed class GraphRegisterFile
    {
        private readonly RegisterBank _ints;
        private readonly RegisterBank _bools;
        private readonly RegisterBank _floats;
        private readonly RegisterBank _entities;
        private readonly List<GraphRegisterReservation> _reservations = new(8);

        private GraphRegisterFile(GraphKind kind)
        {
            Kind = kind;
            _ints = new RegisterBank(GraphVmLimits.MaxIntRegisters);
            _bools = new RegisterBank(GraphVmLimits.MaxBoolRegisters);
            _floats = new RegisterBank(GraphVmLimits.MaxFloatRegisters);
            _entities = new RegisterBank(GraphVmLimits.MaxEntityRegisters);
        }

        public GraphKind Kind { get; }

        public IReadOnlyList<GraphRegisterReservation> Reservations => _reservations;

        public static GraphRegisterFile Create(GraphKind kind)
        {
            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph register file requires an explicit supported kind.");
            }

            var file = new GraphRegisterFile(kind);
            file.Reserve(GraphValueType.Entity, 0, GraphRegisterReserveReason.EntityPreset);
            file.Reserve(GraphValueType.Entity, 1, GraphRegisterReserveReason.EntityPreset);
            file.Reserve(GraphValueType.Entity, 2, GraphRegisterReserveReason.EntityPreset);

            switch (kind)
            {
                case GraphKind.Validation:
                    file.ProtectHostAbi(GraphValueType.Bool, 0);
                    break;
                case GraphKind.Score:
                    file.ProtectHostAbi(GraphValueType.Float, 0);
                    break;
                case GraphKind.Script:
                    file.ProtectHostAbi(GraphValueType.Int, 0);
                    break;
            }

            return file;
        }

        public byte BindEntityPreset(GraphNodeOp op)
        {
            return op switch
            {
                GraphNodeOp.LoadCaster => 0,
                GraphNodeOp.LoadExplicitTarget => 1,
                GraphNodeOp.LoadViewer => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Entity preset bind is only defined for LoadCaster, LoadExplicitTarget, and LoadViewer.")
            };
        }

        public byte Alloc(
            GraphValueType type,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            RegisterBank bank = RequireAllocatableBank(type);
            if (bank.TryAlloc(out byte index))
            {
                return index;
            }

            diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                $"Register budget exceeded ({bank.Capacity}).", nodeId));
            return 0;
        }

        public byte AllocScratch(
            GraphValueType type,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            RegisterBank bank = RequireAllocatableBank(type);
            if (bank.TryAllocScratch(out byte index))
            {
                return index;
            }

            diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                $"Register budget exceeded ({bank.Capacity}).", nodeId));
            return 0;
        }

        public byte PinInt(
            int pin,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            if (pin < 0 || pin >= GraphVmLimits.MaxIntRegisters)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterOutOfRange,
                    $"PinRegister {pin} exceeds MaxIntRegisters.", nodeId));
                return 0;
            }

            byte index = (byte)pin;
            if (_ints.IsCompilerAllocated(index))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.RegisterAliasConflict,
                    $"PinRegister {pin} conflicts with an already allocated int register.", nodeId));
                return 0;
            }

            _ints.Pin(index);
            return index;
        }

        public bool IsUsed(GraphValueType type, byte index)
            => TryGetBank(type, out RegisterBank? bank) && bank!.IsUsed(index);

        private void Reserve(GraphValueType bank, byte index, GraphRegisterReserveReason reason)
        {
            RequireBank(bank).Reserve(index);
            _reservations.Add(new GraphRegisterReservation(bank, index, reason));
        }

        private void ProtectHostAbi(GraphValueType bank, byte index)
        {
            RequireBank(bank).ProtectHostAbi(index);
            _reservations.Add(new GraphRegisterReservation(bank, index, GraphRegisterReserveReason.HostAbi));
        }

        private RegisterBank RequireAllocatableBank(GraphValueType type)
        {
            if (type is GraphValueType.Int or GraphValueType.Bool or GraphValueType.Float or GraphValueType.Entity)
            {
                return RequireBank(type);
            }

            throw new ArgumentOutOfRangeException(nameof(type), type, "Register allocation requires Int, Bool, Float, or Entity.");
        }

        private RegisterBank RequireBank(GraphValueType type)
        {
            if (!TryGetBank(type, out RegisterBank? bank))
            {
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown register bank.");
            }

            return bank!;
        }

        private bool TryGetBank(GraphValueType type, out RegisterBank? bank)
        {
            bank = type switch
            {
                GraphValueType.Int => _ints,
                GraphValueType.Bool => _bools,
                GraphValueType.Float => _floats,
                GraphValueType.Entity => _entities,
                _ => null
            };
            return bank != null;
        }

        private static GraphDiagnostic Error(string graphId, string code, string message, string nodeId)
            => new(GraphDiagnosticSeverity.Error, code, message, graphId, nodeId);

        private sealed class RegisterBank
        {
            private readonly bool[] _used;
            private readonly bool[] _compilerAllocated;
            private readonly bool[] _scratchProtected;

            public RegisterBank(int capacity)
            {
                Capacity = capacity;
                _used = new bool[capacity];
                _compilerAllocated = new bool[capacity];
                _scratchProtected = new bool[capacity];
            }

            public int Capacity { get; }

            public bool IsUsed(byte index) => index < Capacity && _used[index];

            public bool IsCompilerAllocated(byte index) => index < Capacity && _compilerAllocated[index];

            public void Reserve(byte index)
            {
                _used[index] = true;
                _scratchProtected[index] = true;
            }

            public void ProtectHostAbi(byte index)
            {
                _scratchProtected[index] = true;
            }

            public void Pin(byte index)
            {
                _used[index] = true;
            }

            public bool TryAlloc(out byte index)
                => TryTake(requireScratchSafe: false, out index);

            public bool TryAllocScratch(out byte index)
                => TryTake(requireScratchSafe: true, out index);

            private bool TryTake(bool requireScratchSafe, out byte index)
            {
                for (int i = 0; i < Capacity; i++)
                {
                    if (_used[i])
                    {
                        continue;
                    }

                    if (requireScratchSafe && _scratchProtected[i])
                    {
                        continue;
                    }

                    _used[i] = true;
                    _compilerAllocated[i] = true;
                    index = (byte)i;
                    return true;
                }

                index = 0;
                return false;
            }
        }
    }
}
